using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.MaviScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.MaviScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// CANLI VERİYLE DOĞRULANAN AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Mavi Scraper):
//   1. www.mavi.com Cloudflare'in arkasında — düz `curl`/HttpClient "Attention Required!" challenge sayfası
//      alıyor. Gerçek Chrome kanalı (`Channel = "chrome"`) şart.
//   2. Online stok, PDP'nin SSR HTML'ine gömülü düz bir global JS değişkeninde (`sizeVariantJson`) —
//      Bershka/Oysho'daki gibi hydration/component-ağacı taraması gerekmiyor, sayfa yüklenince zaten hazır.
//   3. Mağaza stoğu AYNI domain'de olduğu için (Zara'daki gibi) düz bir HttpClient ile çağrılamıyor —
//      PDP navigasyonuyla kurulan (Cloudflare'i geçmiş) tarayıcı oturumunun çerezleriyle, sayfa içinden
//      (`page.EvaluateAsync` + `fetch`) çağrılması gerekiyor.
public class PlaywrightMaviFetcher : IMaviPdpFetcher, IAsyncDisposable
{
    // ÖNEMLİ: bu, gerçek bir Chrome UA'sı olmalı — motoru Chromium/Chrome olan bir tarayıcının Safari UA'sı
    // göndermesi, UA/motor tutarsızlığı yüzünden başlı başına bariz bir bot sinyali.
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // `sizeVariantJson` global değişkenini düz bir diziye çevirir. PDP'nin zaten hesapladığı gerçek
    // barkod (`id`)/stok adedi (`stockLevel`)/durum (`stockLevelStatus`) bilgisini taşır.
    private const string ExtractSizesScript = """
        () => {
          if (typeof sizeVariantJson === 'undefined' || !Array.isArray(sizeVariantJson)) return null;
          const flat = sizeVariantJson.map(v => ({
            Size: v.size,
            Length: v.length || '',
            Barcode: v.id,
            StockLevel: v.stockLevel,
            StockLevelStatus: v.stockLevelStatus
          }));
          return flat.length > 0 ? JSON.stringify(flat) : null;
        }
        """;

    private const string ScraperName = "mavi";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightMaviFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightMaviFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightMaviFetcher> logger)
    {
        _healthLog = healthLog;
        _logger = logger;
    }

    public async Task<string?> FetchProductSizesJsonAsync(string productUrl, CancellationToken cancellationToken)
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

            // SSR'a gömülü — script etiketleri/global değişkenler DOMContentLoaded'da zaten hazır, ama
            // Cloudflare challenge'ının çözülmesi bir miktar sürebiliyor (canlı doğrulandı, Stradivarius'taki
            // "sabit kısa bekleme yetersiz kalıyor" bulgusuyla aynı gerekçe) — bu yüzden sabit bekleme yerine
            // değişkenin gerçekten tanımlı olmasını bekleyen bir polling kullanılıyor.
            string? sizesJson = null;
            for (var i = 0; i < 15; i++)
            {
                sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);
                if (sizesJson is not null) break;
                await page.WaitForTimeoutAsync(1000);
            }

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (sizeVariantJson bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Mavi ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    public async Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string barcode, double latitude, double longitude, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int? httpStatusCode = null;
        var context_ = $"{productUrl} | barcode={barcode} lat={latitude} lng={longitude}";

        try
        {
            var browser = await GetBrowserAsync(cancellationToken);
            await using var browserContext = await NewContextAsync(browser);
            var page = await browserContext.NewPageAsync();

            await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });
            // Cloudflare challenge'ının çözülmesi için (bkz. FetchProductSizesJsonAsync'teki aynı gerekçe).
            await page.WaitForTimeoutAsync(2000);

            var query = $"latitude={Uri.EscapeDataString(latitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}" +
                        $"&longitude={Uri.EscapeDataString(longitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}" +
                        "&page=0" +
                        $"&barcode={Uri.EscapeDataString(barcode)}" +
                        "&onlyClickAndCollectStores=false&allClickAndCollectStores=false";

            var script = $$"""
                async () => {
                  const res = await fetch('/magazalar/get-stores-by-location?{{query}}', {
                    headers: {'X-Requested-With': 'XMLHttpRequest'}
                  });
                  window.__maviStoreStatus = res.status;
                  if (!res.ok) return null;
                  return await res.text();
                }
                """;

            var resultJson = await page.EvaluateAsync<string?>(script);
            httpStatusCode = await page.EvaluateAsync<int?>("window.__maviStoreStatus");

            await _healthLog.LogAttemptAsync(
                ScraperName, "StoreAvailability", success: resultJson is not null, httpStatusCode,
                errorMessage: resultJson is null ? $"mağaza sorgusu başarısız (HTTP {httpStatusCode})" : null,
                context: context_, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return resultJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Mavi mağaza sorgusu ({Context}) alınamadı.", context_);
            await _healthLog.LogAttemptAsync(
                ScraperName, "StoreAvailability", success: false, httpStatusCode,
                errorMessage: ex.Message, context: context_, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    private async Task<IBrowserContext> NewContextAsync(IBrowser browser)
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

        // Playwright'ın varsayılan olarak sızdırdığı `navigator.webdriver=true` bayrağı, bot yönetim
        // sistemleri için tek başına yeterli bir tespit sinyali — her sayfa yüklenmeden önce bunu maskeliyoruz.
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
