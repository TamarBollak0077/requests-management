namespace RequestsManagement.Api.Domain;

public class Request
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string OrganizationName { get; set; } = string.Empty;

    public RequestStatus Status { get; set; }

    public RequestPriority Priority { get; set; }

    /// <summary>
    /// Identifier or name of the person handling this request.
    /// </summary>
    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// SQL Server rowversion — automatically incremented on every UPDATE.
    /// Used for Optimistic Concurrency; never set manually.
    /// EF translates WHERE Id=@id AND RowVersion=@v into the UPDATE statement.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public ICollection<RequestStatusHistory> StatusHistory { get; set; } = [];
}
