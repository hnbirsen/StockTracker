using FluentAssertions;
using Moq;
using StackExchange.Redis;
using StockTracker.SearchOrchestrator.Services;

namespace StockTracker.SearchOrchestrator.Tests;

public class SearchThrottleServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _database = new();

    private SearchThrottleService CreateSut()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
        return new SearchThrottleService(_redis.Object);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenKeyNotSet_ReturnsTrue()
    {
        _database
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(true);

        var sut = CreateSut();
        var result = await sut.TryAcquireAsync(Guid.NewGuid(), "1234567", "38");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenKeyAlreadySet_ReturnsFalse()
    {
        _database
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(false);

        var sut = CreateSut();
        var result = await sut.TryAcquireAsync(Guid.NewGuid(), "1234567", "38");

        result.Should().BeFalse();
    }
}
