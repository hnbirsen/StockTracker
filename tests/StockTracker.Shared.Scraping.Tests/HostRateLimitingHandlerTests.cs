using System.Net;
using FluentAssertions;
using StockTracker.Shared.Scraping.Http;

namespace StockTracker.Shared.Scraping.Tests;

public class HostRateLimitingHandlerTests
{
    private static HttpMessageInvoker CreateInvoker(int requestsPerMinute, out StubHttpMessageHandler inner)
    {
        inner = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new HostRateLimitingHandler(requestsPerMinute) { InnerHandler = inner };
        return new HttpMessageInvoker(handler);
    }

    // Bucket'lar host'a göre static/paylaşılan bir dictionary'de tutulduğu için (bkz. HostRateLimitingHandler),
    // testler birbirini etkilemesin diye her test kendi benzersiz host'unu kullanır.
    private static Uri UniqueTestUri() => new($"https://rate-limit-test-{Guid.NewGuid():N}.example/urun");

    [Fact]
    public async Task SendAsync_WhenBucketHasCapacity_PassesThroughImmediately()
    {
        var invoker = CreateInvoker(requestsPerMinute: 60, out var inner);
        var uri = UniqueTestUri();

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), CancellationToken.None);

        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenBucketExhausted_BlocksSubsequentRequestUntilCancelled()
    {
        // Dakikada 1 istek — ilk istek anında geçer, ikinci istek bir sonraki token'ı (yaklaşık 60 saniye
        // sonra) bekler. Gerçek 60 saniye beklemek yerine, kısa bir cancellation ile "gerçekten bekliyor,
        // hemen dönmüyor" olduğunu kanıtlıyoruz.
        var invoker = CreateInvoker(requestsPerMinute: 1, out var inner);
        var uri = UniqueTestUri();

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), CancellationToken.None);
        inner.CallCount.Should().Be(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var act = async () => await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_DifferentHosts_HaveIndependentBuckets()
    {
        var invoker = CreateInvoker(requestsPerMinute: 1, out var inner);
        var uriA = UniqueTestUri();
        var uriB = UniqueTestUri();

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uriA), CancellationToken.None);
        // A'nın bucket'ı tükendi, ama B farklı bir host — hemen geçmeli (aynı handler instance'ı içinde bile).
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uriB), CancellationToken.None);

        inner.CallCount.Should().Be(2);
    }
}
