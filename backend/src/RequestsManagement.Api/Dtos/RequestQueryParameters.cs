using System.ComponentModel.DataAnnotations;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Query-string parameters for GET /api/requests.
///
/// Filtering semantics (all filters combined with AND):
///   Status          — exact enum match; null = no filter
///   Priority        — exact enum match; null = no filter
///   OrganizationName— StartsWith (prefix), case-insensitive via DB collation
///   AssignedTo      — exact match, case-insensitive via DB collation;
///                     rows where AssignedTo IS NULL are excluded when this filter is set
///   CreatedFrom/To  — inclusive bounds on CreatedAt (>=, <=); UTC expected
///   Search          — LIKE '%term%' on Title OR OrganizationName (OR, not AND);
///                     NOT index-covered — causes a full scan; documented as known bottleneck
///
/// Case sensitivity: SQL Server default collation (SQL_Latin1_General_CP1_CI_AS) is
/// case-insensitive, so string comparisons work correctly without ToLower().
/// Using ToLower() would break index sargability — it is intentionally avoided here.
/// </summary>
public class RequestQueryParameters : IValidatableObject
{
    // ── Pagination ────────────────────────────────────────────────────────────

    [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater.")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "pageSize must be between 1 and 100.")]
    public int PageSize { get; set; } = 20;

    // ── Filters ───────────────────────────────────────────────────────────────

    public RequestStatus? Status { get; set; }

    public RequestPriority? Priority { get; set; }

    /// <summary>Prefix match (StartsWith). Sargable on IX_Requests_OrganizationName.</summary>
    [MaxLength(150, ErrorMessage = "organizationName filter must be 150 characters or less.")]
    public string? OrganizationName { get; set; }

    /// <summary>Exact match. Case-insensitive via SQL Server collation.</summary>
    [MaxLength(100, ErrorMessage = "assignedTo filter must be 100 characters or less.")]
    public string? AssignedTo { get; set; }

    /// <summary>Inclusive lower bound on CreatedAt.</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>Inclusive upper bound on CreatedAt.</summary>
    public DateTime? CreatedTo { get; set; }

    /// <summary>
    /// Free-text search across Title OR OrganizationName.
    /// Translated to LIKE '%term%' — not index-covered, causes a table/index scan.
    /// Applied last to minimize rows scanned.
    /// </summary>
    [MaxLength(100, ErrorMessage = "search must be 100 characters or less.")]
    public string? Search { get; set; }

    // ── Sorting ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Field to sort by. Validated against AllowedSortFields (whitelist).
    /// Id is always appended as a unique tiebreaker — never exposed as a sortBy option.
    /// </summary>
    public string SortBy { get; set; } = "createdAt";

    /// <summary>"asc" or "desc" (case-insensitive).</summary>
    public string SortDirection { get; set; } = "desc";

    // ── Validation ────────────────────────────────────────────────────────────

    public static readonly IReadOnlySet<string> AllowedSortFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "createdAt",
            "priority",
            "title"
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (!AllowedSortFields.Contains(SortBy))
            yield return new ValidationResult(
                $"sortBy must be one of: {string.Join(", ", AllowedSortFields)}.",
                [nameof(SortBy)]);

        if (!SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) &&
            !SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult(
                "sortDirection must be 'asc' or 'desc'.",
                [nameof(SortDirection)]);

        if (CreatedFrom.HasValue && CreatedTo.HasValue && CreatedFrom > CreatedTo)
            yield return new ValidationResult(
                "createdFrom must not be later than createdTo.",
                [nameof(CreatedFrom), nameof(CreatedTo)]);
    }
}
