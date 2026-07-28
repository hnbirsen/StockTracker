using FluentAssertions;
using StockTracker.Product.Entities;
using StockTracker.Product.Services;

namespace StockTracker.Product.Tests;

public class CodeFormatDetectorTests
{
    private readonly CodeFormatDetector _sut = new();

    [Theory]
    [InlineData("4006381333931")] // gerçek EAN-13 checksum
    [InlineData("5901234123457")]
    public void Detect_WithValidEan13Checksum_ReturnsEan13(string code)
    {
        _sut.Detect(code).Should().Be(ProductCodeType.EAN13);
    }

    [Fact]
    public void Detect_With13DigitsButInvalidChecksum_ReturnsUnknown()
    {
        // 13 haneli ama checksum tutmuyor, brand-specific de sayılmaz (tüm rakam + uzunluk 13)
        _sut.Detect("1234567890123").Should().Be(ProductCodeType.Unknown);
    }

    [Theory]
    [InlineData("96385074")] // gerçek EAN-8 checksum
    public void Detect_WithValidEan8Checksum_ReturnsEan8(string code)
    {
        _sut.Detect(code).Should().Be(ProductCodeType.EAN8);
    }

    [Fact]
    public void Detect_With12Digits_ReturnsUpc()
    {
        _sut.Detect("123456789012").Should().Be(ProductCodeType.UPC);
    }

    [Theory]
    [InlineData("12345/678/123")] // Zara formatı
    [InlineData("ABC-1234")]
    [InlineData("1234_5678")]
    [InlineData("1234567")] // 7 haneli — 8/12/13 değil
    public void Detect_WithBrandSpecificPatterns_ReturnsBrandSpecific(string code)
    {
        _sut.Detect(code).Should().Be(ProductCodeType.BrandSpecific);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Detect_WithNullOrWhitespace_ReturnsUnknown(string? code)
    {
        _sut.Detect(code!).Should().Be(ProductCodeType.Unknown);
    }
}
