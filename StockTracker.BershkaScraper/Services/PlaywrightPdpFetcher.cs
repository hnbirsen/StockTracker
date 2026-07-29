using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.BershkaScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı — birkaç saniye sürer) her istekte yeniden
// kullanır; izolasyon için her çağrıda yeni bir BrowserContext+Page açılıp kapatılır (Chrome
// process'i canlı kalır). Singleton olarak kaydedilmeli (bkz. Program.cs) — DI container, IAsyncDisposable
// implement eden singleton'ları uygulama kapanışında otomatik Dispose eder.
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.BershkaScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// GERÇEK VERİYLE DOĞRULANAN KRİTİK AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Bershka Scraper > Playwright):
//   1. Playwright'ın bundled Chromium'u (varsayılan) Akamai'den anında "Access Denied" (403) alıyor —
//      UA/webdriver maskeleme gibi standart stealth önlemleri bile yetmedi. Gerçek Chrome kanalını
//      (`Channel = "chrome"`) sürmek bu engeli aştı.
//   2. `WaitUntilState.NetworkIdle` bu sayfada hiç tetiklenmiyor (muhtemelen sürekli arka plan telemetri
//      isteği) — `DOMContentLoaded` + sabit bir bekleme kullanılıyor; ağır ürün sayfalarında (çok renk/beden)
//      tüm veri hydrate olmadan içerik okunursa eksik/hatalı sonuç alınıyor (8 saniyede stabil).
//   3. Statik HTML'i regex ile taramak güvenilir değil (bkz. IBershkaPdpFetcher üstündeki not) — bunun
//      yerine sayfanın Vue component ağacı (`__nuxt` kökü) taranıp zaten çözümlenmiş veri okunuyor.
public class PlaywrightPdpFetcher : IBershkaPdpFetcher, IAsyncDisposable
{
    // ÖNEMLİ: bu, gerçek bir Chrome UA'sı olmalı — motoru Chromium/Chrome olan bir tarayıcının Safari UA'sı
    // göndermesi, UA/motor tutarsızlığı yüzünden başlı başına bariz bir bot sinyali.
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // Sayfa yüklendikten sonra Vue component ağacını (document.getElementById('__nuxt').__vue__) tarar;
    // $data/$props içinde colors[].sizes[] barındıran component'i bulup ZATEN ÇÖZÜMLENMİŞ (gerçek, minify
    // sırasında değişkene çıkarılmış olsa bile JS çalışma zamanında her zaman gerçek değere sahip) beden
    // listesini düz bir diziye çevirip JSON olarak döner. Bulamazsa null döner.
    private const string ExtractSizesScript = """
        () => {
          const root = document.getElementById('__nuxt');
          if (!root || !root.__vue__) return null;

          function extract(container) {
            const flat = [];
            for (const color of container.colors) {
              if (!color || !Array.isArray(color.sizes)) continue;
              for (const size of color.sizes) {
                const type = size.types && size.types[0];
                if (!type || !type.partnumber) continue;
                flat.push({
                  Name: size.name,
                  Stock: size.stock,
                  PartNumber: type.partnumber,
                  MastersSizeId: String(type.mastersSizeId),
                  ColorId: String(color.id)
                });
              }
            }
            return flat;
          }

          const seen = new Set();
          let visited = 0;
          function search(comp, depth) {
            if (!comp || depth > 30 || visited > 5000 || seen.has(comp)) return null;
            seen.add(comp);
            visited++;

            for (const src of [comp.$data, comp.$props]) {
              if (!src) continue;
              if (Array.isArray(src.colors) && src.colors.length > 0 && src.colors[0] && Array.isArray(src.colors[0].sizes)) {
                return extract(src);
              }
              for (const key of Object.keys(src)) {
                const val = src[key];
                if (val && Array.isArray(val.colors) && val.colors.length > 0 && val.colors[0] && Array.isArray(val.colors[0].sizes)) {
                  return extract(val);
                }
              }
            }

            for (const kid of (comp.$children || [])) {
              const found = search(kid, depth + 1);
              if (found) return found;
            }
            return null;
          }

          const result = search(root.__vue__.$root, 0);
          return result && result.length > 0 ? JSON.stringify(result) : null;
        }
        """;

    private const string ScraperName = "bershka";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightPdpFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightPdpFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightPdpFetcher> logger)
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
            // 3sn yetersiz kaldı (gerçek veriyle doğrulandı) — ağır ürün sayfalarında tüm renk/beden
            // verisi hydrate olmadan içerik okunuyordu (ör. jean sayfasında 7 bedenden sadece 1'i vardı).
            await page.WaitForTimeoutAsync(8000);

            var sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (Vue ağacında bulunamadı)" : null,
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
