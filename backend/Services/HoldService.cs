using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Outcome of a service call: the HTTP status the controller must return plus the body, or the
/// Problem Details title/detail for a failure. Controllers never decide business rules themselves.
/// </summary>
public sealed record HoldResult<T>(int StatusCode, T? Value, string? ErrorTitle = null, string? ErrorDetail = null)
{
    public bool IsSuccess => StatusCode < 400;

    public static HoldResult<T> Ok(int statusCode, T value) => new(statusCode, value);

    public static HoldResult<T> Fail(int statusCode, string title, string detail) =>
        new(statusCode, default, title, detail);
}

/// <summary>
/// Reservation, confirmation, cancellation and history. Every occupancy-changing method runs one
/// explicit transaction, takes the class row lock first (R6) and reads PostgreSQL time after the
/// lock (R3).
/// </summary>
public sealed class HoldService
{
    public const string StatusHeld = "HELD";
    public const string StatusConfirmed = "CONFIRMED";
    public const string StatusCancelled = "CANCELLED";
    public const string StatusExpired = "EXPIRED";

    private readonly AdmissionDbContext _db;
    private readonly HoldOptions _options;

    public HoldService(AdmissionDbContext db, HoldOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <summary>Effective status (R2): a stored HELD past its expiry is EXPIRED.</summary>
    public static string EffectiveStatus(HoldEntity hold, DateTime dbNow) =>
        hold.Status == StatusHeld && hold.ExpiresAt <= dbNow ? StatusExpired : hold.Status;

    public async Task<HoldResult<HoldCreatedResponse>> CreateAsync(
        string classId, string studentId, CancellationToken ct)
    {
        if (!StudentId.IsValid(studentId))
        {
            return HoldResult<HoldCreatedResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid student id",
                "X-Student-Id must be exactly 17 ASCII digits.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // R6: the class row lock comes before any occupancy read.
        if (await ClassRowLock.LockAsync(_db, classId, ct) is null)
        {
            return HoldResult<HoldCreatedResponse>.Fail(
                StatusCodes.Status404NotFound, "Class not found", $"Class '{classId}' does not exist.");
        }

        // R3: database time is read only after the lock is held.
        var dbNow = await DatabaseClock.NowAsync(_db, ct);

        var studentHolds = await _db.Holds
            .Where(h => h.ClassId == classId && h.StudentId == studentId)
            .ToListAsync(ct);

        if (studentHolds.Any(h => h.Status == StatusConfirmed))
        {
            return HoldResult<HoldCreatedResponse>.Fail(
                StatusCodes.Status409Conflict,
                "Already admitted",
                "This student already has a confirmed admission for this class.");
        }

        var activeHold = studentHolds.FirstOrDefault(h => h.Status == StatusHeld && h.ExpiresAt > dbNow);
        if (activeHold is not null)
        {
            // R8: return the existing hold unchanged; never insert and never extend.
            return HoldResult<HoldCreatedResponse>.Ok(
                StatusCodes.Status200OK,
                new HoldCreatedResponse(
                    activeHold.Id, activeHold.ClassId, activeHold.StudentId, StatusHeld, activeHold.ExpiresAt));
        }

        var counts = await SeatCountQueries.ForClassAsync(_db, classId, dbNow, ct);
        if (counts.HeldSeats + counts.ConfirmedSeats >= counts.TotalSeats)
        {
            return HoldResult<HoldCreatedResponse>.Fail(
                StatusCodes.Status409Conflict,
                "No seats available",
                "All seats are currently occupied.");
        }

        var hold = new HoldEntity
        {
            Id = IdGenerator.NewHoldId(),
            ClassId = classId,
            StudentId = studentId,
            Status = StatusHeld,
            CreatedAt = dbNow,
            ExpiresAt = dbNow.AddSeconds(_options.DurationSeconds)
        };

        _db.Holds.Add(hold);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return HoldResult<HoldCreatedResponse>.Ok(
            StatusCodes.Status201Created,
            new HoldCreatedResponse(hold.Id, hold.ClassId, hold.StudentId, hold.Status, hold.ExpiresAt));
    }

    public async Task<HoldResult<HoldResponse>> GetAsync(string holdId, CancellationToken ct)
    {
        var hold = await _db.Holds.AsNoTracking().SingleOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is null)
        {
            return HoldResult<HoldResponse>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        var dbNow = await DatabaseClock.NowAsync(_db, ct);
        return HoldResult<HoldResponse>.Ok(
            StatusCodes.Status200OK,
            new HoldResponse(hold.Id, hold.ClassId, EffectiveStatus(hold, dbNow), hold.ExpiresAt));
    }

    public async Task<HoldResult<ConfirmResponse>> ConfirmAsync(
        string holdId, string studentId, CancellationToken ct)
    {
        if (!StudentId.IsValid(studentId))
        {
            return HoldResult<ConfirmResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid student id",
                "X-Student-Id must be exactly 17 ASCII digits.");
        }

        // Pre-lock read only to learn which class row to lock; the hold is re-read after the lock.
        var classId = await _db.Holds
            .Where(h => h.Id == holdId)
            .Select(h => h.ClassId)
            .SingleOrDefaultAsync(ct);

        if (classId is null)
        {
            return HoldResult<ConfirmResponse>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        if (await ClassRowLock.LockAsync(_db, classId, ct) is null)
        {
            return HoldResult<ConfirmResponse>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        var dbNow = await DatabaseClock.NowAsync(_db, ct);

        var hold = await _db.Holds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is null || hold.StudentId != studentId)
        {
            // Wrong student is 404, never 403, and never leaks the other student's data.
            return HoldResult<ConfirmResponse>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        if (hold.Status == StatusConfirmed)
        {
            return HoldResult<ConfirmResponse>.Ok(
                StatusCodes.Status200OK, new ConfirmResponse(hold.Id, StatusConfirmed));
        }

        if (hold.Status == StatusCancelled || EffectiveStatus(hold, dbNow) == StatusExpired)
        {
            return HoldResult<ConfirmResponse>.Fail(
                StatusCodes.Status409Conflict,
                "Reservation cannot be confirmed",
                "This reservation is expired or cancelled.");
        }

        hold.Status = StatusConfirmed;
        hold.ConfirmedAt = dbNow;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return HoldResult<ConfirmResponse>.Ok(
            StatusCodes.Status200OK, new ConfirmResponse(hold.Id, StatusConfirmed));
    }

    public async Task<HoldResult<object>> CancelAsync(
        string holdId, string studentId, CancellationToken ct)
    {
        if (!StudentId.IsValid(studentId))
        {
            return HoldResult<object>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid student id",
                "X-Student-Id must be exactly 17 ASCII digits.");
        }

        var classId = await _db.Holds
            .Where(h => h.Id == holdId)
            .Select(h => h.ClassId)
            .SingleOrDefaultAsync(ct);

        if (classId is null)
        {
            return HoldResult<object>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        if (await ClassRowLock.LockAsync(_db, classId, ct) is null)
        {
            return HoldResult<object>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        var dbNow = await DatabaseClock.NowAsync(_db, ct);

        var hold = await _db.Holds.SingleOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is null || hold.StudentId != studentId)
        {
            return HoldResult<object>.Fail(
                StatusCodes.Status404NotFound, "Hold not found", $"Hold '{holdId}' does not exist.");
        }

        if (hold.Status == StatusConfirmed)
        {
            return HoldResult<object>.Fail(
                StatusCodes.Status409Conflict,
                "Confirmed admission cannot be cancelled",
                "A confirmed admission is never released by this endpoint.");
        }

        if (hold.Status == StatusCancelled || EffectiveStatus(hold, dbNow) == StatusExpired)
        {
            // Retry-safe: nothing to change, still 204.
            return HoldResult<object>.Ok(StatusCodes.Status204NoContent, new object());
        }

        hold.Status = StatusCancelled;
        hold.CancelledAt = dbNow;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return HoldResult<object>.Ok(StatusCodes.Status204NoContent, new object());
    }

    public async Task<HoldResult<IReadOnlyList<MyHoldResponse>>> ListMineAsync(
        string studentId, CancellationToken ct)
    {
        if (!StudentId.IsValid(studentId))
        {
            return HoldResult<IReadOnlyList<MyHoldResponse>>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid student id",
                "X-Student-Id must be exactly 17 ASCII digits.");
        }

        // One database time reading for the whole list.
        var dbNow = await DatabaseClock.NowAsync(_db, ct);

        var rows = await _db.Holds
            .AsNoTracking()
            .Where(h => h.StudentId == studentId)
            .Join(
                _db.Classes.AsNoTracking(),
                h => h.ClassId,
                c => c.Id,
                (h, c) => new { Hold = h, c.Name })
            .OrderByDescending(x => x.Hold.CreatedAt)
            .ThenByDescending(x => x.Hold.Id)
            .ToListAsync(ct);

        IReadOnlyList<MyHoldResponse> body = rows
            .Select(x => new MyHoldResponse(
                x.Hold.Id, x.Hold.ClassId, x.Name, EffectiveStatus(x.Hold, dbNow), x.Hold.ExpiresAt))
            .ToList();

        return HoldResult<IReadOnlyList<MyHoldResponse>>.Ok(StatusCodes.Status200OK, body);
    }
}
