using StockTracker.BrandDetection.Entities;

namespace StockTracker.BrandDetection.DTOs;

// Resolve isteği
public record ResolveRequest(string ProductCode);

// Resolve sonucu — tek aday
public record BrandCandidate(
    Guid BrandId,
    string BrandName,
    ConfidenceLevel Confidence,
    string MatchedPattern
);

// Tüm adaylar
public record ResolveResult(
    string ProductCode,
    bool IsResolved,
    List<BrandCandidate> Candidates
);

// Manuel seçim isteği (kullanıcı markayı kendisi seçtiğinde)
public record ManualResolveRequest(
    string ProductCode,
    Guid BrandId,
    string BrandName
);
