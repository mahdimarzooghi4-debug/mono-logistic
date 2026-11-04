using System.Globalization;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;

return await ApiKeyProvisionerCommand.RunAsync(args);

internal static class ApiKeyProvisionerCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (!TryParseArguments(args, out var customerId, out var scopes, out var expiresAt))
        {
            Console.Error.WriteLine("Invalid arguments.");
            PrintUsage();
            return 2;
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            Console.Error.WriteLine("Provisioning requires an interactive terminal with stdin, stdout, and stderr connected directly to the terminal.");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Mono");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__Mono must be set in the trusted operations environment.");
            return 2;
        }

        IssuedApiKey issuedKey;
        try
        {
            issuedKey = MonoApiKeyIssuer.Create(customerId, scopes, expiresAt, DateTimeOffset.UtcNow);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        Console.WriteLine($"Customer: {customerId}");
        Console.WriteLine($"Scopes: {string.Join(", ", issuedKey.Credential.Scopes)}");
        Console.WriteLine($"Expires: {expiresAt:O}");
        Console.Write("Type ISSUE to create this credential: ");
        if (!string.Equals(Console.ReadLine(), "ISSUE", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Provisioning cancelled.");
            return 1;
        }

        try
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new PostgresApiKeyProvisioner(dataSource).CreateAsync(issuedKey.Credential);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            Console.Error.WriteLine("Provisioning failed. No API key was revealed. Verify the database and active customer.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Copy this key directly to the approved secret store. It will not be shown again:");
        Console.WriteLine(issuedKey.Value);
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string customerId,
        out IReadOnlyList<string> scopes,
        out DateTimeOffset expiresAt)
    {
        customerId = string.Empty;
        scopes = Array.Empty<string>();
        expiresAt = default;

        string? parsedCustomerId = null;
        string? parsedExpiry = null;
        var parsedScopes = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (index + 1 >= args.Length)
                return false;

            var value = args[++index];
            switch (option)
            {
                case "--customer-id" when parsedCustomerId is null:
                    parsedCustomerId = value;
                    break;
                case "--scope":
                    parsedScopes.Add(value);
                    break;
                case "--expires-at" when parsedExpiry is null:
                    parsedExpiry = value;
                    break;
                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(parsedCustomerId) ||
            parsedScopes.Count == 0 ||
            string.IsNullOrWhiteSpace(parsedExpiry) ||
            !DateTimeOffset.TryParse(parsedExpiry, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
        {
            return false;
        }

        customerId = parsedCustomerId;
        scopes = parsedScopes;
        expiresAt = parsedDate;
        return true;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: Mono.ApiKeyProvisioner --customer-id <customer-id> --scope <scope> [--scope <scope> ...] --expires-at <ISO-8601 timestamp>");
    }
}
