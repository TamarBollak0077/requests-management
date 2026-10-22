using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public interface IRequestQueryService
{
    Task<PagedResult<RequestListItemDto>> GetPagedAsync(
        RequestQueryParameters parameters,
        CancellationToken ct = default);
}
