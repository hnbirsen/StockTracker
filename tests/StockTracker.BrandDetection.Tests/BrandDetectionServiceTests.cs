using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockTracker.BrandDetection.Data;
using StockTracker.BrandDetection.Entities;
using StockTracker.BrandDetection.Services;

namespace StockTracker.BrandDetection.Tests;

public class BrandDetectionServiceTests
{
    private static BrandDetectionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BrandDetectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static BrandCodeSignature Signature(Guid brandId, string brandName, string pattern, ConfidenceLevel confidence, bool isActive = true) => new()
    {
        BrandId = brandId,
        BrandName = brandName,
        RegexPattern = pattern,
        Confidence = confidence,
        IsActive = isActive
    };

    [Fact]
    public async Task ResolveAsync_WhenSingleHighConfidenceMatch_AutoSavesMappingAndReturnsResolved()
    {
        await using var db = CreateDbContext();
        var zaraId = Guid.NewGuid();
        db.BrandCodeSignatures.Add(Signature(zaraId, "Zara", @"^\d{5}/\d{3}/\d{2,3}$", ConfidenceLevel.High));
        await db.SaveChangesAsync();

        var productClient = new Mock<IProductServiceClient>();
        var sut = new BrandDetectionService(db, productClient.Object);

        var result = await sut.ResolveAsync("12345/678/123");

        result.IsResolved.Should().BeTrue();
        result.Candidates.Should().ContainSingle();
        productClient.Verify(c => c.SaveMappingAsync("12345/678/123", zaraId, "FormatMatch", "High"), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_WhenMultipleCandidatesMatch_ReturnsAllCandidatesWithoutAutoSaving()
    {
        await using var db = CreateDbContext();
        var bershkaId = Guid.NewGuid();
        var pullbearId = Guid.NewGuid();
        db.BrandCodeSignatures.AddRange(
            Signature(bershkaId, "Bershka", @"^\d{7,9}$", ConfidenceLevel.Medium),
            Signature(pullbearId, "Pull&Bear", @"^\d{8}$", ConfidenceLevel.Low));
        await db.SaveChangesAsync();

        var productClient = new Mock<IProductServiceClient>();
        var sut = new BrandDetectionService(db, productClient.Object);

        var result = await sut.ResolveAsync("12345678"); // hem Bershka (7-9 hane) hem Pull&Bear (8 hane) ile eşleşir

        result.IsResolved.Should().BeTrue();
        result.Candidates.Should().HaveCount(2);
        productClient.Verify(c => c.SaveMappingAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_WhenSingleMediumConfidenceMatch_ReturnsCandidateButDoesNotAutoSave()
    {
        await using var db = CreateDbContext();
        var bershkaId = Guid.NewGuid();
        db.BrandCodeSignatures.Add(Signature(bershkaId, "Bershka", @"^\d{7,9}$", ConfidenceLevel.Medium));
        await db.SaveChangesAsync();

        var productClient = new Mock<IProductServiceClient>();
        var sut = new BrandDetectionService(db, productClient.Object);

        var result = await sut.ResolveAsync("1234567");

        result.IsResolved.Should().BeTrue();
        result.Candidates.Should().ContainSingle(c => c.BrandName == "Bershka");
        productClient.Verify(c => c.SaveMappingAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_WhenNoPatternMatches_ReturnsUnresolvedWithEmptyCandidates()
    {
        await using var db = CreateDbContext();
        db.BrandCodeSignatures.Add(Signature(Guid.NewGuid(), "Zara", @"^\d{5}/\d{3}/\d{2,3}$", ConfidenceLevel.High));
        await db.SaveChangesAsync();

        var sut = new BrandDetectionService(db, Mock.Of<IProductServiceClient>());

        var result = await sut.ResolveAsync("not-a-known-format!!!");

        result.IsResolved.Should().BeFalse();
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_IgnoresInactiveSignatures()
    {
        await using var db = CreateDbContext();
        var bershkaId = Guid.NewGuid();
        db.BrandCodeSignatures.Add(Signature(bershkaId, "Bershka", @"^\d{7,9}$", ConfidenceLevel.High, isActive: false));
        await db.SaveChangesAsync();

        var sut = new BrandDetectionService(db, Mock.Of<IProductServiceClient>());

        var result = await sut.ResolveAsync("1234567");

        result.IsResolved.Should().BeFalse();
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualResolveAsync_SavesMappingAsManualHighConfidence()
    {
        await using var db = CreateDbContext();
        var brandId = Guid.NewGuid();
        var productClient = new Mock<IProductServiceClient>();
        var sut = new BrandDetectionService(db, productClient.Object);

        var result = await sut.ManualResolveAsync("1234567", brandId, "Bershka");

        result.IsResolved.Should().BeTrue();
        result.Candidates.Should().ContainSingle(c => c.BrandName == "Bershka" && c.Confidence == ConfidenceLevel.High);
        productClient.Verify(c => c.SaveMappingAsync("1234567", brandId, "Manual", "High"), Times.Once);
    }
}
