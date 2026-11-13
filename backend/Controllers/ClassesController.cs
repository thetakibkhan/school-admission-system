using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.Dtos;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

/// <summary>
/// Class administration and seat availability (PRD section 6, routes 1-3). None of these routes
/// requires <c>X-Student-Id</c>.
/// </summary>
[ApiController]
[Route("classes")]
public sealed class ClassesController : ControllerBase
{
    private readonly ClassService _service;

    public ClassesController(ClassService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClassRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);

        return result.Status switch
        {
            ServiceStatus.Ok => Created($"/classes/{result.Value!.Id}", result.Value),
            ServiceStatus.Validation => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid class",
                detail: result.Detail),
            ServiceStatus.Conflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate class name",
                detail: result.Detail),
            _ => throw new InvalidOperationException($"Unexpected create status {result.Status}.")
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _service.ListAsync(ct);
        return Ok(result.Value);
    }

    [HttpGet("{classId}")]
    public async Task<IActionResult> Get(string classId, CancellationToken ct)
    {
        var result = await _service.GetAsync(classId, ct);

        return result.Status switch
        {
            ServiceStatus.Ok => Ok(result.Value),
            ServiceStatus.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Class not found",
                detail: result.Detail),
            _ => throw new InvalidOperationException($"Unexpected get status {result.Status}.")
        };
    }
}
