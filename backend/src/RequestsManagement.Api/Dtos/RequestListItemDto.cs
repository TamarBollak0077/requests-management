using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Lightweight projection used in the paginated list.
/// RowVersion is Base64 so the client can store it and send it back
/// with a status-update request to enable Optimistic Concurrency.
/// </summary>
public sealed record RequestListItemDto(
    int Id,
    string Title,
    string OrganizationName,
    RequestStatus Status,
    RequestPriority Priority,
    string? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RowVersion);
