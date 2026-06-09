using Microsoft.Data.SqlClient;
using Xunit;

namespace AttendanceSystem.IntegrationTests;

/// <summary>
/// A [Fact] that is skipped when no SQL Server is reachable (xUnit 2.5 has no Assert.Skip).
/// Uses ATTENDANCE_TEST_CONNECTION if set, otherwise LocalDB.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!TestDatabase.IsAvailable)
            Skip = "No SQL Server reachable (set ATTENDANCE_TEST_CONNECTION or install SQL Server LocalDB).";
    }
}

internal static class TestDatabase
{
    /// <summary>Base connection (points at the 'master' db on the server) used for availability checks.</summary>
    public static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("ATTENDANCE_TEST_CONNECTION")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    public static readonly bool IsAvailable = Detect();

    private static bool Detect()
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(BaseConnectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5,
            };
            using var conn = new SqlConnection(builder.ConnectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Connection string for a freshly-named, isolated test database.</summary>
    public static string UniqueDatabaseConnectionString(string dbName)
    {
        var builder = new SqlConnectionStringBuilder(BaseConnectionString)
        {
            InitialCatalog = dbName,
            ConnectTimeout = 15,
        };
        return builder.ConnectionString;
    }
}
