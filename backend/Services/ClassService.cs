using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Services;

/// <summary>Outcome kind of a <see cref="ServiceResult{T}"/>.</summary>
public enum ServiceStatus
{
    Ok,
    NotFound,
    Validation,
    Conflict
}

/// <summary>
/// Typed result of a service call. The controller maps each status to its documented HTTP code;
/// no business decision is made in the controller.
/// </summary>
public sealed record ServiceResult<T>(ServiceStatus Status, T? Value, string? Detail)
{
    public static ServiceResult<T> Ok(T value) => new(ServiceStatus.Ok, value, null);

    public static ServiceResult<T> NotFound(string detail) => new(ServiceStatus.NotFound, default, detail);

    public static ServiceResult<T> Validation(string detail) => new(ServiceStatus.Validation, default, detail);

    public static ServiceResult<T> Conflict(string detail) => new(ServiceStatus.Conflict, default, detail);
}

/// <summary>
/// Class creation, listing and single-class reads. Seat counts are always derived from hold rows
/// (R7) using one PostgreSQL time reading per request (R3).
/// </summary>
public sealed class ClassService
{
    public const int MaxTotalSeats = 100_000;

    private const string UniqueViolationSqlState = "23505";

    private readonly AdmissionDbContext _db;

    public ClassService(AdmissionDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<ClassCreatedResponse>> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return ServiceResult<ClassCreatedResponse>.Validation("name is required and must not be blank.");
        }

        if (request.TotalSeats is null)
        {
            return ServiceResult<ClassCreatedResponse>.Validation("totalSeats is required.");
        }

        if (request.TotalSeats <= 0)
        {
            return ServiceResult<ClassCreatedResponse>.Validation("totalSeats must be a positive whole number.");
        }

        if (request.TotalSeats > MaxTotalSeats)
        {
            return ServiceResult<ClassCreatedResponse>.Validation(
                $"totalSeats must not be greater than {MaxTotalSeats}.");
        }

        var entity = new ClassEntity
        {
            Id = IdGenerator.NewClassId(),
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            TotalSeats = request.TotalSeats.Value,
            CreatedAt = await DatabaseClock.NowAsync(_db, ct)
        };

        _db.Classes.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return ServiceResult<ClassCreatedResponse>.Conflict("A class with this name already exists.");
        }

        return ServiceResult<ClassCreatedResponse>.Ok(
            new ClassCreatedResponse(entity.Id, entity.Name, entity.TotalSeats));
    }

    public async Task<ServiceResult<IReadOnlyList<ClassResponse>>> ListAsync(CancellationToken ct)
    {
        var dbNow = await DatabaseClock.NowAsync(_db, ct);
        var rows = await SeatCountQueries.ForAllAsync(_db, dbNow, ct);

        IReadOnlyList<ClassResponse> classes = rows
            .Select(row => ToResponse(row.Class, row.Counts))
            .ToList();

        return ServiceResult<IReadOnlyList<ClassResponse>>.Ok(classes);
    }

    public async Task<ServiceResult<ClassResponse>> GetAsync(string classId, CancellationToken ct)
    {
        var entity = await _db.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId, ct);

        if (entity is null)
        {
            return ServiceResult<ClassResponse>.NotFound("Class not found.");
        }

        var dbNow = await DatabaseClock.NowAsync(_db, ct);
        var counts = await SeatCountQueries.ForClassAsync(_db, classId, dbNow, ct);

        return ServiceResult<ClassResponse>.Ok(ToResponse(entity, counts));
    }

    private static ClassResponse ToResponse(ClassEntity entity, SeatCounts counts) =>
        new(entity.Id, entity.Name, counts.TotalSeats, counts.HeldSeats, counts.ConfirmedSeats, counts.AvailableSeats);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
