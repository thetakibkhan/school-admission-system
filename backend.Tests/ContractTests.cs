using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

/// <summary>
/// Raw-JSON contract checks for the eight endpoints of PRD section 6 (R1, R5, R9). Each test reads the
/// response body as <see cref="JsonElement"/> and asserts the exact key set, not a deserialized object,
/// so an added or renamed key fails the test.
/// </summary>
public sealed class ContractTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private const string StudentIdHeader = "X-Student-Id";
    private const string StudentA = "20152691234567890";
    private const string StudentB = "20152691234567891";

    private static readonly string[] ClassKeys =
        { "availableSeats", "confirmedSeats", "heldSeats", "id", "name", "totalSeats" };

    private readonly AdmissionWebApplicationFactory _factory;

    public ContractTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    private HttpClient ClientFor(string? studentId)
    {
        var client = _factory.CreateApiClient();
        if (studentId is not null)
        {
            client.DefaultRequestHeaders.Add(StudentIdHeader, studentId);
        }

        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static string[] KeysOf(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static async Task<string> CreateClassAsync(HttpClient client, string name, int totalSeats)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        return body.GetProperty("id").GetString()!;
    }

    private static async Task<(string HoldId, JsonElement Body)> CreateHoldAsync(
        HttpClient client, string classId, string studentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add(StudentIdHeader, studentId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        return (body.GetProperty("holdId").GetString()!, body);
    }

    [Fact]
    public async Task PostClasses_Returns201_WithExactlyIdNameTotalSeats()
    {
        using var client = ClientFor(null);

        using var response = await client.PostAsJsonAsync(
            "/classes", new { name = "Contract - Create", totalSeats = 30 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(new[] { "id", "name", "totalSeats" }, KeysOf(body));
        Assert.Equal("Contract - Create", body.GetProperty("name").GetString());
        Assert.Equal(30, body.GetProperty("totalSeats").GetInt32());
    }

    [Fact]
    public async Task GetClasses_Returns200_ArrayWithExactlySixKeysPerItem()
    {
        using var client = ClientFor(null);
        await CreateClassAsync(client, "Contract - List A", 10);
        await CreateClassAsync(client, "Contract - List B", 20);

        using var response = await client.GetAsync("/classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        foreach (var item in body.EnumerateArray())
        {
            Assert.Equal(ClassKeys, KeysOf(item));
        }
    }

    [Fact]
    public async Task GetClassById_Returns200_WithExactlySixKeys()
    {
        using var client = ClientFor(null);
        var classId = await CreateClassAsync(client, "Contract - Single", 15);

        using var response = await client.GetAsync($"/classes/{classId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(ClassKeys, KeysOf(body));
        Assert.Equal(classId, body.GetProperty("id").GetString());
        Assert.Equal(15, body.GetProperty("totalSeats").GetInt32());
        Assert.Equal(0, body.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, body.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(15, body.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task PostHold_Returns201_WithExactlyFiveKeys_AndExpiresAtEndsWithZ()
    {
        using var client = ClientFor(StudentA);
        var classId = await CreateClassAsync(client, "Contract - Hold", 5);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add(StudentIdHeader, StudentA);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(
            new[] { "classId", "expiresAt", "holdId", "status", "studentId" },
            KeysOf(body));
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal(StudentA, body.GetProperty("studentId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());

        var expiresAt = body.GetProperty("expiresAt").GetString()!;
        Assert.EndsWith("Z", expiresAt, StringComparison.Ordinal);
        Assert.DoesNotContain("+", expiresAt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHoldById_Returns200_WithExactlyFourKeys()
    {
        using var client = ClientFor(StudentA);
        var classId = await CreateClassAsync(client, "Contract - Get hold", 5);
        var (holdId, _) = await CreateHoldAsync(client, classId, StudentA);

        using var response = await client.GetAsync($"/holds/{holdId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(new[] { "classId", "expiresAt", "holdId", "status" }, KeysOf(body));
        Assert.Equal(holdId, body.GetProperty("holdId").GetString());
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());
        Assert.EndsWith("Z", body.GetProperty("expiresAt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostConfirm_Returns200_WithExactlyHoldIdAndConfirmedStatus()
    {
        using var client = ClientFor(StudentA);
        var classId = await CreateClassAsync(client, "Contract - Confirm", 5);
        var (holdId, _) = await CreateHoldAsync(client, classId, StudentA);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        request.Headers.Add(StudentIdHeader, StudentA);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(new[] { "holdId", "status" }, KeysOf(body));
        Assert.Equal(holdId, body.GetProperty("holdId").GetString());
        Assert.Equal("CONFIRMED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DeleteHold_Returns204_WithEmptyBody()
    {
        using var client = ClientFor(StudentA);
        var classId = await CreateClassAsync(client, "Contract - Cancel", 5);
        var (holdId, _) = await CreateHoldAsync(client, classId, StudentA);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        request.Headers.Add(StudentIdHeader, StudentA);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetMyHolds_Returns200_ArrayWithExactlyFiveKeysPerItem()
    {
        using var client = ClientFor(StudentA);
        var classId = await CreateClassAsync(client, "Contract - My holds", 5);
        await CreateHoldAsync(client, classId, StudentA);
        await CreateHoldAsync(client, classId, StudentB);

        using var response = await client.GetAsync("/me/holds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(1, body.GetArrayLength());

        var item = body[0];
        Assert.Equal(
            new[] { "classId", "className", "expiresAt", "holdId", "status" },
            KeysOf(item));
        Assert.Equal(classId, item.GetProperty("classId").GetString());
        Assert.Equal("Contract - My holds", item.GetProperty("className").GetString());
        Assert.EndsWith("Z", item.GetProperty("expiresAt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryError_IsProblemJson_WithStatusMatchingHttpStatus()
    {
        using var client = ClientFor(null);
        var classId = await CreateClassAsync(client, "Contract - Errors", 1);

        // 400: missing identity header on a hold create.
        using (var missingHeader = await client.PostAsync($"/classes/{classId}/holds", null))
        {
            await AssertProblemAsync(missingHeader, HttpStatusCode.BadRequest);
        }

        // 400: malformed 17-digit student id.
        using (var malformed = ClientFor("12345"))
        using (var response = await malformed.PostAsync($"/classes/{classId}/holds", null))
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }

        // 404: unknown class.
        using (var unknownClass = ClientFor(StudentA))
        using (var response = await unknownClass.PostAsync("/classes/c_doesnotexist/holds", null))
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }

        // 404: unknown hold.
        using (var response = await client.GetAsync("/holds/h_doesnotexist"))
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }

        // 409: no seats left.
        using (var first = ClientFor(StudentA))
        using (var created = await first.PostAsync($"/classes/{classId}/holds", null))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var second = ClientFor(StudentB))
        using (var response = await second.PostAsync($"/classes/{classId}/holds", null))
        {
            await AssertProblemAsync(response, HttpStatusCode.Conflict);
        }
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await JsonAsync(response);
        Assert.Equal((int)expected, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("title", out _), "problem+json must carry a title");
        Assert.True(body.TryGetProperty("type", out _), "problem+json must carry a type");
    }
}
