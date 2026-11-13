using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public static class ClassRowLock
{
    // Locks the class row for the rest of the transaction.
    // Returns null when no such class exists.
    public static Task<ClassEntity?> LockAsync(AdmissionDbContext db, string classId, CancellationToken ct) =>
        db.Classes.FromSqlRaw("SELECT * FROM classes WHERE id = {0} FOR UPDATE", classId)
                  .SingleOrDefaultAsync(ct);
}
