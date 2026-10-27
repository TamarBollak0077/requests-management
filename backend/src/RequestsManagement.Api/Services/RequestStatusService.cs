using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Caching.Memory;
using RequestsManagement.Api.Cache;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Domain;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public sealed class RequestStatusService(
    AppDbContext db,
    TimeProvider timeProvider,
    IMemoryCache cache,
    ILogger<RequestStatusService> logger)
    : IRequestStatusService
{
    public async Task<StatusUpdateResult> UpdateStatusAsync(
        int id,
        UpdateStatusDto dto,
        CancellationToken ct = default)
    {
        byte[] clientRowVersion;
        try
        {
            clientRowVersion = Convert.FromBase64String(dto.RowVersion);
        }
        catch (FormatException)
        {
            return new StatusUpdateResult.InvalidTransition(
                "RowVersion is not a valid Base64 string.");
        }

        // ── 2. Load entity (tracked — required for the EF UPDATE) ────────────
        var request = await db.Requests.FindAsync(new object[] { id }, ct);
        if (request is null)
            return new StatusUpdateResult.NotFound();

        var newStatus = dto.NewStatus!.Value;
        if (!StatusTransitionPolicy.CanTransition(request.Status, newStatus))
            return new StatusUpdateResult.InvalidTransition(
                $"Transition from '{request.Status}' to '{newStatus}' is not allowed.");

        // ── 4. Inject the client's RowVersion as the concurrency token ────────
        //
        // FindAsync populates the EF entry with the DB's *current* RowVersion.
        // If we left it as-is, EF would compare the DB value against itself —
        // always equal, never a conflict.  By overwriting OriginalValue with the
        // client's token, EF generates:
        //   UPDATE Requests SET … WHERE Id = @id AND RowVersion = @clientToken
        // If another writer changed the row after the client read it, 0 rows are
        // affected and EF throws DbUpdateConcurrencyException → 409.
        db.Entry(request).Property(r => r.RowVersion).OriginalValue = clientRowVersion;

        var previousStatus = request.Status;
        request.Status = newStatus;
        request.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Added to the same DbContext so it goes in the same transaction below.
        db.RequestStatusHistory.Add(new RequestStatusHistory
        {
            RequestId = id,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedAt = request.UpdatedAt,
            ChangedBy = dto.ChangedBy,
        });

        // ── 7. One SaveChanges = one transaction ──────────────────────────────
        // EF wraps multiple entity changes in an implicit transaction.
        // If the UPDATE fails (DbUpdateConcurrencyException), SQL Server rolls
        // back the whole transaction — the history row is never inserted.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            var dbValues = await entry.GetDatabaseValuesAsync(ct);

            if (dbValues is null)
            {
                // The row was deleted between our load and the UPDATE attempt.
                return new StatusUpdateResult.NotFound();
            }

            logger.LogWarning(
                "Concurrency conflict updating Request {Id}: client RowVersion is stale", id);

            return new StatusUpdateResult.Conflict(
                dbValues.GetValue<RequestStatus>(nameof(Request.Status)),
                Convert.ToBase64String(dbValues.GetValue<byte[]>(nameof(Request.RowVersion))));
        }

        cache.Remove(CacheKeys.Summary);
        logger.LogDebug("Summary cache INVALIDATED (single update, Request {Id})", id);

        logger.LogInformation(
            "Request {Id} status updated from {Previous} to {New} by {Actor}",
            id, previousStatus, newStatus, dto.ChangedBy);

        // After a successful save, EF reloads the DB-generated RowVersion into
        // request.RowVersion — the client must use this value for future updates.
        return new StatusUpdateResult.Success(new UpdateStatusResultDto(
            id,
            request.Status,
            Convert.ToBase64String(request.RowVersion),
            request.UpdatedAt));
    }

    public async Task<BulkStatusUpdateResultDto> BulkUpdateStatusAsync(
        BulkStatusUpdateDto dto,
        CancellationToken ct = default)
    {
        var newStatus = dto.NewStatus!.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // ── 1. Load all requested entities in a single round-trip ────────────
        // Tracked (no AsNoTracking) — we need EF to generate UPDATE statements.
        var ids = dto.Items.Select(i => i.Id).ToList();
        var requestsById = await db.Requests
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var results = new List<BulkItemResultDto>(dto.Items.Count);
        int succeeded = 0;

        // One SaveChangesAsync per item so that a concurrency failure on item N
        // does not prevent items N+1…100 from being saved (Partial Success).
        // Each SaveChanges wraps UPDATE Requests + INSERT RequestStatusHistory
        // in a single implicit transaction — atomicity per item is preserved.
        foreach (var item in dto.Items)
        {
            if (!requestsById.TryGetValue(item.Id, out var request))
            {
                results.Add(new BulkItemResultDto(
                    item.Id, BulkItemOutcome.NotFound, null, "Request not found."));
                continue;
            }

            // DTO validation (BulkStatusUpdateDto.Validate) already guarantees valid
            // Base64 with exactly 8 bytes, so no try/catch needed here.
            byte[] clientRowVersion = Convert.FromBase64String(item.RowVersion);

            if (!StatusTransitionPolicy.CanTransition(request.Status, newStatus))
            {
                results.Add(new BulkItemResultDto(
                    item.Id, BulkItemOutcome.InvalidTransition, null,
                    $"Transition from '{request.Status}' to '{newStatus}' is not allowed."));
                continue;
            }

            // Same mechanism as the single-item update: overriding OriginalValue
            // makes EF emit WHERE RowVersion = @clientToken in the UPDATE.
            db.Entry(request).Property(r => r.RowVersion).OriginalValue = clientRowVersion;

            var previousStatus = request.Status;
            request.Status = newStatus;
            request.UpdatedAt = now;

            // ── Queue audit record in the same DbContext ──────────────────────
            var historyEntry = new RequestStatusHistory
            {
                RequestId = item.Id,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                ChangedAt = now,
                ChangedBy = dto.ChangedBy,
            };
            db.RequestStatusHistory.Add(historyEntry);

            // ── Save (one implicit transaction: UPDATE + INSERT) ──────────────
            try
            {
                await db.SaveChangesAsync(ct);
                succeeded++;
                cache.Remove(CacheKeys.Summary);
                logger.LogDebug("Summary cache INVALIDATED (bulk update, Request {Id})", item.Id);
                results.Add(new BulkItemResultDto(
                    item.Id,
                    BulkItemOutcome.Updated,
                    Convert.ToBase64String(request.RowVersion),
                    null));
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // ── Bulk rule: detach BEFORE the next SaveChanges ─────────────
                // After a concurrency failure the Request entry remains Modified
                // and the history entry remains Added in the ChangeTracker.
                // If we leave them there, the next iteration's SaveChanges will
                // retry both, producing a false second failure or an orphan
                // history row.  Detaching both prevents any accidental retry.
                EntityEntry failedEntry = ex.Entries.Single();
                var dbValues = await failedEntry.GetDatabaseValuesAsync(ct);

                failedEntry.State = EntityState.Detached;
                db.Entry(historyEntry).State = EntityState.Detached;

                if (dbValues is null)
                {
                    results.Add(new BulkItemResultDto(
                        item.Id, BulkItemOutcome.NotFound, null,
                        "Request was deleted by another user."));
                }
                else
                {
                    results.Add(new BulkItemResultDto(
                        item.Id,
                        BulkItemOutcome.Conflict,
                        Convert.ToBase64String(dbValues.GetValue<byte[]>(nameof(Request.RowVersion))),
                        "The request was modified by another user."));
                }
            }
        }

        logger.LogInformation(
            "Bulk update to {NewStatus} by {Actor}: {Succeeded} succeeded, {Failed} failed out of {Total}",
            newStatus, dto.ChangedBy, succeeded, dto.Items.Count - succeeded, dto.Items.Count);

        return new BulkStatusUpdateResultDto(succeeded, dto.Items.Count - succeeded, results);
    }
}
