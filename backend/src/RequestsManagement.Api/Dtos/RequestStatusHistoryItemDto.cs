using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// One audit entry returned by GET /api/requests/{id}/history.
/// PreviousStatus and NewStatus are serialized as strings by the global
/// JsonStringEnumConverter registered in Program.cs.
/// </summary>
public sealed record RequestStatusHistoryItemDto(
    int RequestId,
    RequestStatus PreviousStatus,
    RequestStatus NewStatus,
    DateTime ChangedAt,
    string ChangedBy);
