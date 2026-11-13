namespace SchoolAdmission.Api.Models;

public sealed class ClassEntity
{
    public string Id { get; set; } = "";            // "c_" + 32 lowercase hex chars
    public string Name { get; set; } = "";          // trimmed, as entered
    public string NormalizedName { get; set; } = ""; // Name.Trim().ToLowerInvariant()
    public int TotalSeats { get; set; }
    public DateTime CreatedAt { get; set; }          // UTC
}
