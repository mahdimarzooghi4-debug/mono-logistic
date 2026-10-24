using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record PublicRegistrationPayload(string FullName, string Phone, string? BusinessName);
public sealed record PublicRegistrationReceipt(string RequestId, string Status, DateTimeOffset SubmittedAt);
public sealed record PublicRegistrationQueueItem(string RequestId, string Type, string Status, string FullName, string Phone, string? BusinessName, string PrivacyNoticeVersion, DateTimeOffset SubmittedAt);
public enum PublicRegistrationWriteOutcome { Created, Replayed, Conflict }

public sealed class PostgresPublicRegistrationService(
    NpgsqlDataSource dataSource,
    PublicRegistrationProtector protector,
    IMonoOperationsContext operationsContext,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public bool IsEnabled => protector.IsConfigured &&
        !string.IsNullOrWhiteSpace(configuration["Mono:PublicRegistration:PrivacyNoticeVersion"]);

    public async Task<(PublicRegistrationWriteOutcome Outcome, PublicRegistrationReceipt Receipt)> SubmitAsync(
        string type, PublicRegistrationPayload payload, bool consent, string noticeVersion,
        string idempotencyKey, CancellationToken ct)
    {
        if (!IsEnabled) throw new InvalidOperationException("Public registration intake is not configured.");
        if (!consent || !string.Equals(noticeVersion, configuration["Mono:PublicRegistration:PrivacyNoticeVersion"], StringComparison.Ordinal))
            throw new ArgumentException("Current privacy notice consent is required.");
        type = type.Trim().ToUpperInvariant();
        if (type is not ("BUSINESS" or "COURIER")) throw new ArgumentException("Request type is invalid.");
        var fullName = Clean(payload.FullName, 120);
        var phone = NormalizePhone(payload.Phone);
        var businessName = type == "BUSINESS" ? Clean(payload.BusinessName, 160) : null;
        if (type == "BUSINESS" && businessName is null) throw new ArgumentException("Business name is required.");
        if (idempotencyKey.Length is < 16 or > 128 || idempotencyKey.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("A valid Idempotency-Key is required.");
        var canonical = JsonSerializer.Serialize(new { type, full_name = fullName, phone, business_name = businessName, notice_version = noticeVersion });
        var requestDigest = protector.Digest("request:" + canonical);
        var keyDigest = protector.Digest("key:" + idempotencyKey);
        var now = timeProvider.GetUtcNow();
        var id = "reg_" + Guid.NewGuid().ToString("N");
        var encrypted = protector.Protect(id, JsonSerializer.Serialize(new PublicRegistrationPayload(fullName, phone, businessName)));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("key", NpgsqlDbType.Text, keyDigest);
            await advisory.ExecuteNonQueryAsync(ct);
        }
        await using (var find = new NpgsqlCommand("SELECT request_id, request_digest, status, submitted_at FROM public_registration_requests WHERE idempotency_digest=@key FOR UPDATE", connection, transaction))
        {
            find.Parameters.AddWithValue("key", NpgsqlDbType.Text, keyDigest);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var oldId = reader.GetString(0);
                var oldDigest = reader.GetString(1);
                var status = reader.GetString(2);
                var submittedAt = reader.GetFieldValue<DateTimeOffset>(3);
                await reader.DisposeAsync();
                await transaction.CommitAsync(ct);
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(oldDigest), Encoding.ASCII.GetBytes(requestDigest)))
                    return (PublicRegistrationWriteOutcome.Conflict, new PublicRegistrationReceipt(oldId, status, submittedAt));
                return (PublicRegistrationWriteOutcome.Replayed, new PublicRegistrationReceipt(oldId, status, submittedAt));
            }
        }
        const string sql = """
            INSERT INTO public_registration_requests
              (request_id, request_type, encrypted_payload, privacy_notice_version, consented_at,
               idempotency_digest, request_digest, submitted_at, updated_at, expires_at)
            VALUES (@id,@type,@payload,@notice,@now,@key,@digest,@now,@now,@expires)
            """;
        await using (var insert = new NpgsqlCommand(sql, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
            insert.Parameters.AddWithValue("type", NpgsqlDbType.Text, type);
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Text, encrypted);
            insert.Parameters.AddWithValue("notice", NpgsqlDbType.Text, noticeVersion);
            insert.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Text, keyDigest);
            insert.Parameters.AddWithValue("digest", NpgsqlDbType.Text, requestDigest);
            insert.Parameters.AddWithValue("expires", NpgsqlDbType.TimestampTz, now.AddDays(90));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (PublicRegistrationWriteOutcome.Created, new PublicRegistrationReceipt(id, "SUBMITTED", now));
    }

    public async Task<IReadOnlyList<PublicRegistrationQueueItem>> ListAsync(int limit, CancellationToken ct)
    {
        RequireActor();
        var rows = new List<(string Id, string Type, string Status, string Payload, string Notice, DateTimeOffset At)>();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using (var command = new NpgsqlCommand("""
            SELECT request_id, request_type, status, encrypted_payload, privacy_notice_version, submitted_at
            FROM public_registration_requests
            WHERE status IN ('SUBMITTED','CONTACT_ATTEMPTED')
            ORDER BY submitted_at, request_id LIMIT @limit
            """, connection))
        {
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }
        var actor = operationsContext.ActorId;
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO public_registration_request_access_audit(actor_id, request_count, created_at)
            VALUES (@actor, @count, @now)
            """, connection))
        {
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, actor);
            audit.Parameters.AddWithValue("count", NpgsqlDbType.Integer, rows.Count);
            audit.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, timeProvider.GetUtcNow());
            await audit.ExecuteNonQueryAsync(ct);
        }
        return rows.Select(row =>
        {
            var p = JsonSerializer.Deserialize<PublicRegistrationPayload>(protector.Unprotect(row.Id, row.Payload))!;
            return new PublicRegistrationQueueItem(row.Id, row.Type, row.Status, p.FullName, p.Phone, p.BusinessName, row.Notice, row.At);
        }).ToArray();
    }

    public async Task<bool> TransitionAsync(string id, string target, string? reason, CancellationToken ct)
    {
        var actor = RequireActor();
        if (target is not ("CONTACT_ATTEMPTED" or "DECLINED" or "CLOSED")) throw new ArgumentException("Target status is invalid.");
        if (target == "DECLINED" && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A decline reason code is required.");
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string? previous;
        await using (var read = new NpgsqlCommand("SELECT status FROM public_registration_requests WHERE request_id=@id FOR UPDATE", connection, transaction))
        {
            read.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
            previous = (string?)await read.ExecuteScalarAsync(ct);
        }
        if (previous is null) { await transaction.RollbackAsync(ct); return false; }
        if (previous is "DECLINED" or "CLOSED" || (previous == "CONTACT_ATTEMPTED" && target == "CONTACT_ATTEMPTED"))
            throw new ArgumentException("This status transition is not allowed.");
        var expires = target is "DECLINED" or "CLOSED" ? now.AddDays(30) : (DateTimeOffset?)null;
        await using (var update = new NpgsqlCommand("""
            UPDATE public_registration_requests SET status=@target, updated_at=@now,
              expires_at=CASE WHEN @expires IS NULL THEN expires_at ELSE LEAST(@expires, submitted_at + interval '90 days') END WHERE request_id=@id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("target", NpgsqlDbType.Text, target);
            update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            update.Parameters.AddWithValue("expires", NpgsqlDbType.TimestampTz, (object?)expires ?? DBNull.Value);
            update.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
            await update.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO public_registration_request_events(request_id,actor_id,from_status,to_status,reason_code,created_at)
            VALUES (@id,@actor,@previous,@target,@reason,@now)
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, actor);
            audit.Parameters.AddWithValue("previous", NpgsqlDbType.Text, previous);
            audit.Parameters.AddWithValue("target", NpgsqlDbType.Text, target);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
            audit.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    private string RequireActor()
    {
        var actor = operationsContext.ActorId;
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Mono administrator access is required.");
        return actor;
    }
    private static string Clean(string? value, int max)
    {
        var result = value?.Trim().Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(result) || result.Length > max || result.Any(char.IsControl))
            throw new ArgumentException("Registration fields are invalid.");
        return result;
    }
    private static string NormalizePhone(string? value)
    {
        value ??= "";
        if (value.Any(c => !char.IsWhiteSpace(c) && !char.IsAsciiDigit(c) &&
            !(c is >= '۰' and <= '۹') && !(c is >= '٠' and <= '٩') &&
            c is not ('+' or '-' or '(' or ')')))
            throw new ArgumentException("Iranian mobile number is invalid.");
        var digits = value.Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + c - '۰'),
            >= '٠' and <= '٩' => (char)('0' + c - '٠'),
            _ => c
        }).Where(char.IsAsciiDigit).ToArray();
        var phone = new string(digits);
        if (phone.Length == 12 && phone.StartsWith("989", StringComparison.Ordinal)) phone = "0" + phone[2..];
        if (phone.Length != 11 || !phone.StartsWith("09", StringComparison.Ordinal))
            throw new ArgumentException("Iranian mobile number is invalid.");
        return phone;
    }
}