using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

/// <summary>Application history for the student identified by <c>X-Student-Id</c>.</summary>
[ApiController]
[Route("me")]
public sealed class MeController : ControllerBase
{
    private readonly HoldService _holds;

    // HoldService is registered by Program.cs (foundation-owned, R11); see the handoff line in the
    // task report. Until that registration lands, every hold route returns 500.
    public MeController(HoldService holds)
    {
        _holds = holds;
    }

    [HttpGet("holds")]
    public async Task<IActionResult> ListHolds(CancellationToken ct)
    {
        var raw = Request.Headers[StudentId.HeaderName].ToString();
        if (!StudentId.IsValid(raw))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid student id",
                detail: "X-Student-Id must be exactly 17 ASCII digits.");
        }

        var result = await _holds.ListMineAsync(raw, ct);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, title: result.ErrorTitle, detail: result.ErrorDetail);
    }
}
