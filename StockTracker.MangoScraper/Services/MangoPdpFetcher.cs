using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MangoScraper.Services;

// Next.js App Router'ın React Server Components ("RSC") akışını ayrıştırır — bkz. IMangoPdpFetcher
// üstündeki not. `self.__next_f.push([N, "..."])` çağrılarının argümanı ZATEN geçerli bir JSON dizisi
// (`[sayı, string]`) olduğu için, doğru yaklaşım kendi elle unescape mantığımızı yazmak değil, bu diziyi
// `System.Text.Json` ile normal şekilde deserialize edip [1]. elemanını (çift-escape'siz, düz JSON içeren
// gerçek string) almak — bu, olası tüm escape dizilerini (unicode, ters slash vb.) doğru işler. O düz
// string içinde `"colors":[` aranıp parantez dengesiyle (bracket-balance) dizi sınırı bulunur, sonra o alt
// dizi normal `JsonSerializer.Deserialize` ile ayrıştırılır.
public class MangoPdpFetcher : IMangoPdpFetcher
{
    private const string ScraperName = "mango";

    // Bir sayfada birden fazla `self.__next_f.push(...)` çağrısı olabilir (metadata, çeviri sözlüğü vb.) —
    // yalnızca "colors" verisini taşıyan parça işleniyor. `(?:[^"\\]|\\.)*` deseni JSON string escape
    // kurallarını doğru işler (örn. `\"` bir string sonlandırıcısı sayılmaz).
    private static readonly Regex NextFPushRegex = new(
        @"self\.__next_f\.push\((\[\d+,""(?:[^""\\]|\\.)*""\])\)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<MangoPdpFetcher> _logger;

    public MangoPdpFetcher(HttpClient httpClient, IScraperHealthLogService healthLog, ILogger<MangoPdpFetcher> logger)
    {
        _httpClient = httpClient;
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int? httpStatusCode = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, productUrl);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            httpStatusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                await _healthLog.LogAttemptAsync(
                    ScraperName, "PdpFetch", success: false, httpStatusCode,
                    errorMessage: $"HTTP {httpStatusCode}", context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var sizesJson = ExtractColorsAsSizeEntries(html);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PdpFetch", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "\"colors\" verisi RSC akışında bulunamadı" : null,
                context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Mango ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PdpFetch", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    private string? ExtractColorsAsSizeEntries(string html)
    {
        foreach (Match match in NextFPushRegex.Matches(html))
        {
            string? inner;
            try
            {
                using var pushDoc = JsonDocument.Parse(match.Groups[1].Value);
                var root = pushDoc.RootElement;
                inner = root.GetArrayLength() >= 2 && root[1].ValueKind == JsonValueKind.String
                    ? root[1].GetString()
                    : null;
            }
            catch (JsonException)
            {
                continue;
            }

            if (string.IsNullOrEmpty(inner)) continue;

            var idx = inner.IndexOf("\"colors\":[", StringComparison.Ordinal);
            if (idx < 0) continue;

            var arrayStart = idx + "\"colors\":".Length;
            var arrayText = ExtractBalancedJsonArray(inner, arrayStart);
            if (arrayText is null) continue;

            List<MangoColorDto>? colors;
            try
            {
                colors = JsonSerializer.Deserialize<List<MangoColorDto>>(arrayText, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "\"colors\" dizisi ayrıştırılamadı.");
                continue;
            }

            if (colors is null || colors.Count == 0) continue;

            var flat = colors
                .SelectMany(c => c.Sizes.Select(s => new SizeEntry(s.Label, s.Available, c.Id)))
                .ToList();

            return flat.Count > 0 ? JsonSerializer.Serialize(flat, JsonOptions) : null;
        }

        return null;
    }

    // `[` karakterinden başlayarak, iç içe geçmiş dizi/nesneleri (string içindeki `[`/`]` karakterleri hariç
    // tutarak) dengeleyip dış dizinin tam metnini döner.
    private static string? ExtractBalancedJsonArray(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var started = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '[':
                    depth++;
                    started = true;
                    break;
                case ']':
                    depth--;
                    if (started && depth == 0)
                    {
                        return text[startIndex..(i + 1)];
                    }
                    break;
            }
        }

        return null;
    }

    private record MangoColorDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("sizes")] List<MangoSizeDto> Sizes);

    private record MangoSizeDto(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("available")] bool Available);

    private record SizeEntry(string Name, bool Available, string ColorId);
}
