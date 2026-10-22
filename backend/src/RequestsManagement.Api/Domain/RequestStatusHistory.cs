namespace RequestsManagement.Api.Domain;

public class RequestStatusHistory
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public RequestStatus PreviousStatus { get; set; }

    public RequestStatus NewStatus { get; set; }

    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// User identifier or display name provided by the caller.
    /// No authentication layer — caller supplies this value.
    /// This is a known limitation (documented in decisions.md).
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;

    // Navigation
    public Request Request { get; set; } = null!;
}
