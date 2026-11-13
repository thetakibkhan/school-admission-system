using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Tests;

public sealed class FoundationTests : IClassFixture<AdmissionWebApplicationFactory>
{
    private readonly AdmissionWebApplicationFactory _factory;

    public FoundationTests(AdmissionWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Health_ReturnsOk_WithDocumentedSettings()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal("ok", body!["status"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public void Startup_Fails_WhenHoldDurationIsNotAPositiveInteger(string value)
    {
        using var factory = new AdmissionWebApplicationFactory(_factory.ConnectionString)
            .WithWebHostBuilder(builder => builder.UseSetting("HOLD_DURATION_SECONDS", value));

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.Contains("HOLD_DURATION_SECONDS", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldDuration_DefaultsTo600Seconds()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<SchoolAdmission.Api.Configuration.HoldOptions>();

        Assert.Equal(600, options.DurationSeconds);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Migration_CreatesExpectedTablesAndColumns()
    {
        var columns = await _factory.WithDbAsync(async db =>
        {
            var rows = await db.Database
                .SqlQueryRaw<ColumnRow>(
                    """
                    SELECT table_name AS "TableName", column_name AS "ColumnName", data_type AS "DataType"
                    FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name IN ('classes', 'holds')
                    """)
                .ToListAsync();
            return rows;
        });

        var classes = columns.Where(c => c.TableName == "classes")
            .ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.Ordinal);
        var holds = columns.Where(c => c.TableName == "holds")
            .ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.Ordinal);

        Assert.Equal(
            new[] { "created_at", "id", "name", "normalized_name", "total_seats" },
            classes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                "cancelled_at", "class_id", "confirmed_at", "created_at",
                "expires_at", "id", "status", "student_id"
            },
            holds.Keys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal("integer", classes["total_seats"]);
        Assert.Equal("text", classes["id"]);
        Assert.Equal("text", holds["student_id"]);
        Assert.Equal("timestamp with time zone", holds["expires_at"]);
        Assert.Equal("timestamp with time zone", holds["created_at"]);
    }

    [Fact]
    public async Task Migration_CreatesIndexesAndCheckConstraints()
    {
        var indexes = await _factory.WithDbAsync(db =>
            db.Database.SqlQueryRaw<string>(
                    """
                    SELECT indexname AS "Value" FROM pg_indexes
                    WHERE schemaname = 'public' AND tablename IN ('classes', 'holds')
                    """)
                .ToListAsync());

        Assert.Contains("ux_classes_normalized_name", indexes);
        Assert.Contains("ix_holds_class_id", indexes);
        Assert.Contains("ix_holds_class_id_status_expires_at", indexes);
        Assert.Contains("ix_holds_student_id_created_at", indexes);
        Assert.Contains("ix_holds_class_id_student_id", indexes);
        Assert.Contains("ux_holds_class_id_student_id_confirmed", indexes);

        var constraints = await _factory.WithDbAsync(db =>
            db.Database.SqlQueryRaw<string>(
                    """
                    SELECT conname AS "Value" FROM pg_constraint
                    WHERE conrelid IN ('classes'::regclass, 'holds'::regclass) AND contype = 'c'
                    """)
                .ToListAsync());

        Assert.Contains("ck_classes_total_seats_positive", constraints);
        Assert.Contains("ck_holds_student_id_format", constraints);
        Assert.Contains("ck_holds_status", constraints);
    }

    [Fact]
    public async Task SeatCounts_AreZero_ForClassWithoutHolds()
    {
        var classId = IdGenerator.NewClassId();

        var counts = await _factory.WithDbAsync(async db =>
        {
            db.Classes.Add(new ClassEntity
            {
                Id = classId,
                Name = "Class 6",
                NormalizedName = "class 6",
                TotalSeats = 40,
                CreatedAt = await DatabaseClock.NowAsync(db, CancellationToken.None)
            });
            await db.SaveChangesAsync();

            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);
            return await SeatCountQueries.ForClassAsync(db, classId, now, CancellationToken.None);
        });

        Assert.Equal(40, counts.TotalSeats);
        Assert.Equal(0, counts.HeldSeats);
        Assert.Equal(0, counts.ConfirmedSeats);
        Assert.Equal(40, counts.AvailableSeats);
    }

    [Fact]
    public async Task SeatCounts_ForAll_OrdersByCreatedAtThenId_AndCountsHolds()
    {
        var olderId = IdGenerator.NewClassId();
        var newerId = IdGenerator.NewClassId();
        var sameInstantFirstId = IdGenerator.NewClassId();
        var sameInstantSecondId = IdGenerator.NewClassId();
        var sameInstant = new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
        var older = sameInstant.AddMinutes(-10);
        var newer = sameInstant.AddMinutes(10);

        var result = await _factory.WithDbAsync(async db =>
        {
            var now = await DatabaseClock.NowAsync(db, CancellationToken.None);

            db.Classes.AddRange(
                new ClassEntity { Id = newerId, Name = "Newer", NormalizedName = "newer", TotalSeats = 5, CreatedAt = newer },
                new ClassEntity { Id = olderId, Name = "Older", NormalizedName = "older", TotalSeats = 10, CreatedAt = older },
                new ClassEntity { Id = sameInstantSecondId, Name = "Tie B", NormalizedName = "tie b", TotalSeats = 3, CreatedAt = sameInstant },
                new ClassEntity { Id = sameInstantFirstId, Name = "Tie A", NormalizedName = "tie a", TotalSeats = 3, CreatedAt = sameInstant });

            db.Holds.AddRange(
                // older: one active hold, one expired hold, one confirmed, one cancelled.
                NewHold(olderId, "11111111111111111", "HELD", now.AddMinutes(5)),
                NewHold(olderId, "22222222222222222", "HELD", now.AddMinutes(-5)),
                NewHold(olderId, "33333333333333333", "CONFIRMED", now.AddMinutes(-5)),
                NewHold(olderId, "44444444444444444", "CANCELLED", now.AddMinutes(5)),
                // newer: one active hold only.
                NewHold(newerId, "55555555555555555", "HELD", now.AddMinutes(5)));

            await db.SaveChangesAsync();

            return await SeatCountQueries.ForAllAsync(db, now, CancellationToken.None);
        });

        var tieOrder = new[] { sameInstantFirstId, sameInstantSecondId }
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { olderId, tieOrder[0], tieOrder[1], newerId },
            result.Select(r => r.Class.Id).ToArray());

        var olderCounts = result.Single(r => r.Class.Id == olderId).Counts;
        Assert.Equal(10, olderCounts.TotalSeats);
        Assert.Equal(1, olderCounts.HeldSeats);
        Assert.Equal(1, olderCounts.ConfirmedSeats);
        Assert.Equal(8, olderCounts.AvailableSeats);

        var newerCounts = result.Single(r => r.Class.Id == newerId).Counts;
        Assert.Equal(5, newerCounts.TotalSeats);
        Assert.Equal(1, newerCounts.HeldSeats);
        Assert.Equal(0, newerCounts.ConfirmedSeats);
        Assert.Equal(4, newerCounts.AvailableSeats);

        foreach (var tieId in tieOrder)
        {
            var tieCounts = result.Single(r => r.Class.Id == tieId).Counts;
            Assert.Equal(0, tieCounts.HeldSeats);
            Assert.Equal(0, tieCounts.ConfirmedSeats);
            Assert.Equal(3, tieCounts.AvailableSeats);
        }
    }

    private static HoldEntity NewHold(string classId, string studentId, string status, DateTime expiresAt) =>
        new()
        {
            Id = IdGenerator.NewHoldId(),
            ClassId = classId,
            StudentId = studentId,
            Status = status,
            CreatedAt = new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc),
            ExpiresAt = expiresAt,
            ConfirmedAt = status == "CONFIRMED" ? new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc) : null,
            CancelledAt = status == "CANCELLED" ? new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc) : null
        };

    [Theory]
    [InlineData("00000000000000000", true)]
    [InlineData("20152691234567890", true)]
    [InlineData("99999999999999999", true)]
    [InlineData("2015269123456789", false)]
    [InlineData("201526912345678901", false)]
    [InlineData("2015269123456789A", false)]
    [InlineData("2015269123456789 ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void StudentId_IsValid(string? value, bool expected)
    {
        Assert.Equal(expected, StudentId.IsValid(value));
    }

    public sealed class ColumnRow
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
    }
}
