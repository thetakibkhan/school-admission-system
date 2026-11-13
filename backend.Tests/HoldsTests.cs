using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Tests;

public sealed class HoldsTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private const string StudentA = "20152691234567890";
    private const string StudentB = "20152691234567891";
    private const string StudentC = "20152691234567892";

    private readonly AdmissionWebApplicationFactory _factory;

    public HoldsTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<string> CreateClassAsync(string name, int totalSeats)
    {
        var classId = IdGenerator.NewClassId();

        await _factory.WithDbAsync(async db =>
        {
            db.Classes.Add(new ClassEntity
            {
                Id = classId,
                Name = name,
                NormalizedName = name.Trim().ToLowerInvariant(),
                TotalSeats = totalSeats,
                CreatedAt = await DatabaseClock.NowAsync(db, CancellationToken.None)
            });
            await db.SaveChangesAsync();
            return 0;
        });

        return classId;
    }

    private HttpClient ClientFor(string? studentId)
    {
        var client = _factory.CreateApiClient();
        if (studentId is not null)
        {
            client.DefaultRequestHeaders.Add(StudentId.HeaderName, studentId);
        }

        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private async Task<SeatCounts> CountsAsync(string classId) =>
        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            return await SeatCountQueries.ForClassAsync(db, classId, now, CancellationToken.None);
        });

    private async Task<HoldEntity?> HoldAsync(string holdId) =>
        await _factory.WithDbAsync(db =>
            db.Holds.AsNoTracking().SingleOrDefaultAsync(h => h.Id == holdId));

    private async Task ExpireHoldAsync(string holdId) =>
        await _factory.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE holds SET expires_at = clock_timestamp() - interval '1 second' WHERE id = {0}",
                holdId));

    private async Task<(HttpStatusCode Status, string HoldId, string ExpiresAt)> CreateHoldAsync(
        string classId, string studentId)
    {
        using var client = ClientFor(studentId);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);
        var body = await JsonAsync(response);
        return (response.StatusCode, body.GetProperty("holdId").GetString()!, body.GetProperty("expiresAt").GetString()!);
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task Create_Returns201_WithFiveFields_AndExpiryAboutConfiguredDuration()
    {
        var classId = await CreateClassAsync("Class 6 - Morning", 10);
        var before = await _factory.WithDbAsync(db => DatabaseClock.NowAsync(db, CancellationToken.None));

        using var client = ClientFor(StudentA);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(
            new[] { "classId", "expiresAt", "holdId", "status", "studentId" },
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal(StudentA, body.GetProperty("studentId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());
        Assert.StartsWith("h_", body.GetProperty("holdId").GetString(), StringComparison.Ordinal);

        var expiresAt = body.GetProperty("expiresAt").GetDateTime();
        var delta = expiresAt - before;
        Assert.InRange(delta.TotalSeconds, 590, 610);
    }

    [Fact]
    public async Task Create_SerializesExpiresAt_AsIsoUtcWithZ()
    {
        var classId = await CreateClassAsync("Class 7", 5);

        using var client = ClientFor(StudentA);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);
        var raw = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(raw);
        var expiresAt = document.RootElement.GetProperty("expiresAt").GetString()!;
        Assert.EndsWith("Z", expiresAt, StringComparison.Ordinal);
        Assert.DoesNotContain("+", expiresAt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Duplicate_Returns200_SameHoldIdAndExpiry()
    {
        var classId = await CreateClassAsync("Class 8", 5);

        var first = await CreateHoldAsync(classId, StudentA);
        var second = await CreateHoldAsync(classId, StudentA);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.HoldId, second.HoldId);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);

        var counts = await CountsAsync(classId);
        Assert.Equal(1, counts.HeldSeats);
    }

    [Fact]
    public async Task Create_OnFullClass_Returns409_AndInsertsNothing()
    {
        var classId = await CreateClassAsync("Class 9", 1);

        var first = await CreateHoldAsync(classId, StudentA);
        Assert.Equal(HttpStatusCode.Created, first.Status);

        using var client = ClientFor(StudentB);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var rows = await _factory.WithDbAsync(db =>
            db.Holds.CountAsync(h => h.ClassId == classId));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Create_OnUnknownClass_Returns404()
    {
        using var client = ClientFor(StudentA);
        using var response = await client.PostAsync("/classes/c_doesnotexist/holds", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutHeader_Returns400()
    {
        var classId = await CreateClassAsync("Class 10", 5);

        using var client = ClientFor(null);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("2015269123456789")]
    [InlineData("201526912345678901")]
    [InlineData("2015269123456789A")]
    public async Task Create_WithMalformedStudentId_Returns400(string studentId)
    {
        var classId = await CreateClassAsync("Class 11", 5);

        using var client = ClientFor(studentId);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_BeforeExpiry_Returns200_AndMovesSeatFromHeldToConfirmed()
    {
        var classId = await CreateClassAsync("Class 12", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using var response = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(created.HoldId, body.GetProperty("holdId").GetString());
        Assert.Equal("CONFIRMED", body.GetProperty("status").GetString());

        var counts = await CountsAsync(classId);
        Assert.Equal(0, counts.HeldSeats);
        Assert.Equal(1, counts.ConfirmedSeats);
        Assert.Equal(4, counts.AvailableSeats);
    }

    [Fact]
    public async Task Confirm_Twice_Returns200BothTimes_WithOneConfirmedRow()
    {
        var classId = await CreateClassAsync("Class 13", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using var first = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);
        using var second = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var confirmed = await _factory.WithDbAsync(db =>
            db.Holds.CountAsync(h => h.ClassId == classId && h.Status == "CONFIRMED"));
        Assert.Equal(1, confirmed);
    }

    [Fact]
    public async Task Confirm_ExpiredHold_Returns409()
    {
        var classId = await CreateClassAsync("Class 14", 5);
        var created = await CreateHoldAsync(classId, StudentA);
        await ExpireHoldAsync(created.HoldId);

        using var client = ClientFor(StudentA);
        using var response = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var hold = await HoldAsync(created.HoldId);
        Assert.Equal("HELD", hold!.Status);
        Assert.Null(hold.ConfirmedAt);
    }

    [Fact]
    public async Task Confirm_WithWrongStudent_Returns404_AndChangesNothing()
    {
        var classId = await CreateClassAsync("Class 15", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentB);
        using var response = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var hold = await HoldAsync(created.HoldId);
        Assert.Equal("HELD", hold!.Status);
        Assert.Null(hold.ConfirmedAt);
    }

    [Fact]
    public async Task Cancel_HeldHold_Returns204_WithEmptyBody_AndReleasesSeat()
    {
        var classId = await CreateClassAsync("Class 16", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using var response = await client.DeleteAsync($"/holds/{created.HoldId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());

        var counts = await CountsAsync(classId);
        Assert.Equal(0, counts.HeldSeats);
        Assert.Equal(5, counts.AvailableSeats);
    }

    [Fact]
    public async Task Cancel_Twice_Returns204BothTimes_WithNoSideEffect()
    {
        var classId = await CreateClassAsync("Class 17", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using var first = await client.DeleteAsync($"/holds/{created.HoldId}");
        using var second = await client.DeleteAsync($"/holds/{created.HoldId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var hold = await HoldAsync(created.HoldId);
        Assert.Equal("CANCELLED", hold!.Status);
        Assert.NotNull(hold.CancelledAt);
    }

    [Fact]
    public async Task Cancel_ConfirmedAdmission_Returns409_AndKeepsSeatOccupied()
    {
        var classId = await CreateClassAsync("Class 18", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using var confirm = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var response = await client.DeleteAsync($"/holds/{created.HoldId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var counts = await CountsAsync(classId);
        Assert.Equal(1, counts.ConfirmedSeats);
        Assert.Equal(4, counts.AvailableSeats);
    }

    [Fact]
    public async Task Cancel_ExpiredHold_Returns204_WithNoChange()
    {
        var classId = await CreateClassAsync("Class 19", 5);
        var created = await CreateHoldAsync(classId, StudentA);
        await ExpireHoldAsync(created.HoldId);

        using var client = ClientFor(StudentA);
        using var response = await client.DeleteAsync($"/holds/{created.HoldId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var hold = await HoldAsync(created.HoldId);
        Assert.Equal("HELD", hold!.Status);
        Assert.Null(hold.CancelledAt);
    }

    [Fact]
    public async Task Get_ReportsExpiredForPastHeld_AndConfirmedForPastConfirmed()
    {
        var classId = await CreateClassAsync("Class 20", 5);

        var expired = await CreateHoldAsync(classId, StudentA);
        await ExpireHoldAsync(expired.HoldId);

        var confirmed = await CreateHoldAsync(classId, StudentB);
        using (var confirmClient = ClientFor(StudentB))
        using (var confirm = await confirmClient.PostAsync($"/holds/{confirmed.HoldId}/confirm", null))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        await ExpireHoldAsync(confirmed.HoldId);

        using var client = ClientFor(null);

        using var expiredResponse = await client.GetAsync($"/holds/{expired.HoldId}");
        Assert.Equal(HttpStatusCode.OK, expiredResponse.StatusCode);
        var expiredBody = await JsonAsync(expiredResponse);
        Assert.Equal("EXPIRED", expiredBody.GetProperty("status").GetString());
        Assert.Equal(
            new[] { "classId", "expiresAt", "holdId", "status" },
            expiredBody.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        using var confirmedResponse = await client.GetAsync($"/holds/{confirmed.HoldId}");
        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);
        var confirmedBody = await JsonAsync(confirmedResponse);
        Assert.Equal("CONFIRMED", confirmedBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_UnknownHold_Returns404()
    {
        using var client = ClientFor(null);
        using var response = await client.GetAsync("/holds/h_doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListMine_ReturnsOnlyThatStudentsRows_NewestFirst_WithClassName()
    {
        var firstClass = await CreateClassAsync("Class 21 - Morning", 5);
        var secondClass = await CreateClassAsync("Class 22 - Evening", 5);

        var first = await CreateHoldAsync(firstClass, StudentA);
        var second = await CreateHoldAsync(secondClass, StudentA);
        await CreateHoldAsync(firstClass, StudentB);

        using var client = ClientFor(StudentA);
        using var response = await client.GetAsync("/me/holds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        var newest = body[0];
        Assert.Equal(second.HoldId, newest.GetProperty("holdId").GetString());
        Assert.Equal("Class 22 - Evening", newest.GetProperty("className").GetString());
        Assert.Equal(
            new[] { "classId", "className", "expiresAt", "holdId", "status" },
            newest.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal(first.HoldId, body[1].GetProperty("holdId").GetString());
        Assert.Equal("Class 21 - Morning", body[1].GetProperty("className").GetString());
    }

    [Fact]
    public async Task ListMine_ForStudentWithoutHolds_ReturnsEmptyArray()
    {
        using var client = ClientFor(StudentC);
        using var response = await client.GetAsync("/me/holds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task ListMine_WithoutHeader_Returns400()
    {
        using var client = ClientFor(null);
        using var response = await client.GetAsync("/me/holds");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenStudentAlreadyConfirmed_Returns409()
    {
        var classId = await CreateClassAsync("Class 23", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using var client = ClientFor(StudentA);
        using (var confirm = await client.PostAsync($"/holds/{created.HoldId}/confirm", null))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        using var response = await client.PostAsync($"/classes/{classId}/holds", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var rows = await _factory.WithDbAsync(db =>
            db.Holds.CountAsync(h => h.ClassId == classId && h.StudentId == StudentA));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Create_AfterOldHoldExpired_SucceedsWhenSeatsRemain()
    {
        var classId = await CreateClassAsync("Class 24", 5);
        var old = await CreateHoldAsync(classId, StudentA);
        await ExpireHoldAsync(old.HoldId);

        var fresh = await CreateHoldAsync(classId, StudentA);

        Assert.Equal(HttpStatusCode.Created, fresh.Status);
        Assert.NotEqual(old.HoldId, fresh.HoldId);

        var counts = await CountsAsync(classId);
        Assert.Equal(1, counts.HeldSeats);
    }
}
