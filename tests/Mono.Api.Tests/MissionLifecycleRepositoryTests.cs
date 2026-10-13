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

            const string secondaryMissionId = "mis_11111111111111111111111111111111";
            await using (var seedSecondary = await dataSource.OpenConnectionAsync())
            await using (var insertSecondary = new NpgsqlCommand("""
                INSERT INTO missions (
                    mission_id, customer_id, delivery_request_id, committed_delivery_at, created_at, status)
                VALUES (
                    @mission_id, @customer_id, 'drq_11111111111111111111111111111111',
                    now() + interval '1 day', now() + interval '1 minute', 'SEARCHING_COURIER');
                """, seedSecondary))
            {
                insertSecondary.Parameters.AddWithValue("mission_id", secondaryMissionId);
                insertSecondary.Parameters.AddWithValue("customer_id", CustomerId);
                await insertSecondary.ExecuteNonQueryAsync();
            }

            var dispatchClock = new AdjustableTimeProvider(DateTimeOffset.UtcNow.AddDays(-1));
            var dispatchQueue = new PostgresMissionDispatchQueueService(dataSource, dispatchClock);
            var dueFirstPage = await dispatchQueue.ListAsync(1, null, null, null, sortBy: "delivery_due");
            Assert.Equal(MissionId, Assert.Single(dueFirstPage!.Items).MissionId);
            Assert.Equal(MissionId, dueFirstPage.NextCursor);
            var dueSecondPage = await dispatchQueue.ListAsync(
                1, null, null, dueFirstPage.NextCursor, sortBy: "delivery_due");
            Assert.Equal(secondaryMissionId, Assert.Single(dueSecondPage!.Items).MissionId);
            Assert.Null(dueSecondPage.NextCursor);
            var upcomingFirstPage = await dispatchQueue.ListAsync(
                1, null, null, null, deliveryWindowState: "upcoming");
            Assert.Equal(secondaryMissionId, Assert.Single(upcomingFirstPage!.Items).MissionId);
            var upcomingSecondPage = await dispatchQueue.ListAsync(
                1, null, null, upcomingFirstPage.NextCursor, deliveryWindowState: "upcoming");
            Assert.Equal(MissionId, Assert.Single(upcomingSecondPage!.Items).MissionId);
            Assert.Null(await dispatchQueue.ListAsync(
                1, null, null, upcomingFirstPage.NextCursor, deliveryWindowState: "overdue"));
            var defaultFirstPage = await dispatchQueue.ListAsync(1, null, null, null);
            Assert.Equal(secondaryMissionId, Assert.Single(defaultFirstPage!.Items).MissionId);

            DateTimeOffset primaryDeliveryAt;
            await using (var readDue = await dataSource.OpenConnectionAsync())
            await using (var readDueCommand = new NpgsqlCommand(
                "SELECT committed_delivery_at FROM missions WHERE mission_id = @mission_id;", readDue))
            {
                readDueCommand.Parameters.AddWithValue("mission_id", MissionId);
                var primaryDeliveryValue = (DateTime)(await readDueCommand.ExecuteScalarAsync())!;
                primaryDeliveryAt = new DateTimeOffset(DateTime.SpecifyKind(primaryDeliveryValue, DateTimeKind.Utc));
            }
            var deliveryWindow = await dispatchQueue.ListAsync(
                10, null, null, null, deliveryAfter: primaryDeliveryAt);
            Assert.Equal(2, deliveryWindow!.Items.Count);
            var exactLowerBound = await dispatchQueue.ListAsync(
                10, null, null, null, deliveryAfter: deliveryWindow.Items[0].CommittedDeliveryAt);
            Assert.Equal(secondaryMissionId, Assert.Single(exactLowerBound!.Items).MissionId);
            var noDeliveryAfterUpperBound = await dispatchQueue.ListAsync(
                10, null, null, null, deliveryAfter: deliveryWindow.Items[0].CommittedDeliveryAt.AddMilliseconds(1));
            Assert.Empty(noDeliveryAfterUpperBound!.Items);
            var windowFirstPage = await dispatchQueue.ListAsync(
                1, null, null, null, deliveryAfter: primaryDeliveryAt);
            var cursorRejectedByChangedDeliveryWindow = await dispatchQueue.ListAsync(
                1, null, null, windowFirstPage!.NextCursor,
                deliveryAfter: deliveryWindow.Items[0].CommittedDeliveryAt.AddMilliseconds(1));
            Assert.Null(cursorRejectedByChangedDeliveryWindow);
            await Assert.ThrowsAsync<ArgumentException>(() => dispatchQueue.ListAsync(
                10, null, null, null,
                deliveryBefore: primaryDeliveryAt.AddTicks(-1), deliveryAfter: primaryDeliveryAt));

            await using (var removeSecondary = await dataSource.OpenConnectionAsync())
            await using (var deleteSecondary = new NpgsqlCommand(
                "DELETE FROM missions WHERE mission_id = @mission_id;", removeSecondary))
            {
                deleteSecondary.Parameters.AddWithValue("mission_id", secondaryMissionId);
                await deleteSecondary.ExecuteNonQueryAsync();
            }

            var dispatchAudit = new PostgresMissionOperationsReadAuditService(dataSource, context);
            var pendingBeforeOffer = await dispatchQueue.ListAsync(20, false, null, null);
            Assert.Single(pendingBeforeOffer!.Items);
            Assert.Null(pendingBeforeOffer.Items[0].ActiveOffer);
            Assert.Equal(0, pendingBeforeOffer.Items[0].AttemptCount);
            Assert.Null(pendingBeforeOffer.Items[0].LatestAttempt);
            Assert.False(pendingBeforeOffer.Items[0].DeliveryOverdue);
            Assert.Equal("upcoming", pendingBeforeOffer.Items[0].DeliveryWindowState);
            Assert.Single((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryWindowState: "upcoming"))!.Items);
            Assert.Null(await dispatchQueue.ListAsync(
                20, null, null, MissionId, deliveryWindowState: "due_now"));
            await Assert.ThrowsAsync<ArgumentException>(() => dispatchQueue.ListAsync(
                20, null, null, null, deliveryWindowState: "unknown"));
            var committedDeliveryCutoff = pendingBeforeOffer.Items[0].CommittedDeliveryAt;
            dispatchClock.Set(committedDeliveryCutoff.AddMinutes(-5));
            var dueWithinFiveMinutes = await dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 5);
            Assert.Single(dueWithinFiveMinutes!.Items);
            var outsideFourMinuteHorizon = await dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 4);
            Assert.Empty(outsideFourMinuteHorizon!.Items);
            Assert.Null(await dispatchQueue.ListAsync(
                20, null, null, MissionId, deliveryDueWithinMinutes: 4));
            await Assert.ThrowsAsync<ArgumentException>(() => dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 0));
            await Assert.ThrowsAsync<ArgumentException>(() => dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 10081));
            dispatchClock.Set(committedDeliveryCutoff);
            Assert.Single((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 1))!.Items);
            dispatchClock.Set(committedDeliveryCutoff.AddMilliseconds(1));
            Assert.Empty((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryDueWithinMinutes: 5))!.Items);
            dispatchClock.Set(committedDeliveryCutoff.AddMilliseconds(-1));
            var nonOverdueQueue = await dispatchQueue.ListAsync(
                20, null, null, null, deliveryOverdue: false);
            Assert.Single(nonOverdueQueue!.Items);
            dispatchClock.Set(committedDeliveryCutoff);
            var overdueAtExactDeadline = await dispatchQueue.ListAsync(
                20, null, null, null, deliveryOverdue: true);
            Assert.Single(overdueAtExactDeadline!.Items);
            Assert.True(overdueAtExactDeadline.Items[0].DeliveryOverdue);
            Assert.Equal("due_now", overdueAtExactDeadline.Items[0].DeliveryWindowState);
            Assert.Single((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryWindowState: "due_now"))!.Items);
            Assert.Empty((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryWindowState: "upcoming"))!.Items);
            dispatchClock.Set(committedDeliveryCutoff.AddMilliseconds(1));
            var overdueAfterDeadline = await dispatchQueue.ListAsync(
                20, null, null, null, deliveryOverdue: true);
            Assert.Equal("overdue", Assert.Single(overdueAfterDeadline!.Items).DeliveryWindowState);
            Assert.Single((await dispatchQueue.ListAsync(
                20, null, null, null, deliveryWindowState: "overdue"))!.Items);
            dispatchClock.Set(committedDeliveryCutoff.AddMilliseconds(-1));
            var cursorRejectedByDeliveryOverdueFilter = await dispatchQueue.ListAsync(
                20, null, null, MissionId, deliveryOverdue: true);
            Assert.Null(cursorRejectedByDeliveryOverdueFilter);
            var noAttemptMatchesRecencyCutoff = await dispatchQueue.ListAsync(
                20, null, null, null, lastAttemptBefore: DateTimeOffset.UtcNow.AddDays(1));
            Assert.Empty(noAttemptMatchesRecencyCutoff!.Items);
            await dispatchAudit.RecordAsync(
                PostgresMissionOperationsReadAuditService.MissionDispatchQueue,
                null, pendingBeforeOffer.Items.Count);

            var offerClock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
            var offers = new PostgresMissionCourierOfferService(dataSource, context, offerClock);
            var duplicateMissionBatch = new[]
            {
                new MissionCourierOfferBatchCommand(
                    MissionId, courierId, "zone-central", "batch-offer-duplicate-1"),
                new MissionCourierOfferBatchCommand(
                    MissionId, courierId, "zone-central", "batch-offer-duplicate-2")
            };
            await Assert.ThrowsAsync<ArgumentException>(
                () => offers.CreateBatchAsync(duplicateMissionBatch));
            Assert.Empty(await offers.ListForMissionAsync(MissionId));

            var offerCommands = new[]
            {
                new MissionCourierOfferBatchCommand(
                    MissionId, courierId, "zone-central", "mission-offer-create")
            };
            var createdBatch = await offers.CreateBatchAsync(offerCommands);
            var createdBatchItem = Assert.Single(createdBatch);
            Assert.Equal("Applied", createdBatchItem.Outcome);
            var createdBatchReplay = await offers.CreateBatchAsync(offerCommands);
            Assert.Equal("Replayed", Assert.Single(createdBatchReplay).Outcome);
            var created = new MissionCourierOfferResult(
                Enum.Parse<MissionCourierOfferOutcome>(createdBatchItem.Outcome),
                createdBatchItem.Offer);
            Assert.Equal(MissionCourierOfferOutcome.Applied, created.Outcome);
            Assert.Equal("OFFERED", created.Offer!.Status);
            Assert.Single(await offers.ListMyOffersAsync(courierId));
            var operationsQueue = await offers.ListForMissionAsync(MissionId);
            Assert.Single(operationsQueue);
            Assert.Equal(created.Offer.OfferId, operationsQueue[0].OfferId);
            Assert.Equal("OFFERED", operationsQueue[0].Status);

            var pendingWithOffer = await dispatchQueue.ListAsync(20, true, MissionId, null);
            Assert.Single(pendingWithOffer!.Items);
            Assert.Equal(created.Offer.OfferId, pendingWithOffer.Items[0].ActiveOffer!.OfferId);
            Assert.Equal("OFFERED", pendingWithOffer.Items[0].ActiveOffer!.Status);
            Assert.Equal(created.Offer.ExpiresAt, pendingWithOffer.Items[0].ActiveOffer.ExpiresAt);
            Assert.False(pendingWithOffer.Items[0].ActiveOffer.DeadlinePassed);
            var committedDeliveryAt = pendingWithOffer.Items[0].CommittedDeliveryAt;
            var dueByCommittedDelivery = await dispatchQueue.ListAsync(
                20, true, MissionId, null, deliveryBefore: committedDeliveryAt);
            Assert.Single(dueByCommittedDelivery!.Items);
            var beforeCommittedDelivery = await dispatchQueue.ListAsync(
                20, true, MissionId, null, deliveryBefore: committedDeliveryAt.AddTicks(-1));
            Assert.Empty(beforeCommittedDelivery!.Items);
            var lastAttemptAt = pendingWithOffer.Items[0].LatestAttempt!.LastOccurredAt!.Value;
            var attemptedByExactTime = await dispatchQueue.ListAsync(
                20, null, null, null, lastAttemptBefore: lastAttemptAt);
            Assert.Single(attemptedByExactTime!.Items);
            var attemptedBeforeLastEvent = await dispatchQueue.ListAsync(
                20, null, null, null, lastAttemptBefore: lastAttemptAt.AddTicks(-1));
            Assert.Empty(attemptedBeforeLastEvent!.Items);
            var atLeastOneAttempt = await dispatchQueue.ListAsync(
                20, null, null, null, minAttemptCount: 1);
            Assert.Single(atLeastOneAttempt!.Items);
            var atLeastTwoAttempts = await dispatchQueue.ListAsync(
                20, null, null, null, minAttemptCount: 2);
            Assert.Empty(atLeastTwoAttempts!.Items);
            var cursorRejectedByAttemptFilter = await dispatchQueue.ListAsync(
                20, null, null, MissionId, minAttemptCount: 2);
            Assert.Null(cursorRejectedByAttemptFilter);
            var atMostOneAttempt = await dispatchQueue.ListAsync(
                20, null, null, null, maxAttemptCount: 1);
            Assert.Single(atMostOneAttempt!.Items);
            var atMostZeroAttempts = await dispatchQueue.ListAsync(
                20, null, null, null, maxAttemptCount: 0);
            Assert.Empty(atMostZeroAttempts!.Items);
            await Assert.ThrowsAsync<ArgumentException>(
                () => dispatchQueue.ListAsync(20, null, null, null, minAttemptCount: 2, maxAttemptCount: 1));
            var notOverdueOnly = await dispatchQueue.ListAsync(
                20, true, MissionId, null, deadlinePassed: false);
            Assert.Single(notOverdueOnly!.Items);
            var overdueOnlyBeforeDeadline = await dispatchQueue.ListAsync(
                20, true, MissionId, null, deadlinePassed: true);
            Assert.Empty(overdueOnlyBeforeDeadline!.Items);
            Assert.Equal(1, pendingWithOffer.Items[0].AttemptCount);
            Assert.Equal("OFFERED", pendingWithOffer.Items[0].LatestAttempt!.Status);
            Assert.Equal("OFFERED", pendingWithOffer.Items[0].LatestAttempt.LastAction);
            var offeredOutcomeOnly = await dispatchQueue.ListAsync(
                20, null, null, null, latestAction: "OFFERED");
            Assert.Single(offeredOutcomeOnly!.Items);
            await Assert.ThrowsAsync<ArgumentException>(
                () => dispatchQueue.ListAsync(20, null, null, null, latestAction: "NOT_AN_ACTION"));
            await dispatchAudit.RecordAsync(
                PostgresMissionOperationsReadAuditService.MissionDispatchQueue,
                null, pendingWithOffer.Items.Count);
            Assert.Empty((await dispatchQueue.ListAsync(20, false, null, null))!.Items);

            var replayedCreate = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, replayedCreate.Outcome);
            Assert.Equal(created.Offer, replayedCreate.Offer);
            var changedCreate = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-east", "mission-offer-create");
            Assert.Equal(MissionCourierOfferOutcome.Conflict, changedCreate.Outcome);

            var withdrawn = await offers.WithdrawAsync(
                MissionId, created.Offer.OfferId, "mission-offer-withdraw");
            Assert.Equal(MissionCourierOfferOutcome.Applied, withdrawn.Outcome);
            Assert.Equal("WITHDRAWN", withdrawn.Offer!.Status);
            Assert.Equal("SEARCHING_COURIER", withdrawn.MissionStatus);
            var withdrawalReplay = await offers.WithdrawAsync(
                MissionId, created.Offer.OfferId, "mission-offer-withdraw");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, withdrawalReplay.Outcome);
            Assert.Equal(withdrawn.Offer, withdrawalReplay.Offer);
            Assert.Equal("WITHDRAWN", await GetOfferStatusAsync(dataSource, created.Offer.OfferId));
            var history = await offers.ListOfferEventsAsync(MissionId, created.Offer.OfferId);
            Assert.NotNull(history);
            Assert.Equal(new[] { "OFFERED", "WITHDRAWN" }, history!.Select(item => item.Action));
            var readAudit = new PostgresMissionOperationsReadAuditService(dataSource, context);
            await readAudit.RecordAsync(
                PostgresMissionOperationsReadAuditService.MissionCourierOfferHistory,
                MissionId, history.Count);
            await using (var auditConnection = await dataSource.OpenConnectionAsync())
            await using (var auditCommand = new NpgsqlCommand("""
                SELECT action, mission_id, result_count FROM mission_operations_read_audit
                ORDER BY occurred_at DESC, audit_id DESC LIMIT 1;
                """, auditConnection))
            await using (var auditReader = await auditCommand.ExecuteReaderAsync())
            {
                Assert.True(await auditReader.ReadAsync());
                Assert.Equal(PostgresMissionOperationsReadAuditService.MissionCourierOfferHistory, auditReader.GetString(0));
                Assert.Equal(MissionId, auditReader.GetString(1));
                Assert.Equal(2, auditReader.GetInt32(2));
            }

            var pendingAfterWithdrawal = await dispatchQueue.ListAsync(20, false, MissionId, null);
            Assert.Single(pendingAfterWithdrawal!.Items);
            Assert.Equal(1, pendingAfterWithdrawal.Items[0].AttemptCount);
            Assert.Equal("WITHDRAWN", pendingAfterWithdrawal.Items[0].LatestAttempt!.Status);
            Assert.Equal("OPERATIONS_WITHDREW_OFFER", pendingAfterWithdrawal.Items[0].LatestAttempt.LastReason);

            var replacementOffer = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create-replacement");
            Assert.Equal(MissionCourierOfferOutcome.Applied, replacementOffer.Outcome);
            Assert.Equal("OFFERED", replacementOffer.Offer!.Status);

            var suspension = new PostgresCourierSuspensionService(dataSource, context);
            var suspended = await suspension.SuspendAsync(courierId, "TEST_SUSPENSION", "offer-courier-suspend");
            Assert.Equal(CourierOperationalStatusOutcome.Applied, suspended.Outcome);
            Assert.Equal("WITHDRAWN", await GetOfferStatusAsync(dataSource, replacementOffer.Offer.OfferId));
            Assert.Contains(suspended.Snapshot!.History, item => item.Action == "SUSPENDED");

            var reinstated = await suspension.ReinstateAsync(courierId, "TEST_REINSTATEMENT", "offer-courier-reinstate");
            Assert.Equal(CourierOperationalStatusOutcome.Applied, reinstated.Outcome);
            var finalOffer = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create-after-reinstate");
            Assert.Equal(MissionCourierOfferOutcome.Applied, finalOffer.Outcome);
            Assert.Equal("OFFERED", finalOffer.Offer!.Status);

            var accepted = await offers.AcceptAsync(
                finalOffer.Offer.OfferId, courierId, "mission-offer-accept");
            Assert.Equal(MissionCourierOfferOutcome.Applied, accepted.Outcome);
            Assert.Equal("ACCEPTED", accepted.Offer!.Status);
            Assert.Equal("COURIER_ASSIGNED", accepted.MissionStatus);

            var acceptedReplay = await offers.AcceptAsync(
                finalOffer.Offer.OfferId, courierId, "mission-offer-accept");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, acceptedReplay.Outcome);
            Assert.Equal(accepted.Offer, acceptedReplay.Offer);

            var issues = new PostgresMissionCourierIssueService(dataSource, context);
            var wrongCourierReport = await issues.ReportAsync(
                "crr_11111111111111111111111111111111", MissionId, "SAFETY_CONCERN", "wrong-courier-issue");
            Assert.Equal(MissionCourierIssueOutcome.InvalidState, wrongCourierReport.Outcome);
            var issue = await issues.ReportAsync(
                courierId, MissionId, "SAFETY_CONCERN", "courier-issue-report");
            Assert.Equal(MissionCourierIssueOutcome.Applied, issue.Outcome);
            Assert.Equal("OPEN", issue.Report!.Status);
            var issueReplay = await issues.ReportAsync(
                courierId, MissionId, "SAFETY_CONCERN", "courier-issue-report");
            Assert.Equal(MissionCourierIssueOutcome.Replayed, issueReplay.Outcome);
            Assert.Equal(issue.Report, issueReplay.Report);
            var changedIssueReplay = await issues.ReportAsync(
                courierId, MissionId, "OTHER", "courier-issue-report");
            Assert.Equal(MissionCourierIssueOutcome.Conflict, changedIssueReplay.Outcome);
            var issueQueue = await issues.ListForMissionAsync(MissionId);
            Assert.Single(issueQueue);
            var issueReadAudit = new PostgresMissionOperationsReadAuditService(dataSource, context);
            await issueReadAudit.RecordAsync(
                PostgresMissionOperationsReadAuditService.MissionCourierIssueSearch,
                MissionId, issueQueue.Count);
            var resolvedIssue = await issues.ResolveAsync(
                MissionId, issue.Report.ReportId, "operations-issue-resolve");
            Assert.Equal(MissionCourierIssueOutcome.Applied, resolvedIssue.Outcome);
            Assert.Equal("RESOLVED", resolvedIssue.Report!.Status);
            var resolveReplay = await issues.ResolveAsync(
                MissionId, issue.Report.ReportId, "operations-issue-resolve");
            Assert.Equal(MissionCourierIssueOutcome.Replayed, resolveReplay.Outcome);
            Assert.Equal(resolvedIssue.Report, resolveReplay.Report);
            Assert.Equal("RESOLVED", (await issues.ListForMissionAsync(MissionId)).Single().Status);

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

            var released = await offers.ReleaseAssignmentAsync(
                MissionId, finalOffer.Offer.OfferId, "release-before-handoff");
            Assert.Equal(MissionCourierOfferOutcome.Applied, released.Outcome);
            Assert.Equal("RELEASED", released.Offer!.Status);
            Assert.Equal("SEARCHING_COURIER", released.MissionStatus);
            var releaseReplay = await offers.ReleaseAssignmentAsync(
                MissionId, finalOffer.Offer.OfferId, "release-before-handoff");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, releaseReplay.Outcome);
            Assert.Equal(released.Offer, releaseReplay.Offer);
            Assert.Equal("SEARCHING_COURIER", await GetMissionStatusAsync(dataSource));

            var expiringOffer = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create-expiring");
            Assert.Equal(MissionCourierOfferOutcome.Applied, expiringOffer.Outcome);
            Assert.NotNull(expiringOffer.Offer!.ExpiresAt);
            var earlyExpiry = await offers.ExpireAsync(
                MissionId, expiringOffer.Offer.OfferId, "expire-overdue-offer");
            Assert.Equal(MissionCourierOfferOutcome.InvalidState, earlyExpiry.Outcome);
            offerClock.Advance(TimeSpan.FromMinutes(6));
            var overdueQueue = await new PostgresMissionDispatchQueueService(dataSource, offerClock)
                .ListAsync(20, true, MissionId, null);
            Assert.Single(overdueQueue!.Items);
            Assert.Equal("OFFERED", overdueQueue.Items[0].ActiveOffer!.Status);
            Assert.True(overdueQueue.Items[0].ActiveOffer.DeadlinePassed);
            var overdueOnly = await new PostgresMissionDispatchQueueService(dataSource, offerClock)
                .ListAsync(20, true, MissionId, null, deadlinePassed: true);
            Assert.Single(overdueOnly!.Items);
            var notOverdueOnlyAfterDeadline = await new PostgresMissionDispatchQueueService(dataSource, offerClock)
                .ListAsync(20, true, MissionId, null, deadlinePassed: false);
            Assert.Empty(notOverdueOnlyAfterDeadline!.Items);
            Assert.True(overdueQueue.Items[0].ActiveOffer.ExpiresAt!.Value <= offerClock.GetUtcNow());


            var duplicateExpiryBatch = new[]
            {
                new MissionCourierOfferExpiryCommand(
                    MissionId, expiringOffer.Offer.OfferId, "expire-overdue-offer"),
                new MissionCourierOfferExpiryCommand(
                    MissionId, expiringOffer.Offer.OfferId, "duplicate-expiry-key")
            };
            await Assert.ThrowsAsync<ArgumentException>(
                () => offers.ExpireBatchAsync(duplicateExpiryBatch));
            Assert.Equal("OFFERED", await GetOfferStatusAsync(dataSource, expiringOffer.Offer.OfferId));

            var expiryCommands = new[]
            {
                new MissionCourierOfferExpiryCommand(
                    MissionId, expiringOffer.Offer.OfferId, "expire-overdue-offer"),
                new MissionCourierOfferExpiryCommand(
                    MissionId, "mco_22222222222222222222222222222222", "expire-missing-offer")
            };
            var expiryBatch = await offers.ExpireBatchAsync(expiryCommands);
            var expiredOffer = expiryBatch[0];
            Assert.Equal("Applied", expiredOffer.Outcome);
            Assert.Equal("OfferNotFound", expiryBatch[1].Outcome);
            Assert.Null(expiryBatch[1].Offer);
            Assert.Equal("EXPIRED", expiredOffer.Offer!.Status);
            Assert.Equal("SEARCHING_COURIER", expiredOffer.MissionStatus);
            var expiryBatchReplay = await offers.ExpireBatchAsync(expiryCommands);
            Assert.Equal("Replayed", expiryBatchReplay[0].Outcome);
            Assert.Equal("OfferNotFound", expiryBatchReplay[1].Outcome);
            var expiryReplay = await offers.ExpireAsync(
                MissionId, expiringOffer.Offer.OfferId, "expire-overdue-offer");
            Assert.Equal(MissionCourierOfferOutcome.Replayed, expiryReplay.Outcome);
            Assert.Equal(expiredOffer.Offer, expiryReplay.Offer);
            var attemptHistoryService = new PostgresMissionDispatchAttemptHistoryService(dataSource);
            var attemptHistory = await attemptHistoryService.FindAsync(MissionId);
            Assert.NotNull(attemptHistory);
            Assert.Equal(4, attemptHistory.Attempts.Count);
            var expiredAttempt = Assert.Single(attemptHistory.Attempts,
                item => item.OfferId == expiringOffer.Offer.OfferId);
            Assert.Equal("EXPIRED", expiredAttempt.Status);
            Assert.Equal("EXPIRED", expiredAttempt.LastAction);
            var expiredOutcomeOnly = await new PostgresMissionDispatchQueueService(dataSource, offerClock)
                .ListAsync(20, null, null, null, latestAction: "EXPIRED");
            Assert.Single(expiredOutcomeOnly!.Items);
            Assert.Equal("EXPIRED", expiredOutcomeOnly.Items[0].LatestAttempt!.LastAction);
            var declinedOutcomeOnly = await dispatchQueue.ListAsync(
                20, null, null, null, latestAction: "DECLINED");
            Assert.Empty(declinedOutcomeOnly!.Items);
            Assert.Equal("OPERATIONS_EXPIRED_OFFER", expiredAttempt.LastReason);
            Assert.NotNull(expiredAttempt.LastOccurredAt);
            var withdrawnAttempt = Assert.Single(attemptHistory.Attempts,
                item => item.OfferId == created.Offer.OfferId);
            Assert.Equal("OPERATIONS_WITHDREW_OFFER", withdrawnAttempt.LastReason);
            var suspendedAttempt = Assert.Single(attemptHistory.Attempts,
                item => item.OfferId == replacementOffer.Offer!.OfferId);
            Assert.Equal("COURIER_SUSPENDED", suspendedAttempt.LastReason);
            var releasedAttempt = Assert.Single(attemptHistory.Attempts,
                item => item.OfferId == finalOffer.Offer!.OfferId);
            Assert.Equal("RELEASED", releasedAttempt.Status);
            Assert.Equal("OPERATIONS_REASSIGNED_COURIER", releasedAttempt.LastReason);

            var attemptReadAudit = new PostgresMissionOperationsReadAuditService(dataSource, context);
            await attemptReadAudit.RecordAsync(
                PostgresMissionOperationsReadAuditService.MissionDispatchAttemptHistory,
                MissionId, attemptHistory.Attempts.Count);
            await using (var auditConnection = await dataSource.OpenConnectionAsync())
            await using (var auditCommand = new NpgsqlCommand("""
                SELECT action, mission_id, result_count FROM mission_operations_read_audit
                ORDER BY occurred_at DESC, audit_id DESC LIMIT 1;
                """, auditConnection))
            await using (var auditReader = await auditCommand.ExecuteReaderAsync())
            {
                Assert.True(await auditReader.ReadAsync());
                Assert.Equal(PostgresMissionOperationsReadAuditService.MissionDispatchAttemptHistory, auditReader.GetString(0));
                Assert.Equal(MissionId, auditReader.GetString(1));
                Assert.Equal(4, auditReader.GetInt32(2));
            }

            offerClock.Set(DateTimeOffset.UtcNow);

            var reassignedOffer = await offers.CreateOfferAsync(
                MissionId, courierId, "zone-central", "mission-offer-create-reassigned");
            Assert.Equal(MissionCourierOfferOutcome.Applied, reassignedOffer.Outcome);
            var reassignedAcceptance = await offers.AcceptAsync(
                reassignedOffer.Offer!.OfferId, courierId, "mission-offer-accept-reassigned");
            Assert.Equal(MissionCourierOfferOutcome.Applied, reassignedAcceptance.Outcome);
            Assert.Equal("COURIER_ASSIGNED", reassignedAcceptance.MissionStatus);

            var unauthorizedCourier = await lifecycle.ApplyForCourierAsync(
                "crr_11111111111111111111111111111111", MissionId,
                "mission.arrived_pickup", "Wrong courier", "wrong-courier-progress");
            Assert.Equal(MissionLifecycleOutcome.MissionNotFound, unauthorizedCourier.Outcome);

            var progressEvents = new[]
            {
                ("mission.arrived_pickup", "ARRIVED_PICKUP"),
                ("mission.handed_to_courier", "HANDED_TO_COURIER"),
                ("mission.picked_up", "PICKED_UP"),
                ("mission.in_transit", "IN_TRANSIT"),
                ("mission.arrived_dropoff", "ARRIVED_DROPOFF"),
                ("mission.delivered", "DELIVERED")
            };
            for (var index = 0; index < progressEvents.Length; index++)
            {
                var (eventType, expectedStatus) = progressEvents[index];
                var progress = await lifecycle.ApplyForCourierAsync(
                    courierId, MissionId, eventType, "Courier milestone " + index, "courier-progress-" + index);
                Assert.Equal(MissionLifecycleOutcome.Applied, progress.Outcome);
                Assert.Equal(expectedStatus, progress.Transition!.Status);
                if (index == 1)
                {
                    var tooLateRelease = await offers.ReleaseAssignmentAsync(
                        MissionId, reassignedOffer.Offer.OfferId, "release-after-handoff");
                    Assert.Equal(MissionCourierOfferOutcome.InvalidState, tooLateRelease.Outcome);
                }
                if (index == 0)
                {
                    var replayedProgress = await lifecycle.ApplyForCourierAsync(
                        courierId, MissionId, eventType, "Courier milestone " + index, "courier-progress-" + index);
                    Assert.Equal(MissionLifecycleOutcome.Replayed, replayedProgress.Outcome);
                    Assert.Equal(progress.Transition, replayedProgress.Transition);
                }
            }
            Assert.Equal("DELIVERED", await GetMissionStatusAsync(dataSource));
            Assert.Equal("WITHDRAWN", await GetOfferStatusAsync(dataSource, created.Offer.OfferId));
            Assert.Equal("WITHDRAWN", await GetOfferStatusAsync(dataSource, replacementOffer.Offer.OfferId));
            Assert.Equal("RELEASED", await GetOfferStatusAsync(dataSource, finalOffer.Offer.OfferId));
            Assert.Equal("EXPIRED", await GetOfferStatusAsync(dataSource, expiringOffer.Offer.OfferId));
            Assert.Equal("COMPLETED", await GetOfferStatusAsync(dataSource, reassignedOffer.Offer.OfferId));
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

        var migrationNames = new[]
        {
            "024_mission_operations_read_audit.sql",
            "025_mission_operations_read_audit_inspection.sql",
            "033_courier_onboarding.sql",
            "034_courier_operations_read_audit.sql",
            "035_courier_availability_windows.sql",
            "036_courier_planned_capacity_audit_action.sql",
            "037_courier_operational_suspension.sql",
            "038_courier_planning_eligibility_audit_action.sql",
            "039_courier_identity_bindings.sql",
            "040_courier_live_presence.sql",
            "041_mission_courier_offers.sql",
            "042_mission_courier_offer_withdrawal.sql",
            "043_courier_suspension_withdraws_offers.sql",
            "044_mission_courier_offer_history_audit.sql",
            "045_mission_courier_issue_reports.sql",
            "046_mission_courier_issue_read_audit.sql",
            "047_mission_courier_assignment_release.sql",
            "048_mission_dispatch_queue_read_audit.sql",
            "049_mission_courier_offer_expiry.sql",
            "050_mission_dispatch_attempt_history_audit.sql"
        };
        foreach (var migrationName in migrationNames)
        {
            var resourceName = "Mono.Api.Tests.Migrations." + migrationName;
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

    private static async Task<string> GetMissionStatusAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status FROM missions WHERE mission_id = @mission_id;", connection);
        command.Parameters.AddWithValue("mission_id", MissionId);
        return (string?)await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Mission not found.");
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
                committed_delivery_at timestamp with time zone NOT NULL DEFAULT now(),
                created_at timestamp with time zone NOT NULL DEFAULT now(),
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

    private sealed class AdjustableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);

        public void Set(DateTimeOffset value) => _now = value;
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "mission-lifecycle-test-admin";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
