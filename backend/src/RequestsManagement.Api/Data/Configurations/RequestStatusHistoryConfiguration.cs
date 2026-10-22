using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Data.Configurations;

public class RequestStatusHistoryConfiguration : IEntityTypeConfiguration<RequestStatusHistory>
{
    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.ToTable("RequestStatusHistory");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .UseIdentityColumn();

        builder.Property(h => h.RequestId)
            .IsRequired();

        builder.Property(h => h.PreviousStatus)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(h => h.NewStatus)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(h => h.ChangedAt)
            .IsRequired();

        builder.Property(h => h.ChangedBy)
            .IsRequired()
            .HasMaxLength(100);

        // Index on (RequestId, ChangedAt DESC) — covers
        // GET /api/requests/{id}/history ordered newest-first.
        builder.HasIndex(h => new { h.RequestId, h.ChangedAt })
            .HasDatabaseName("IX_RequestStatusHistory_RequestId_ChangedAt")
            .IsDescending(false, true);
    }
}
