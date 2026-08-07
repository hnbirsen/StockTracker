using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.OyshoScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı — birkaç saniye sürer) her istekte yeniden kullanır;
// izolasyon için her çağrıda yeni bir BrowserContext+Page açılıp kapatılır (Chrome process'i canlı kalır).
// Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.OyshoScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// GERÇEK VERİYLE DOĞRULANAN AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Oysho Scraper):
//   1. www.oysho.com Akamai Bot Manager'ın arkasında — düz `curl`/HttpClient bir `bm-verify` JS-yönlendirme
//      (challenge) sayfası alıyor (Zara/Massimo Dutti/Pull&Bear ile birebir aynı format). Gerçek Chrome
//      kanalı (`Channel = "chrome"`) şart.
//   2. Veri Bershka'daki gibi bir component ağacında DEĞİL — SUNUCU TARAFINDA render edilmiş, doğrudan
//      `#oyshoServer-state` script etiketinin içinde geçerli JSON olarak geliyor
//      (`PRODUCT_HYDRATION_KEY.product.colors[].sizes[]`). Hydration/component-ağacı taraması gerekmiyor,
//      script etiketinin DOM'da belirmesini beklemek yeterli.
public class PlaywrightOyshoFetcher : IOyshoPdpFetcher, IAsyncDisposable
{
    // ÖNEMLİ: bu, gerçek bir Chrome UA'sı olmalı — motoru Chromium/Chrome olan bir tarayıcının Safari UA'sı
    // göndermesi, UA/motor tutarsızlığı yüzünden başlı başına bariz bir bot sinyali.
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // `#oyshoServer-state` script etiketini okuyup PRODUCT_HYDRATION_KEY.product.colors[].sizes[]'i
    // düz bir diziye çevirir. Her SizeEntry, PDP'nin zaten hesapladığı gerçek partnumber/masterSizeId/
    // availability/hasFewUnits alanlarını taşır — biz hiçbir kod/id üretmiyoruz.
    private const string ExtractSizesScript = """
        () => {
          const script = document.getElementById('oyshoServer-state');
          if (!script) return null;

          let data;
          try { data = JSON.parse(script.textContent); } catch (e) { return null; }

          const product = data && data.PRODUCT_HYDRATION_KEY && data.PRODUCT_HYDRATION_KEY.product;
          if (!product || !Array.isArray(product.colors)) return null;

          const flat = [];
          for (const color of product.colors) {
            if (!color || !Array.isArray(color.sizes)) continue;
            for (const size of color.sizes) {
              if (!size || !size.partnumber) continue;
              flat.push({
                Name: size.name,
                Availability: size.availability,
                HasFewUnits: !!size.hasFewUnits,
                PartNumber: size.partnumber,
                MasterSizeId: String(size.masterSizeId),
                ColorId: String(color.id)
              });
            }
          }
          return flat.length > 0 ? JSON.stringify(flat) : null;
        }
        """;

    private const string ScraperName = "oysho";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightOyshoFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightOyshoFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightOyshoFetcher> logger)
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

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
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

            // Playwright'ın varsayılan olarak sızdırdığı `navigator.webdriver=true` bayrağı, Akamai gibi bot
            // yönetim sistemleri için tek başına yeterli bir tespit sinyali — her sayfa yüklenmeden önce bunu maskeliyoruz.
            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            var page = await context.NewPageAsync();

            var response = await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });
            httpStatusCode = response?.Status;

            // Veri SSR ile geldiği için Bershka'daki gibi uzun bir hydration beklemesine gerek yok —
            // script etiketinin DOM'a eklenmesini (Akamai challenge'ı çözülüp gerçek sayfa render olana
            // kadar) beklemek yeterli.
            try
            {
                // ÖNEMLİ: State=Attached şart — <script> etiketleri hiçbir zaman "visible" olmaz
                // (varsayılan bekleme durumu), bu yüzden varsayılanla her zaman timeout alınıyordu
                // (bu ortamda gerçek Chrome kanalıyla canlı test edilirken bulunan gerçek bir hataydı).
                await page.WaitForSelectorAsync("#oyshoServer-state", new PageWaitForSelectorOptions
                {
                    Timeout = 15000,
                    State = WaitForSelectorState.Attached
                });
            }
            catch (TimeoutException)
            {
                // Akamai challenge çözülmemiş ya da sayfa engellenmiş olabilir — aşağıdaki extraction zaten null dönecek.
            }

            var sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (oyshoServer-state bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
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
