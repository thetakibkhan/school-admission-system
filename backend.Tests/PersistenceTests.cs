using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Tests;

/// <summary>
/// Persistence proof (EP-01 US-01.4, EP-10 US-10.4). The first test shows state written through the
/// API is readable from a brand-new <see cref="AdmissionDbContext"/> after the previous one is
/// disposed. The second is a static check of the compose persistence path; the real down/up proof
/// belongs to the delivery task.
/// </summary>
public sealed class PersistenceTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private readonly AdmissionWebApplicationFactory _factory;

    public PersistenceTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    private AdmissionDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AdmissionDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .Options;
        return new AdmissionDbContext(options);
    }

    [Fact]
    public async Task DataWrittenThroughApi_SurvivesDbContextDisposal()
    {
        string classId;
        string holdId;

        using (var client = _factory.CreateApiClient())
        {
            using var createClass = await client.PostAsJsonAsync(
                "/classes", new { name = "Persistence - Survives", totalSeats = 7 });
            Assert.Equal(HttpStatusCode.Created, createClass.StatusCode);
            var classBody = await createClass.Content.ReadFromJsonAsync<JsonElement>();
            classId = classBody.GetProperty("id").GetString()!;

            using var createHold = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            createHold.Headers.Add("X-Student-Id", "20152691234567890");
            using var holdResponse = await client.SendAsync(createHold);
            Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
            var holdBody = await holdResponse.Content.ReadFromJsonAsync<JsonElement>();
            holdId = holdBody.GetProperty("holdId").GetString()!;
        }

        // First context: read, then dispose.
        ClassEntity? firstRead;
        using (var first = NewContext())
        {
            firstRead = await first.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId);
        }

        Assert.NotNull(firstRead);
        Assert.Equal("Persistence - Survives", firstRead!.Name);
        Assert.Equal(7, firstRead.TotalSeats);

        // Second, independent context against the same database: the rows are still there.
        using (var second = NewContext())
        {
            var storedClass = await second.Classes.AsNoTracking().SingleAsync(c => c.Id == classId);
            var storedHold = await second.Holds.AsNoTracking().SingleAsync(h => h.Id == holdId);

            Assert.Equal(classId, storedHold.ClassId);
            Assert.Equal("20152691234567890", storedHold.StudentId);
            Assert.Equal("HELD", storedHold.Status);
            Assert.Equal(storedClass.Id, storedHold.ClassId);
        }

        // And the API still reports the same derived counts from a fresh request.
        using (var client = _factory.CreateApiClient())
        using (var response = await client.GetAsync($"/classes/{classId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("heldSeats").GetInt32());
            Assert.Equal(6, body.GetProperty("availableSeats").GetInt32());
        }
    }

    [Fact]
    public void ComposeFile_DeclaresNamedVolume_AndNoBindMountForPostgresData()
    {
        var composePath = FindComposeFile();
        var text = File.ReadAllText(composePath);

        // A named volume is declared at the top level and mounted for the database data directory.
        Assert.Contains("volumes:", text, StringComparison.Ordinal);
        Assert.Contains("pgdata:/var/lib/postgresql/data", text, StringComparison.Ordinal);

        // The data directory is mounted from the named volume only. A bind mount would put a host path
        // ("./pgdata", "/srv/pgdata", "${PWD}/pgdata") before the colon.
        var dataMounts = text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("/var/lib/postgresql/data", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(dataMounts);
        Assert.All(dataMounts, line => Assert.StartsWith("- pgdata:", line, StringComparison.Ordinal));

        // The normal shutdown flow never deletes the volume.
        Assert.DoesNotContain("down -v", text, StringComparison.Ordinal);
    }

    private static string FindComposeFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docker-compose.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "docker-compose.yml was not found above the test output directory.");
    }
}
