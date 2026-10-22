using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Data.Configurations;

public class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("Requests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .UseIdentityColumn();

        builder.Property(r => r.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.OrganizationName)
            .IsRequired()
            .HasMaxLength(150);

        // Status and Priority stored as tinyint (byte) — keeps indexes narrow.
        // JSON serialization uses string via JsonStringEnumConverter (configured in Program.cs).
        builder.Property(r => r.Status)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(r => r.Priority)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(r => r.AssignedTo)
            .HasMaxLength(100);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // SQL Server rowversion: incremented automatically on every UPDATE.
        // EF adds WHERE RowVersion = @original_value to UPDATE statements.
        // DbUpdateConcurrencyException is thrown when 0 rows are affected.
        builder.Property(r => r.RowVersion)
            .IsRowVersion()
            .IsRequired();

        // Relationships
        builder.HasMany(r => r.StatusHistory)
            .WithOne(h => h.Request)
            .HasForeignKey(h => h.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes ──────────────────────────────────────────────────────────
        // All composite indexes include CreatedAt DESC as the trailing key so
        // that the common "most recent first" sort is covered without a sort
        // operator in the execution plan.  Id is added as a non-key INCLUDE
        // on the first index to support stable keyset pagination (future).

        // Default sort, date-range filter.  Used by almost every list query.
        builder.HasIndex(r => new { r.CreatedAt })
            .HasDatabaseName("IX_Requests_CreatedAt")
            .IsDescending(true);

        // Status filter + default sort; also covers GROUP BY Status (summary).
        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .HasDatabaseName("IX_Requests_Status_CreatedAt")
            .IsDescending(false, true);

        // Priority filter + default sort; also covers GROUP BY Priority (summary).
        builder.HasIndex(r => new { r.Priority, r.CreatedAt })
            .HasDatabaseName("IX_Requests_Priority_CreatedAt")
            .IsDescending(false, true);

        // "My requests" filter by handler.
        builder.HasIndex(r => new { r.AssignedTo, r.CreatedAt })
            .HasDatabaseName("IX_Requests_AssignedTo_CreatedAt")
            .IsDescending(false, true);

        // OrganizationName StartsWith filter and sort.
        // LIKE 'Acme%' is sargable on this index; LIKE '%Acme%' is not.
        // Full-text search (Contains) will still cause a scan — documented as
        // a known bottleneck in docs/performance.md.
        builder.HasIndex(r => r.OrganizationName)
            .HasDatabaseName("IX_Requests_OrganizationName");
    }
}
