using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresCourierOnboardingServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Registration_and_trust_transitions_are_atomic_idempotent_and_auditable()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central", "zone-east");
        var service = new PostgresCourierOnboardingService(
            database.DataSource, new TestOperationsContext(),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero)));

        var created = await service.RegisterAsync("BIKE", ["zone-central"], "register-1");
        Assert.Equal(CourierOperationsWriteOutcome.Applied, created.Outcome);
        Assert.Equal("REGISTERED", created.Snapshot!.Status);
        Assert.Empty(created.Snapshot.TrustTransitions);

        var replay = await service.RegisterAsync("BIKE", ["zone-central"], "register-1");
        Assert.Equal(CourierOperationsWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(created.Snapshot.CourierId, replay.Snapshot!.CourierId);

        var conflict = await service.RegisterAsync("BIKE", ["zone-east"], "register-1");
        Assert.Equal(CourierOperationsWriteOutcome.Conflict, conflict.Outcome);

        var courierId = created.Snapshot.CourierId;
        var secondCourier = await service.RegisterAsync("WALK", ["zone-east"], "register-second");
        Assert.Equal(CourierOperationsWriteOutcome.Applied, secondCourier.Outcome);
        var transitions = new[]
        {
            "IDENTITY_VERIFIED",
            "DOCUMENTS_SUBMITTED",
            "TRUST_REQUIREMENTS_VERIFIED",
            "MONO_APPROVED",
            "ACTIVE"
        };
        CourierOperationsWriteResult? last = null;
        for (var index = 0; index < transitions.Length; index++)
        {
            var status = transitions[index];
            last = await service.AdvanceTrustAsync(
                courierId,
                status,
                status == "ACTIVE" ? ["zone-central"] : null,
                status == "ACTIVE" ? "zone-central" : null,
                "REVIEW_COMPLETED",
                $"review:{status}",
                $"transition-{index + 1}");
            Assert.Equal(CourierOperationsWriteOutcome.Applied, last.Outcome);
            Assert.Equal(status, last.Snapshot!.Status);
        }

        Assert.Equal(5, last!.Snapshot!.TrustTransitions.Count);
        Assert.Equal("staff-operations-1", last.Snapshot.TrustTransitions[^1].ActorId);
        Assert.Equal("review:ACTIVE", last.Snapshot.TrustTransitions[^1].EvidenceReference);

        var transitionReplay = await service.AdvanceTrustAsync(
            courierId, "ACTIVE", ["zone-central"], "zone-central",
            "REVIEW_COMPLETED", "review:ACTIVE", "transition-5");
        Assert.Equal(CourierOperationsWriteOutcome.Replayed, transitionReplay.Outcome);
        Assert.Equal("ACTIVE", transitionReplay.Snapshot!.Status);
        Assert.Equal(last.Snapshot.TrustTransitions.Count, transitionReplay.Snapshot.TrustTransitions.Count);

        var detail = await service.FindAsync(courierId);
        Assert.NotNull(detail);
        Assert.Equal("ACTIVE", detail.Status);
        Assert.Equal(new[] { "zone-central" }, detail.ApprovedZoneIds);

        var page = await service.ListAsync(1, null);
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.NotNull(page.NextCursor);
        var nextPage = await service.ListAsync(1, page.NextCursor);
        Assert.NotNull(nextPage);
        Assert.Single(nextPage.Items);
        Assert.NotEqual(page.Items[0].CourierId, nextPage.Items[0].CourierId);
        Assert.Null(nextPage.NextCursor);
    }

    [Fact]
    public async Task Does_not_advance_when_trust_order_or_zone_approval_is_invalid()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central", "zone-east");
        var service = new PostgresCourierOnboardingService(database.DataSource, new TestOperationsContext());
        var created = await service.RegisterAsync("WALK", ["zone-central"], "register-2");
        var courierId = created.Snapshot!.CourierId;

        var skipped = await service.AdvanceTrustAsync(
            courierId, "DOCUMENTS_SUBMITTED", null, null,
            "REVIEW_COMPLETED", "review:skip", "transition-skip");
        Assert.Equal(CourierOperationsWriteOutcome.InvalidTransition, skipped.Outcome);

        for (var index = 0; index < 4; index++)
        {
            var statuses = new[]
            {
                "IDENTITY_VERIFIED", "DOCUMENTS_SUBMITTED",
                "TRUST_REQUIREMENTS_VERIFIED", "MONO_APPROVED"
            };
            var result = await service.AdvanceTrustAsync(
                courierId, statuses[index], null, null, "REVIEW_COMPLETED",
                $"review:{statuses[index]}", $"valid-{index}");
            Assert.Equal(CourierOperationsWriteOutcome.Applied, result.Outcome);
        }

        var invalidApproval = await service.AdvanceTrustAsync(
            courierId, "ACTIVE", ["zone-east"], "zone-east",
            "APPROVED", "review:activation", "transition-bad-zone");
        Assert.Equal(CourierOperationsWriteOutcome.InvalidTransition, invalidApproval.Outcome);

        var current = await service.FindAsync(courierId);
        Assert.Equal("MONO_APPROVED", current!.Status);
        Assert.Empty(current.ApprovedZoneIds);
    }

    [Fact]
    public async Task Courier_transition_and_idempotency_ledgers_reject_mutation()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central");
        var service = new PostgresCourierOnboardingService(database.DataSource, new TestOperationsContext());
        var created = await service.RegisterAsync("CAR", ["zone-central"], "register-immutability");
        var courierId = created.Snapshot!.CourierId;
        await using (var skippedConnection = await database.DataSource.OpenConnectionAsync())
        {
            await using var skippedStatus = new NpgsqlCommand(
                "UPDATE couriers SET status = 'ACTIVE' WHERE courier_id = @courier_id;", skippedConnection);
            skippedStatus.Parameters.AddWithValue("courier_id", courierId);
            await Assert.ThrowsAsync<PostgresException>(() => skippedStatus.ExecuteNonQueryAsync());
        }

        await using (var unauditedConnection = await database.DataSource.OpenConnectionAsync())
        {
            await using var unauditedStatus = new NpgsqlCommand(
                "UPDATE couriers SET status = 'IDENTITY_VERIFIED' WHERE courier_id = @courier_id;", unauditedConnection);
            unauditedStatus.Parameters.AddWithValue("courier_id", courierId);
            await Assert.ThrowsAsync<PostgresException>(() => unauditedStatus.ExecuteNonQueryAsync());
        }

        var result = await service.AdvanceTrustAsync(
            courierId, "IDENTITY_VERIFIED", null, null,
            "IDENTITY_REVIEWED", "review:immutable", "transition-immutable");
        Assert.Equal(CourierOperationsWriteOutcome.Applied, result.Outcome);

        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using var updateTransition = new NpgsqlCommand(
            "UPDATE courier_trust_transitions SET reason_code = 'TAMPERED' WHERE courier_id = @courier_id;",
            connection);
        updateTransition.Parameters.AddWithValue("courier_id", courierId);
        await Assert.ThrowsAsync<PostgresException>(() => updateTransition.ExecuteNonQueryAsync());

        await using var deleteIdempotency = new NpgsqlCommand(
            "DELETE FROM courier_operations_idempotency WHERE actor_id = @actor_id;", connection);
        deleteIdempotency.Parameters.AddWithValue("actor_id", "staff-operations-1");
        await Assert.ThrowsAsync<PostgresException>(() => deleteIdempotency.ExecuteNonQueryAsync());
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "staff-operations-1";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Courier_reads_are_audited_and_audit_ledger_is_append_only()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central");
        var onboarding = new PostgresCourierOnboardingService(database.DataSource, new TestOperationsContext());
        var courier = await onboarding.RegisterAsync("BIKE", ["zone-central"], "read-audit-register");
        var service = new PostgresCourierOperationsReadAuditService(
            database.DataSource, new TestOperationsContext());

        await service.RecordAsync(PostgresCourierOperationsReadAuditService.CourierSearch, null, 1);
        await service.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierDetail, courier.Snapshot!.CourierId, 1);
        await service.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierDetail,
            "crr_00000000000000000000000000000000", 0);
        var page = await service.ListAsync(10, null);

        Assert.NotNull(page);
        Assert.Contains(page.Items, item =>
            item.Action == PostgresCourierOperationsReadAuditService.CourierSearch &&
            item.ResultCount == 1 && item.CourierId is null);
        Assert.Contains(page.Items, item =>
            item.Action == PostgresCourierOperationsReadAuditService.CourierDetail &&
            item.CourierId == courier.Snapshot.CourierId && item.ResultCount == 1);
        Assert.Contains(page.Items, item =>
            item.Action == PostgresCourierOperationsReadAuditService.CourierDetail &&
            item.CourierId == "crr_00000000000000000000000000000000" && item.ResultCount == 0);

        await using var connection = await database.DataSource.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM courier_operations_read_audit;", connection);
            await command.ExecuteNonQueryAsync();
        });
    }

    [Fact]
    public async Task Planned_availability_windows_are_idempotent_non_overlapping_and_cancellable()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central");
        var now = DateTimeOffset.UtcNow;
        var context = new TestOperationsContext();
        var onboarding = new PostgresCourierOnboardingService(
            database.DataSource, context, new FixedTimeProvider(now));
        var registration = await onboarding.RegisterAsync("BIKE", ["zone-central"], "availability-register");
        var courierId = registration.Snapshot!.CourierId;
        var transitions = new[]
        {
            "IDENTITY_VERIFIED", "DOCUMENTS_SUBMITTED", "TRUST_REQUIREMENTS_VERIFIED",
            "MONO_APPROVED", "ACTIVE"
        };
        for (var index = 0; index < transitions.Length; index++)
        {
            var status = transitions[index];
            var result = await onboarding.AdvanceTrustAsync(
                courierId, status, status == "ACTIVE" ? ["zone-central"] : null,
                status == "ACTIVE" ? "zone-central" : null, "REVIEW_COMPLETED",
                $"review:{status}", $"availability-transition-{index}");
            Assert.Equal(CourierOperationsWriteOutcome.Applied, result.Outcome);
        }

        var readAudit = new PostgresCourierOperationsReadAuditService(database.DataSource, context);
        var availability = new PostgresCourierAvailabilityService(
            database.DataSource, context, readAudit, new FixedTimeProvider(now));
        var startsAt = now.AddHours(1);
        var endsAt = now.AddHours(4);
        var created = await availability.CreateAsync(
            courierId, "zone-central", startsAt, endsAt, "availability-create");
        Assert.Equal(CourierAvailabilityWriteOutcome.Applied, created.Outcome);
        Assert.Equal("SCHEDULED", created.Window!.Status);

        var replay = await availability.CreateAsync(
            courierId, "zone-central", startsAt, endsAt, "availability-create");
        Assert.Equal(CourierAvailabilityWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(created.Window.WindowId, replay.Window!.WindowId);

        var overlap = await availability.CreateAsync(
            courierId, "zone-central", now.AddHours(3), now.AddHours(5), "availability-overlap");
        Assert.Equal(CourierAvailabilityWriteOutcome.Overlap, overlap.Outcome);

        var page = await availability.ListAsync(now, now.AddDays(1), 10, null, null);
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(created.Window.WindowId, page.Items[0].WindowId);

        var cancelled = await availability.CancelAsync(
            created.Window.WindowId, "SHIFT_CHANGED", "availability-cancel");
        Assert.Equal(CourierAvailabilityWriteOutcome.Applied, cancelled.Outcome);
        Assert.Equal("CANCELLED", cancelled.Window!.Status);
        var cancelReplay = await availability.CancelAsync(
            created.Window.WindowId, "SHIFT_CHANGED", "availability-cancel");
        Assert.Equal(CourierAvailabilityWriteOutcome.Replayed, cancelReplay.Outcome);
        Assert.Equal("CANCELLED", cancelReplay.Window!.Status);

        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using var mutation = new NpgsqlCommand(
            "UPDATE courier_availability_windows SET starts_at = starts_at + interval '1 hour' WHERE window_id = @id;",
            connection);
        mutation.Parameters.AddWithValue("id", NpgsqlTypes.NpgsqlDbType.Text, created.Window.WindowId);
        await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Capacity_projection_counts_planned_couriers_per_zone_time_bucket_and_audits_read()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(
            utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, (utcNow.Minute / 15) * 15, 0, TimeSpan.Zero);
        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central");
        var context = new TestOperationsContext();
        var onboarding = new PostgresCourierOnboardingService(
            database.DataSource, context, new FixedTimeProvider(now));
        var registration = await onboarding.RegisterAsync("BIKE", ["zone-central"], "capacity-register");
        var courierId = registration.Snapshot!.CourierId;
        var transitions = new[]
        {
            "IDENTITY_VERIFIED", "DOCUMENTS_SUBMITTED", "TRUST_REQUIREMENTS_VERIFIED",
            "MONO_APPROVED", "ACTIVE"
        };
        for (var index = 0; index < transitions.Length; index++)
        {
            var status = transitions[index];
            var result = await onboarding.AdvanceTrustAsync(
                courierId, status, status == "ACTIVE" ? ["zone-central"] : null,
                status == "ACTIVE" ? "zone-central" : null, "REVIEW_COMPLETED",
                $"review:{status}", $"capacity-transition-{index}");
            Assert.Equal(CourierOperationsWriteOutcome.Applied, result.Outcome);
        }

        var readAudit = new PostgresCourierOperationsReadAuditService(database.DataSource, context);
        var availability = new PostgresCourierAvailabilityService(
            database.DataSource, context, readAudit, new FixedTimeProvider(now));
        var created = await availability.CreateAsync(
            courierId, "zone-central", now, now.AddMinutes(45), "capacity-window");
        Assert.Equal(CourierAvailabilityWriteOutcome.Applied, created.Outcome);

        var projectionService = new PostgresCourierCapacityProjectionService(
            database.DataSource, context, readAudit);
        var projection = await projectionService.ProjectAsync(
            "zone-central", now, now.AddHours(1), 15);

        Assert.Equal("zone-central", projection.ZoneId);
        Assert.Equal(4, projection.Buckets.Count);
        Assert.Equal(new[] { 1, 1, 1, 0 },
            projection.Buckets.Select(bucket => bucket.PlannedCourierCount));
        Assert.Equal(now, projection.Buckets[0].From);
        Assert.Equal(now.AddMinutes(15), projection.Buckets[0].To);

        var auditPage = await readAudit.ListAsync(10, null);
        Assert.NotNull(auditPage);
        Assert.Contains(auditPage.Items, item =>
            item.Action == PostgresCourierOperationsReadAuditService.CourierCapacityProjection &&
            item.ResultCount == 4 && item.CourierId is null);
    }

    [Fact]
    public async Task Courier_suspension_is_separate_audited_idempotent_and_excludes_planned_capacity()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(
            utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, (utcNow.Minute / 15) * 15, 0, TimeSpan.Zero);
        await using var database = await TestDatabase.CreateAsync(connectionString);
        await database.SeedActiveZonesAsync("zone-central");
        var context = new TestOperationsContext();
        var onboarding = new PostgresCourierOnboardingService(
            database.DataSource, context, new FixedTimeProvider(now));
        var registration = await onboarding.RegisterAsync("BIKE", ["zone-central"], "suspension-register");
        var courierId = registration.Snapshot!.CourierId;
        var statuses = new[]
        {
            "IDENTITY_VERIFIED", "DOCUMENTS_SUBMITTED", "TRUST_REQUIREMENTS_VERIFIED",
            "MONO_APPROVED", "ACTIVE"
        };
        for (var index = 0; index < statuses.Length; index++)
        {
            var status = statuses[index];
            var transition = await onboarding.AdvanceTrustAsync(
                courierId, status, status == "ACTIVE" ? ["zone-central"] : null,
                status == "ACTIVE" ? "zone-central" : null, "REVIEW_COMPLETED",
                $"review:{status}", $"suspension-transition-{index}");
            Assert.Equal(CourierOperationsWriteOutcome.Applied, transition.Outcome);
        }

        var readAudit = new PostgresCourierOperationsReadAuditService(
            database.DataSource, context, new FixedTimeProvider(now));
        var availability = new PostgresCourierAvailabilityService(
            database.DataSource, context, readAudit, new FixedTimeProvider(now));
        var planned = await availability.CreateAsync(
            courierId, "zone-central", now, now.AddMinutes(45), "suspension-window");
        Assert.Equal(CourierAvailabilityWriteOutcome.Applied, planned.Outcome);

        var suspension = new PostgresCourierSuspensionService(
            database.DataSource, context, new FixedTimeProvider(now));
        var suspended = await suspension.SuspendAsync(
            courierId, "SAFETY_REVIEW", "suspend-courier");
        Assert.Equal(CourierOperationalStatusOutcome.Applied, suspended.Outcome);
        Assert.Equal("ACTIVE", suspended.Snapshot!.TrustStatus);
        Assert.Equal("SUSPENDED", suspended.Snapshot.OperationalStatus);
        Assert.Single(suspended.Snapshot.History);

        var replay = await suspension.SuspendAsync(
            courierId, "SAFETY_REVIEW", "suspend-courier");
        Assert.Equal(CourierOperationalStatusOutcome.Replayed, replay.Outcome);
        Assert.Equal(suspended.Snapshot.History[0].EventId, replay.Snapshot!.History[0].EventId);

        var changedReplay = await suspension.SuspendAsync(
            courierId, "OTHER_REASON", "suspend-courier");
        Assert.Equal(CourierOperationalStatusOutcome.Conflict, changedReplay.Outcome);

        var duplicateSuspension = await suspension.SuspendAsync(
            courierId, "SAFETY_REVIEW", "suspend-again");
        Assert.Equal(CourierOperationalStatusOutcome.InvalidState, duplicateSuspension.Outcome);

        var rejectedWindow = await availability.CreateAsync(
            courierId, "zone-central", now.AddHours(1), now.AddHours(2), "suspended-window");
        Assert.Equal(CourierAvailabilityWriteOutcome.InvalidCourierState, rejectedWindow.Outcome);

        await using (var connection = await database.DataSource.OpenConnectionAsync())
        {
            await using var cancelledWindow = new NpgsqlCommand(
                """
                SELECT status, cancellation_reason FROM courier_availability_windows
                WHERE window_id = @window_id;
                """, connection);
            cancelledWindow.Parameters.AddWithValue("window_id", planned.Window!.WindowId);
            await using var reader = await cancelledWindow.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("CANCELLED", reader.GetString(0));
            Assert.Equal("COURIER_SUSPENDED", reader.GetString(1));
        }

        var projection = new PostgresCourierCapacityProjectionService(database.DataSource, context, readAudit);
        var suspendedCapacity = await projection.ProjectAsync(
            "zone-central", now, now.AddHours(1), 15);
        Assert.All(suspendedCapacity.Buckets, bucket => Assert.Equal(0, bucket.PlannedCourierCount));

        var reinstated = await suspension.ReinstateAsync(
            courierId, "REVIEW_CLEARED", "reinstate-courier");
        Assert.Equal(CourierOperationalStatusOutcome.Applied, reinstated.Outcome);
        Assert.Equal("ACTIVE", reinstated.Snapshot!.OperationalStatus);
        Assert.Equal(new[] { "REINSTATED", "SUSPENDED" },
            reinstated.Snapshot.History.Select(item => item.Action));

        var activeCapacity = await projection.ProjectAsync(
            "zone-central", now, now.AddHours(1), 15);
        Assert.All(activeCapacity.Buckets, bucket => Assert.Equal(0, bucket.PlannedCourierCount));

        var replacementWindow = await availability.CreateAsync(
            courierId, "zone-central", now.AddMinutes(45), now.AddHours(1), "reinstated-window");
        Assert.Equal(CourierAvailabilityWriteOutcome.Applied, replacementWindow.Outcome);
        var replacementCapacity = await projection.ProjectAsync(
            "zone-central", now, now.AddHours(1), 15);
        Assert.Equal(new[] { 0, 0, 0, 1 },
            replacementCapacity.Buckets.Select(bucket => bucket.PlannedCourierCount));

        var operationalStatusSnapshot = await suspension.FindAsync(courierId);
        Assert.Equal("ACTIVE", operationalStatusSnapshot!.TrustStatus);
        Assert.Equal("ACTIVE", operationalStatusSnapshot.OperationalStatus);
        Assert.Equal(2, operationalStatusSnapshot.History.Count);

        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using var mutation = new NpgsqlCommand(
            "UPDATE courier_operational_status_events SET reason_code = 'TAMPERED' WHERE courier_id = @courier_id;",
            connection);
        mutation.Parameters.AddWithValue("courier_id", courierId);
        await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;

        private TestDatabase(string adminConnectionString, string schema, NpgsqlDataSource dataSource)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            DataSource = dataSource;
        }

        public NpgsqlDataSource DataSource { get; }

        public static async Task<TestDatabase> CreateAsync(string connectionString)
        {
            var schema = "courier_ops_" + Guid.NewGuid().ToString("N");
            var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema};", admin);
                await create.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            var database = new TestDatabase(adminConnectionString, schema, dataSource);
            await database.InitializeAsync();
            return database;
        }

        public async Task SeedActiveZonesAsync(params string[] zoneIds)
        {
            await using var connection = await DataSource.OpenConnectionAsync();
            foreach (var zoneId in zoneIds)
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO zone_geometry_active (zone_id) VALUES (@zone_id);", connection);
                command.Parameters.AddWithValue("zone_id", zoneId);
                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task InitializeAsync()
        {
            await using (var connection = await DataSource.OpenConnectionAsync())
            await using (var createZones = new NpgsqlCommand(
                "CREATE TABLE zone_geometry_active (zone_id text PRIMARY KEY);", connection))
            {
                await createZones.ExecuteNonQueryAsync();
            }

            const string resourceName = "Mono.Api.Tests.Migrations.033_courier_onboarding.sql";
            await using var stream = typeof(PostgresCourierOnboardingServiceTests).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {resourceName}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var migrationConnection = await DataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, migrationConnection);
            await command.ExecuteNonQueryAsync();

            const string auditResourceName = "Mono.Api.Tests.Migrations.034_courier_operations_read_audit.sql";
            await using var auditStream = typeof(PostgresCourierOnboardingServiceTests).Assembly
                .GetManifestResourceStream(auditResourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {auditResourceName}.");
            using var auditReader = new StreamReader(auditStream);
            var auditSql = await auditReader.ReadToEndAsync();
            await using var auditConnection = await DataSource.OpenConnectionAsync();
            await using var auditCommand = new NpgsqlCommand(auditSql, auditConnection);
            await auditCommand.ExecuteNonQueryAsync();

            const string availabilityResourceName = "Mono.Api.Tests.Migrations.035_courier_availability_windows.sql";
            await using var availabilityStream = typeof(PostgresCourierOnboardingServiceTests).Assembly
                .GetManifestResourceStream(availabilityResourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {availabilityResourceName}.");
            using var availabilityReader = new StreamReader(availabilityStream);
            var availabilitySql = await availabilityReader.ReadToEndAsync();
            await using var availabilityConnection = await DataSource.OpenConnectionAsync();
            await using var availabilityCommand = new NpgsqlCommand(availabilitySql, availabilityConnection);
            await availabilityCommand.ExecuteNonQueryAsync();

            const string capacityAuditResourceName = "Mono.Api.Tests.Migrations.036_courier_planned_capacity_audit_action.sql";
            await using var capacityAuditStream = typeof(PostgresCourierOnboardingServiceTests).Assembly
                .GetManifestResourceStream(capacityAuditResourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {capacityAuditResourceName}.");
            using var capacityAuditReader = new StreamReader(capacityAuditStream);
            var capacityAuditSql = await capacityAuditReader.ReadToEndAsync();
            await using var capacityAuditConnection = await DataSource.OpenConnectionAsync();
            await using var capacityAuditCommand = new NpgsqlCommand(capacityAuditSql, capacityAuditConnection);
            await capacityAuditCommand.ExecuteNonQueryAsync();

            const string suspensionResourceName = "Mono.Api.Tests.Migrations.037_courier_operational_suspension.sql";
            await using var suspensionStream = typeof(PostgresCourierOnboardingServiceTests).Assembly
                .GetManifestResourceStream(suspensionResourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {suspensionResourceName}.");
            using var suspensionReader = new StreamReader(suspensionStream);
            var suspensionSql = await suspensionReader.ReadToEndAsync();
            await using var suspensionConnection = await DataSource.OpenConnectionAsync();
            await using var suspensionCommand = new NpgsqlCommand(suspensionSql, suspensionConnection);
            await suspensionCommand.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {_schema} CASCADE;", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
