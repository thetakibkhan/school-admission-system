namespace SchoolAdmission.Api.Data;

public static class IdGenerator
{
    public static string NewClassId() => "c_" + Guid.NewGuid().ToString("N");

    public static string NewHoldId() => "h_" + Guid.NewGuid().ToString("N");
}
