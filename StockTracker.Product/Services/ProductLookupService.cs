using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StockTracker.Product.Data;
using StockTracker.Product.DTOs;
using StockTracker.Product.Entities;
using System.Text.Json;

namespace StockTracker.Product.Services;

public interface IProductLookupService
{
    Task<ProductLookupResult> LookupAsync(string productCode);
    Task SaveMappingAsync(string productCode, Guid brandId, ResolvedVia resolvedVia, ConfidenceLevel confidence, string? productUrl = null);
}

public class ProductLookupService : IProductLookupService
{
    private readonly ProductDbContext _db;
    private readonly ICodeFormatDetector _formatDetector;
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheMetricsService _metrics;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24);

    public ProductLookupService(
        ProductDbContext db,
        ICodeFormatDetector formatDetector,
        IConnectionMultiplexer redis,
        ICacheMetricsService metrics)
    {
        _db = db;
        _formatDetector = formatDetector;
        _redis = redis;
        _metrics = metrics;
    }

    public async Task<ProductLookupResult> LookupAsync(string productCode)
    {
        var code = productCode.Trim();
        var cache = _redis.GetDatabase();
        var cacheKey = $"product:lookup:{code}";

        // 1. Redis cache kontrolü
        var cached = await cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            var cachedResult = JsonSerializer.Deserialize<ProductLookupResult>(cached.ToString());
            if (cachedResult is not null)
            {
                await _metrics.RecordHitAsync(cacheKey);
                return cachedResult with { FromCache = true };
            }
        }

        await _metrics.RecordMissAsync(cacheKey);

        // 2. DB kontrolü
        var mapping = await _db.ProductBrandMaps
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => p.ProductCode == code);

        var codeType = _formatDetector.Detect(code);

        ProductLookupResult result;

        if (mapping is not null)
        {
            result = new ProductLookupResult(
                ProductCode: code,
                CodeType: codeType,
                IsResolved: true,
                BrandId: mapping.BrandId,
                BrandName: mapping.Brand.Name,
                ScraperQueueName: mapping.Brand.ScraperQueueName,
                ProductUrl: mapping.ProductUrl,
                Confidence: mapping.Confidence,
                ResolvedVia: mapping.ResolvedVia,
                FromCache: false
            );

            await cache.StringSetAsync(cacheKey, JsonSerializer.Serialize(result), _cacheTtl);
        }
        else
        {
            result = new ProductLookupResult(
                ProductCode: code,
                CodeType: codeType,
                IsResolved: false,
                BrandId: null,
                BrandName: null,
                ScraperQueueName: null,
                ProductUrl: null,
                Confidence: null,
                ResolvedVia: null,
                FromCache: false
            );
        }

        return result;
    }

    public async Task SaveMappingAsync(string productCode, Guid brandId, ResolvedVia resolvedVia, ConfidenceLevel confidence, string? productUrl = null)
    {
        var code = productCode.Trim();
        var existing = await _db.ProductBrandMaps.FirstOrDefaultAsync(p => p.ProductCode == code);

        if (existing is not null)
        {
            existing.BrandId = brandId;
            existing.ResolvedVia = resolvedVia;
            existing.Confidence = confidence;
            existing.ProductUrl = productUrl;
            existing.ResolvedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ProductBrandMaps.Add(new ProductBrandMap
            {
                ProductCode = code,
                BrandId = brandId,
                ResolvedVia = resolvedVia,
                Confidence = confidence,
                ProductUrl = productUrl
            });
        }

        await _db.SaveChangesAsync();

        // Cache'i temizle (mapping değişti, bayat veri kalmasın)
        var cache = _redis.GetDatabase();
        await cache.KeyDeleteAsync($"product:lookup:{code}");
    }
}
