using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public sealed class RequestQueryService(AppDbContext db) : IRequestQueryService
{
    public async Task<PagedResult<RequestListItemDto>> GetPagedAsync(
        RequestQueryParameters parameters,
        CancellationToken ct = default)
    {
        // Step 1 — base query (no materialisation yet; just an IQueryable<Request>).
        // AsNoTracking: we never update these entities here, so EF skips identity-map
        // tracking — lower memory, faster for pure reads.
        var baseQuery = db.Requests.AsNoTracking();

        // Step 2 — total count.
        // Runs: SELECT COUNT(*) FROM [Requests] AS [r]
        // EF Core automatically strips the ORDER BY from COUNT queries.
        var totalCount = await baseQuery.CountAsync(ct);

        // Step 3 — ordered, paginated projection.
        // OrderByDescending + ThenByDescending gives a deterministic order:
        //   primary key  = CreatedAt DESC  (newest first)
        //   tiebreaker   = Id DESC         (stable when many rows share the same second)
        //
        // Skip((page-1)*pageSize).Take(pageSize) maps to:
        //   OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
        // which SQL Server executes entirely on the server — no full-table load.
        //
        // Select projects into the DTO *inside the SQL query* (SELECT column list),
        // so only the needed columns are transferred over the wire.
        // RowVersion (byte[]) is converted to Base64 here; EF translates the rest.
        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
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
            items, totalCount, parameters.Page, parameters.PageSize);
    }
}
