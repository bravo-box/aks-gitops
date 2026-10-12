using Microsoft.Data.SqlClient;

namespace WebApp.Services;

// Builds the connection string from discrete config values so the SA password
// (sourced from the mssql-secret k8s Secret) is never duplicated as a formatted string.
public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration configuration)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = configuration["Db:Server"] ?? "mssql,1433",
            InitialCatalog = configuration["Db:Database"] ?? "master",
            UserID = configuration["Db:User"] ?? "sa",
            Password = configuration["Db:Password"]
                ?? throw new InvalidOperationException("Db:Password is not configured."),
            Encrypt = false,
            TrustServerCertificate = true
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<string> GetServerVersionAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@VERSION";
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? "unknown";
    }

    public async Task<SqlServerIdentity> GetServerIdentityAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // master's database_guid is baked into the SQL Server image at build time, so it's
        // identical on every deployment of the same image — not a real per-instance identifier.
        // Generate and persist our own GUID on first run instead, scoped to this instance's PVC.
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = @"
                IF OBJECT_ID('dbo.AppInstanceIdentity') IS NULL
                BEGIN
                    CREATE TABLE dbo.AppInstanceIdentity (InstanceId UNIQUEIDENTIFIER NOT NULL);
                    INSERT INTO dbo.AppInstanceIdentity (InstanceId) VALUES (NEWID());
                END";
            await setup.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                CONVERT(varchar(36), SERVERPROPERTY('ServerName')) AS ServerName,
                CONVERT(varchar(128), SERVERPROPERTY('MachineName')) AS MachineName,
                CONVERT(varchar(36), (SELECT TOP 1 InstanceId FROM dbo.AppInstanceIdentity)) AS InstanceId";

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new SqlServerIdentity("unknown", "unknown", "unknown");
        }

        return new SqlServerIdentity(
            reader["ServerName"]?.ToString() ?? "unknown",
            reader["MachineName"]?.ToString() ?? "unknown",
            reader["InstanceId"]?.ToString() ?? "unknown");
    }
}

public record SqlServerIdentity(string ServerName, string MachineName, string InstanceId);
