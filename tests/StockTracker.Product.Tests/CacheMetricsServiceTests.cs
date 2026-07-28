using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.Product.Services;

namespace StockTracker.Product.Tests;

public class CacheMetricsServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _database = new();

    private CacheMetricsService CreateSut()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
        return new CacheMetricsService(_redis.Object, Mock.Of<ILogger<CacheMetricsService>>());
    }

    [Fact]
    public async Task RecordHitAsync_IncrementsHitCounter()
    {
        _database
            .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var sut = CreateSut();
        await sut.RecordHitAsync("product:lookup:1234567");

        _database.Verify(d => d.StringIncrementAsync(
            It.Is<RedisKey>(k => k == "cache:metrics:hits"), 1, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RecordMissAsync_IncrementsMissCounter()
    {
        _database
            .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var sut = CreateSut();
        await sut.RecordMissAsync("product:lookup:1234567");

        _database.Verify(d => d.StringIncrementAsync(
            It.Is<RedisKey>(k => k == "cache:metrics:misses"), 1, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenNoHitsOrMisses_ReturnsZeroedSummaryWithoutDivideByZero()
    {
        _database
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = CreateSut();
        var summary = await sut.GetSummaryAsync();

        summary.TotalHits.Should().Be(0);
        summary.TotalMisses.Should().Be(0);
        summary.HitRatePercent.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_ComputesHitRatePercent()
    {
        _database
            .Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == "cache:metrics:hits"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("3"));
        _database
            .Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == "cache:metrics:misses"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("1"));

        var sut = CreateSut();
        var summary = await sut.GetSummaryAsync();

        summary.TotalHits.Should().Be(3);
        summary.TotalMisses.Should().Be(1);
        summary.HitRatePercent.Should().Be(75.0);
    }
}
