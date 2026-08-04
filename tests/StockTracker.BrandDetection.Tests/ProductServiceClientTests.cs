using System.Net;
using FluentAssertions;
using StockTracker.BrandDetection.Services;

namespace StockTracker.BrandDetection.Tests;

public class ProductServiceClientTests
{
    private static ProductServiceClient CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new FakeHttpMessageHandler(responder)) { BaseAddress = new Uri("https://product.internal") });

    [Fact]
    public async Task SaveMappingAsync_WhenResponseSuccessful_CompletesWithoutThrowing()
    {
        var sut = CreateSut(_ => FakeHttpResponses.Json(HttpStatusCode.OK, "{}"));

        var act = async () => await sut.SaveMappingAsync("02891054426", Guid.NewGuid(), "FormatMatch", "High");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveMappingAsync_WhenResponseUnsuccessful_ThrowsHttpRequestException()
    {
        // Regresyon testi: eskiden PostAsJsonAsync yanıtı hiç kontrol edilmiyordu — Product Service
        // kaydı reddetse/hata dönse bile Brand Detection bunu fark etmeden sessizce devam ediyordu.
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = async () => await sut.SaveMappingAsync("02891054426", Guid.NewGuid(), "FormatMatch", "High");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
