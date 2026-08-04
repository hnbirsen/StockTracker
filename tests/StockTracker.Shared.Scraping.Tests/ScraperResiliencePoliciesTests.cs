using System.Net;
using FluentAssertions;
using StockTracker.Shared.Scraping.Http;

namespace StockTracker.Shared.Scraping.Tests;

public class ScraperResiliencePoliciesTests
{
    [Fact]
    public void ComputeRetryDelay_WhenRetryAfterHeaderPresent_UsesItInsteadOfBackoff()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var delay = ScraperResiliencePolicies.ComputeRetryDelay(retryAttempt: 1, response);

        delay.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void ComputeRetryDelay_WhenNoRetryAfterHeader_FallsBackToExponentialBackoff()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var delay = ScraperResiliencePolicies.ComputeRetryDelay(retryAttempt: 3, response);

        delay.Should().Be(TimeSpan.FromSeconds(8)); // 2^3
    }

    [Fact]
    public void ComputeRetryDelay_WhenResponseIsNull_FallsBackToExponentialBackoff()
    {
        // response null olabilir: policy, sonuç değil exception (HttpRequestException) yakaladığında.
        var delay = ScraperResiliencePolicies.ComputeRetryDelay(retryAttempt: 2, response: null);

        delay.Should().Be(TimeSpan.FromSeconds(4)); // 2^2
    }

    [Fact]
    public async Task RetryWithRetryAfterAwareness_Retries429_WhichDefaultTransientErrorHandlingDoesNotCover()
    {
        // Bu, gerçek bir regresyon senaryosu: Polly.Extensions.Http'nin `HandleTransientHttpError()`
        // predicate'i yalnızca 5xx/408/network hatalarını kapsar, 429'u KAPSAMAZ — eski kod
        // (`AddTransientHttpErrorPolicy`) 429'da hiç retry yapmıyordu.
        var callCount = 0;
        var policy = ScraperResiliencePolicies.RetryWithRetryAfterAwareness(retryCount: 2);

        var result = await policy.ExecuteAsync(() =>
        {
            callCount++;
            if (callCount < 3)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        callCount.Should().Be(3);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BotDetectionCircuitBreaker_OpensAfterThresholdOf403sAndBlocksFurtherCalls()
    {
        var policy = ScraperResiliencePolicies.BotDetectionCircuitBreaker(threshold: 2, duration: TimeSpan.FromMinutes(5));
        var callCount = 0;

        Func<Task<HttpResponseMessage>> forbidden = () =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        };

        await policy.ExecuteAsync(forbidden);
        await policy.ExecuteAsync(forbidden);

        // Eşik (2) aşıldı — devre açık, bir sonraki çağrı gerçekten denenmeden BrokenCircuitException fırlatmalı.
        var act = async () => await policy.ExecuteAsync(forbidden);
        await act.Should().ThrowAsync<Polly.CircuitBreaker.BrokenCircuitException>();

        callCount.Should().Be(2);
    }
}
