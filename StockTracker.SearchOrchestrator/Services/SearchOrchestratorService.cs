using MassTransit;
using StockTracker.SearchOrchestrator.DTOs;
using StockTracker.Shared.Contracts.Messages.V2;
using StockTracker.Shared.Contracts.Messaging;

namespace StockTracker.SearchOrchestrator.Services;

public interface ISearchOrchestratorService
{
    Task<SearchResponse> SearchAsync(SearchRequest request);
}

public class SearchOrchestratorService : ISearchOrchestratorService
{
    private readonly IProductServiceClient _productClient;
    private readonly IBrandDetectionServiceClient _brandDetectionClient;
    private readonly IStoreReferenceServiceClient _storeReferenceClient;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<SearchOrchestratorService> _logger;

    public SearchOrchestratorService(
        IProductServiceClient productClient,
        IBrandDetectionServiceClient brandDetectionClient,
        IStoreReferenceServiceClient storeReferenceClient,
        ISendEndpointProvider sendEndpointProvider,
        ILogger<SearchOrchestratorService> logger)
    {
        _productClient = productClient;
        _brandDetectionClient = brandDetectionClient;
        _storeReferenceClient = storeReferenceClient;
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
    }

    // Bilinen marka domain'leri — yalnızca kullanıcıya "bu ürünü tanıyoruz ama kataloğumuzda henüz yok"
    // gibi anlamlı bir mesaj vermek için (bkz. SearchAsync altındaki UrlNotCatalogued dalı). Marka TESPİTİ
    // için kullanılmıyor — o iş zaten LookupByUrlAsync'in bulduğu kayıtta netleşiyor, burası yalnızca
    // "hiç kaydımız yoksa bile hangi markanın linki olduğunu anlayalım" amaçlı, kesin/regex bir eşleşme değil.
    private static readonly Dictionary<string, string> KnownBrandHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bershka.com"] = "Bershka",
        ["zara.com"] = "Zara",
        ["pullandbear.com"] = "Pull&Bear",
        ["mango.com"] = "Mango",
        ["hm.com"] = "H&M",
        ["massimodutti.com"] = "Massimo Dutti",
        ["beymen.com"] = "Beymen",
        ["stradivarius.com"] = "Stradivarius",
        ["oysho.com"] = "Oysho"
    };

    public async Task<SearchResponse> SearchAsync(SearchRequest request)
    {
        // Kullanıcı ürün kodu yerine doğrudan ürün sayfası linkini yapıştırdıysa — BrandCodeSignature
        // regex katmanı tamamen atlanır (URL zaten markayı kesin olarak belirtiyor). Yalnızca DAHA ÖNCE
        // kaydedilmiş bir eşleme bulunabilir; hiç görülmemiş bir URL için otomatik kod çıkarımı YAPILMAZ
        // (her markanın gerçek ürün/renk kodunu yalnızca PDP'yi çekerek öğrenebiliyoruz — bkz.
        // .claude/ARCHITECTURE.md ilgili scraper bölümleri). Bu durumda kullanıcıya "kataloğumuzda henüz
        // yok" denir — Faz'da planlanan katalog senkronizasyonu (sitemap taraması) tamamlanınca bilinen
        // her ürünün URL'i zaten kayıtlı olacağı için bu dal giderek daha az tetiklenecek.
        if (string.IsNullOrWhiteSpace(request.ProductCode) && !string.IsNullOrWhiteSpace(request.ProductUrl))
        {
            return await SearchByUrlAsync(request);
        }

        var productCode = request.ProductCode!.Trim();
        var lookup = await _productClient.LookupAsync(productCode);

        if (lookup is null || !lookup.IsResolved)
        {
            var resolve = await _brandDetectionClient.ResolveAsync(productCode);

            if (resolve.Candidates.Count == 0)
            {
                return new SearchResponse(
                    Guid.NewGuid(),
                    "BrandUnknown",
                    "Ürün kodu için marka tespit edilemedi. Lütfen kodu kontrol edin.",
                    null
                );
            }

            // Tek + yüksek güvenilirlikli eşleşme Brand Detection tarafından otomatik kaydedilmiş olabilir —
            // güncel durumu görmek için Product Service'i tekrar sorguluyoruz.
            lookup = await _productClient.LookupAsync(productCode);

            if (lookup is null || !lookup.IsResolved)
            {
                return new SearchResponse(
                    Guid.NewGuid(),
                    "BrandUnknown",
                    "Birden fazla marka adayı bulundu. Lütfen /api/brand-detection/resolve/manual ile manuel seçim yapın.",
                    resolve.Candidates
                        .Select(c => new BrandCandidateResponse(c.BrandId, c.BrandName, ConfidenceLevelName(c.Confidence), c.MatchedPattern))
                        .ToList()
                );
            }
        }

        return await DispatchCheckStockCommandsAsync(lookup, request);
    }

    private async Task<SearchResponse> SearchByUrlAsync(SearchRequest request)
    {
        var url = request.ProductUrl!.Trim();
        var lookup = await _productClient.LookupByUrlAsync(url);

        if (lookup is null || !lookup.IsResolved)
        {
            var recognizedBrand = KnownBrandHosts
                .FirstOrDefault(kv => url.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;

            var message = recognizedBrand is not null
                ? $"Bu linkin {recognizedBrand} ürünü olduğunu tanıyoruz ama kataloğumuzda henüz kayıtlı değil. Lütfen ürün kodunu girerek tekrar deneyin."
                : "Bu link tanınan markalardan birine ait değil ya da kataloğumuzda henüz kayıtlı değil. Lütfen ürün kodunu girerek tekrar deneyin.";

            return new SearchResponse(Guid.NewGuid(), "UrlNotCatalogued", message, null);
        }

        return await DispatchCheckStockCommandsAsync(lookup, request);
    }

    // ProductCode ya da ProductUrl'den çözümlenmiş bir lookup'ı alıp, konum(lar) verilmişse ilgili
    // mağazalara, verilmemişse yalnızca online kontrol için CheckStockCommand'ları kuyruğa yollar.
    private async Task<SearchResponse> DispatchCheckStockCommandsAsync(ProductLookupResponse lookup, SearchRequest request)
    {
        var locations = request.Locations is { Count: > 0 } ? request.Locations : null;

        if (locations is null)
        {
            await SendCheckStockCommandAsync(lookup, request.Size, storeId: null, brandSpecificStoreId: null, city: null, district: null);
        }
        else
        {
            foreach (var location in locations)
            {
                var stores = await _storeReferenceClient.GetStoresAsync(lookup.BrandId!.Value, location.City, location.District);

                if (stores.Count == 0)
                {
                    // Store Reference'ta bu marka/il/ilçe için kayıtlı mağaza yok — StoreId'siz gönder,
                    // scraper en azından online stok kontrolü yapabilsin (bkz. .claude/ARCHITECTURE.md).
                    await SendCheckStockCommandAsync(lookup, request.Size, storeId: null, brandSpecificStoreId: null, location.City, location.District);
                    continue;
                }

                foreach (var store in stores)
                {
                    await SendCheckStockCommandAsync(lookup, request.Size, store.Id, store.BrandSpecificStoreId, location.City, location.District, store.Latitude, store.Longitude);
                }
            }
        }

        return new SearchResponse(
            Guid.NewGuid(),
            "Queued",
            "İsteğiniz alındı, stok sonucu bildirim ile iletilecek.",
            null
        );
    }

    private async Task SendCheckStockCommandAsync(ProductLookupResponse lookup, string size, Guid? storeId, string? brandSpecificStoreId, string? city, string? district, double? storeLatitude = null, double? storeLongitude = null)
    {
        var queueName = QueueNaming.StockCheckQueue(lookup.ScraperQueueName!);
        var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queueName}"));

        await sendEndpoint.Send(new CheckStockCommand(
            CommandId: Guid.NewGuid(),
            ProductCode: lookup.ProductCode,
            BrandId: lookup.BrandId!.Value,
            BrandName: lookup.BrandName!,
            Size: size,
            StoreId: storeId,
            BrandSpecificStoreId: brandSpecificStoreId,
            City: city,
            District: district,
            ProductUrl: lookup.ProductUrl,
            RequestedAt: DateTime.UtcNow,
            StoreLatitude: storeLatitude,
            StoreLongitude: storeLongitude
        ));

        _logger.LogInformation(
            "CheckStockCommand gönderildi — queue: {Queue}, product: {ProductCode}, storeId: {StoreId}, city: {City}, district: {District}",
            queueName, lookup.ProductCode, storeId, city, district);
    }

    private static string ConfidenceLevelName(int confidence) => confidence switch
    {
        1 => "Low",
        2 => "Medium",
        3 => "High",
        _ => "Unknown"
    };
}
