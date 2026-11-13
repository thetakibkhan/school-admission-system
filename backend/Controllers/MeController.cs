using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

/// <summary>Application history for the student identified by <c>X-Student-Id</c>.</summary>
[ApiController]
[Route("me")]
public sealed class MeController : ControllerBase
{
    private readonly HoldService _holds;

    // Program.cs is foundation-owned (R11) and registers no application services, so the service is
    // built from the two dependencies the container does provide.
    public MeController(AdmissionDbContext db, HoldOptions options)
    {
        _holds = new HoldService(db, options);
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
