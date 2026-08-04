namespace StockTracker.Shared.Contracts.Messages.V1;

// v1 sözleşmesi: breaking değişiklik gerekirse yeni tip V2 namespace'inde eklenir, bu tip değişmez.
// Identity Service, kayıt tamamlandıktan sonra fanout olarak publish eder (birden fazla dinleyici ilgi
// duyabilir — şu an yalnızca Billing Service, kullanıcıya otomatik Free plan atamak için tüketiyor).
public record UserRegisteredEvent(
    Guid UserId,
    string Email,
    DateTime RegisteredAt
);
