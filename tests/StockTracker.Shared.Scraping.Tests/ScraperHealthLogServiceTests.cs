using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.Shared.Scraping.Tests;

public class ScraperHealthLogServiceTests
{
    private static RedisValue Entry(string source, bool success, int? httpStatusCode, string? errorMessage = null, string? context = null, int durationMs = 100) =>
        $$"""
        {"source":"{{source}}","success":{{(success ? "true" : "false")}},"httpStatusCode":{{(httpStatusCode?.ToString() ?? "null")}},"errorMessage":{{(errorMessage is null ? "null" : $"\"{errorMessage}\"")}},"context":{{(context is null ? "null" : $"\"{context}\"")}},"durationMs":{{durationMs}},"timestamp":"2026-01-01T00:00:00Z"}
        """;

    private static (ScraperHealthLogService Sut, Mock<IDatabase> Db) CreateSut(RedisValue[]? rangeResult = null, bool throwOnPush = false)
    {
        var db = new Mock<IDatabase>();

        if (throwOnPush)
        {
            db.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "bağlantı yok"));
        }
        else
        {
            db.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1L);
        }

        db.Setup(d => d.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(rangeResult ?? []);

        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        var sut = new ScraperHealthLogService(redis.Object, Mock.Of<ILogger<ScraperHealthLogService>>());
        return (sut, db);
    }

    [Fact]
    public async Task LogAttemptAsync_PushesEntryAndTrimsListForScraperSpecificKey()
    {
        var (sut, db) = CreateSut();

        await sut.LogAttemptAsync("bershka", "PlaywrightPdp", success: true, httpStatusCode: 200, errorMessage: null, context: "https://example.com/urun", durationMs: 250);

        db.Verify(d => d.ListLeftPushAsync(
            It.Is<RedisKey>(k => k.ToString() == "scraper:health:bershka:log"),
            It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.ListTrimAsync(
            It.Is<RedisKey>(k => k.ToString() == "scraper:health:bershka:log"),
            0, 499, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task LogAttemptAsync_MultilineErrorMessage_IsNormalizedToSingleLineBeforeStoring()
    {
        // Gerçek verilerle bulunan durum: Playwright'ın DNS/navigasyon exception'ları çok satırlı call log
        // içeriyor — redis-cli ile ham kayda bakıldığında okunabilirliği bozuyordu (kullanıcı fark etti).
        RedisValue? pushed = null;
        var db = new Mock<IDatabase>();
        db.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, When, CommandFlags>((_, v, _, _) => pushed = v)
            .ReturnsAsync(1L);
        db.Setup(d => d.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        var sut = new ScraperHealthLogService(redis.Object, Mock.Of<ILogger<ScraperHealthLogService>>());

        var multilineError = "net::ERR_NAME_NOT_RESOLVED at https://example.invalid/\nCall log:\n  - navigating to \"...\"";
        await sut.LogAttemptAsync("bershka", "PlaywrightPdp", success: false, httpStatusCode: null, errorMessage: multilineError, context: "https://example.invalid/", durationMs: 10);

        pushed.Should().NotBeNull();
        var pushedJson = pushed!.Value.ToString();
        pushedJson.Should().NotContain("\n");
        pushedJson.Should().Contain("net::ERR_NAME_NOT_RESOLVED");
    }

    [Fact]
    public async Task LogAttemptAsync_WhenRedisThrows_DoesNotPropagateException()
    {
        // Sağlık loglaması scraper'ın asıl iş akışını kesintiye uğratmamalı (bkz. IScraperHealthLogService notu).
        var (sut, _) = CreateSut(throwOnPush: true);

        var act = async () => await sut.LogAttemptAsync("bershka", "StockApi", success: false, httpStatusCode: 500, errorMessage: "boom", context: "https://example.com/urun | store=8359", durationMs: 10);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetStatsAsync_WhenNoEntries_ReturnsZeroStatsWithoutAlert()
    {
        var (sut, _) = CreateSut(rangeResult: []);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SampleSize.Should().Be(0);
        stats.SuccessRatePercent.Should().Be(0);
        stats.AlertTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatsAsync_ComputesSuccessRateAndHttpStatusCodeDistribution()
    {
        var entries = new RedisValue[]
        {
            Entry("StockApi", true, 200),
            Entry("StockApi", true, 200),
            Entry("StockApi", false, 403),
            Entry("PlaywrightPdp", true, 200),
        };
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SampleSize.Should().Be(4);
        stats.SuccessRatePercent.Should().Be(75.0);
        stats.HttpStatusCodeDistribution["200"].Should().Be(3);
        stats.HttpStatusCodeDistribution["403"].Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_EntriesWithNullHttpStatusCode_GroupedUnderNone()
    {
        var entries = new RedisValue[]
        {
            Entry("PlaywrightPdp", false, null, "timeout"),
        };
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.HttpStatusCodeDistribution["none"].Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_WhenSampleTooSmall_DoesNotTriggerAlertEvenIfAllFailed()
    {
        // Soğuk başlangıçta (ör. ilk 3 deneme) tek bir geçici hata "başarı oranı çöktü" alarmını tetiklememeli.
        var entries = new RedisValue[]
        {
            Entry("StockApi", false, 500),
            Entry("StockApi", false, 500),
            Entry("StockApi", false, 500),
        };
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SuccessRatePercent.Should().Be(0);
        stats.AlertTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatsAsync_WhenSuccessRateBelowThresholdWithSufficientSample_TriggersAlert()
    {
        var entries = Enumerable.Range(0, 10)
            .Select(i => Entry("StockApi", success: i < 5, httpStatusCode: i < 5 ? 200 : 403))
            .ToArray();
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SuccessRatePercent.Should().Be(50.0);
        stats.AlertTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatsAsync_WhenSuccessRateAboveThreshold_DoesNotTriggerAlert()
    {
        var entries = Enumerable.Range(0, 10)
            .Select(i => Entry("StockApi", success: i < 9, httpStatusCode: i < 9 ? 200 : 500))
            .ToArray();
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SuccessRatePercent.Should().Be(90.0);
        stats.AlertTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatsAsync_SkipsUnparseableEntriesWithoutThrowing()
    {
        var entries = new RedisValue[]
        {
            Entry("StockApi", true, 200),
            "not-a-valid-json",
        };
        var (sut, _) = CreateSut(rangeResult: entries);

        var stats = await sut.GetStatsAsync("bershka");

        stats.SampleSize.Should().Be(1);
    }

    [Fact]
    public async Task GetRecentFailuresAsync_ReturnsOnlyFailedEntriesWithContext()
    {
        var entries = new RedisValue[]
        {
            Entry("PlaywrightPdp", true, 200, context: "https://bershka.com/urun-1"),
            Entry("PlaywrightPdp", false, 404, errorMessage: "sayfa bulunamadı", context: "https://bershka.com/urun-2"),
            Entry("StockApi", false, 500, errorMessage: "HTTP 500", context: "https://bershka.com/urun-3 | store=8359"),
        };
        var (sut, _) = CreateSut(rangeResult: entries);

        var failures = await sut.GetRecentFailuresAsync("bershka");

        failures.Should().HaveCount(2);
        failures.Should().Contain(f => f.Context == "https://bershka.com/urun-2" && f.ErrorMessage == "sayfa bulunamadı");
        failures.Should().Contain(f => f.Context == "https://bershka.com/urun-3 | store=8359" && f.HttpStatusCode == 500);
    }

    [Fact]
    public async Task GetRecentFailuresAsync_RespectsLastNLimit()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(i => Entry("StockApi", false, 500, context: $"urun-{i}"))
            .ToArray();
        var (sut, _) = CreateSut(rangeResult: entries);

        var failures = await sut.GetRecentFailuresAsync("bershka", lastN: 2);

        failures.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStatsAsync_DifferentScraperNames_UseIndependentRedisKeys()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        var sut = new ScraperHealthLogService(redis.Object, Mock.Of<ILogger<ScraperHealthLogService>>());

        await sut.GetStatsAsync("bershka");
        await sut.GetStatsAsync("zara");

        db.Verify(d => d.ListRangeAsync(
            It.Is<RedisKey>(k => k.ToString() == "scraper:health:bershka:log"),
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.ListRangeAsync(
            It.Is<RedisKey>(k => k.ToString() == "scraper:health:zara:log"),
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}
