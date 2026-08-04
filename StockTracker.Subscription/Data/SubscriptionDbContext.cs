using Microsoft.EntityFrameworkCore;
using StockTracker.Subscription.Entities;

namespace StockTracker.Subscription.Data;

public class SubscriptionDbContext : DbContext
{
    public SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options) : base(options) { }

    public DbSet<WatchGroup> WatchGroups => Set<WatchGroup>();
    public DbSet<UserWatch> UserWatches => Set<UserWatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WatchGroup>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.ProductCode).IsRequired().HasMaxLength(100);
            entity.Property(w => w.Size).IsRequired().HasMaxLength(50);
            entity.Property(w => w.LastKnownStatus).HasConversion<string>();
            // Dedup araması bu index'ten geçer (StoreId nullable olduğundan Postgres'te unique constraint
            // NULL'ları birbirinden ayrı sayar — dedup mantığı bu yüzden application-level'da, WatchService'te yapılır).
            entity.HasIndex(w => new { w.ProductCode, w.Size, w.StoreId });
        });

        modelBuilder.Entity<UserWatch>(entity =>
        {
            entity.HasKey(uw => uw.Id);
            entity.HasIndex(uw => new { uw.UserId, uw.WatchGroupId }).IsUnique();
            entity.HasOne(uw => uw.WatchGroup)
                .WithMany()
                .HasForeignKey(uw => uw.WatchGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
