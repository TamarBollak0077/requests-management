using System.ComponentModel.DataAnnotations;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Query-string parameters for GET /api/requests.
/// Only pagination for now; filters and sorting are added later (T-201/T-202).
/// </summary>
public class RequestQueryParameters
{
    /// <summary>1-based page number.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater.")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// Number of records per page.
    /// Default 20; maximum 100 (assignment requirement R-1.7).
    /// </summary>
    [Range(1, 100, ErrorMessage = "pageSize must be between 1 and 100.")]
    public int PageSize { get; set; } = 20;
}
