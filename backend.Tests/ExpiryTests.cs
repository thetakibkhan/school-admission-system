using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

/// <summary>
/// Expiry proof (PRD 5.2, EP-10 US-10.2). The host is configured with
/// <c>HOLD_DURATION_SECONDS=1</c>; expiry is then moved with a direct SQL update, never by sleeping.
/// </summary>
public sealed class ExpiryTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private const string StudentIdHeader = "X-Student-Id";
    private const string StudentA = "20152691234567890";
    private const string StudentB = "20152691234567891";

    private readonly AdmissionWebApplicationFactory _factory;

    // One derived host for the whole class: it carries HOLD_DURATION_SECONDS=1 and points at the same
    // database as the fixture. Built once, reused by every test in the class.
    private static WebApplicationFactory<Program>? _sharedApi;

    private readonly WebApplicationFactory<Program> _api;

    public ExpiryTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
        _api = _sharedApi ??= factory.WithWebHostBuilder(builder =>
            builder.UseSetting("HOLD_DURATION_SECONDS", "1"));
    }

    private HttpClient ClientFor(string? studentId)
    {
        var client = _api.CreateClient();
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

    private async Task<string> CreateClassAsync(string name, int totalSeats)
    {
        using var client = ClientFor(null);
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        return body.GetProperty("id").GetString()!;
    }

    private async Task<(HttpStatusCode Status, string HoldId, string ExpiresAt)> CreateHoldAsync(
        string classId, string studentId)
    {
        using var client = ClientFor(studentId);
        using var response = await client.PostAsync($"/classes/{classId}/holds", null);
        var body = await JsonAsync(response);
        return (
            response.StatusCode,
            body.GetProperty("holdId").GetString()!,
            body.GetProperty("expiresAt").GetString()!);
    }

    private async Task<SeatCounts> CountsAsync(string classId) =>
        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            return await SeatCountQueries.ForClassAsync(db, classId, now, CancellationToken.None);
        });

    // Moves the stored expiry into the past with SQL. No sleep, no worker, no cleanup call.
    private async Task ExpireHoldAsync(string holdId) =>
        await _factory.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE holds SET expires_at = clock_timestamp() - interval '1 second' WHERE id = {0}",
                holdId));

    [Fact]
    public async Task HoldPastExpiry_StopsCountingAsHeld_WithNoRequestAndNoWorker()
    {
        var classId = await CreateClassAsync("Expiry - Released", 1);

        var created = await CreateHoldAsync(classId, StudentA);
        Assert.Equal(HttpStatusCode.Created, created.Status);

        // The configured duration is 1 second, so the stored expiry is about one second out.
        var expiresAt = DateTime.Parse(
            created.ExpiresAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        var remaining = expiresAt - DateTime.UtcNow;
        Assert.InRange(remaining.TotalSeconds, -1, 5);

        var before = await CountsAsync(classId);
        Assert.Equal(1, before.HeldSeats);
        Assert.Equal(0, before.AvailableSeats);

        await ExpireHoldAsync(created.HoldId);

        var after = await CountsAsync(classId);
        Assert.Equal(0, after.HeldSeats);
        Assert.Equal(0, after.ConfirmedSeats);
        Assert.Equal(1, after.AvailableSeats);

        // The row is still stored as HELD; EXPIRED is derived, never written (R2).
        var stored = await _factory.WithDbAsync(db =>
            db.Holds.AsNoTracking().SingleAsync(h => h.Id == created.HoldId));
        Assert.Equal("HELD", stored.Status);
    }

    [Fact]
    public async Task ReleasedSeat_IsTakenByADifferentStudent()
    {
        var classId = await CreateClassAsync("Expiry - Reuse", 1);

        var first = await CreateHoldAsync(classId, StudentA);
        Assert.Equal(HttpStatusCode.Created, first.Status);

        await ExpireHoldAsync(first.HoldId);

        var second = await CreateHoldAsync(classId, StudentB);
        Assert.Equal(HttpStatusCode.Created, second.Status);
        Assert.NotEqual(first.HoldId, second.HoldId);

        var counts = await CountsAsync(classId);
        Assert.Equal(1, counts.HeldSeats);
        Assert.Equal(0, counts.AvailableSeats);
    }

    [Fact]
    public async Task ConfirmingExpiredHold_Returns409_AndGetReportsExpired()
    {
        var classId = await CreateClassAsync("Expiry - Confirm", 5);
        var created = await CreateHoldAsync(classId, StudentA);
        await ExpireHoldAsync(created.HoldId);

        using var client = ClientFor(StudentA);
        using var confirm = await client.PostAsync($"/holds/{created.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("application/problem+json", confirm.Content.Headers.ContentType?.MediaType);

        using var get = await client.GetAsync($"/holds/{created.HoldId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await JsonAsync(get);
        Assert.Equal("EXPIRED", body.GetProperty("status").GetString());

        var stored = await _factory.WithDbAsync(db =>
            db.Holds.AsNoTracking().SingleAsync(h => h.Id == created.HoldId));
        Assert.Equal("HELD", stored.Status);
        Assert.Null(stored.ConfirmedAt);
    }

    [Fact]
    public async Task ConfirmedRowPastExpiry_StillReportsConfirmed_AndStillOccupiesASeat()
    {
        var classId = await CreateClassAsync("Expiry - Confirmed stays", 5);
        var created = await CreateHoldAsync(classId, StudentA);

        using (var client = ClientFor(StudentA))
        using (var confirm = await client.PostAsync($"/holds/{created.HoldId}/confirm", null))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        await ExpireHoldAsync(created.HoldId);

        using (var client = ClientFor(null))
        using (var get = await client.GetAsync($"/holds/{created.HoldId}"))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var body = await JsonAsync(get);
            Assert.Equal("CONFIRMED", body.GetProperty("status").GetString());
        }

        var counts = await CountsAsync(classId);
        Assert.Equal(0, counts.HeldSeats);
        Assert.Equal(1, counts.ConfirmedSeats);
        Assert.Equal(4, counts.AvailableSeats);
    }
}
