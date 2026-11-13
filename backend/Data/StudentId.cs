namespace SchoolAdmission.Api.Data;

public static class StudentId
{
    public const string HeaderName = "X-Student-Id";

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != 17)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
