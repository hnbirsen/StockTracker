using System.Net;
using System.Text;

namespace StockTracker.StradivariusScraper.Tests;

internal class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<string> RequestedUris { get; } = [];
    public List<string?> RequestedBodies { get; } = [];
    public List<string?> AuthorizationHeaders { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!.ToString());
        RequestedBodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
        return Task.FromResult(responder(request));
    }
}

internal static class FakeHttpResponses
{
    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
