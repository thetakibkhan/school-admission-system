using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

/// <summary>
/// Mandatory concurrency proof (PRD 5.4, EP-10 US-10.1 and US-10.3). Every test runs against real
/// PostgreSQL through the real HTTP stack: no EF InMemory, no mocks, no sleeps. Requests are released
/// together with a <see cref="TaskCompletionSource"/> barrier.
/// </summary>
public sealed class ConcurrencyTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private const string StudentIdHeader = "X-Student-Id";

    private readonly AdmissionWebApplicationFactory _factory;

    public ConcurrencyTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    // 17 ASCII digits, derived from a per-test seed and a per-request index, never from the clock.
    private static string StudentId(int seed, int index) =>
        (10_000_000_000_000_000L + (seed * 1_000L) + index).ToString(CultureInfo.InvariantCulture);

    private static HttpRequestMessage HoldRequest(string classId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add(StudentIdHeader, studentId);
        return request;
    }

    private static HttpRequestMessage ConfirmRequest(string holdId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        request.Headers.Add(StudentIdHeader, studentId);
        return request;
    }

    private static HttpRequestMessage CancelRequest(string holdId, string studentId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        request.Headers.Add(StudentIdHeader, studentId);
        return request;
    }

    /// <summary>
    /// Starts every request, parks each one on a shared gate, then opens the gate so all of them are
    /// in flight at once. No <see cref="Thread.Sleep"/> and no per-request delay.
    /// </summary>
    private static async Task<HttpResponseMessage[]> FireTogetherAsync(
        HttpClient client, IReadOnlyList<HttpRequestMessage> requests)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = requests
            .Select(async request =>
            {
                await gate.Task.ConfigureAwait(false);
                return await client.SendAsync(request).ConfigureAwait(false);
            })
            .ToArray();

        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task<string> CreateClassAsync(HttpClient client, string name, int totalSeats)
    {
        using var response = await client.PostAsJsonAsync("/classes", new { name, totalSeats });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateHoldAsync(HttpClient client, string classId, string studentId)
    {
        using var request = HoldRequest(classId, studentId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("holdId").GetString()!;
    }

    private async Task<SeatCounts> CountsAsync(string classId) =>
        await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            return await SeatCountQueries.ForClassAsync(db, classId, now, CancellationToken.None);
        });

    [Fact]
    public async Task NoOverselling_50ConcurrentHoldsFor10Seats_Exactly10CreatedAnd40Conflicts()
    {
        using var client = _factory.CreateApiClient();
        var classId = await CreateClassAsync(client, "Concurrency - No overselling", 10);

        var requests = Enumerable.Range(0, 50)
            .Select(index => HoldRequest(classId, StudentId(1, index)))
            .ToList();

        var responses = await FireTogetherAsync(client, requests);

        try
        {
            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(40, statuses.Count(s => s == HttpStatusCode.Conflict));

            using var classResponse = await client.GetAsync($"/classes/{classId}");
            Assert.Equal(HttpStatusCode.OK, classResponse.StatusCode);
            var classBody = await classResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(10, classBody.GetProperty("heldSeats").GetInt32());
            Assert.Equal(0, classBody.GetProperty("confirmedSeats").GetInt32());
            Assert.Equal(0, classBody.GetProperty("availableSeats").GetInt32());

            var heldRows = await _factory.WithDbAsync(db =>
                db.Holds.CountAsync(h => h.ClassId == classId && h.Status == "HELD"));
            Assert.Equal(10, heldRows);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task SameStudentRace_TwoConcurrentHolds_OneRow_One201One200_SameHoldIdAndExpiry()
    {
        using var client = _factory.CreateApiClient();
        var classId = await CreateClassAsync(client, "Concurrency - Same student", 5);
        var studentId = StudentId(2, 0);

        var responses = await FireTogetherAsync(
            client,
            new[] { HoldRequest(classId, studentId), HoldRequest(classId, studentId) });

        try
        {
            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));

            var created = responses.Single(r => r.StatusCode == HttpStatusCode.Created);
            var existing = responses.Single(r => r.StatusCode == HttpStatusCode.OK);

            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            var existingBody = await existing.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                createdBody.GetProperty("holdId").GetString(),
                existingBody.GetProperty("holdId").GetString());
            Assert.Equal(
                createdBody.GetProperty("expiresAt").GetString(),
                existingBody.GetProperty("expiresAt").GetString());

            var rows = await _factory.WithDbAsync(db =>
                db.Holds.CountAsync(h => h.ClassId == classId && h.StudentId == studentId));
            Assert.Equal(1, rows);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConfirmRace_TwoConcurrentConfirms_OneConfirmedRow_Two200()
    {
        using var client = _factory.CreateApiClient();
        var classId = await CreateClassAsync(client, "Concurrency - Confirm race", 5);
        var studentId = StudentId(3, 0);
        var holdId = await CreateHoldAsync(client, classId, studentId);

        var responses = await FireTogetherAsync(
            client,
            new[] { ConfirmRequest(holdId, studentId), ConfirmRequest(holdId, studentId) });

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            var confirmed = await _factory.WithDbAsync(db =>
                db.Holds.CountAsync(h => h.Id == holdId && h.Status == "CONFIRMED"));
            Assert.Equal(1, confirmed);

            var counts = await CountsAsync(classId);
            Assert.Equal(0, counts.HeldSeats);
            Assert.Equal(1, counts.ConfirmedSeats);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConfirmVersusCancel_EndsInExactlyOneTerminalState()
    {
        using var client = _factory.CreateApiClient();
        var classId = await CreateClassAsync(client, "Concurrency - Confirm versus cancel", 5);
        var studentId = StudentId(4, 0);
        var holdId = await CreateHoldAsync(client, classId, studentId);

        var responses = await FireTogetherAsync(
            client,
            new[] { ConfirmRequest(holdId, studentId), CancelRequest(holdId, studentId) });

        try
        {
            var confirmStatus = responses[0].StatusCode;
            var cancelStatus = responses[1].StatusCode;

            var hold = await _factory.WithDbAsync(db =>
                db.Holds.AsNoTracking().SingleAsync(h => h.Id == holdId));

            Assert.Contains(hold.Status, new[] { "CONFIRMED", "CANCELLED" });

            if (hold.Status == "CONFIRMED")
            {
                Assert.Equal(HttpStatusCode.OK, confirmStatus);
                Assert.Equal(HttpStatusCode.Conflict, cancelStatus);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, confirmStatus);
                Assert.Equal(HttpStatusCode.NoContent, cancelStatus);
            }

            var counts = await CountsAsync(classId);
            Assert.Equal(0, counts.HeldSeats);
            if (hold.Status == "CONFIRMED")
            {
                Assert.Equal(1, counts.ConfirmedSeats);
                Assert.Equal(counts.TotalSeats - 1, counts.AvailableSeats);
            }
            else
            {
                Assert.Equal(0, counts.ConfirmedSeats);
                Assert.Equal(counts.TotalSeats, counts.AvailableSeats);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task IndependentClasses_OneSeatEach_AcceptOneHoldEachConcurrently()
    {
        using var client = _factory.CreateApiClient();
        var classA = await CreateClassAsync(client, "Concurrency - Independent A", 1);
        var classB = await CreateClassAsync(client, "Concurrency - Independent B", 1);

        var responses = await FireTogetherAsync(
            client,
            new[]
            {
                HoldRequest(classA, StudentId(5, 0)),
                HoldRequest(classB, StudentId(5, 1))
            });

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

            var countsA = await CountsAsync(classA);
            var countsB = await CountsAsync(classB);

            Assert.Equal(1, countsA.HeldSeats);
            Assert.Equal(0, countsA.AvailableSeats);
            Assert.Equal(1, countsB.HeldSeats);
            Assert.Equal(0, countsB.AvailableSeats);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
