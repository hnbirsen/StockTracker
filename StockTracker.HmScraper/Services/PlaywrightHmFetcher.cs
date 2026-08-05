using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.HmScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
// Kurulum gereksinimi Zara/Bershka ile aynı — bkz. .claude/ENVIRONMENT_SETUP.md.
//
// CANLI VERİYLE DOĞRULANAN KRİTİK AYRINTILAR:
//   1. Zara/Bershka'da olduğu gibi bundled Chromium Akamai'den 403 alır — `Channel = "chrome"` şart
//      (bu spesifik iddia H&M için ayrıca tekrar test edilmedi, ama `curl` ile doğrulanan "Access Denied"
//      sayfası Zara/Bershka'yla birebir aynı formatta olduğu için aynı Akamai altyapısı kullanıldığı ve
//      aynı çözümün geçerli olduğu varsayılıyor).
//   2. PDP verisi `__NEXT_DATA__` içinde SSR ile gömülü (Zara/Bershka'nın Vue-hydration/component-tree
//      beklentisinden farklı, Next.js'in klasik Pages Router mekanizması) — sayfa DOMContentLoaded olur
//      olmaz zaten mevcut, uzun bir hydration beklemesine gerek yok.
//   3. Mağaza stok endpoint'i (`/tr_tr/sis/tr/...`) da PDP kadar Akamai korumalı (canlı doğrulandı: `curl`
//      403 alıyor) — bu yüzden Zara'daki gibi HTTPClient DEĞİL, PDP'ye yapılan navigasyonla kurulan
//      tarayıcı oturumunun çerezleriyle sayfa içinden (`fetch`) çağrılıyor. Bu endpoint ayrıca
//      `Content-Type: application/json` header'ı OLMADAN `415 Unsupported Media Type` döndürüyor (canlı
//      doğrulandı) — bu yüzden script bu header'ı açıkça ekliyor.
//   4. Zara'da bulunan hıza dayalı (velocity-based) Akamai bloklaması H&M için AYRICA test edilmedi, ama
//      aynı altyapı (Akamai) olduğu için aynı riskin geçerli olabileceği varsayılıp aynı savunmacı
//      önlem (mağaza sorguları arası minimum bekleme + tekil eşzamanlılık) burada da uygulanıyor.
public class PlaywrightHmFetcher : IHmPdpFetcher, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ScraperName = "hm";

    private static readonly TimeSpan MinStoreQueryInterval = TimeSpan.FromSeconds(6);

    private const string ExtractSizesScript = """
        () => {
          const el = document.getElementById('__NEXT_DATA__');
          if (!el) return null;
          let data;
          try { data = JSON.parse(el.textContent); } catch (e) { return null; }

          const ppp = data.props?.pageProps?.productPageProps;
          if (!ppp) return null;

          const articleCode = ppp.articleCode;
          const ssrAvail = ppp.ssrAvailability || {};
          const availSet = new Set(ssrAvail.availability || []);
          const fewSet = new Set(ssrAvail.fewPieceLeft || []);

          const variation = ppp.aemData?.productArticleDetails?.variations?.[articleCode];
          const sizes = variation?.sizes || [];
          if (sizes.length === 0) return null;

          const flat = sizes.map(s => ({
            Name: s.name,
            SizeCode: s.size,
            Available: availSet.has(s.sizeCode),
            FewPieceLeft: fewSet.has(s.sizeCode)
          }));

          return JSON.stringify(flat);
        }
        """;

    private const string FetchStoreAvailabilityScript = """
        async ({ productId, artId, latitude, longitude }) => {
          const url = `/tr_tr/sis/tr/${productId}/${artId}?latitude=${latitude}&longitude=${longitude}&radiusinmeters=15000&maxnumberofstores=100&brand=000&channel=02`;
          try {
            const res = await fetch(url, { headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' } });
            const body = res.ok ? await res.text() : null;
            return JSON.stringify({ status: res.status, ok: res.ok, body });
          } catch (e) {
            return JSON.stringify({ status: 0, ok: false, body: null });
          }
        }
        """;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _storeQueryGate = new(1, 1);
    private DateTime _lastStoreQueryAt = DateTime.MinValue;

    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightHmFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightHmFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightHmFetcher> logger)
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
            await page.WaitForTimeoutAsync(2000);

            var sizesJson = await page.EvaluateAsync<string?>(ExtractSizesScript);

            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: sizesJson is not null, httpStatusCode,
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (__NEXT_DATA__ bulunamadı)" : null,
                context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile H&M ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    public async Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string productId, string artId, double latitude, double longitude, CancellationToken cancellationToken)
    {
        await _storeQueryGate.WaitAsync(cancellationToken);
        try
        {
            var elapsedSinceLast = DateTime.UtcNow - _lastStoreQueryAt;
            if (elapsedSinceLast < MinStoreQueryInterval)
            {
                await Task.Delay(MinStoreQueryInterval - elapsedSinceLast, cancellationToken);
            }

            var stopwatch = Stopwatch.StartNew();
            int? httpStatusCode = null;
            var context = $"{productUrl} | productId={productId} artId={artId} lat={latitude} lng={longitude}";

            try
            {
                var browser = await GetBrowserAsync(cancellationToken);
                await using var browserContext = await NewContextAsync(browser);
                var page = await browserContext.NewPageAsync();

                var response = await page.GotoAsync(productUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });
                httpStatusCode = response?.Status;
                await page.WaitForTimeoutAsync(1500);

                var rawResult = await page.EvaluateAsync<string>(FetchStoreAvailabilityScript,
                    new { productId, artId, latitude, longitude });

                string? availabilityJson = null;
                try
                {
                    using var doc = JsonDocument.Parse(rawResult);
                    var root = doc.RootElement;
                    httpStatusCode = root.TryGetProperty("status", out var statusEl) ? statusEl.GetInt32() : httpStatusCode;
                    if (root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean() &&
                        root.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String)
                    {
                        availabilityJson = bodyEl.GetString();
                    }
                }
                catch (JsonException)
                {
                    // rawResult ayrıştırılamadı — availabilityJson null kalır, aşağıda hata olarak loglanır.
                }

                await _healthLog.LogAttemptAsync(
                    ScraperName, "StoreAvailability", success: availabilityJson is not null, httpStatusCode,
                    errorMessage: availabilityJson is null ? $"sis yanıtı alınamadı (HTTP {httpStatusCode})" : null,
                    context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

                return availabilityJson;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Playwright ile H&M mağaza stok bilgisi ({Context}) alınamadı.", context);
                await _healthLog.LogAttemptAsync(
                    ScraperName, "StoreAvailability", success: false, httpStatusCode,
                    errorMessage: ex.Message, context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
                return null;
            }
            finally
            {
                _lastStoreQueryAt = DateTime.UtcNow;
            }
        }
        finally
        {
            _storeQueryGate.Release();
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
        _storeQueryGate.Dispose();
    }
}
