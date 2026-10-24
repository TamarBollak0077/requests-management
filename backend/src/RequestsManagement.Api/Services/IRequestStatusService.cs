using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public interface IRequestStatusService
{
    Task<StatusUpdateResult> UpdateStatusAsync(
        int id,
        UpdateStatusDto dto,
        CancellationToken ct = default);

    Task<BulkStatusUpdateResultDto> BulkUpdateStatusAsync(
        BulkStatusUpdateDto dto,
        CancellationToken ct = default);
}
