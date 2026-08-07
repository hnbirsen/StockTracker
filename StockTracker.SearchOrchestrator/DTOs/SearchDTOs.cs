namespace StockTracker.SearchOrchestrator.DTOs;

public record SearchLocationRequest(string City, string District);

// ProductCode VE ProductUrl ikisi de opsiyonel ama en az biri dolu olmalı — kullanıcı ya bildiği ürün
// kodunu ya da doğrudan ürün sayfası linkini yapıştırabilir. İkisi de doluysa ProductCode öncelikli
// (geriye dönük uyumluluk). Yalnızca ProductUrl verildiğinde BrandCodeSignature regex katmanı tamamen
// atlanır — bkz. SearchOrchestratorService.SearchAsync ve IProductLookupService.LookupByUrlAsync.
public record SearchRequest(
    Guid UserId,
    string? ProductCode,
    string? ProductUrl,
    string Size,
    List<SearchLocationRequest>? Locations
);

public record BrandCandidateResponse(
    Guid BrandId,
    string BrandName,
    string Confidence,
    string MatchedPattern
);

public record SearchResponse(
    Guid SearchId,
    string Status,
    string Message,
    List<BrandCandidateResponse>? Candidates
);
