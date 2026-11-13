using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Tests;

public sealed class ClassesTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private const string StudentA = "20152691234567890";
    private const string StudentB = "20152691234567891";
    private const string StudentC = "20152691234567892";

    private readonly AdmissionWebApplicationFactory _factory;

    public ClassesTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Create_ValidClass_Returns201WithExactFieldsAndLocation()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/classes",
            new { name = "Class 6 - Morning Shift", totalSeats = 120 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal(
            new[] { "id", "name", "totalSeats" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        var id = json.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("Class 6 - Morning Shift", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(120, json.RootElement.GetProperty("totalSeats").GetInt32());

        Assert.Equal($"/classes/{id}", response.Headers.Location?.OriginalString);

        var stored = await _factory.WithDbAsync(db => db.Classes.CountAsync());
        Assert.Equal(1, stored);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Create_NonPositiveTotalSeats_Returns400AndInsertsNothing(int totalSeats)
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/classes", new { name = "Class 7", totalSeats });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await ReadJsonAsync(response);
        Assert.Contains("totalSeats", json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var stored = await _factory.WithDbAsync(db => db.Classes.CountAsync());
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Create_TotalSeatsAboveLimit_Returns400()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/classes", new { name = "Huge", totalSeats = 100_001 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankName_Returns400(string name)
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats = 30 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await ReadJsonAsync(response);
        Assert.Contains("name", json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var stored = await _factory.WithDbAsync(db => db.Classes.CountAsync());
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Create_NameDifferingOnlyByCaseAndSpaces_Returns409AndKeepsOneRow()
    {
        using var client = _factory.CreateApiClient();

        using var first = await client.PostAsJsonAsync("/classes", new { name = "Class 6 - Morning Shift", totalSeats = 40 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var duplicate = await client.PostAsJsonAsync("/classes", new { name = "  class 6 - MORNING shift  ", totalSeats = 50 });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        var classes = await _factory.WithDbAsync(db => db.Classes.ToListAsync());
        var only = Assert.Single(classes);
        Assert.Equal("Class 6 - Morning Shift", only.Name);
        Assert.Equal(40, only.TotalSeats);
    }

    [Fact]
    public async Task Create_TwoShiftNames_CreateIndependentClassesWithIndependentCounts()
    {
        using var client = _factory.CreateApiClient();

        var morning = await CreateClassAsync(client, "Class 6 - Morning Shift", 10);
        var day = await CreateClassAsync(client, "Class 6 - Day Shift", 20);

        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            db.Holds.Add(new HoldEntity
            {
                Id = IdGenerator.NewHoldId(),
                ClassId = morning,
                StudentId = StudentA,
                Status = "HELD",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            });
            return await db.SaveChangesAsync();
        });

        var morningCounts = await GetClassAsync(client, morning);
        var dayCounts = await GetClassAsync(client, day);

        Assert.Equal(1, morningCounts.GetProperty("heldSeats").GetInt32());
        Assert.Equal(9, morningCounts.GetProperty("availableSeats").GetInt32());
        Assert.Equal(0, dayCounts.GetProperty("heldSeats").GetInt32());
        Assert.Equal(20, dayCounts.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task List_EmptyDatabase_Returns200WithEmptyArray()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync("/classes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task List_NewClass_ReportsZeroHeldZeroConfirmedAndFullAvailability()
    {
        using var client = _factory.CreateApiClient();
        var id = await CreateClassAsync(client, "Class 8", 25);

        using var response = await client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);
        var item = Assert.Single(json.RootElement.EnumerateArray().ToList());

        Assert.Equal(
            new[] { "availableSeats", "confirmedSeats", "heldSeats", "id", "name", "totalSeats" },
            item.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal("Class 8", item.GetProperty("name").GetString());
        Assert.Equal(25, item.GetProperty("totalSeats").GetInt32());
        Assert.Equal(0, item.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, item.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(25, item.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task List_OrdersByCreatedAtThenId()
    {
        using var client = _factory.CreateApiClient();
        var first = await CreateClassAsync(client, "Class 1", 10);
        var second = await CreateClassAsync(client, "Class 2", 10);
        var third = await CreateClassAsync(client, "Class 3", 10);

        using var response = await client.GetAsync("/classes");
        var json = await ReadJsonAsync(response);

        var ids = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToList();

        Assert.Equal(new[] { first, second, third }, ids);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync("/classes/c_doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await ReadJsonAsync(response);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Get_KnownClass_ReturnsExactSixFields()
    {
        using var client = _factory.CreateApiClient();
        var id = await CreateClassAsync(client, "Class 9", 15);

        using var response = await client.GetAsync($"/classes/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal(
            new[] { "availableSeats", "confirmedSeats", "heldSeats", "id", "name", "totalSeats" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(15, json.RootElement.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task Get_SeatCounts_AreDerivedFromHoldRows()
    {
        using var client = _factory.CreateApiClient();
        var id = await CreateClassAsync(client, "Class 10", 10);

        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);

            // Active HELD: occupies a seat.
            db.Holds.Add(new HoldEntity
            {
                Id = IdGenerator.NewHoldId(),
                ClassId = id,
                StudentId = StudentA,
                Status = "HELD",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            });

            // Expired HELD: does not occupy a seat.
            db.Holds.Add(new HoldEntity
            {
                Id = IdGenerator.NewHoldId(),
                ClassId = id,
                StudentId = StudentB,
                Status = "HELD",
                CreatedAt = now.AddMinutes(-20),
                ExpiresAt = now.AddMinutes(-10)
            });

            // CONFIRMED: occupies a seat even though its expiry is in the past.
            db.Holds.Add(new HoldEntity
            {
                Id = IdGenerator.NewHoldId(),
                ClassId = id,
                StudentId = StudentC,
                Status = "CONFIRMED",
                CreatedAt = now.AddMinutes(-30),
                ExpiresAt = now.AddMinutes(-20),
                ConfirmedAt = now.AddMinutes(-25)
            });

            return await db.SaveChangesAsync();
        });

        var counts = await GetClassAsync(client, id);

        Assert.Equal(10, counts.GetProperty("totalSeats").GetInt32());
        Assert.Equal(1, counts.GetProperty("heldSeats").GetInt32());
        Assert.Equal(1, counts.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(8, counts.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task Get_CancelledHold_DoesNotOccupyASeat()
    {
        using var client = _factory.CreateApiClient();
        var id = await CreateClassAsync(client, "Class 11", 5);

        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            db.Holds.Add(new HoldEntity
            {
                Id = IdGenerator.NewHoldId(),
                ClassId = id,
                StudentId = StudentA,
                Status = "CANCELLED",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10),
                CancelledAt = now
            });
            return await db.SaveChangesAsync();
        });

        var counts = await GetClassAsync(client, id);

        Assert.Equal(0, counts.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, counts.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, counts.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task ClassRoutes_DoNotRequireStudentHeader()
    {
        using var client = _factory.CreateApiClient();

        using var create = await client.PostAsJsonAsync("/classes", new { name = "Class 12", totalSeats = 3 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var list = await client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    private static async Task<string> CreateClassAsync(HttpClient client, string name, int totalSeats)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ReadJsonAsync(response);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetClassAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync($"/classes/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);
        return json.RootElement.Clone();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }
}
