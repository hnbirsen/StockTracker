using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.PullBearScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.PullBearScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// CANLI VERİYLE DOĞRULANAN KRİTİK AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Pull&Bear Scraper):
//   1. Zara/Bershka/Massimo Dutti'de olduğu gibi bundled Chromium Akamai'den anında engelleniyor —
//      `Channel = "chrome"` şart.
//   2. Ürün verisi Massimo Dutti'deki `#mdfrontw-state` script'inin AKSİNE bir custom element'in JS
//      özelliğinde tutuluyor: `<product-modular>` elementinin `__product` property'si (SSR script değil,
//      hydration sonrası JS state). Bu yüzden DOMContentLoaded sonrası daha uzun bir bekleme + `__product`
//      alanının dolmasını bekleyen bir polling script gerekiyor (sabit bir script yerine).
public class PlaywrightPullBearFetcher : IPullBearPdpFetcher, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ScraperName = "pullbear";

    // `product-modular` elementinin `__product` özelliği hydration tamamlanana kadar `undefined` kalıyor —
    // bu yüzden sabit bir bekleme yerine, alan dolana kadar (veya zaman aşımına kadar) polling yapan bir
    // script kullanılıyor. Bulunca renk/beden verisini düzleştirip (flatten) tek bir küçük JSON dizisi
    // olarak dönüyoruz (Massimo Dutti'deki ExtractSizesScript deseniyle aynı çıktı şekli).
    private const string ExtractSizesScript = """
        async () => {
          const deadline = Date.now() + 8000;
          let product = null;
          while (Date.now() < deadline) {
            const el = document.querySelector('product-modular');
            if (el && el.__product && el.__product.detail && Array.isArray(el.__product.detail.colors)) {
              product = el.__product;
              break;
            }
            await new Promise(r => setTimeout(r, 200));
          }
          if (!product) return null;

          const flat = [];
          for (const color of product.detail.colors) {
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
    private readonly ILogger<PlaywrightPullBearFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightPullBearFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightPullBearFetcher> logger)
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

            var sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (product-modular.__product bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Pull&Bear ürün sayfası ({Url}) alınamadı.", productUrl);
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
        // bir bot tespit sinyali — her sayfa yüklenmeden önce maskeleniyor (Bershka/Zara/Massimo Dutti'de de
        // aynı yaklaşım).
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
