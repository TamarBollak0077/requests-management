using Microsoft.AspNetCore.Mvc;
using RequestsManagement.Api.Dtos;
using RequestsManagement.Api.Services;

namespace RequestsManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RequestsController(IRequestQueryService queryService) : ControllerBase
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
}
