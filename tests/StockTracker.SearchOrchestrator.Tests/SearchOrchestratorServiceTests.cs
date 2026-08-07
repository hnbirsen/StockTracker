using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.SearchOrchestrator.DTOs;
using StockTracker.SearchOrchestrator.Services;
using StockTracker.Shared.Contracts.Messages.V2;

namespace StockTracker.SearchOrchestrator.Tests;

public class SearchOrchestratorServiceTests
{
    private readonly Mock<IProductServiceClient> _productClient = new();
    private readonly Mock<IBrandDetectionServiceClient> _brandDetectionClient = new();
    private readonly Mock<IStoreReferenceServiceClient> _storeReferenceClient = new();
    private readonly Mock<ISendEndpointProvider> _sendEndpointProvider = new();
    private readonly Mock<ISendEndpoint> _sendEndpoint = new();

    private SearchOrchestratorService CreateSut()
    {
        _sendEndpointProvider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(_sendEndpoint.Object);

        // Varsayılan: Store Reference'ta eşleşen mağaza yok — testler aksini kurmadıkça fallback (StoreId=null) yolu izlenir.
        _storeReferenceClient
            .Setup(c => c.GetStoresAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<StoreDto>());

        return new SearchOrchestratorService(
            _productClient.Object,
            _brandDetectionClient.Object,
            _storeReferenceClient.Object,
            _sendEndpointProvider.Object,
            Mock.Of<ILogger<SearchOrchestratorService>>());
    }

    [Fact]
    public async Task SearchAsync_WhenBrandAlreadyResolved_SendsCommandAndReturnsQueued()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "38", null);

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, Guid.NewGuid(), "Bershka", "bershka", "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        response.Candidates.Should().BeNull();

        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(
            It.Is<Uri>(u => u.ToString() == "queue:stock.check.bershka")), Times.Once);
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.ProductCode == request.ProductCode && cmd.Size == request.Size
                && cmd.ProductUrl == "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"),
            It.IsAny<CancellationToken>()), Times.Once);
        _brandDetectionClient.Verify(c => c.ResolveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleLocations_SendsOneCommandPerLocation()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "38", new List<SearchLocationRequest>
        {
            new("Istanbul", "Kadikoy"),
            new("Ankara", "Cankaya")
        });

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, Guid.NewGuid(), "Bershka", "bershka", "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.City == "Istanbul" && cmd.District == "Kadikoy"),
            It.IsAny<CancellationToken>()), Times.Once);
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.City == "Ankara" && cmd.District == "Cankaya"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WhenBrandUnknownAndNoCandidatesFound_ReturnsBrandUnknownWithoutSending()
    {
        var request = new SearchRequest(Guid.NewGuid(), "UNKNOWNCODE", null, "M", null);

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null, null));

        _brandDetectionClient
            .Setup(c => c.ResolveAsync(request.ProductCode))
            .ReturnsAsync(new ResolveResponse(request.ProductCode, false, new List<BrandCandidateDto>()));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("BrandUnknown");
        response.Candidates.Should().BeNull();
        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenMultipleCandidatesAndStillUnresolvedAfterRecheck_ReturnsCandidatesForManualSelection()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "M", null);
        var candidates = new List<BrandCandidateDto>
        {
            new(Guid.NewGuid(), "Bershka", 2, "^\\d{7,9}$"),
            new(Guid.NewGuid(), "PullAndBear", 1, "^\\d{8}$")
        };

        _productClient
            .SetupSequence(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null, null))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null, null));

        _brandDetectionClient
            .Setup(c => c.ResolveAsync(request.ProductCode))
            .ReturnsAsync(new ResolveResponse(request.ProductCode, true, candidates));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("BrandUnknown");
        response.Candidates.Should().HaveCount(2);
        response.Candidates!.Select(c => c.BrandName).Should().Contain(new[] { "Bershka", "PullAndBear" });
        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenSingleHighConfidenceCandidateAutoResolvedOnRecheck_SendsCommand()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "M", null);
        var brandId = Guid.NewGuid();

        _productClient
            .SetupSequence(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null, null))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, brandId, "Bershka", "bershka", "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"));

        _brandDetectionClient
            .Setup(c => c.ResolveAsync(request.ProductCode))
            .ReturnsAsync(new ResolveResponse(request.ProductCode, true, new List<BrandCandidateDto>
            {
                new(brandId, "Bershka", 3, "^\\d{7,9}$")
            }));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.BrandId == brandId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WhenLocationHasNoMatchingStore_SendsCommandWithNullStoreId()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "38", new List<SearchLocationRequest>
        {
            new("Antalya", "Muratpasa")
        });

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, Guid.NewGuid(), "Bershka", "bershka", "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"));

        var sut = CreateSut();
        await sut.SearchAsync(request);

        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.StoreId == null && cmd.City == "Antalya" && cmd.District == "Muratpasa"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WhenLocationHasMatchingStores_SendsOneCommandPerResolvedStoreId()
    {
        var brandId = Guid.NewGuid();
        var storeId1 = Guid.NewGuid();
        var storeId2 = Guid.NewGuid();
        var request = new SearchRequest(Guid.NewGuid(), "1234567", null, "38", new List<SearchLocationRequest>
        {
            new("Istanbul", "Kadikoy")
        });

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, brandId, "Bershka", "bershka", "https://www.bershka.com/tr/test-urun-c0p123456789.html?colorId=100"));

        var sut = CreateSut();

        // CreateSut()'un genel "eşleşme yok" fallback setup'ından SONRA kaydediliyor ki Moq bu daha
        // spesifik setup'ı önceliklendirsin (aynı mock'ta en son eklenen eşleşen setup kazanır).
        _storeReferenceClient
            .Setup(c => c.GetStoresAsync(brandId, "Istanbul", "Kadikoy"))
            .ReturnsAsync(new List<StoreDto>
            {
                new(storeId1, brandId, "Istanbul", "Kadikoy", "BSK-IST-KDK-01"),
                new(storeId2, brandId, "Istanbul", "Kadikoy", "BSK-IST-KDK-02")
            });

        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.StoreId == storeId1),
            It.IsAny<CancellationToken>()), Times.Once);
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.StoreId == storeId2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WhenOnlyProductUrlGivenAndAlreadyCatalogued_SendsCommandWithoutCallingBrandDetection()
    {
        var url = "https://www.oysho.com/tr/test-urun-l36613922";
        var request = new SearchRequest(Guid.NewGuid(), null, url, "M", null);
        var brandId = Guid.NewGuid();

        _productClient
            .Setup(c => c.LookupByUrlAsync(url))
            .ReturnsAsync(new ProductLookupResponse("36613922/814", true, brandId, "Oysho", "oysho", url));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(
            It.Is<Uri>(u => u.ToString() == "queue:stock.check.oysho")), Times.Once);
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.ProductCode == "36613922/814" && cmd.Size == "M" && cmd.ProductUrl == url),
            It.IsAny<CancellationToken>()), Times.Once);
        _productClient.Verify(c => c.LookupAsync(It.IsAny<string>()), Times.Never);
        _brandDetectionClient.Verify(c => c.ResolveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenProductUrlNotCataloguedButHostRecognized_ReturnsUrlNotCataloguedWithBrandHint()
    {
        var url = "https://www.zara.com/tr/tr/hic-gorulmemis-urun-p99999999.html";
        var request = new SearchRequest(Guid.NewGuid(), null, url, "M", null);

        _productClient
            .Setup(c => c.LookupByUrlAsync(url))
            .ReturnsAsync((ProductLookupResponse?)null);

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("UrlNotCatalogued");
        response.Message.Should().Contain("Zara");
        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenProductUrlNotCataloguedAndHostUnrecognized_ReturnsGenericUrlNotCataloguedMessage()
    {
        var url = "https://www.some-random-shop.com/product/123";
        var request = new SearchRequest(Guid.NewGuid(), null, url, "M", null);

        _productClient
            .Setup(c => c.LookupByUrlAsync(url))
            .ReturnsAsync((ProductLookupResponse?)null);

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("UrlNotCatalogued");
        response.Message.Should().NotContain("Zara").And.NotContain("Bershka");
        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(It.IsAny<Uri>()), Times.Never);
    }
}
