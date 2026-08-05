using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.MangoScraper.Services;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MangoScraper.Tests;

public class MangoPdpFetcherTests
{
    // Gerçek Mango PDP'sinin Next.js RSC akışını mirror eder: `self.__next_f.push([N, "..."])` çağrısının
    // argümanı, içinde normal (tek seviye escape'li) JSON barındıran, ÇİFT escape'li bir string taşıyan
    // geçerli bir JSON dizisi. `JsonSerializer.Serialize` ile üretmek, gerçek sayfadaki escape düzenini
    // birebir taklit ediyor (bkz. MangoPdpFetcher üstündeki yorum).
    private static string BuildPageHtml(string colorsJsonArray, bool includeNoiseChunk = true)
    {
        var innerContent = $"1:[\"$\",\"div\",null,{{\"featureFlags\":{{}}}}]\n" +
            $"2:{{\"name\":\"Test Product\",\"colors\":{colorsJsonArray},\"other\":\"noise\"}}";

        var pushArg = JsonSerializer.Serialize(new object[] { 1, innerContent });
        var script = $"<script>self.__next_f.push({pushArg})</script>";

        var noise = includeNoiseChunk
            ? $"<script>self.__next_f.push({JsonSerializer.Serialize(new object[] { 2, "{\"translations\":{\"shoppingBag.error.noStock.title\":\"Ürün tükendi\"}}" })})</script>"
            : string.Empty;

        return $"<html><head></head><body>{noise}{script}</body></html>";
    }

    private static (MangoPdpFetcher Sut, FakeHttpMessageHandler Handler) CreateSut(HttpStatusCode statusCode, string html)
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpResponses.Html(statusCode, html));
        var httpClient = new HttpClient(handler);
        var healthLog = new Mock<IScraperHealthLogService>();
        healthLog.Setup(h => h.LogAttemptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new MangoPdpFetcher(httpClient, healthLog.Object, Mock.Of<ILogger<MangoPdpFetcher>>());
        return (sut, handler);
    }

    [Fact]
    public async Task FetchProductDataJsonAsync_ExtractsFlattenedSizeEntriesAcrossColors()
    {
        var colors = """
            [
              {"id":"77","label":"Şarap Rengi","sizes":[
                {"id":"19","label":"XS","available":true,"warehouses":["001"]},
                {"id":"20","label":"S","available":false,"warehouses":[]}
              ]},
              {"id":"56","label":"Lacivert","sizes":[
                {"id":"19","label":"XS","available":true,"warehouses":["052"]}
              ]}
            ]
            """;
        var html = BuildPageHtml(colors);
        var (sut, _) = CreateSut(HttpStatusCode.OK, html);

        var json = await sut.FetchProductDataJsonAsync("https://shop.mango.com/tr/tr/p/test/37013869/77/00", CancellationToken.None);

        json.Should().NotBeNull();
        var entries = JsonSerializer.Deserialize<List<TestSizeEntry>>(json!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        entries.Should().HaveCount(3);
        entries.Should().ContainSingle(e => e.ColorId == "77" && e.Name == "XS" && e.Available);
        entries.Should().ContainSingle(e => e.ColorId == "77" && e.Name == "S" && !e.Available);
        entries.Should().ContainSingle(e => e.ColorId == "56" && e.Name == "XS" && e.Available);
    }

    [Fact]
    public async Task FetchProductDataJsonAsync_WhenNoColorsChunkPresent_ReturnsNull()
    {
        var html = BuildPageHtml("[]", includeNoiseChunk: true);
        // Boş colors dizisi -> flat liste boş -> null (bkz. Bershka/Zara'daki "boş sonuç cache'lenmez" deseni).
        var (sut, _) = CreateSut(HttpStatusCode.OK, html);

        var json = await sut.FetchProductDataJsonAsync("https://shop.mango.com/tr/tr/p/test/00000000/00/00", CancellationToken.None);

        json.Should().BeNull();
    }

    [Fact]
    public async Task FetchProductDataJsonAsync_WhenHttpRequestFails_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.NotFound, "not found");

        var json = await sut.FetchProductDataJsonAsync("https://shop.mango.com/tr/tr/p/broken-url", CancellationToken.None);

        json.Should().BeNull();
    }

    [Fact]
    public async Task FetchProductDataJsonAsync_IgnoresUnrelatedPushChunks()
    {
        // Sayfada "colors" içermeyen (çeviri sözlüğü gibi) başka push çağrıları da olabilir — bunlar
        // atlanıp doğru veri taşıyan parça bulunmalı (canlı sayfada "stock" kelimesi feature-flag/çeviri
        // metinlerinde de geçiyordu, gerçek "colors" verisiyle karıştırılmamalı).
        var colors = """[{"id":"10","label":"Siyah","sizes":[{"id":"1","label":"M","available":true}]}]""";
        var html = BuildPageHtml(colors, includeNoiseChunk: true);
        var (sut, _) = CreateSut(HttpStatusCode.OK, html);

        var json = await sut.FetchProductDataJsonAsync("https://shop.mango.com/tr/tr/p/test/12345678/10/00", CancellationToken.None);

        json.Should().NotBeNull();
        var entries = JsonSerializer.Deserialize<List<TestSizeEntry>>(json!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        entries.Should().ContainSingle(e => e.ColorId == "10" && e.Name == "M" && e.Available);
    }

    private record TestSizeEntry(string Name, bool Available, string ColorId);
}
