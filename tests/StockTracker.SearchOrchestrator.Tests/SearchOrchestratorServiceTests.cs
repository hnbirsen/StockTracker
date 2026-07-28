using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.SearchOrchestrator.DTOs;
using StockTracker.SearchOrchestrator.Services;
using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.SearchOrchestrator.Tests;

public class SearchOrchestratorServiceTests
{
    private readonly Mock<IProductServiceClient> _productClient = new();
    private readonly Mock<IBrandDetectionServiceClient> _brandDetectionClient = new();
    private readonly Mock<ISendEndpointProvider> _sendEndpointProvider = new();
    private readonly Mock<ISendEndpoint> _sendEndpoint = new();

    private SearchOrchestratorService CreateSut()
    {
        _sendEndpointProvider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(_sendEndpoint.Object);

        return new SearchOrchestratorService(
            _productClient.Object,
            _brandDetectionClient.Object,
            _sendEndpointProvider.Object,
            Mock.Of<ILogger<SearchOrchestratorService>>());
    }

    [Fact]
    public async Task SearchAsync_WhenBrandAlreadyResolved_SendsCommandAndReturnsQueued()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", "38", null);

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, Guid.NewGuid(), "Bershka", "bershka"));

        var sut = CreateSut();
        var response = await sut.SearchAsync(request);

        response.Status.Should().Be("Queued");
        response.Candidates.Should().BeNull();

        _sendEndpointProvider.Verify(p => p.GetSendEndpoint(
            It.Is<Uri>(u => u.ToString() == "queue:stock.check.bershka")), Times.Once);
        _sendEndpoint.Verify(e => e.Send(
            It.Is<CheckStockCommand>(cmd => cmd.ProductCode == request.ProductCode && cmd.Size == request.Size),
            It.IsAny<CancellationToken>()), Times.Once);
        _brandDetectionClient.Verify(c => c.ResolveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleLocations_SendsOneCommandPerLocation()
    {
        var request = new SearchRequest(Guid.NewGuid(), "1234567", "38", new List<SearchLocationRequest>
        {
            new("Istanbul", "Kadikoy"),
            new("Ankara", "Cankaya")
        });

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, Guid.NewGuid(), "Bershka", "bershka"));

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
        var request = new SearchRequest(Guid.NewGuid(), "UNKNOWNCODE", "M", null);

        _productClient
            .Setup(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null));

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
        var request = new SearchRequest(Guid.NewGuid(), "1234567", "M", null);
        var candidates = new List<BrandCandidateDto>
        {
            new(Guid.NewGuid(), "Bershka", 2, "^\\d{7,9}$"),
            new(Guid.NewGuid(), "PullAndBear", 1, "^\\d{8}$")
        };

        _productClient
            .SetupSequence(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null));

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
        var request = new SearchRequest(Guid.NewGuid(), "1234567", "M", null);
        var brandId = Guid.NewGuid();

        _productClient
            .SetupSequence(c => c.LookupAsync(request.ProductCode))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, false, null, null, null))
            .ReturnsAsync(new ProductLookupResponse(request.ProductCode, true, brandId, "Bershka", "bershka"));

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
}
