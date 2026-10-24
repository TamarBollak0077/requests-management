using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Returned on HTTP 200 after a successful status update.
/// RowVersion is the NEW server-generated value — the client must use this for
/// any subsequent update on the same request.
/// </summary>
public sealed record UpdateStatusResultDto(
    int Id,
    RequestStatus Status,
    string RowVersion,   // Base64-encoded new rowversion
    DateTime UpdatedAt);
