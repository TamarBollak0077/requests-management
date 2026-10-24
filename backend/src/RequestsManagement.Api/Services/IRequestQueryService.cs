using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public interface IRequestQueryService
{
    Task<PagedResult<RequestListItemDto>> GetPagedAsync(
        RequestQueryParameters parameters,
        CancellationToken ct = default);

    Task<RequestSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns history entries for the given request ordered by ChangedAt descending.
    /// Returns null when the request does not exist (→ 404).
    /// Returns an empty list when the request exists but has no history (→ 200 []).
    /// </summary>
    Task<IReadOnlyList<RequestStatusHistoryItemDto>?> GetHistoryAsync(
        int requestId,
        CancellationToken ct = default);
}
