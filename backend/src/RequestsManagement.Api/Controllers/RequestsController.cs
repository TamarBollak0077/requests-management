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

    // POST /api/requests/status/bulk
    //
    // Body: { newStatus, changedBy, items: [{ id, rowVersion }] }
    // 400 — body validation fails (empty/oversized items, duplicate IDs, invalid status).
    // 200 — always returned for valid requests; per-item outcomes in results[].
    //       Partial failures (NotFound, Conflict, InvalidTransition) are embedded in
    //       the response body, not represented as HTTP error codes.
    [HttpPost("status/bulk")]
    [ProducesResponseType(typeof(BulkStatusUpdateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkStatusUpdateResultDto>> BulkUpdateStatusAsync(
        [FromBody] BulkStatusUpdateDto dto,
        CancellationToken ct)
    {
        var result = await statusService.BulkUpdateStatusAsync(dto, ct);
        return Ok(result);
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
