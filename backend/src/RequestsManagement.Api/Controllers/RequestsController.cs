using Microsoft.AspNetCore.Mvc;
using RequestsManagement.Api.Dtos;
using RequestsManagement.Api.Services;

namespace RequestsManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RequestsController(
    IRequestQueryService queryService,
    IRequestStatusService statusService) : ControllerBase
{
    // GET /api/requests?page=1&pageSize=20
    //
    // [ApiController] validates RequestQueryParameters automatically using DataAnnotations.
    // If page < 1 or pageSize > 100, the framework returns HTTP 400 ValidationProblemDetails
    // before this method body runs — no manual ModelState check needed.
    //
    // CancellationToken is bound from the HTTP request's abort signal.
    // If the client disconnects, EF Core cancels the in-flight SQL query.
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RequestListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<RequestListItemDto>>> GetAsync(
        [FromQuery] RequestQueryParameters parameters,
        CancellationToken ct)
    {
        var result = await queryService.GetPagedAsync(parameters, ct);
        return Ok(result);
    }

    // Declared before any {id} route so ASP.NET Core matches "summary" literally
    // and never interprets it as an id segment.
    [HttpGet("summary")]
    [ProducesResponseType(typeof(RequestSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestSummaryDto>> GetSummaryAsync(CancellationToken ct)
    {
        var result = await queryService.GetSummaryAsync(ct);
        return Ok(result);
    }

    // GET /api/requests/{id}/history
    //
    // 200 — array of history entries, newest first.  Empty array when no history yet.
    // 404 — request not found.
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<RequestStatusHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RequestStatusHistoryItemDto>>> GetHistoryAsync(
        int id,
        CancellationToken ct)
    {
        var history = await queryService.GetHistoryAsync(id, ct);
        if (history is null)
            return NotFound();
        return Ok(history);
    }

    // PATCH /api/requests/{id}/status
    //
    // Body: { newStatus, rowVersion (Base64), changedBy }
    // 200 — update applied; body contains new RowVersion for subsequent updates.
    // 400 — invalid body or forbidden transition (ProblemDetails).
    // 404 — request not found.
    // 409 — another user updated the row after the client read it (ProblemDetails
    //       includes currentStatus and currentRowVersion for UI refresh).
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(UpdateStatusResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PatchStatusAsync(
        int id,
        [FromBody] UpdateStatusDto dto,
        CancellationToken ct)
    {
        var result = await statusService.UpdateStatusAsync(id, dto, ct);

        return result switch
        {
            StatusUpdateResult.Success s       => Ok(s.Dto),
            StatusUpdateResult.NotFound        => NotFound(),
            StatusUpdateResult.InvalidTransition t => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Status Transition",
                detail: t.Message),
            StatusUpdateResult.Conflict c      => StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Concurrency Conflict",
                    Detail = "The request was modified by another user. Refresh and try again.",
                    Extensions =
                    {
                        ["currentStatus"]     = (object?)c.CurrentStatus.ToString(),
                        ["currentRowVersion"] = (object?)c.CurrentRowVersion,
                    },
                }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
