namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Generic paginated response envelope returned by every list endpoint.
/// TotalPages is derived so the client never needs to calculate it.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>Ceiling division; at least 1 even when TotalCount is 0.</summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;
}
