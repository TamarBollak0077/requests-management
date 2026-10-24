using RequestsManagement.Api.Domain;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

/// <summary>
/// Discriminated result type returned by RequestStatusService.
/// The service never throws for expected outcomes — the controller
/// pattern-matches this to produce the correct HTTP response code.
/// </summary>
public abstract class StatusUpdateResult
{
    public sealed class Success(UpdateStatusResultDto dto) : StatusUpdateResult
    {
        public UpdateStatusResultDto Dto { get; } = dto;
    }

    public sealed class NotFound : StatusUpdateResult { }

    public sealed class InvalidTransition(string message) : StatusUpdateResult
    {
        public string Message { get; } = message;
    }

    public sealed class Conflict(RequestStatus currentStatus, string currentRowVersion)
        : StatusUpdateResult
    {
        public RequestStatus CurrentStatus { get; } = currentStatus;
        public string CurrentRowVersion { get; } = currentRowVersion;
    }
}
