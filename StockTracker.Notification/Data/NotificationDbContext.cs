using Microsoft.EntityFrameworkCore;
using StockTracker.Notification.Entities;

namespace StockTracker.Notification.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }

    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<WatchGroupNotificationState> WatchGroupNotificationStates => Set<WatchGroupNotificationState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.ProductCode).IsRequired().HasMaxLength(100);
            entity.Property(n => n.Size).IsRequired().HasMaxLength(50);
            entity.Property(n => n.Channel).HasConversion<string>();
            // Idempotency guard: aynı event (CommandId) aynı kullanıcıya aynı kanaldan iki kez gönderilemez.
            entity.HasIndex(n => new { n.CommandId, n.UserId, n.Channel }).IsUnique();
        });

        modelBuilder.Entity<WatchGroupNotificationState>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ProductCode).IsRequired().HasMaxLength(100);
            entity.Property(s => s.Size).IsRequired().HasMaxLength(50);
            entity.Property(s => s.LastKnownStatus).HasConversion<string>();
            entity.HasIndex(s => new { s.ProductCode, s.Size, s.StoreId }).IsUnique();
        });
    }
}
