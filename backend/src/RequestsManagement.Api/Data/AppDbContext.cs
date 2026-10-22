using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Data.Configurations;
using RequestsManagement.Api.Domain;

namespace RequestsManagement.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestStatusHistory> RequestStatusHistory => Set<RequestStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RequestConfiguration());
        modelBuilder.ApplyConfiguration(new RequestStatusHistoryConfiguration());
    }
}
