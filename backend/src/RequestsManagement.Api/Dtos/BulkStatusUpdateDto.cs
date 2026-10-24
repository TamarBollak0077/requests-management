using System.ComponentModel.DataAnnotations;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Dtos;

/// <summary>
/// Request body for POST /api/requests/status/bulk.
/// All items in one call share the same target status and actor.
/// </summary>
public sealed class BulkStatusUpdateDto : IValidatableObject
{
    /// <summary>
    /// Nullable so [Required] rejects a missing or unrecognised value.
    /// JsonStringEnumConverter rejects unknown strings at deserialisation (→ 400).
    /// </summary>
    [Required]
    public RequestStatus? NewStatus { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MaxLength(100)]
    public string ChangedBy { get; init; } = string.Empty;

    [Required]
    public List<BulkItemDto> Items { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Items.Count == 0)
            yield return new ValidationResult(
                "items must contain at least one entry.", [nameof(Items)]);

        if (Items.Count > 100)
            yield return new ValidationResult(
                $"items must not exceed 100 entries (received {Items.Count}).",
                [nameof(Items)]);

        var duplicateIds = Items
            .GroupBy(i => i.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
            yield return new ValidationResult(
                $"items contains duplicate IDs: {string.Join(", ", duplicateIds)}.",
                [nameof(Items)]);

        // Validate each item's RowVersion format.
        // IValidatableObject on BulkItemDto itself would NOT be called by ASP.NET Core
        // for collection items — only DataAnnotations are applied recursively.
        // We validate here so malformed tokens produce HTTP 400, not a per-item outcome.
        for (int i = 0; i < Items.Count; i++)
        {
            var rv = Items[i].RowVersion;
            if (string.IsNullOrEmpty(rv)) continue; // [Required] on BulkItemDto handles this

            byte[]? bytes = null;
            try { bytes = Convert.FromBase64String(rv); }
            catch (FormatException) { }

            if (bytes is null)
                yield return new ValidationResult(
                    $"items[{i}].rowVersion is not valid Base64.", [nameof(Items)]);
            else if (bytes.Length != 8)
                yield return new ValidationResult(
                    $"items[{i}].rowVersion must decode to 8 bytes (SQL Server rowversion); got {bytes.Length}.",
                    [nameof(Items)]);
        }
    }
}

/// <summary>Per-item entry: the request to update and the client's concurrency token.</summary>
public sealed class BulkItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Id must be a positive integer.")]
    public int Id { get; init; }

    /// <summary>Base64-encoded rowversion from the last GET response.</summary>
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}
