using System.Diagnostics;
using Microsoft.Playwright;
using StockTracker.Shared.Scraping.Health;

namespace StockTracker.ZaraScraper.Services;

// Chrome'u process başına bir kez başlatıp (pahalı) her istekte yeniden kullanır; izolasyon için her
// navigasyonda yeni bir BrowserContext+Page açılıp kapatılır. Singleton olarak kaydedilmeli (bkz. Program.cs).
//
// NOT: bu servis çalışmadan önce makinede/CI'da gerçek Chrome kanalı indirilmiş olmalı:
//   dotnet build && pwsh StockTracker.ZaraScraper/bin/.../playwright.ps1 install chrome
// (Mac/Linux'ta pwsh yoksa: `node .playwright/package/cli.js install chrome` — bkz. .claude/ENVIRONMENT_SETUP.md)
//
// CANLI VERİYLE DOĞRULANAN KRİTİK AYRINTILAR (bkz. .claude/ARCHITECTURE.md > Zara Scraper):
//   1. Bershka'da olduğu gibi bundled Chromium Akamai'den anında 403 alıyor — `Channel = "chrome"` şart.
//   2. PDP verisi (`window.zara.viewPayload`) SSR ile gömülü olduğu için Bershka'nın Vue-hydration
//      bekleyişi kadar uzun bir sabit beklemeye gerek yok; yine de tüm inline script'lerin çalışması için
//      DOMContentLoaded sonrası kısa bir bekleme (3sn) uygulanıyor.
//   3. store-product-availability endpoint'i de Akamai korumalı (düz curl/HttpClient 403 döner, gerçekçi
//      UA farketmiyor) — bu yüzden HTTPClient DEĞİL, PDP'ye yapılan navigasyonla kurulan tarayıcı
//      oturumunun çerezleriyle sayfa içinden (`fetch`) çağrılıyor.
//   4. HIZA DAYALI (velocity-based) Akamai bloklaması: birkaç saniye içinde art arda birden fazla
//      store-product-availability isteği atıldığında — daha önce ÇALIŞAN bir sorgu bile dahil — TÜM
//      tarayıcı oturumu 403'e düşüyor (canlı testte doğrulandı). Bu yüzden mağaza sorguları tekil
//      eşzamanlılıkla (semaphore) ve aralarında kasıtlı bir minimum bekleme süresiyle sınırlanıyor.
//   5. Yanıt şekli "seyrek" (sparse): sorgulanan mağazalardan sadece o ÜRÜN/BEDEN kombinasyonuna gerçekten
//      stoğu olanlar dizide yer alıyor — dizide hiç yer almayan bir mağaza, hata değil "bu mağazada bu
//      üründen yok" anlamına geliyor (aynı istekte iki mağaza sorgulanıp biri dönmemesiyle doğrulandı).
public class PlaywrightZaraFetcher : IZaraPdpFetcher, IAsyncDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ScraperName = "zara";

    // Akamai'nin hıza dayalı bloklamasını tetiklememek için store-product-availability istekleri arasında
    // uygulanan minimum bekleme. CANLI VERİYLE DOĞRULANDI (Faz 6.1, birden çok test turu): 8 sn aralıkla
    // bile 7. istekte oturum 403'e düşüyor ve ~45-60 sn'lik ek bir bekleme bunu çözmüyor.
    //
    // DÖRDÜNCÜ TURDA KESİNLEŞEN VE ÖNCEKİ HİPOTEZİ ÇÜRÜTEN BULGU: bu blok IP/ağ itibarına dayalı DEĞİL.
    // Kanıt: (1) kalıcı bir tarayıcı profili (`LaunchPersistentContextAsync`) + organik gezinme simülasyonu
    // eklenerek tekrar denendi, yine 403 alındı; (2) TAM O SIRADA, aynı makineden, GERÇEK bir kullanıcı
    // tarayıcı oturumuyla atılan aynı istek `200` başarılı oldu — aynı IP, aynı an, farklı sonuç; (3)
    // Selenium+ChromeDriver (farklı bir otomasyon protokolü) de aynı şekilde 403 aldı. Yani engelleme,
    // otomasyon aracının KENDİSİNİN (Playwright/Selenium fark etmeksizin — CDP izleri, ChromeDriver'ın
    // enjekte ettiği `$cdc_` değişkenleri, `navigator.webdriver` vb.) tespit edilmesinden kaynaklanıyor —
    // IP değil. **Bu yüzden proxy/IP rotasyonu (Faz 7) bu sorunu ÇÖZMEYECEK** — bkz. `.claude/PENDING_INPUTS.md`.
    // Daha derin bir çözüm (CDP/WebDriver izi gizleme, fingerprint sahteciliği) kasıtlı olarak
    // uygulanmadı: bu, projenin bilinçli olarak kapsam dışı bıraktığı "bot-tespitini aktif atlatma"
    // sınırını aşar (bkz. `.claude/ARCHITECTURE.md` > Ölçeklenme Riski, en alttaki not).
    // FetchStoreAvailabilityJsonAsync zaten 403/hata durumunda null döner (çağıran taraf bunu Unknown'a
    // çevirir, exception fırlatmaz) — bu yüzden blok, hatalı sonuç değil sadece azalan başarı oranı üretir.
    private static readonly TimeSpan MinStoreQueryInterval = TimeSpan.FromSeconds(6);

    private const string ExtractSizesScript = """
        () => {
          const payload = window.zara && window.zara.viewPayload;
          const detail = payload && payload.product && payload.product.detail;
          if (!detail || !Array.isArray(detail.colors)) return null;

          const flat = [];
          for (const color of detail.colors) {
            if (!color || !Array.isArray(color.sizes)) continue;
            for (const size of color.sizes) {
              flat.push({
                Name: size.name,
                Availability: size.availability,
                ColorId: String(color.id),
                Sku: String(size.sku),
                ProductId: String(color.productId)
              });
            }
          }
          return flat.length > 0 ? JSON.stringify(flat) : null;
        }
        """;

    // GERÇEK VERİYLE BULUNAN DÜZELTME: eskiden `!res.ok` durumunda doğrudan `null` dönüyordu — bu, health
    // log'a yalnızca sayfa navigasyonunun (200) durumunu bırakıyor, asıl fetch çağrısının gerçek durum
    // kodunu (403 vb.) tamamen gizliyordu (canlı testte "success:false, httpStatusCode:200" gibi yanıltıcı
    // kayıtlarla fark edildi). Artık her zaman `{status, ok, body}` şeklinde JSON döner — C# tarafı gerçek
    // durum kodunu health log'a yazabiliyor.
    private const string FetchStoreAvailabilityScript = """
        async ({ productId, storeId }) => {
          const url = `/tr/tr/store-product-availability?productId=${encodeURIComponent(productId)}&physicalStoreIds=${encodeURIComponent(storeId)}&ajax=true`;
          try {
            const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
            const body = res.ok ? await res.text() : null;
            return JSON.stringify({ status: res.status, ok: res.ok, body });
          } catch (e) {
            return JSON.stringify({ status: 0, ok: false, body: null });
          }
        }
        """;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private record FetchResult(int Status, bool Ok, string? Body);

    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Mağaza sorguları arasında minimum aralığı uygulamak için tekil eşzamanlılık + son çağrı zamanı.
    private readonly SemaphoreSlim _storeQueryGate = new(1, 1);
    private DateTime _lastStoreQueryAt = DateTime.MinValue;

    private readonly IScraperHealthLogService _healthLog;
    private readonly ILogger<PlaywrightZaraFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightZaraFetcher(IScraperHealthLogService healthLog, ILogger<PlaywrightZaraFetcher> logger)
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
                errorMessage: sizesJson is null ? "Beden verisi çıkarılamadı (window.zara.viewPayload bulunamadı)" : null,
                context: productUrl,
                (int)stopwatch.ElapsedMilliseconds, cancellationToken);

            return sizesJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright ile Zara ürün sayfası ({Url}) alınamadı.", productUrl);
            await _healthLog.LogAttemptAsync(
                ScraperName, "PlaywrightPdp", success: false, httpStatusCode,
                errorMessage: ex.Message, context: productUrl, (int)stopwatch.ElapsedMilliseconds, cancellationToken);
            return null;
        }
    }

    public async Task<string?> FetchStoreAvailabilityJsonAsync(string productUrl, string productId, string physicalStoreId, CancellationToken cancellationToken)
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
            var context = $"{productUrl} | productId={productId} storeId={physicalStoreId}";

            try
            {
                var browser = await GetBrowserAsync(cancellationToken);
                await using var browserContext = await NewContextAsync(browser);
                var page = await browserContext.NewPageAsync();

                // Mağaza stoğu isteğinden önce ürün sayfasına navigasyon şart: hem Akamai'nin çerez tabanlı
                // "temiz" oturum kanıtını kurmak (bkz. sınıf üstündeki not, bulgu #3) hem de meşru bir
                // referrer sağlamak için.
                var response = await page.GotoAsync(productUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });
                httpStatusCode = response?.Status;
                await page.WaitForTimeoutAsync(2000);

                var rawResult = await page.EvaluateAsync<string>(FetchStoreAvailabilityScript,
                    new { productId, storeId = physicalStoreId });
                var fetchResult = System.Text.Json.JsonSerializer.Deserialize<FetchResult>(rawResult, JsonOptions);

                // Fetch'in KENDİ durum kodu (sayfa navigasyonunun değil) health log'a yazılıyor — eskiden
                // burada yalnızca sayfa navigasyon durumu (genelde 200) loglanıyordu, asıl fetch'in gerçek
                // 403'ü tamamen gizli kalıyordu (canlı testte fark edilen bir gözlemlenebilirlik açığıydı).
                httpStatusCode = fetchResult?.Status ?? httpStatusCode;
                var availabilityJson = fetchResult?.Ok == true ? fetchResult.Body : null;

                await _healthLog.LogAttemptAsync(
                    ScraperName, "StoreAvailability", success: availabilityJson is not null, httpStatusCode,
                    errorMessage: availabilityJson is null ? $"store-product-availability yanıtı alınamadı (HTTP {httpStatusCode})" : null,
                    context, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

                return availabilityJson;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Playwright ile Zara mağaza stok bilgisi ({Context}) alınamadı.", context);
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

        // Playwright'ın varsayılan olarak sızdırdığı `navigator.webdriver=true` bayrağı tek başına yeterli
        // bir bot tespit sinyali — her sayfa yüklenmeden önce maskeleniyor (Bershka'da da aynı yaklaşım).
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
