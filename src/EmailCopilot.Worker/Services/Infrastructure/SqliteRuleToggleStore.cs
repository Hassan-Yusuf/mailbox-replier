using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class SqliteRuleToggleStore : IRuleToggleStore
{
    private const string StoreName = "RuleToggle";
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private IReadOnlyDictionary<string, bool>? _cachedSnapshot;

    public SqliteRuleToggleStore(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureDatabaseDirectoryExists();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS RuleToggles (
                    RuleName     TEXT PRIMARY KEY,
                    IsEnabled    INTEGER NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SqliteMigrationHelpers.EnsureSchemaVersionTableAsync(connection, cancellationToken);
        await SqliteMigrationHelpers.SetSchemaVersionAsync(connection, StoreName, CurrentSchemaVersion, cancellationToken);

        InvalidateCache();
    }

    public async Task<IReadOnlyDictionary<string, bool>> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        var cached = _cachedSnapshot;
        if (cached is not null)
        {
            return cached;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSnapshot is not null)
            {
                return _cachedSnapshot;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT RuleName, IsEnabled FROM RuleToggles;";

            var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ruleName = reader.GetString(0);
                var isEnabled = reader.GetInt32(1) != 0;
                snapshot[ruleName] = isEnabled;
            }

            _cachedSnapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task SetAsync(string ruleName, bool isEnabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            throw new ArgumentException("Rule name must not be empty.", nameof(ruleName));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO RuleToggles (RuleName, IsEnabled, UpdatedAtUtc)
            VALUES ($ruleName, $isEnabled, $updatedAtUtc)
            ON CONFLICT(RuleName) DO UPDATE SET
                IsEnabled = excluded.IsEnabled,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;

        command.Parameters.AddWithValue("$ruleName", ruleName);
        command.Parameters.AddWithValue("$isEnabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        InvalidateCache();
    }

    private void InvalidateCache()
    {
        _cachedSnapshot = null;
    }

    private void EnsureDatabaseDirectoryExists()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            return;
        }

        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
