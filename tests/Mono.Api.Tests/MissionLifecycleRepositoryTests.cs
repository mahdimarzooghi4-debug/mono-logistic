using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class MissionLifecycleRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Applies_ordered_transitions_once_and_blocks_cancellation_after_handoff()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "mission_lifecycle_" + Guid.NewGuid().ToString("N");
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema};", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            await CreatePreLifecycleSchemaAsync(dataSource);
            await ApplyLifecycleMigrationAsync(dataSource);
            await SeedMissionAsync(dataSource);

            var repository = new PostgresMissionLifecycleRepository(
                dataSource, new TestOperationsContext());

            var searching = await repository.ApplyAsync(
                MissionId, "mission.searching_courier", "Dispatch started", "transition-1");
            Assert.Equal(MissionLifecycleOutcome.Applied, searching.Outcome);
            Assert.Equal("SEARCHING_COURIER", searching.Transition!.Status);
            Assert.Equal(2, searching.Transition.Sequence);

            var replay = await repository.ApplyAsync(
                MissionId, "mission.searching_courier", "Dispatch started", "transition-1");
            Assert.Equal(MissionLifecycleOutcome.Replayed, replay.Outcome);
            Assert.Equal(searching.Transition, replay.Transition);

            var changedReplay = await repository.ApplyAsync(
                MissionId, "mission.searching_courier", "Changed reason", "transition-1");
            Assert.Equal(MissionLifecycleOutcome.Conflict, changedReplay.Outcome);

            var skippedEvent = await repository.ApplyAsync(
                MissionId, "mission.arrived_pickup", "Skip assignment", "transition-skip");
            Assert.Equal(MissionLifecycleOutcome.InvalidTransition, skippedEvent.Outcome);

            await ApplyAsync(repository, "mission.courier_assigned", "Courier selected", "transition-2", 3);
            await ApplyAsync(repository, "mission.arrived_pickup", "Courier at pickup", "transition-3", 4);
            await ApplyAsync(repository, "mission.handed_to_courier", "Goods handed over", "transition-4", 5);

            var lateCancellation = await repository.ApplyAsync(
                MissionId, "mission.cancelled", "Cancel after handoff", "transition-late-cancel");
            Assert.Equal(MissionLifecycleOutcome.InvalidTransition, lateCancellation.Outcome);

            await ApplyAsync(repository, "mission.picked_up", "Pickup confirmed", "transition-5", 6);
            await ApplyAsync(repository, "mission.in_transit", "Route started", "transition-6", 7);
            await ApplyAsync(repository, "mission.arrived_dropoff", "Courier at dropoff", "transition-7", 8);
            await ApplyAsync(repository, "mission.delivered", "Recipient confirmed", "transition-8", 9);

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var state = new NpgsqlCommand(
                "SELECT status FROM missions WHERE mission_id = @mission_id;", connection);
            state.Parameters.AddWithValue("mission_id", MissionId);
            Assert.Equal("DELIVERED", await state.ExecuteScalarAsync());

            await using var events = new NpgsqlCommand("""
                SELECT event_type, sequence
                FROM outbox_events
                WHERE customer_id = @customer_id AND aggregate_type = 'mission' AND aggregate_id = @mission_id
                ORDER BY sequence;
                """, connection);
            events.Parameters.AddWithValue("customer_id", CustomerId);
            events.Parameters.AddWithValue("mission_id", MissionId);
            await using var reader = await events.ExecuteReaderAsync();
            var rows = new List<(string EventType, long Sequence)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetInt64(1)));

            Assert.Equal(9, rows.Count);
            Assert.Equal(Enumerable.Range(1, 9).Select(value => (long)value), rows.Select(row => row.Sequence));
            Assert.Equal("mission.created", rows[0].EventType);
            Assert.Equal("mission.delivered", rows[^1].EventType);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE;", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }


    [Fact]
    public async Task Courier_offer_acceptance_assigns_mission_atomically_and_terminal_state_closes_offer()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "mission_courier_offer_" + Guid.NewGuid().ToString("N");
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema};", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            await CreatePreLifecycleSchemaAsync(dataSource);
            await ApplyLifecycleMigrationAsync(dataSource);
            await SeedMissionAsync(dataSource);
            await ApplyCourierMigrationsAsync(dataSource);
            await SeedActiveZoneAsync(dataSource, "zone-central");

            var context = new TestOperationsContext();
            var onboarding = new PostgresCourierOnboardingService(dataSource, context);
            var registered = await onboarding.RegisterAsync("BIKE", ["zone-central"], "offer-courier-register");
            var courierId = registered.Snapshot!.CourierId;
            var statuses = new[]
            {
                "IDENTITY_VERIFIED", "DOCUMENTS_SUBMITTED", "TRUST_REQUIREMENTS_VERIFIED",
                "MONO_APPROVED", "ACTIVE"
            };
            for (var index = 0; index < statuses.Length; index++)
            {
                var status = statuses[index];
                var transition = await onboarding.AdvanceTrustAsync(
                    courierId, status,
                    status == "ACTIVE" ? ["zone-central"] : null,
                    status == "ACTIVE" ? "zone-central" : null,
                    "REVIEW_COMPLETED", "mission-offer:" + status, "offer-trust-" + index);
                Assert.Equal(CourierOperationsWriteOutcome.Applied, transition.Outcome);
            }

            var presence = new PostgresCourierLivePresenceService(dataSource);
            Assert.Equal(CourierLivePresenceOutcome.Applied,
                (await presence.GoOnlineAsync(courierId, "zone-central", "offer-presence-online")).Outcome);

            var lifecycle = new PostgresMissionLifecycleRepository(dataSource, context);
            Assert.Equal(MissionLifecycleOutcome.Applied,
                (await lifecycle.ApplyAsync(MissionId, "mission.searching_courier",
                    "Dispatch initiated", "offer-searching")).Outcome);

            var offers = new PostgresMissionCourierOfferService(dataSource, context);
            var created = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create");
            Assert.Equal(MissionCourierOfferOutcome.Applied, created.Outcome);
            Assert.Equal("OFFERED", created.Offer!.Status);
            Assert.Single(await offers.ListMyOffersAsync(courierId));

            var replayedCreate = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, replayedCreate.Outcome);
            Assert.Equal(created.Offer, replayedCreate.Offer);
            var changedCreate = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-east", "mission-offer-create");
            Assert.Equal(MissionCourierOfferOutcome.Conflict, changedCreate.Outcome);

            var accepted = await offers.AcceptAsync(
                created.Offer.OfferId, courierId, "mission-offer-accept");
            Assert.Equal(MissionCourierOfferOutcome.Applied, accepted.Outcome);
            Assert.Equal("ACCEPTED", accepted.Offer!.Status);
            Assert.Equal("COURIER_ASSIGNED", accepted.MissionStatus);

            var acceptedReplay = await offers.AcceptAsync(
                created.Offer.OfferId, courierId, "mission-offer-accept");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, acceptedReplay.Outcome);
            Assert.Equal(accepted.Offer, acceptedReplay.Offer);

            await using (var connection = await dataSource.OpenConnectionAsync())
            {
                await using var state = new NpgsqlCommand(
                    "SELECT status FROM missions WHERE mission_id = @mission_id;", connection);
                state.Parameters.AddWithValue("mission_id", MissionId);
                Assert.Equal("COURIER_ASSIGNED", await state.ExecuteScalarAsync());

                await using var assignedEvent = new NpgsqlCommand("""
                    SELECT event_type, sequence, data->>'courier_id'
                    FROM outbox_events
                    WHERE aggregate_type = 'mission' AND aggregate_id = @mission_id
                    ORDER BY sequence DESC LIMIT 1;
                    """, connection);
                assignedEvent.Parameters.AddWithValue("mission_id", MissionId);
                await using var reader = await assignedEvent.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("mission.courier_assigned", reader.GetString(0));
                Assert.Equal(3L, reader.GetInt64(1));
                Assert.Equal(courierId, reader.GetString(2));
            }

            var cancelled = await lifecycle.ApplyAsync(
                MissionId, "mission.cancelled", "Cancellation before handoff", "offer-mission-cancel");
            Assert.Equal(MissionLifecycleOutcome.Applied, cancelled.Outcome);
            Assert.Equal("CANCELLED", (await offers.ListMyOffersAsync(courierId)).FirstOrDefault()?.Status ??
                (await GetOfferStatusAsync(dataSource, created.Offer.OfferId))!);
            Assert.Equal("CANCELLED", await GetOfferStatusAsync(dataSource, created.Offer.OfferId));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE;", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("PROMISED", "mission.cancelled", true)]
    [InlineData("ARRIVED_PICKUP", "mission.cancelled", true)]
    [InlineData("HANDED_TO_COURIER", "mission.cancelled", false)]
    [InlineData("DELIVERED", "mission.failed", false)]
    [InlineData("SEARCHING_COURIER", "mission.failed", true)]
    public void Enforces_handoff_and_terminal_transition_boundaries(
        string currentStatus, string eventType, bool expected)
    {
        Assert.Equal(
            expected,
            MissionLifecycleTransitions.TryGetNextStatus(currentStatus, eventType, out _));
    }

    private static async Task ApplyAsync(
        PostgresMissionLifecycleRepository repository,
        string eventType,
        string reason,
        string idempotencyKey,
        long expectedSequence)
    {
        var result = await repository.ApplyAsync(MissionId, eventType, reason, idempotencyKey);
        Assert.Equal(MissionLifecycleOutcome.Applied, result.Outcome);
        Assert.Equal(expectedSequence, result.Transition!.Sequence);
    }


    private static async Task ApplyCourierMigrationsAsync(NpgsqlDataSource dataSource)
    {
        const string createZoneSql = "CREATE TABLE zone_geometry_active (zone_id text PRIMARY KEY);";
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var createZone = new NpgsqlCommand(createZoneSql, connection))
            await createZone.ExecuteNonQueryAsync();

        for (var number = 33; number <= 41; number++)
        {
            var resourceName = $"Mono.Api.Tests.Migrations.{number:D3}_{number switch
            {
                33 => "courier_onboarding",
                34 => "courier_operations_read_audit",
                35 => "courier_availability_windows",
                36 => "courier_planned_capacity_audit_action",
                37 => "courier_operational_suspension",
                38 => "courier_planning_eligibility_audit_action",
                39 => "courier_identity_bindings",
                40 => "courier_live_presence",
                41 => "mission_courier_offers",
                _ => throw new InvalidOperationException()
            }}.sql";
            await using var stream = typeof(MissionLifecycleRepositoryTests).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {resourceName}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedActiveZoneAsync(NpgsqlDataSource dataSource, string zoneId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO zone_geometry_active (zone_id) VALUES (@zone_id);", connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> GetOfferStatusAsync(NpgsqlDataSource dataSource, string offerId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status FROM mission_courier_offers WHERE offer_id = @offer_id;", connection);
        command.Parameters.AddWithValue("offer_id", offerId);
        return (string?)await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Offer not found.");
    }

    private const string CustomerId = "cus_0123456789abcdef0123456789abcdef";
    private const string MissionId = "mis_0123456789abcdef0123456789abcdef";

    private static async Task CreatePreLifecycleSchemaAsync(NpgsqlDataSource dataSource)
    {
        const string sql = """
            CREATE TABLE customers (
                customer_id text PRIMARY KEY);
            CREATE TABLE missions (
                mission_id text PRIMARY KEY,
                customer_id text NOT NULL REFERENCES customers(customer_id),
                delivery_request_id text NOT NULL,
                status text NOT NULL CONSTRAINT missions_status_check CHECK (status = 'PROMISED'));
            CREATE TABLE outbox_events (
                event_id text PRIMARY KEY,
                event_type text NOT NULL CONSTRAINT outbox_events_event_type_check
                    CHECK (event_type = 'mission.created'),
                event_version integer NOT NULL,
                occurred_at timestamp with time zone NOT NULL,
                producer text NOT NULL,
                correlation_id text NOT NULL,
                causation_id text NULL,
                customer_id text NOT NULL REFERENCES customers(customer_id),
                aggregate_type text NOT NULL,
                aggregate_id text NOT NULL,
                sequence bigint NOT NULL,
                data jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyLifecycleMigrationAsync(NpgsqlDataSource dataSource)
    {
        const string resourceName = "Mono.Api.Tests.Migrations.032_mission_lifecycle.sql";
        await using var stream = typeof(MissionLifecycleRepositoryTests).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded migration resource {resourceName}.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedMissionAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var customer = new NpgsqlCommand(
            "INSERT INTO customers (customer_id) VALUES (@customer_id);", connection))
        {
            customer.Parameters.AddWithValue("customer_id", CustomerId);
            await customer.ExecuteNonQueryAsync();
        }

        await using (var mission = new NpgsqlCommand("""
            INSERT INTO missions (mission_id, customer_id, delivery_request_id, status)
            VALUES (@mission_id, @customer_id, 'drq_0123456789abcdef0123456789abcdef', 'PROMISED');
            """, connection))
        {
            mission.Parameters.AddWithValue("mission_id", MissionId);
            mission.Parameters.AddWithValue("customer_id", CustomerId);
            await mission.ExecuteNonQueryAsync();
        }

        await using var createdEvent = new NpgsqlCommand("""
            INSERT INTO outbox_events (
                event_id, event_type, event_version, occurred_at, producer, correlation_id,
                causation_id, customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
            VALUES (
                'evt_0123456789abcdef0123456789abcdef', 'mission.created', 1, now(), 'test',
                'drq_0123456789abcdef0123456789abcdef', NULL, @customer_id, 'mission',
                @mission_id, 1, '{}'::jsonb, now());
            """, connection);
        createdEvent.Parameters.AddWithValue("customer_id", CustomerId);
        createdEvent.Parameters.AddWithValue("mission_id", MissionId);
        await createdEvent.ExecuteNonQueryAsync();
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "mission-lifecycle-test-admin";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
