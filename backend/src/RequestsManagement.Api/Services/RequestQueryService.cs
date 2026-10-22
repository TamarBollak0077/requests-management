using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Domain;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public sealed class RequestQueryService(AppDbContext db) : IRequestQueryService
{
    public async Task<PagedResult<RequestListItemDto>> GetPagedAsync(
        RequestQueryParameters p,
        CancellationToken ct = default)
    {
        // ── Step 1: base query (IQueryable — nothing hits the DB yet) ─────────
        // AsNoTracking: read-only path; skip EF identity-map overhead.
        var query = db.Requests.AsNoTracking();

        // ── Step 2: apply filters (AND-combined predicates) ───────────────────
        // Each guard is a null/empty check so the WHERE clause is emitted only
        // when the filter is actually requested.  All comparisons are on indexed
        // columns wherever possible (see RequestConfiguration indexes).

        if (p.Status.HasValue)
            query = query.Where(r => r.Status == p.Status.Value);

        if (p.Priority.HasValue)
            query = query.Where(r => r.Priority == p.Priority.Value);

        if (!string.IsNullOrWhiteSpace(p.OrganizationName))
        {
            // StartsWith → LIKE 'term%' — sargable on IX_Requests_OrganizationName.
            // SQL Server CI collation handles case-insensitivity without ToLower().
            var prefix = p.OrganizationName.Trim();
            query = query.Where(r => r.OrganizationName.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(p.AssignedTo))
        {
            // Exact match; rows with NULL AssignedTo are excluded (correct intent).
            var assignedTo = p.AssignedTo.Trim();
            query = query.Where(r => r.AssignedTo == assignedTo);
        }

        if (p.CreatedFrom.HasValue)
            query = query.Where(r => r.CreatedAt >= p.CreatedFrom.Value);

        if (p.CreatedTo.HasValue)
        {
            // The client sends a calendar date with no time (e.g. "2024-06-30" → midnight).
            // Filtering with <= midnight excludes every record created after 00:00 on that day.
            // Fix: advance to the exclusive start of the next day and use strict <.
            // The constant is computed once in C# — no function is applied to the DB column,
            // so the filter remains sargable on IX_Requests_CreatedAt.
            var exclusiveUpperBound = p.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(r => r.CreatedAt < exclusiveUpperBound);
        }

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            // LIKE '%term%' on Title OR OrganizationName.
            // Leading wildcard prevents index use — full scan on both columns.
            // Applied AFTER all sargable filters to keep the scanned set small.
            var term = p.Search.Trim();
            query = query.Where(r =>
                r.Title.Contains(term) ||
                r.OrganizationName.Contains(term));
        }

        // ── Step 3: COUNT (with all filters applied) ─────────────────────────
        // EF strips ORDER BY when translating CountAsync.
        var totalCount = await query.CountAsync(ct);

        // ── Step 4: sort ──────────────────────────────────────────────────────
        var ordered = ApplySort(query, p.SortBy, p.SortDirection);

        // ── Step 5: paginate and project ─────────────────────────────────────
        // Skip/Take → OFFSET n ROWS FETCH NEXT m ROWS ONLY (SQL Server).
        // Select runs inside the SQL query — only needed columns travel over the wire.
        // RowVersion (byte[]) cannot be base-64 encoded in SQL, so we fetch it raw
        // and convert it in the in-process Select below.
        var items = await ordered
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(r => new RequestListItemDto(
                r.Id,
                r.Title,
                r.OrganizationName,
                r.Status,
                r.Priority,
                r.AssignedTo,
                r.CreatedAt,
                r.UpdatedAt,
                Convert.ToBase64String(r.RowVersion)))
            .ToListAsync(ct);

        return new PagedResult<RequestListItemDto>(
            items, totalCount, p.Page, p.PageSize);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies ORDER BY from a validated (sortBy, sortDirection) pair.
    ///
    /// Whitelist approach: only fields in AllowedSortFields reach this method.
    /// RequestQueryParameters.Validate() rejects anything else at the model-
    /// binding stage, so the switch default is a safety net, not dead code.
    ///
    /// Id is always appended as a unique tiebreaker.  This guarantees page
    /// stability: two rows with identical primary-sort values are always split
    /// the same way across pages.
    /// </summary>
    private static IOrderedQueryable<Request> ApplySort(
        IQueryable<Request> query,
        string sortBy,
        string sortDirection)
    {
        bool desc = sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Request> ordered = sortBy.ToLowerInvariant() switch
        {
            "priority"  => desc
                ? query.OrderByDescending(r => r.Priority)
                : query.OrderBy(r => r.Priority),

            "title"     => desc
                ? query.OrderByDescending(r => r.Title)
                : query.OrderBy(r => r.Title),

            // "createdat" (default) — also handles any unexpected value after validation
            _           => desc
                ? query.OrderByDescending(r => r.CreatedAt)
                : query.OrderBy(r => r.CreatedAt),
        };

        // Unique tiebreaker — same direction as primary sort keeps the index scan
        // direction consistent and avoids a sort-merge on Id.
        return desc
            ? ordered.ThenByDescending(r => r.Id)
            : ordered.ThenBy(r => r.Id);
    }
}
