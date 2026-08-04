namespace StockTracker.Billing.Entities;

public enum SubscriptionStatus
{
    Active = 0,
    GracePeriod = 1,
    Cancelled = 2,
    Expired = 3,
    Refunded = 4,
    Unknown = 5
}
