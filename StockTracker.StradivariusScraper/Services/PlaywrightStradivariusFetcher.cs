using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.StradivariusScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.StradivariusScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// CANLI VERİYLE DOĞRULANAN KRİTİK AYRINTILAR (bkz. IStradivariusPdpFetcher üstündeki yorum ve
// .claude/ARCHITECTURE.md > Stradivarius Scraper):
//   1. Zara/Bershka/Massimo Dutti/Pull&Bear'da olduğu gibi bundled Chromium Akamai'den anında engelleniyor
//      — `Channel = "chrome"` şart.
//   2. Online stok SSR HTML'den doğrudan okunur (`button[data-testid="size-item"]` + `data-sku`) —
//      polling/hydration beklemesi GEREKMİYOR, diğer Inditex markalarının aksine.
//   3. Aynı ziyarette, kullanıcının paylaştığı gerçek ağ trafiğinden doğrulanan misafir oturum
//      `access_token` çerezi de okunuyor — bu, mağaza stok API'sinin (bkz. StradivariusStockApiClient)
//      `Authorization: Bearer` başlığı için gerekli tek kimlik bilgisi.
public class PlaywrightStradivariusFetcher : IStradivariusPdpFetcher, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ScraperName = "stradivarius";

    private const string ExtractOnlineSizesScript = """
        () => {
          const buttons = Array.from(document.querySelectorAll('button[data-testid="size-item"]'));
          if (buttons.length === 0) return null;
          const sizes = buttons.map(btn => {
            const nameEl = btn.querySelector('[data-testid="size-name"]');
            const name = nameEl ? nameEl.getAttribute('data-text') : btn.textContent.trim();
            const sku = nameEl ? nameEl.getAttribute('data-sku') : null;
            const fullText = btn.textContent || '';
            return {
              Name: (name || '').trim(),
              Sku: sku,
              Disabled: !!btn.disabled || btn.getAttribute('aria-disabled') === 'true',
              LowStock: fullText.toLowerCase().includes('tükenmek')
            };
          });
          const match = document.cookie.match(/(^| )access_token=([^;]+)/);
          const accessToken = match ? decodeURIComponent(match[2]) : null;
          return JSON.stringify({ AccessToken: accessToken, Sizes: sizes });
        }
        """;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightStradivariusFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightStradivariusFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightStradivariusFetcher> logger)
    {
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<string?> FetchOnlineSizesJsonAsync(string productUrl, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int? httpStatusCode = null;

        try
        {
            var browser = await GetBrowserAsync(cancellationToken);
            await using var context = await NewContextAsync(browser);
            var page = await context.NewPageAsync();

            var response = await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });
            httpStatusCode = response?.Status;

            // SSR HTML'den okunuyor — Massimo Dutti/Pull&Bear'daki hydration polling'ine gerek yok, ama
            // React'ın ilk render'ının oturması için kısa bir bekleme yine de güvenli. Guest-session
            // access_token çerezi de sayfa yüklenirken otomatik set ediliyor.
            await page.WaitForTimeoutAsync(500);
            var resultJson = await page.EvaluateAsync<string?>(ExtractOnlineSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: resultJson is not null, httpStatusCode,
                errorMessage: resultJson is null ? "Beden verisi çıkarılamadı (size-item butonu bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return resultJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Stradivarius ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgent,
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul",
            ViewportSize = new ViewportSize { Width = 1544, Height = 784 },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7"
            }
        });

        // Playwright'ın varsayılan olarak sızdırdığı `navigator.webdriver=true` bayrağı tek başına yeterli
        // bir bot tespit sinyali — her sayfa yüklenmeden önce maskeleniyor (diğer scraper'larla aynı yaklaşım).
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        return context;
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is not null) return _browser;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is not null) return _browser;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Channel = "chrome",
                Args = ["--disable-blink-features=AutomationControlled"]
            });
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
