namespace SchoolAdmission.Api.Models;

public sealed class HoldEntity
{
    public string Id { get; set; } = "";            // "h_" + 32 lowercase hex chars
    public string ClassId { get; set; } = "";
    public string StudentId { get; set; } = "";     // exactly 17 ASCII digits, text
    public string Status { get; set; } = "HELD";    // HELD | CONFIRMED | CANCELLED
    public DateTime CreatedAt { get; set; }         // UTC
    public DateTime ExpiresAt { get; set; }         // UTC, set once
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
