using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public sealed record SeatCounts(int TotalSeats, int HeldSeats, int ConfirmedSeats)
{
    public int AvailableSeats => TotalSeats - HeldSeats - ConfirmedSeats;
}

public static class SeatCountQueries
{
    // heldSeats: status = 'HELD' AND expires_at > dbNow
    // confirmedSeats: status = 'CONFIRMED'
    public static async Task<SeatCounts> ForClassAsync(AdmissionDbContext db, string classId, DateTime dbNow, CancellationToken ct)
    {
        var row = await db.Classes
            .Where(c => c.Id == classId)
            .Select(c => new
            {
                c.TotalSeats,
                HeldSeats = db.Holds.Count(h => h.ClassId == c.Id && h.Status == "HELD" && h.ExpiresAt > dbNow),
                ConfirmedSeats = db.Holds.Count(h => h.ClassId == c.Id && h.Status == "CONFIRMED")
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            throw new KeyNotFoundException($"Class '{classId}' does not exist.");
        }

        return new SeatCounts(row.TotalSeats, row.HeldSeats, row.ConfirmedSeats);
    }

    // One query for every class, same rules, ordered by created_at then id.
    public static async Task<IReadOnlyList<(ClassEntity Class, SeatCounts Counts)>> ForAllAsync(AdmissionDbContext db, DateTime dbNow, CancellationToken ct)
    {
        var rows = await db.Classes
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                Class = c,
                HeldSeats = db.Holds.Count(h => h.ClassId == c.Id && h.Status == "HELD" && h.ExpiresAt > dbNow),
                ConfirmedSeats = db.Holds.Count(h => h.ClassId == c.Id && h.Status == "CONFIRMED")
            })
            .ToListAsync(ct);

        return rows
            .Select(r => (r.Class, new SeatCounts(r.Class.TotalSeats, r.HeldSeats, r.ConfirmedSeats)))
            .ToList();
    }
}
