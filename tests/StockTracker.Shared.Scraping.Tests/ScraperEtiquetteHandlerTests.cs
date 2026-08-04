using System.Net;
using FluentAssertions;
using StockTracker.Shared.Scraping.Http;

namespace StockTracker.Shared.Scraping.Tests;

public class ScraperEtiquetteHandlerTests
{
    private static (HttpMessageInvoker Invoker, StubHttpMessageHandler Inner) CreateInvoker(TimeSpan? minDelay = null, TimeSpan? maxDelay = null)
    {
        var inner = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new ScraperEtiquetteHandler(minDelay, maxDelay) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner);
    }

    [Fact]
    public async Task SendAsync_SetsUserAgentAndAcceptLanguageHeaders()
    {
        var (invoker, inner) = CreateInvoker(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"), CancellationToken.None);

        inner.LastRequest!.Headers.UserAgent.ToString().Should().NotBeNullOrEmpty();
        inner.LastRequest.Headers.AcceptLanguage.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendAsync_AppliesSameProfileConsistently_UserAgentAndSecChUaNeverMismatched()
    {
        var (invoker, inner) = CreateInvoker(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

        // Birden fazla kez çalıştır — hangi profil seçilirse seçilsin, Firefox/Safari UA'sıyla sec-ch-ua
        // birlikte gelmemeli (motor/tarayıcı tutarsızlığı, bkz. BrowserProfiles notu).
        for (var i = 0; i < 30; i++)
        {
            await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"), CancellationToken.None);
            var ua = inner.LastRequest!.Headers.UserAgent.ToString();
            var hasSecChUa = inner.LastRequest.Headers.Contains("sec-ch-ua");

            var isChromium = ua.Contains("Chrome/");
            hasSecChUa.Should().Be(isChromium, $"UA={ua} için sec-ch-ua varlığı tarayıcı motoruyla tutarlı olmalı");
        }
    }

    [Fact]
    public async Task SendAsync_DelaysWithinConfiguredRange()
    {
        var (invoker, _) = CreateInvoker(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/"), CancellationToken.None);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(15);
    }
}
