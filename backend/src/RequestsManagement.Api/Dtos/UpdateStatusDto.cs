using System.ComponentModel.DataAnnotations;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

public sealed class UpdateStatusDto
{
    /// <summary>
    /// Nullable so [Required] rejects a missing field; [ApiController] returns 400
    /// before the action runs. An unrecognised string (e.g. "Foo") is rejected at
    /// deserialization by JsonStringEnumConverter, also producing 400.
    /// </summary>
    [Required]
    public RequestStatus? NewStatus { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server rowversion as returned by the last GET response.
    /// Sent back verbatim so the service can use it as the optimistic concurrency token.
    /// </summary>
    [Required]
    public string RowVersion { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [MaxLength(100)]
    public string ChangedBy { get; init; } = string.Empty;
}
