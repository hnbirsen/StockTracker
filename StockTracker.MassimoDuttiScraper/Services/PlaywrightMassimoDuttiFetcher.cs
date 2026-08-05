using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MassimoDuttiScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.MassimoDuttiScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// CANLI VERİYLE DOĞRULANAN KRİTİK AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Massimo Dutti Scraper):
//   1. Zara/Bershka'da olduğu gibi bundled Chromium Akamai'den anında engelleniyor — `Channel = "chrome"` şart.
//   2. Ürün verisi bir Angular SSR state script'inde (`#mdfrontw-state`, düz JSON — RSC gibi çift-escape
//      edilmiş değil) gömülü: `{"ITX_GET_PRODUCT_DETAIL_KEY": {"colors": [{"id": "251", "sizes": [...]}]}}`.
//      Bershka'nın Vue-tree taramasına benzer bir bekleyişe gerek yok — script DOMContentLoaded ile birlikte
//      hazır, yine de güvenlik payı için kısa bir bekleme uygulanıyor.
public class PlaywrightMassimoDuttiFetcher : IMassimoDuttiPdpFetcher, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ScraperName = "massimodutti";

    // `#mdfrontw-state` içindeki state, Mango'nun RSC akışının aksine çift-escape edilmiş bir string değil —
    // doğrudan geçerli JSON. Bu yüzden burada renk/beden verisini düzleştirip (flatten) tek bir küçük JSON
    // dizisi olarak dönüyoruz (Zara/Mango'daki ExtractSizesScript deseniyle birebir aynı yaklaşım).
    private const string ExtractSizesScript = """
        () => {
          const stateEl = document.getElementById('mdfrontw-state');
          if (!stateEl) return null;

          let data;
          try {
            data = JSON.parse(stateEl.textContent);
          } catch (e) {
            return null;
          }

          const product = data && data['ITX_GET_PRODUCT_DETAIL_KEY'];
          if (!product || !Array.isArray(product.colors)) return null;

          const flat = [];
          for (const color of product.colors) {
            if (!color || !Array.isArray(color.sizes)) continue;
            for (const size of color.sizes) {
              flat.push({
                Name: size.name,
                ColorId: String(color.id),
                CatEntryId: String(color.catentryId),
                MastersSizeId: String(size.mastersSizeId),
                IsBuyable: !!size.isBuyable,
                BackSoon: size.backSoon
              });
            }
          }
          return flat.length > 0 ? JSON.stringify(flat) : null;
        }
        """;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightMassimoDuttiFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightMassimoDuttiFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightMassimoDuttiFetcher> logger)
    {
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<string?> FetchProductDataJsonAsync(string productUrl, CancellationToken cancellationToken)
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
            await page.WaitForTimeoutAsync(3000);

            var sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (#mdfrontw-state bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Massimo Dutti ürün sayfası ({Url}) alınamadı.", productUrl);
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
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7"
            }
        });

        // Playwright'ın varsayılan olarak sızdırdığı `navigator.webdriver=true` bayrağı tek başına yeterli
        // bir bot tespit sinyali — her sayfa yüklenmeden önce maskeleniyor (Bershka/Zara'da da aynı yaklaşım).
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
