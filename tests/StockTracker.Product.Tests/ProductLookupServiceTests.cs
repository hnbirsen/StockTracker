using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StackExchange.Redis;
using StockTracker.Product.Data;
using StockTracker.Product.DTOs;
using StockTracker.Product.Entities;
using StockTracker.Product.Services;

namespace StockTracker.Product.Tests;

public class ProductLookupServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _database = new();
    private readonly Mock<ICacheMetricsService> _metrics = new();

    private static ProductDbContext CreateDbContext()
    {
        var db = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        // Brand seed'i OnModelCreating'ten (HasData) InMemory provider'a otomatik yansımaz,
        // testte ihtiyaç duyulan brand'i açıkça ekliyoruz.
        return db;
    }

    private ProductLookupService CreateSut(ProductDbContext db)
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
        return new ProductLookupService(db, new CodeFormatDetector(), _redis.Object, _metrics.Object);
    }

    [Fact]
    public async Task LookupAsync_WhenCacheHit_ReturnsCachedResultAndRecordsHit()
    {
        await using var db = CreateDbContext();
        var cached = new ProductLookupResult(
            "1234567", ProductCodeType.BrandSpecific, true, Guid.NewGuid(), "Bershka", "bershka", null, ConfidenceLevel.High, ResolvedVia.FormatMatch, false);

        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(JsonSerializer.Serialize(cached)));

        var sut = CreateSut(db);
        var result = await sut.LookupAsync("1234567");

        result.FromCache.Should().BeTrue();
        result.BrandName.Should().Be("Bershka");
        _metrics.Verify(m => m.RecordHitAsync(It.IsAny<string>()), Times.Once);
        _metrics.Verify(m => m.RecordMissAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LookupAsync_WhenCacheMissAndMappingExists_ReturnsResolvedResultAndCachesIt()
    {
        await using var db = CreateDbContext();
        var brand = new Brand { Name = "Bershka", ScraperQueueName = "bershka" };
        db.Brands.Add(brand);
        db.ProductBrandMaps.Add(new ProductBrandMap
        {
            ProductCode = "1234567",
            BrandId = brand.Id,
            Brand = brand,
            ResolvedVia = ResolvedVia.FormatMatch,
            Confidence = ConfidenceLevel.High
        });
        await db.SaveChangesAsync();

        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = CreateSut(db);
        var result = await sut.LookupAsync("1234567");

        result.IsResolved.Should().BeTrue();
        result.BrandName.Should().Be("Bershka");
        result.ScraperQueueName.Should().Be("bershka");
        result.FromCache.Should().BeFalse();

        _metrics.Verify(m => m.RecordMissAsync(It.IsAny<string>()), Times.Once);
        _database.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task LookupAsync_WhenCacheMissAndNoMapping_ReturnsUnresolvedResultAndDoesNotCache()
    {
        await using var db = CreateDbContext();

        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = CreateSut(db);
        var result = await sut.LookupAsync("UNKNOWNCODE");

        result.IsResolved.Should().BeFalse();
        result.BrandId.Should().BeNull();

        _database.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task LookupByUrlAsync_WhenCacheHit_ReturnsCachedResultAndRecordsHit()
    {
        await using var db = CreateDbContext();
        var url = "https://www.oysho.com/tr/test-urun-l36613922";
        var cached = new ProductLookupResult(
            "36613922/814", ProductCodeType.BrandSpecific, true, Guid.NewGuid(), "Oysho", "oysho", url, ConfidenceLevel.Medium, ResolvedVia.Manual, false);

        _database
            .Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == $"product:lookup-url:{url}"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(JsonSerializer.Serialize(cached)));

        var sut = CreateSut(db);
        var result = await sut.LookupByUrlAsync(url);

        result!.FromCache.Should().BeTrue();
        result.BrandName.Should().Be("Oysho");
        _metrics.Verify(m => m.RecordHitAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task LookupByUrlAsync_WhenCacheMissAndMappingExists_ReturnsResolvedResultAndCachesIt()
    {
        await using var db = CreateDbContext();
        var url = "https://www.oysho.com/tr/test-urun-l36613922";
        var brand = new Brand { Name = "Oysho", ScraperQueueName = "oysho" };
        db.Brands.Add(brand);
        db.ProductBrandMaps.Add(new ProductBrandMap
        {
            ProductCode = "36613922/814",
            BrandId = brand.Id,
            Brand = brand,
            ResolvedVia = ResolvedVia.Manual,
            Confidence = ConfidenceLevel.Medium,
            ProductUrl = url
        });
        await db.SaveChangesAsync();

        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = CreateSut(db);
        var result = await sut.LookupByUrlAsync(url);

        result.Should().NotBeNull();
        result!.IsResolved.Should().BeTrue();
        result.ProductCode.Should().Be("36613922/814");
        result.BrandName.Should().Be("Oysho");

        _database.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == $"product:lookup-url:{url}"), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task LookupByUrlAsync_WhenNoMappingForUrl_ReturnsNullAndDoesNotCache()
    {
        await using var db = CreateDbContext();

        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = CreateSut(db);
        var result = await sut.LookupByUrlAsync("https://www.unknown-brand.com/tr/never-seen-product");

        result.Should().BeNull();
        _database.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveMappingAsync_WhenNewMapping_AddsMappingAndInvalidatesCache()
    {
        await using var db = CreateDbContext();
        var brand = new Brand { Name = "Zara", ScraperQueueName = "zara" };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.SaveMappingAsync("12345/678/123", brand.Id, ResolvedVia.Manual, ConfidenceLevel.High);

        var saved = await db.ProductBrandMaps.SingleAsync();
        saved.ProductCode.Should().Be("12345/678/123");
        saved.BrandId.Should().Be(brand.Id);

        _database.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey>(k => k == "product:lookup:12345/678/123"), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SaveMappingAsync_WhenMappingAlreadyExists_UpdatesExistingRowInsteadOfInserting()
    {
        await using var db = CreateDbContext();
        var oldBrand = new Brand { Name = "PullAndBear", ScraperQueueName = "pullbear" };
        var newBrand = new Brand { Name = "Bershka", ScraperQueueName = "bershka" };
        db.Brands.AddRange(oldBrand, newBrand);
        db.ProductBrandMaps.Add(new ProductBrandMap
        {
            ProductCode = "1234567",
            BrandId = oldBrand.Id,
            Brand = oldBrand,
            ResolvedVia = ResolvedVia.FormatMatch,
            Confidence = ConfidenceLevel.Low
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        await sut.SaveMappingAsync("1234567", newBrand.Id, ResolvedVia.Manual, ConfidenceLevel.High);

        (await db.ProductBrandMaps.CountAsync()).Should().Be(1);
        var updated = await db.ProductBrandMaps.SingleAsync();
        updated.BrandId.Should().Be(newBrand.Id);
        updated.Confidence.Should().Be(ConfidenceLevel.High);
    }
}
