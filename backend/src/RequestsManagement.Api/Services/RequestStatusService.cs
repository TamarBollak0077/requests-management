using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Domain;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Api.Services;

public sealed class RequestStatusService(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<RequestStatusService> logger)
    : IRequestStatusService
{
    public async Task<StatusUpdateResult> UpdateStatusAsync(
        int id,
        UpdateStatusDto dto,
        CancellationToken ct = default)
    {
        // ── 1. Parse client RowVersion (Base64 → byte[]) ─────────────────────
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

        // ── 3. Validate transition ────────────────────────────────────────────
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

        // ── 5. Apply the update ───────────────────────────────────────────────
        var previousStatus = request.Status;
        request.Status = newStatus;
        request.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // ── 6. Queue audit record ─────────────────────────────────────────────
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
}
