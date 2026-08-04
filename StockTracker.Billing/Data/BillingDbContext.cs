using Microsoft.EntityFrameworkCore;
using StockTracker.Billing.Entities;

namespace StockTracker.Billing.Data;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<UserPlan> UserPlans => Set<UserPlan>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    // Free plan seed'inin Id'si — kod tarafında (UserPlanService) yeni kullanıcıya atanacak planı
    // bulmak için kullanılıyor, isme göre string eşleştirmek yerine sabit bir Guid'e referans veriyor.
    public static readonly Guid FreePlanId = Guid.Parse("f1000000-0000-0000-0000-000000000001");
    public static readonly Guid PremiumPlanId = Guid.Parse("f1000000-0000-0000-0000-000000000002");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plan>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<UserPlan>(entity =>
        {
            entity.HasKey(up => up.Id);
            entity.HasIndex(up => up.UserId).IsUnique();
            entity.HasOne(up => up.Plan)
                .WithMany()
                .HasForeignKey(up => up.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.UserId).IsUnique();
            entity.Property(s => s.Platform).HasConversion<string>();
            entity.Property(s => s.Status).HasConversion<string>();
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasConversion<string>();
            entity.Property(e => e.EventId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.Provider, e.EventId }).IsUnique();
        });

        var seedCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Plan>().HasData(
            new Plan
            {
                Id = FreePlanId,
                Name = "Free",
                MaxTrackedProducts = 3,
                CheckFrequencyMinutes = 60,
                AppStoreProductId = null,
                PlayStoreProductId = null,
                IsActive = true,
                CreatedAt = seedCreatedAt
            },
            new Plan
            {
                Id = PremiumPlanId,
                Name = "Premium",
                MaxTrackedProducts = 50,
                CheckFrequencyMinutes = 5,
                // Gerçek store ürünleri Faz 4.2'de App Store Connect/Play Console'da oluşturulunca doldurulacak.
                AppStoreProductId = null,
                PlayStoreProductId = null,
                IsActive = true,
                CreatedAt = seedCreatedAt
            }
        );
    }
}
