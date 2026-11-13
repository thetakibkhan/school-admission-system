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

    [ComposeFileFact]
    public void ComposeFile_DeclaresNamedVolume_AndNoBindMountForPostgresData()
    {
        var composePath = ComposeFileLocator.Find()!;

        // Comments are stripped first: a comment that warns about `docker compose down -v` is not a
        // shutdown command, and must not fail the check for the wrong reason.
        var lines = File.ReadAllLines(composePath)
            .Select(StripComment)
            .ToArray();

        // A named volume is declared at the top level.
        Assert.Contains(lines, line => line.TrimEnd() == "volumes:");
        Assert.Contains(lines, line => line.Trim() == "pgdata:");

        // The database data directory is mounted from that named volume, in either the short
        // ("pgdata:/var/lib/postgresql/data") or the long ("type: volume" / "source: pgdata") form.
        var mounts = DataMounts(lines);
        Assert.NotEmpty(mounts);
        Assert.All(mounts, mount =>
        {
            Assert.Equal("volume", mount.Type);
            Assert.Equal("pgdata", mount.Source);
        });

        // The normal shutdown flow never deletes the volume. Only real (non-comment) lines count.
        Assert.DoesNotContain(lines, line => line.Contains("down -v", StringComparison.Ordinal));
    }

    private const string PostgresDataPath = "/var/lib/postgresql/data";

    private sealed record DataMount(string Source, string Type);

    /// <summary>Removes a YAML comment: a <c>#</c> at line start or after whitespace.</summary>
    private static string StripComment(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    /// <summary>
    /// Every volume list item whose target is the PostgreSQL data directory, parsed from either the
    /// short or the long compose form.
    /// </summary>
    private static IReadOnlyList<DataMount> DataMounts(IReadOnlyList<string> lines)
    {
        var mounts = new List<DataMount>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            // One list item: the "- " line plus every following line indented deeper than it.
            var itemIndent = Indent(lines[i]);
            var item = new List<string> { trimmed[2..].Trim() };
            var next = i + 1;
            while (next < lines.Count
                   && lines[next].Trim().Length > 0
                   && Indent(lines[next]) > itemIndent)
            {
                item.Add(lines[next].Trim());
                next++;
            }

            var mount = ParseMount(item);
            if (mount is not null)
            {
                mounts.Add(mount);
            }

            i = next - 1;
        }

        return mounts;
    }

    private static DataMount? ParseMount(IReadOnlyList<string> item)
    {
        var first = item[0];
        var colon = first.IndexOf(':');

        // Short form: "<source>:<target>[:<mode>]".
        if (colon > 0 && !IsLongFormKey(first[..colon]))
        {
            var source = first[..colon].Trim();
            var target = first[(colon + 1)..].Split(':')[0].Trim();
            return target == PostgresDataPath ? new DataMount(source, ClassifySource(source)) : null;
        }

        // Long form: "type:" / "source:" / "target:" keys in one list item.
        string? type = null;
        string? longSource = null;
        string? longTarget = null;

        foreach (var line in item)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "type":
                    type = value;
                    break;
                case "source":
                    longSource = value;
                    break;
                case "target":
                    longTarget = value;
                    break;
            }
        }

        if (longTarget != PostgresDataPath || longSource is null)
        {
            return null;
        }

        return new DataMount(longSource, type ?? ClassifySource(longSource));
    }

    private static bool IsLongFormKey(string key) =>
        key is "type" or "source" or "target" or "read_only" or "volume";

    /// <summary>A host path is a bind mount; a bare name is a named volume.</summary>
    private static string ClassifySource(string source) =>
        source.StartsWith('.') || source.StartsWith('/') || source.StartsWith('~')
        || source.Contains('$') || source.Contains('\\')
            ? "bind"
            : "volume";
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips the compose check with a clear reason when
/// <c>docker-compose.yml</c> is not reachable from the test output directory, for example when the
/// test binary is copied out of the repository tree. xUnit 2.5 has no runtime skip, so the decision
/// is made at discovery time.
/// </summary>
public sealed class ComposeFileFactAttribute : FactAttribute
{
    public ComposeFileFactAttribute()
    {
        if (ComposeFileLocator.Find() is null)
        {
            Skip = "docker-compose.yml was not found above the test output directory; run the suite " +
                   "from inside the repository tree to check the compose persistence path.";
        }
    }
}

/// <summary>Locates <c>docker-compose.yml</c> by walking up from the test output directory.</summary>
public static class ComposeFileLocator
{
    public static string? Find()
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

        return null;
    }
}
