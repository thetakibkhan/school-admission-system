using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

/// <summary>
/// Test host for the API. Reads <c>TEST_DATABASE_URL</c>, fails loudly when it is missing, and
/// recreates the schema with the real migrations. Never loads <c>.env</c> files and never falls back
/// to another database.
/// </summary>
public sealed class AdmissionWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestDatabaseUrlVariable = "TEST_DATABASE_URL";

    public string ConnectionString { get; }

    public AdmissionWebApplicationFactory()
        : this(ReadConnectionString())
    {
    }

    internal AdmissionWebApplicationFactory(string connectionString)
    {
        ConnectionString = connectionString;
    }

    private static string ReadConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(TestDatabaseUrlVariable);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{TestDatabaseUrlVariable} is not set. Tests require a real PostgreSQL database; " +
                "there is no fallback connection string.");
        }

        return value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("APPLY_MIGRATIONS", "false");
    }

    /// <summary>Drops and recreates the schema with the real migrations.</summary>
    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
        db.Database.EnsureDeleted();
        db.Database.Migrate();
    }

    /// <summary>Creates a fresh <see cref="HttpClient"/> against the test host.</summary>
    public HttpClient CreateApiClient() => CreateClient();

    /// <summary>Runs a unit of work against a fresh <see cref="AdmissionDbContext"/>.</summary>
    public async Task<T> WithDbAsync<T>(Func<AdmissionDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
        return await work(db);
    }
}
