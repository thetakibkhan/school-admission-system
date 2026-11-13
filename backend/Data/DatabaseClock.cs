using Microsoft.EntityFrameworkCore;

namespace SchoolAdmission.Api.Data;

public static class DatabaseClock
{
    // PostgreSQL clock, read inside the current transaction.
    public static Task<DateTime> NowAsync(AdmissionDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
}
