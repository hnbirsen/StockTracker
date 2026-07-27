using StockTracker.Product.Entities;

namespace StockTracker.Product.Services;

public interface ICodeFormatDetector
{
    ProductCodeType Detect(string code);
}

public class CodeFormatDetector : ICodeFormatDetector
{
    public ProductCodeType Detect(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return ProductCodeType.Unknown;

        var trimmed = code.Trim();

        if (IsEan13(trimmed)) return ProductCodeType.EAN13;
        if (IsEan8(trimmed)) return ProductCodeType.EAN8;
        if (IsUpc(trimmed)) return ProductCodeType.UPC;
        if (IsBrandSpecific(trimmed)) return ProductCodeType.BrandSpecific;

        return ProductCodeType.Unknown;
    }

    private static bool IsEan13(string code) =>
        code.Length == 13 && code.All(char.IsDigit) && ValidateEanChecksum(code);

    private static bool IsEan8(string code) =>
        code.Length == 8 && code.All(char.IsDigit) && ValidateEanChecksum(code);

    private static bool IsUpc(string code) =>
        code.Length == 12 && code.All(char.IsDigit);

    private static bool IsBrandSpecific(string code) =>
        code.Any(char.IsLetter)
        || code.Contains('/')
        || code.Contains('-')
        || code.Contains('_')
        || (code.All(char.IsDigit) && code.Length != 8 && code.Length != 12 && code.Length != 13);

    private static bool ValidateEanChecksum(string code)
    {
        var digits = code.Select(c => int.Parse(c.ToString())).ToArray();
        var sum = 0;
        for (var i = 0; i < digits.Length - 1; i++)
            sum += i % 2 == 0 ? digits[i] : digits[i] * 3;

        var checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == digits[^1];
    }
}
