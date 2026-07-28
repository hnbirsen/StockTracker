using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StockTracker.BrandDetection.Data;
using StockTracker.BrandDetection.DTOs;
using StockTracker.BrandDetection.Entities;

namespace StockTracker.BrandDetection.Services;

public interface IBrandDetectionService
{
    Task<ResolveResult> ResolveAsync(string productCode);
    Task<ResolveResult> ManualResolveAsync(string productCode, Guid brandId, string brandName);
}

public class BrandDetectionService : IBrandDetectionService
{
    private readonly BrandDetectionDbContext _db;
    private readonly IProductServiceClient _productClient;

    public BrandDetectionService(BrandDetectionDbContext db, IProductServiceClient productClient)
    {
        _db = db;
        _productClient = productClient;
    }

    public async Task<ResolveResult> ResolveAsync(string productCode)
    {
        var code = productCode.Trim();
        var signatures = await _db.BrandCodeSignatures
            .Where(s => s.IsActive)
            .ToListAsync();

        // Tüm aktif pattern'leri dene, eşleşenleri topla
        var candidates = signatures
            .Where(s => Regex.IsMatch(code, s.RegexPattern, RegexOptions.IgnoreCase))
            .Select(s => new BrandCandidate(s.BrandId, s.BrandName, s.Confidence, s.RegexPattern))
            .OrderByDescending(c => c.Confidence)
            .ToList();

        // Tek ve yüksek güvenilirlikte eşleşme → Product Service'e otomatik kaydet
        if (candidates.Count == 1 && candidates[0].Confidence == ConfidenceLevel.High)
        {
            await _productClient.SaveMappingAsync(
                code,
                candidates[0].BrandId,
                "FormatMatch",
                "High"
            );
        }

        return new ResolveResult(code, candidates.Count > 0, candidates);
    }

    public async Task<ResolveResult> ManualResolveAsync(string productCode, Guid brandId, string brandName)
    {
        // Manuel seçimi Product Service'e kaydet
        await _productClient.SaveMappingAsync(productCode.Trim(), brandId, "Manual", "High");

        return new ResolveResult(
            productCode.Trim(),
            true,
            new List<BrandCandidate> { new(brandId, brandName, ConfidenceLevel.High, "ManualSelection") }
        );
    }
}
