using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

public sealed record StatusCountDto(RequestStatus Status, int Count);

public sealed record PriorityCountDto(RequestPriority Priority, int Count);

public sealed record RequestSummaryDto(
    IReadOnlyList<StatusCountDto> ByStatus,
    IReadOnlyList<PriorityCountDto> ByPriority);
