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
    }
}
