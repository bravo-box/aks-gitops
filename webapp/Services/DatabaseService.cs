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
}
