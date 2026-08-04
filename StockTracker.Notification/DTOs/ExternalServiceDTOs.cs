namespace StockTracker.Notification.DTOs;

// Identity Service GET /internal/users/{id} yanıtının ihtiyaç duyulan alt kümesi.
public record UserLookupResponse(Guid Id, string Email, string? FirstName, string? LastName, bool IsEmailVerified);
