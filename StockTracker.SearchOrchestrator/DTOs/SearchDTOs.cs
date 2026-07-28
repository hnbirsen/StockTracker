namespace StockTracker.SearchOrchestrator.DTOs;

public record SearchLocationRequest(string City, string District);

public record SearchRequest(
    Guid UserId,
    string ProductCode,
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
