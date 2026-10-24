namespace RequestsManagement.Api.Dtos;

/// <summary>Outcome for a single item in a bulk status update.</summary>
public enum BulkItemOutcome
{
    Updated,
    NotFound,
    Conflict,
    InvalidTransition,
}

/// <summary>
/// Per-item result within the bulk response.
/// RowVersion is populated on Updated (new server value) and on Conflict
/// (current server value, so the client can refresh and retry).
/// </summary>
public sealed record BulkItemResultDto(
    int Id,
    BulkItemOutcome Outcome,
    string? RowVersion,
    string? Detail);

/// <summary>
/// Response body for POST /api/requests/status/bulk.
/// Always HTTP 200 — partial failures are represented in Results, not via error codes.
/// </summary>
public sealed record BulkStatusUpdateResultDto(
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkItemResultDto> Results);
