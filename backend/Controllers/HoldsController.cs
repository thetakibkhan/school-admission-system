using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

/// <summary>
/// Reservation lifecycle: create, read, confirm and cancel. Every route that changes occupancy
/// requires <c>X-Student-Id</c>; a missing or malformed header is rejected with 400 before any
/// database work.
/// </summary>
[ApiController]
public sealed class HoldsController : ControllerBase
{
    private readonly HoldService _holds;

    // HoldService is registered by Program.cs (foundation-owned, R11); see the handoff line in the
    // task report. Until that registration lands, every hold route returns 500.
    public HoldsController(HoldService holds)
    {
        _holds = holds;
    }

    [HttpPost("classes/{classId}/holds")]
    public async Task<IActionResult> Create(string classId, CancellationToken ct)
    {
        if (!TryReadStudentId(out var studentId))
        {
            return InvalidStudentId();
        }

        var result = await _holds.CreateAsync(classId, studentId, ct);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, title: result.ErrorTitle, detail: result.ErrorDetail);
    }

    [HttpGet("holds/{holdId}")]
    public async Task<IActionResult> Get(string holdId, CancellationToken ct)
    {
        var result = await _holds.GetAsync(holdId, ct);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, title: result.ErrorTitle, detail: result.ErrorDetail);
    }

    [HttpPost("holds/{holdId}/confirm")]
    public async Task<IActionResult> Confirm(string holdId, CancellationToken ct)
    {
        if (!TryReadStudentId(out var studentId))
        {
            return InvalidStudentId();
        }

        var result = await _holds.ConfirmAsync(holdId, studentId, ct);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, title: result.ErrorTitle, detail: result.ErrorDetail);
    }

    [HttpDelete("holds/{holdId}")]
    public async Task<IActionResult> Cancel(string holdId, CancellationToken ct)
    {
        if (!TryReadStudentId(out var studentId))
        {
            return InvalidStudentId();
        }

        var result = await _holds.CancelAsync(holdId, studentId, ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, title: result.ErrorTitle, detail: result.ErrorDetail);
    }

    private bool TryReadStudentId(out string studentId)
    {
        var raw = Request.Headers[StudentId.HeaderName].ToString();
        studentId = raw;
        return StudentId.IsValid(raw);
    }

    private ObjectResult InvalidStudentId() =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid student id",
            detail: "X-Student-Id must be exactly 17 ASCII digits.");
}
