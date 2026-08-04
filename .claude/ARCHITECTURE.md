# Mimari Dokümanı

## Genel Yaklaşım

Sistem, **database-per-service** prensibiyle çalışan bağımsız mikroservislerden oluşur. Dış client trafiği YARP Gateway üzerinden gelir. Servisler arası iç iletişim ise doğrudan HTTP ile yapılır — gateway'den geçmez.

## Servisler

| Servis | Sorumluluk | Veritabanı | Port | Durum |
|---|---|---|---|---|
| Identity Service | Kayıt, login, JWT/refresh token, logout | `identity_db` | 5001 | ✅ Tamamlandı |
| Product Service | Barkod/kod lookup, marka eşleştirme, Redis cache | `product_db` | 5002 | ✅ Tamamlandı |
| Brand Detection Service | Regex format eşleşmesi, manuel marka seçimi | `brand_db` | 5003 | ✅ Tamamlandı |
| Store Reference Service | İl/ilçe → marka-spesifik mağaza ID eşleştirme | `store_db` | 5004 | ✅ Tamamlandı |
| Search Orchestrator | Kullanıcı sorgusunu kuyruğa yönlendirme | — (Redis: throttle) | 5005 | ✅ Tamamlandı |
| Subscription Service | Watch group, takip listesi, dedup | `subscription_db` | 5006 | ✅ Faz 3.1 tamamlandı |
| Billing Service | Freemium plan yönetimi, App Store/Play Store IAP doğrulama + webhook | `billing_db` | 5007 | 🔜 Planlanıyor |
| Notification Service | Push (FCM) + e-posta bildirimleri | `notification_db` | 5008 | ✅ Faz 3.3 tamamlandı (gerçek Firebase/SendGrid hesabı hariç) |
| Bershka Scraper | `CheckStockCommand` tüketir, `StockResultEvent` yayınlar | — (Redis: PDP cache-aside, kendi DB'si yok) | 5009 | ✅ Gerçek API entegre edildi (bkz. altta) |
| API Gateway (YARP) | Dış trafik yönlendirme, JWT doğrulama, rate limiting | — | 8000 | ✅ Tamamlandı |

## Servisler Arası İletişim

### Dış Trafik (Client → Servis)
Client'lardan (web/mobil) gelen tüm istekler API Gateway (port 8000) üzerinden geçer. Gateway JWT doğrulaması ve rate limiting yapar, ardından ilgili servise yönlendirir.

### İç Trafik (Servis → Servis)
Servisler birbirleriyle **doğrudan HTTP** üzerinden iletişim kurar — gateway bypass edilir. Bu yaklaşımın nedenleri:
- Gereksiz gecikme ve hop'tan kaçınmak
- Gateway down olduğunda iç iletişimin etkilenmemesi
- İç endpoint'lerin dışarıya açılmaması

Mevcut iç iletişim: `BrandDetection → Product Service` (mapping kaydetmek için)

### Asenkron (RabbitMQ)
Stok değişikliği, bildirim tetikleme, scraping tamamlanma gibi olaylar event olarak yayınlanır:
1. Scraper stok değişikliğini tespit eder → `StockResultEvent` yayınlar
2. Notification Service event'i dinler → kullanıcıya push/e-posta gönderir

## YARP Gateway Route Yapısı

```
/api/identity/*        → Identity Service       (5001)
/api/product/*         → Product Service         (5002)
/api/brand-detection/* → Brand Detection Service (5003)
/api/store-reference/* → Store Reference Service (5004)
/api/search/*          → Search Orchestrator     (5005)
/api/subscriptions/*   → Subscription Service    (5006)
/api/billing/*         → Billing Service         (5007)
/api/notifications/*   → Notification Service    (5008)
```

> `PathPattern` ve `PathRemovePrefix` transform'ları **aynı blokta kullanılamaz** — her biri ayrı transform bloğunda tanımlanır.

## Kimlik Doğrulama Akışı

1. Client → `POST /api/identity/auth/login` → Identity Service JWT üretir
2. Client her istekte `Authorization: Bearer <token>` header'ı gönderir
3. Gateway JWT'yi doğrular, geçerliyse servise iletir
4. Access token süresi dolunca → `POST /api/identity/auth/refresh` ile yeni token çifti alınır

**İç iletişim (Faz 3.3)**: `GET /internal/users/{id}` — gateway bypass, direkt HTTP. Notification Service, bildirim göndereceği kullanıcının email adresini buradan çözer.

## Marka Tespit Akışı (Brand Detection)

```
Kullanıcı ürün kodu girer
    ↓
Product Service — ProductBrandMap'te var mı? (Redis cache → DB)
    ↓ Hayır
Brand Detection Service — format imzası eşleşmesi (regex)
    ↓ Tek + yüksek güvenilirlik → Product Service'e otomatik kaydet
    ↓ Çoklu aday veya düşük güvenilirlik → kullanıcıya aday listesi sun
    ↓ Kullanıcı seçti → POST /resolve/manual → Product Service'e kaydet
```

Katmanlar (öncelik sırasıyla):
1. **Regex format signature** — bilinen marka kod formatlarıyla eşleştirme (şu an aktif)
2. **Site-search API fallback** — markanın site içi arama API'si (Faz 2'de eklenecek)
3. **Genel arama motoru fallback** — yukarıdakiler başarısız olursa (Faz 2'de eklenecek)

Başarılı eşleşmeler `ProductBrandMap` tablosuna yazılır → sonraki sorgular Redis cache'ten anında döner.

## Scraping Mimarisi (Faz 2)

- Her marka için ayrı, izole scraper worker servisi
- RabbitMQ üzerinden `CheckStockCommand` alır, `StockResultEvent` yayınlar
- Polly ile retry + circuit breaker — bir markanın engellenmesi diğerlerini etkilemez
- Scraper health monitoring — başarı oranı düşünce alert üretir

### Mesajlaşma Katmanı (Faz 2.1 — Tamamlandı)

- **Sözleşmeler**: `StockTracker.Shared.Contracts/Messages/V1/` altında `CheckStockCommand` ve `StockResultEvent` — namespace bazlı versiyonlama (`Messages.V1`). Breaking değişiklik gerektiğinde eski consumer'ları bozmadan `Messages.V2` namespace'i eklenir.
- **Kuyruk isimlendirme**: `QueueNaming.StockCheckQueue(brandName)` → `stock.check.{brandName}` (küçük harf). Her markanın kendi izole kuyruğu vardır; bir markanın scraper'ı tıkandığında diğerleri etkilenmez.
- **`CheckStockCommand`** marka-spesifik kuyruğa doğrudan **send** edilir (point-to-point — komutu sadece o markanın scraper'ı işler). **`StockResultEvent`** ise **publish** edilir (fanout — sonuca birden fazla dinleyici, örn. Notification Service ve Search Orchestrator, ilgi duyabilir).
- **MassTransit vs ham RabbitMQ.Client kararı**: MassTransit seçildi — Polly'yle tutarlı retry/circuit-breaker middleware'i, connection/topology yönetimini soyutlaması, ve test edilebilirliği (`MassTransit.TestFramework`) nedeniyle. ⚠️ **MassTransit v9+ ticari lisans gerektiriyor** (bkz. `SetLicense`/`MT_LICENSE`); proje son açık kaynak (Apache 2.0) sürüm olan **8.5.5**'e sabitlendi (`StockTracker.Shared.Contracts.csproj`). Paket sürümü ileride yükseltilecekse önce lisans gereksinimi kontrol edilmeli.
- **Kurulum**: `StockTracker.Shared.Contracts.Messaging.ServiceCollectionExtensions.AddStockTrackerRabbitMq(...)` — tüm servislerin ortak kullandığı bağlantı/host okuma mantığı (`RABBITMQ_HOST`/`RABBITMQ_USER`/`RABBITMQ_PASSWORD`). Consumer kaydı ve endpoint routing'i `configureConsumers`/`configureEndpoints` delegate'leri ile servis bazlı yapılır.
- **Uçtan uca doğrulama**: Gerçek RabbitMQ container'ına karşı `CheckStockCommand` → `stock.check.bershka` kuyruğuna gönderim → consume → `StockResultEvent` publish → consume round-trip'i manuel bir smoke-test ile doğrulandı (kod kalıcı değil, sadece doğrulama amaçlıydı). Search Orchestrator (Faz 2.2) ve Bershka Scraper (Faz 2.4) servisleri gerçek iş mantığıyla bu topolojiyi kullanacak.

### Search Orchestrator (Faz 2.2 — Tamamlandı)

- `POST /search` — `{ userId, productCode, size, locations? }` alır. Akış: Product Service'e `GET /lookup/{code}` → çözülmemişse Brand Detection'a `POST /resolve` → tek+yüksek-güven otomatik kaydedilmişse Product tekrar sorgulanır. Marka biliniyorsa her lokasyon için (veya lokasyon yoksa tek "online" komut için) `CheckStockCommand` `stock.check.{ScraperQueueName}` kuyruğuna **send** edilir (point-to-point, `ISendEndpointProvider.GetSendEndpoint(new Uri("queue:..."))`). `StoreId` şu an her zaman `null` — gerçek mağaza eşlemesi Faz 2.3'te Store Reference Service ile gelecek; City/District ham metin olarak taşınır.
- **Throttle**: Redis `search:throttle:{userId}:{productCode}:{size}` key'i `SETNX` ile 30 saniyelik pencerede kilitlenir — aynı kullanıcı aynı ürün/beden için art arda istek atarsa `429 Too Many Requests` döner.
- **Yanıt tasarımı**: Marka biliniyorsa `202 Accepted` + `{searchId, status:"Queued", message:"..."}` (asenkron, sonuç ileride bildirimle gelecek). Marka bilinmiyorsa (aday yok veya çoklu aday/manuel seçim gerekiyor) `200 OK` + `status:"BrandUnknown"` + varsa aday listesi — client bunu doğrudan `/api/brand-detection/resolve/manual`'a yönlendirebilir.
- Servis stateless'tir (kendi veritabanı yok); Product/Brand Detection/Store Reference ile internal HTTP (`ProductServiceClient`, `BrandDetectionServiceClient`, `StoreReferenceServiceClient`), RabbitMQ ile mesajlaşma, Redis ile throttle durumu tutar.

### Store Reference Service (Faz 2.3 — Tamamlandı)

- `GET /stores?brandId=&city=&district=` — üçü de opsiyonel filtre; sadece `IsActive=true` mağazalar döner. `city`/`district` karşılaştırması case-insensitive.
- `Stores` tablosu: `BrandId`/`BrandName` (product_db.Brands ile convention-based referans, FK değil), `City`, `District`, `StoreName`, `BrandSpecificStoreId` (markanın kendi sitesindeki mağaza kodu — scraper bunu kullanacak).
- Seed data: Bershka için 4 mağaza (Istanbul/Kadıköy, Istanbul/Şişli, Ankara/Çankaya, Izmir/Bornova). `BrandSpecificStoreId` değerleri şu an yer tutucudur (`BSK-IST-KDK-01` gibi) — Faz 2.4'te Bershka scraper'ı geliştirilirken gerçek mağaza kodlarıyla güncellenmesi gerekiyor.
- **Search Orchestrator entegrasyonu**: bir arama isteğinde konum (city/district) verildiğinde, Search Orchestrator önce `GET /stores?brandId=&city=&district=` ile bu markanın o ilçedeki mağazalarını sorgular. Eşleşme varsa her mağaza için ayrı `CheckStockCommand` (`StoreId` dolu) gönderilir; eşleşme yoksa `StoreId=null` ile fallback komut gönderilir (scraper en azından online stok kontrolü yapabilsin diye — bkz. Mesajlaşma Katmanı notu).
- **`GET /stores/{id}`** (Faz 3.2'de eklendi): tek bir mağazayı ID'sinden döner (`IsActive=false` ise 404). Subscription Service'in Stock Poller'ı, `WatchGroup.StoreId`'den `BrandSpecificStoreId`'yi çözmek için bunu kullanır — `GET /stores` yalnızca liste/filtre destekliyordu, poller'ın ihtiyaç duyduğu tekil ID sorgusu yoktu.

### Bershka Scraper (Faz 2.4 — Tamamlandı)

- **Consumer**: `CheckStockCommandConsumer`, `stock.check.bershka` kuyruğunu dinler. `CheckStockCommand` (**V2**, bkz. altta) alır, `BershkaStockCheckService.CheckAsync(...)` çağırır, dönen `StockResultEvent`'i (V1, değişmedi) publish eder.
- **Yönlendirme mantığı** (`BershkaStockCheckService`): önce `command.ProductUrl` boş mu diye bakılır — boşsa API client'a hiç gidilmeden `Unknown` (`ScraperSource: "no-product-url"`) dönülür. Doluysa: `command.StoreId` doluysa VE `BrandSpecificStoreId` de doluysa → `IBershkaStockApiClient.CheckStoreStockAsync(...)` (fiziksel mağaza kontrolü); aksi halde → `CheckOnlineStockAsync(...)` (online kontrol).
- **Dayanıklılık**: Stok API HttpClient'ı `AddTransientHttpErrorPolicy` ile 3 kez exponential backoff retry (2s, 4s, 8s) + art arda 5 hatada 30 saniyelik devre kesici kullanır (`Microsoft.Extensions.Http.Polly`). `ScraperEtiquetteHandler` her istekte 4 gerçekçi tarayıcı User-Agent'ı arasından rastgele seçim yapar ve 300-1200ms rastgele gecikme uygular (bkz. `.claude/SECURITY.md`). Prodüksiyona karşı doğrulandı: varsayılan `curl` User-Agent'ı ile istek **403** dönerken, gerçekçi bir tarayıcı User-Agent'ı ile aynı istek **200** dönüyor.

#### `CheckStockCommand` V2 — neden `ProductUrl` eklendi

`Messages.V1.CheckStockCommand` değişmez (dokümante edilmiş kural). `Messages.V2.CheckStockCommand` tek bir alan ekliyor: `ProductUrl` (Product Service'in `ProductBrandMap.ProductUrl` alanından geliyor, sadece manuel/site-search ile çözülmüş mapping'lerde dolu — regex-only çözümlerde null). Search Orchestrator artık V2 gönderiyor, BershkaScraper V2 tüketiyor. Bu alan gerekli oldu çünkü **iki ayrı yanlış varsayım gerçek verilerle çürütüldü**:

1. `productCode`'un ilk hanesi sabit `0` değil, ürün kategorisine göre değişiyor (giyimde `0`, ayakkabıda `1` görüldü — REF `1032/864/100` çizme → gerçek partnumber `1103286410035-I2026`).
2. Alfabetik bedenlerde (XS/S/M/L) part-number'ın son 2 hanesi bedenin kendisinden değil, **ürüne özgü bir sıra numarasından** geliyor (tahmin edilemez).

Tek güvenilir kaynak: ürünün kendi sayfasında Bershka'nın zaten hesapladığı gerçek `partnumber`/`stock` bilgisi — bu da sayfanın URL'sini gerektiriyor.

#### Ürün sayfasından (PDP) gerçek stok verisi okuma

Ürün sayfasının SSR HTML'ine gömülü minified JS nesnesinde (`colors[].sizes[]`) Bershka'nın **her beden için** zaten hesapladığı gerçek veriler var — tam olarak sitede "beden seçildiğinde disabled görünme" davranışının kaynağı (ör. `{stock:"in_stock",name:"XS",types:[{partnumber:"0332736067601-I2026",mastersSizeId:"101"}]}`).

**İlk deneme (terk edildi) — statik HTML'i regex ile ayrıştırma**: Chrome kanalıyla çekilen HTML'e karşı bir regex (`SizeEntryRegex`) çalıştırılıyordu. Bu, jean/çizme/bazı top'larda çalıştı ama **çiçekli korse askılı top**'ta (REF 3327/360/676, kullanıcının bildirdiği ürün) tutarlı şekilde başarısız oldu — canlı HTML incelendiğinde nedeni bulundu: bu sayfada değerler literal string olarak değil (`stock:"in_stock"`), **paylaşılan değişken referansı** olarak geliyordu (`stock:am`, gerçek değer `am="in_stock"` sayfanın başka bir yerinde tanımlı). Bu, bundler'ın string tekilleştirme (deduplication) optimizasyonu — hangi sayfada tetikleneceği dışarıdan öngörülemez, bazı ürünlerde oluyor bazılarında olmuyor. Statik metinde gerçek değer hiç yazılı olmadığından regex'i "düzeltmek" mümkün değildi.

**Nihai tasarım — Vue component ağacından JS çalışma zamanı okuma**: `PlaywrightPdpFetcher.FetchProductSizesJsonAsync`, sayfa yüklendikten sonra `document.getElementById('__nuxt').__vue__` kökünden başlayıp `$children` ağacını (sınırlı derinlik/ziyaret sayısıyla, ~600-1000 component tipik) tarar; `$data`/`$props` içinde `colors[].sizes[]` barındıran component'i bulup `page.EvaluateAsync` ile **JS'in kendi çalışma zamanında çözümlediği gerçek değerleri** okur. Bu, minifier'ın string'i literal mi değişkene mi çıkardığından tamamen bağımsız çalışır — Vue'nun reaktif state'i her zaman gerçek JS değerini tutar, kaynak kodun görünümü onu etkilemez. Sonuç doğrudan `List<SizeEntry>` (Name, Stock, PartNumber, MastersSizeId) şeklinde JSON olarak C# tarafına dönüyor; `BershkaStockApiClient` artık hiçbir regex/HTML ayrıştırması yapmıyor.

`BershkaStockApiClient`, verilen `productCode`'un **son 3 hanesinden çıkardığı `ColorId`** (PDP'nin kendi `color.id` alanıyla, aile/partnumber öneki bağımsız — bkz. altta neden StartsWith yerine bu kullanılıyor) ve beden adına (case-insensitive **string eşleşmesi** — "36" da "XS" de aynı şekilde çalışır) sahip kaydı bulur:
- **Online stok**: o bedenin `Stock` alanı `"in_stock"` mı? (`"coming_soon"` ve `"out_of_stock"` — üçüncü bir değer, canlı veride keşfedildi — ikisi de `false` sayılır).
- **Mağaza stok**: o bedenin gerçek `PartNumber`'ı (campaign dahil, `-` ile ayrılıyor) ile `api.inditex.com/.../stock/campaign/{campaignId}/product/part-number/{digits}?physicalStoreId=...` sorgulanır; dönen `sizeStocks[]` içinden `MastersSizeId`'ye eşit **`size`** alanı filtrelenir (tek çağrı, mağaza başına TÜM bedenlerin stoğunu döndürüyor — filtrelemezsek yanlış bedenin adedini okumuş oluruz).

> #### ⚠️ Kritik hata bulundu ve düzeltildi — `sizeId` ≠ `size`
> Kullanıcının 10 gerçek üründe canlı testi çapraz doğrulaması sırasında bulundu: stok API'sinin yanıtındaki `sizeStocks[]` dizisinde **`sizeId` ve `size` farklı alanlar**:
> ```json
> {"sizeId": 3, "size": 103, "quantity": 4}
> ```
> `sizeId`, mağaza içindeki bedenlerin sıralı/anlamsız bir indeksi (1,2,3,4...); `size` ise PDP'den okunan `MastersSizeId` ile birebir eşleşen gerçek beden kodu. İlk implementasyon `sizeId`'ye göre filtreliyordu — sayısal bedenlerde (jean: 32,34,36) `sizeId` ve `size` tesadüfen eşit çıktığı için bu fark edilmedi, ama alfabetik/3-haneli `mastersSizeId` sistemi (101,102,103...) kullanan ürünlerde `sizeId=3` hiçbir zaman aranan `103`'e eşit olmadığından **gerçek stok varken sürekli yanlışlıkla `OutOfStock` dönüyordu**. Kullanıcı bunu, uygulamanın söylediği ile sitede/mağazada bizzat gördüğü arasındaki farkı bildirerek yakaladı (bkz. Faz 2.4 canlı doğrulama). `SizeStockDto`'ya `Size` alanı eklenip filtreleme buna göre düzeltildi; regresyon testi eklendi.

> #### Boş mağaza yanıtı artık `Unknown`, `OutOfStock` değil
> Bazı ürünler mağaza stok özelliğini hiç desteklemiyor (sitede "Mağazada mevcudiyet" bölümü bile görünmüyor) — bu durumda stok API'si `{"stocks":[]}` döner. Bunu `OutOfStock` saymak yanlış ve zararlı: kullanıcı ürünü mağazada gerçekten görebilir, biz sadece o mağaza için hiç veri alamamışızdır — "bilmiyorum" demek, yanlış "yok" demekten daha güvenli (kullanıcının deneyimini korur). `CheckStoreStockAsync` artık: (1) `sizeStocks` tamamen boşsa, (2) hedef beden yanıtta hiç bulunamazsa → `Unknown` (`null`) döner; yalnızca gerçek bir `sizeStocks` kaydı bulunup adedi sıfırsa `OutOfStock` döner.

> #### ⚠️ Kritik hata bulundu ve düzeltildi — bir renk, birden fazla partnumber "ailesi"ne bölünmüş olabiliyor
> 3. tur canlı testinden sonra kullanıcı, uygulamanın `OutOfStock`/`Unknown` dediği 2 üründe (ürün 2, ürün 8) ürünü bizzat sitede/mağazada **stokta** gördüğünü bildirdi. Kök neden, ürün 2 ("Kısa kollu poplin bağlamalı gömlek", "Haki" rengi) üzerinde derinlemesine incelenip doğrulandı:
> - Aynı `ColorId` (renk) içindeki bedenler **farklı partnumber öneklerine** ("aile") dağılmış olabiliyor — ör. Haki'de XS/S/L bedenleri `01337711507` ailesinden, M/XL ise **tamamen farklı** `01337015507` ailesinden geliyordu. İkisi de gerçek, bağımsız stoklanan SKU kayıtları — Bershka/Inditex tarafında bir parti/batch ayrımı.
> - Sitenin kendi "Mağazada mevcudiyet" akışı, Playwright ile ağ trafiği yakalanarak izlendiğinde, **her iki aileyi de aynı beden-konum koduyla paralel sorgulayıp** (ör. `part-number/0133771150701` VE `part-number/0133701550701`, aynı 9 mağaza için) sonuçları birleştirdiği görüldü.
> - Eski implementasyon sadece hedef bedenin KENDİ partnumber'ını (tek bir aile) sorguluyordu — beden diğer ailede kayıtlıysa (PDP feed'indeki aile ataması güvenilir değil) o ailenin o mağazadaki gerçek stoğu tamamen kaçırılıyordu.
> - Ayrıca, eski `ResolveSizeEntryAsync` bir bedeni bulmak için `entry.PartNumber.StartsWith(productCode)` kontrolü yapıyordu — bu da AYNI hatanın bir başka yüzüydü: caller'ın gönderdiği `productCode` sadece BİR aileyle prefix-eşleşiyordu, o ailede bulunmayan bedenler (ör. M/XL, `01337711507` ailesinde hiç yok) için **hiç eşleşme bulunamıyor**, sessizce `Unknown` dönüyordu.
>
> **Düzeltme**: (1) beden eşleştirmesi artık partnumber prefix'i yerine PDP'nin kendi `color.id` alanından türetilen `ColorId`'ye göre yapılıyor (aile ayrımından bağımsız, güvenilir); (2) `CheckStoreStockAsync`, hedef bedenin kendi konum kodunu (partnumber'ın son 2 hanesi) aynı `ColorId`'ye ait TÜM bilinen aile öneklerine uygulayıp hepsini paralel sorguluyor, dönen adetleri topluyor — sitenin kendi davranışıyla birebir aynı. Regresyon testleri eklendi (`ResolveSizeEntry_MatchesByColorIdNotPartNumberPrefix_...`, `CheckStoreStockAsync_WhenColorHasMultiplePartNumberFamilies_...`).

Bu yaklaşım kategoriye/beden sistemine göre dallanma **gerektirmiyor** — Bershka'nın kendi hesapladığı veriyi okuyoruz, kendimiz kod/id üretmiyoruz.

> **Değerlendirilip vazgeçilen alternatif**: DOM'daki beden butonlarının `aria-disabled` niteliği de (`size-button--disabled` class'ı, `aria-description="Yakında stokta olacak!"`) online stok sinyalini güvenilir şekilde veriyor — ama üzerinde `partnumber` gibi bir `data-*` niteliği yok, yani mağaza-bazlı kontrol için tek başına yeterli değil. Vue ağacı taraması hem online hem mağaza ihtiyacını tek kaynaktan karşıladığı için tercih edildi.

#### Playwright + Redis cache-aside — neden gerekli

PDP'ler Akamai Bot Manager'ın arkasında. Prodüksiyona karşı doğrulanan üç ayrı katman:

1. **Çerezsiz `curl`** (herhangi bir UA ile) → tutarlı şekilde ~2.3KB'lık bir JS interstitial (proof-of-work challenge) sayfası döner, gerçek içerik yok. Stok/mağaza-bulucu JSON API'leri bundan etkilenmiyor.
2. **Playwright'ın varsayılan (bundled) Chromium'u** → UA/`navigator.webdriver` maskeleme gibi standart stealth önlemlerine rağmen Akamai'den **anında "Access Denied" (403)** alındı (interstitial bile değil, sert bir edge-level red). Kök neden kesin olarak izole edilemedi (TLS/fingerprint farkı olası).
3. **Gerçek Chrome kanalı** (`BrowserTypeLaunchOptions.Channel = "chrome"`, Playwright'ın kendi Chromium'u değil, makinedeki gerçek Google Chrome) → engeli aştı, gerçek içerik döndü. `WaitUntilState.NetworkIdle` bu sayfada hiç tetiklenmediği için (muhtemelen sürekli arka plan telemetri isteği) `DOMContentLoaded` + sabit bir bekleme kullanılıyor; ağır ürün sayfalarında (çok renk/beden) tüm veri hydrate olması **8 saniye** sürebiliyor (3 saniye yetersiz kaldı, gerçek veriyle doğrulandı — jean sayfasında 7 bedenden yalnızca 1'i gelmişti).

- **`IBershkaPdpFetcher` / `PlaywrightPdpFetcher`**: gerçek Chrome kanalını headless çalıştırıp JS'i işleterek Akamai'nin challenge'ını bir tarayıcı gibi geçer. Chrome process'i pahalı olduğu için singleton olarak bir kez başlatılır, her istekte yeni bir `BrowserContext`/`Page` açılıp kapatılır (izolasyon, process yeniden başlatılmadan).
- **Redis cache-aside** (`BershkaStockApiClient`, key: `bershka:pdp-sizes:{productUrl}`, TTL 15 dk): bir PDP çekimi ürünün **tüm bedenlerinin** verisini tek seferde verdiği için, aynı ürüne yapılan art arda aramalar (farklı il/ilçe, farklı kullanıcı, hem online hem mağaza kontrolü) Playwright'ı tekrar tetiklemez. Bu, hem gecikmeyi (çoğu istek cache'den milisaniyelerde döner) hem de Akamai'nin hacimle biriken "bot itibarı" riskini azaltır (bkz. altta Ölçeklenme Riski). Boş sonuç cache'lenmez — geçici bir hata olabilir, bir sonraki istek tekrar dener.
- **Kurulum gereksinimi**: Playwright'ın .NET paketi tarayıcı binary'sini indirmez — her geliştirici makinesinde/deploy ortamında bir kez `playwright install chrome` (bundled `chromium` değil, `chrome` kanalı) çalıştırılmalı. `bin/` gitignore'da olduğu için bu adım repo klonlandığında görünmez — bkz. `.claude/ENVIRONMENT_SETUP.md` → "Bershka Scraper — Playwright/Chromium Kurulumu".
- **Alfabetik bedenler destekleniyor**: eski tasarımda (`TryBuildSizeId`) sadece sayısal bedenler kabul ediliyordu; artık PDP'den gerçek veri okunduğu için hem sayısal hem alfabetik bedenler aynı kod yoluyla çalışıyor.

**Playwright'ın güvenilirliği hakkında dürüst bir not** (kullanıcıyla konuşuldu, bkz. altta Ölçeklenme Riski): headless/otomasyon tespiti ve hacim arttıkça düşen başarı oranı kalıcı riskler — cache-aside bunu *azaltır*, ortadan kaldırmaz. CAPTCHA'ya eskalasyon durumunda (kapsam dışı, çözülmeye çalışılmıyor) sonuç `Unknown` olur. **Çerez/oturum yeniden kullanımı** (Playwright'ın çözdüğü Akamai çerezini bir süre plain `HttpClient` ile tekrar kullanmak, Playwright çağrısını daha da seyrekleştirmek için) bilinçli olarak **şimdilik eklenmedi** — cache-aside'ın yeterliliği görüldükten sonra gerekirse eklenecek bir sonraki optimizasyon olarak not edildi.

**Somut örnek — gerçek geçici timeout**: 10 ürünlük ikinci canlı test turunda bir üründe `CheckOnlineStockAsync` beklenmedik şekilde `Unknown` döndü. Kök neden araştırıldı: aynı ürün 3 kez art arda çekildi, dönen veri (renk/beden/stok) **tamamen tutarlıydı** — yani bir veri tutarsızlığı değildi. Ayrı bir denemede ise `page.GotoAsync` gerçekten **30 saniyelik navigasyon timeout'una** takıldı. Yani ara sıra yaşanan `Unknown` sonuçları çoğunlukla Playwright'ın kendi geçici ağ/render yavaşlığından kaynaklanıyor — sistem bunu doğru şekilde `Unknown`'a çeviriyor (yanlış `true`/`false` uydurmuyor), ama bu, prodüksiyonda belirli bir oranda beklenmesi gereken, normal bir davranış.

**Canlı doğrulama — tamamlandı, beş tur**: `PlaywrightPdpFetcher`/`BershkaStockApiClient`, geliştirici makinesinde gerçek Chrome kurulduktan sonra bizzat çalıştırılıp **toplam 40+ farklı gerçek üründe** (çeşitli kategoriler: jean, top, elbise, etek, pantolon, gömlek, şort, sweatshirt, hırka, bot, spor ayakkabı) canlı Bershka sitesine, gerçek mağazalara (Kadıköy/Şişli/Çankaya/Bornova rotasyonlu) ve gerçek `api.inditex.com` stok API'sine karşı test edildi:
- **1. tur (4 ürün, sadece online)**: 7/7 sonuç sitedeki gerçek durumla birebir örtüştü (kullanıcının bildirdiği "XS stokta, S/M/L değil" senaryosu dahil).
- **2. tur (10 ürün, online + mağaza)**: kullanıcı sonuçları sitede/mağazada bizzat kontrol edip 3 üründe (6, 7, 8) tutarsızlık bildirdi — bu, yukarıdaki `sizeId`/`size` hatasının bizzat kullanıcı tarafından yakalanmasını sağladı. Düzeltme sonrası **3. tur (10 yeni ürün)** ile yeniden doğrulandı; ayrıca bir üründeki beklenmeyen `Unknown` sonucu ayrıca araştırılıp gerçek bir geçici Playwright timeout'u olduğu (veri tutarsızlığı değil) kanıtlandı.
- **4. tur (3. turdaki sonuçların kullanıcı tarafından yeniden çapraz doğrulanması)**: kullanıcı 2 üründe (2, 8) yine tutarsızlık bildirdi — bu, yukarıdaki "bir renk, birden fazla partnumber ailesi" hatasının bulunup düzeltilmesini sağladı (ürün 2 üzerinde Playwright ile sitenin kendi ağ trafiği yakalanarak kök nedeni kanıtlandı). Diğer iki üründe (3, 9) kullanıcının "beden bilgisi olmadan aratılmış" gözlemi araştırıldı; PDP'nin Vue verisi ve DOM beden butonları her iki üründe de test edilen bedenin geçerli ve aktif olduğunu doğruladı.
- **5. tur (`ColorId`/çoklu-aile düzeltmesi sonrası, 10 yeni ürün)**: üretimdeki `BershkaStockApiClient`/`PlaywrightPdpFetcher` sınıfları doğrudan (yalnızca Redis cache mock'landı — aynı PDP'nin iki kez Playwright ile çekilmesini önlemek için) top/jean/elbise kategorilerinden 10 üründe, her birinde 2 farklı beden × 2 farklı gerçek mağaza olacak şekilde çalıştırıldı; toplam 20 online + 40 mağaza kontrolü gerçek stok API'sine karşı yapıldı. Bazı ürün/mağaza kombinasyonlarında beklenen ve doğru `Unknown` (o mağaza için stok verisi hiç dönmeyen ürünler) gözlendi, önceki turlarda bulunan hata sınıflarından hiçbiri tekrarlanmadı. **Kullanıcı sonuçları doğru olarak onayladı.**

Kod, aynı veri şekillerini taklit eden JSON mock'larla 24 unit testle kapsanıyor (`sizeId`≠`size` regresyonu, boş-mağaza-yanıtı→`Unknown`, `ColorId` bazlı eşleştirme ve çoklu-aile toplama testleri dahil).

### Scraper Health Monitoring (Faz 2.5 — Tamamlandı)

Her deneme (Playwright PDP çekimi, stok API'sine giden her HTTP isteği) başarılı/başarısız olarak, HTTP durum koduyla birlikte loglanır — amaç, bir marka scraper'ının sessizce bozulmaya başladığını (ör. Akamai tespiti sıkılaştı, oran %200→%403'e kaydı) erken yakalamak.

> #### Tasarım kararı: ayrı bir Postgres DB yerine paylaşılan Redis
> İlk tasarım `ScraperHealthLog`'u kendi Postgres tablosu olan yeni bir `bershka_scraper_db` olarak öngörüyordu (proje genelinde "database-per-service" prensibiyle tutarlı olsun diye). Kullanıcı bunu haklı olarak sorguladı: **birden fazla marka scraper'ı** (Faz 6.1'de Zara, Pull&Bear) geldiğinde her biri için ayrı bir DB açmak (docker-compose girişi, migration, connection string — N marka için N kez tekrar) hem gereksiz operasyonel yük hem de merkezi görünürlüğü ("hangi scraper şu an başarısız oluyor" gibi tek bir soruya cevap vermek için N farklı DB'yi sorgulamak gerekir) zorlaştırır. `ScraperHealthLog` aslında Bershka'ya özel bir domain verisi değil — tüm scraper'larda birebir aynı şekilde tekrarlanacak operasyonel telemetri. Proje zaten Search Orchestrator throttle'ı ve Bershka PDP cache'ini tam bu gerekçeyle (domain değil, operasyonel/cross-cutting veri) paylaşılan tek Redis instance'ında tutuyor — health log de aynı kategoriye girer. **Karar**: yeni bir DB açılmadı; `scraper:health:{scraperName}:log` key'inde (scraper adına göre namespace'lenmiş) bir capped-list (`LPUSH` + `LTRIM` ile son 500 deneme) kullanılıyor.

- **`StockTracker.Shared.Scraping`** (yeni paylaşılan proje — `.claude/ARCHITECTURE.md` > Ölçeklenme Riski'nde öngörülen "paylaşılan scraping altyapısı"nın ilk parçası): `IScraperHealthLogService`/`ScraperHealthLogService`. `LogAttemptAsync(scraperName, source, success, httpStatusCode, errorMessage, durationMs)` her denemeyi Redis'e yazar — **asla exception fırlatmaz** (Redis'e erişilemese bile stok kontrolü sonucu kullanıcıya dönmeye devam etmeli, sağlık loglaması ikincil bir kaygı). `GetStatsAsync(scraperName, lastN)` son N denemeyi okuyup `SuccessRatePercent`, `HttpStatusCodeDistribution` (ör. `{"200":18,"403":2}`) ve `AlertTriggered` hesaplar.
- **Alarm mekanizması**: örneklem en az 10 olmadan (soğuk başlangıçta tek bir geçici hatanın "başarı oranı çöktü" alarmını tetiklememesi için) ve başarı oranı %70 eşiğinin altına düştüğünde `AlertTriggered=true` döner + `ILogger` üzerinden bir Warning log basılır. Ayrı bir bildirim kanalı (Slack/e-posta) şu an yok — proje genelinde henüz böyle bir altyapı yok, bu yüzden yapılandırılmış log, bu fazın kapsamındaki en gerçekçi "alert" biçimi.
- **Entegrasyon noktaları**: `PlaywrightPdpFetcher.FetchProductSizesJsonAsync` (`source: "PlaywrightPdp"`, `httpStatusCode` gerçek `page.GotoAsync` yanıtının `Status`'undan, `context: productUrl`) ve `BershkaStockApiClient.CheckStoreStockAsync`'in her aile-prefix'i için attığı stok API isteği (`source: "StockApi"`, `context: "{productUrl} | store={id} partNumber={digits}"`).
- **`Context` alanı**: kullanıcı ("hangi urlde hata alındığını anlamlandıralım") talebiyle eklendi — her log kaydı artık hangi ürün/mağaza/partnumber üzerinde olduğunu insan tarafından okunabilir şekilde taşıyor; `redis-cli LRANGE` ile ham JSON'a bakıldığında bile doğrudan anlamlı.
- **Görüntüleme**: `GET /health/scraper-stats?lastN=100` — `{ scraperName, sampleSize, successRatePercent, httpStatusCodeDistribution, alertTriggered }` döner. `GET /health/scraper-failures?lastN=20` — `GetRecentFailuresAsync` ile son N BAŞARISIZ denemeyi (Context + ErrorMessage dahil) döner, Redis'e elle bakmadan "hangi üründe hata alındı" sorusuna cevap verir.
- Gerçek Chrome + gerçek Redis + gerçek `api.inditex.com` ile doğrulandı (4 gerçek ürün + 1 kasıtlı bozuk URL): PDP çekimi başarısızlıklarının cache'lenmemesi tasarım kararı, health log'da aynı bozuk URL için tekrarlanan gerçek 404 kayıtları olarak (online + mağaza kontrolünün ikisi de ayrı ayrı denediği için) doğru şekilde görünüyor — Redis'teki ham kayıt ile servisin hesapladığı istatistikler birebir tutarlı bulundu.
- 12 unit test (`tests/StockTracker.Shared.Scraping.Tests`): başarı oranı/durum kodu dağılımı hesaplama, küçük örneklemde alarm tetiklenmemesi, Redis hatası durumunda exception fırlatılmaması, farklı scraper isimlerinin bağımsız key kullanması, `GetRecentFailuresAsync`'in yalnızca başarısızları context'iyle birlikte döndürmesi dahil.

### Subscription Service — Watch Group Modeli (Faz 3.1 — Tamamlandı)

- `POST /watches` — `{userId, productCode, size, storeId?}` alır. Önce `WatchGroups`'ta aynı `ProductCode`+`Size`+`StoreId` var mı bakılır; yoksa oluşturulur. Ardından o kullanıcının bu `WatchGroup`'a ait `UserWatch`'ı var mı bakılır (yoksa oluşturulur) — bu iki adımlı akış dedup'ın kendisi: aynı ürünü farklı kullanıcılar takip ettiğinde tek `WatchGroup`, kullanıcı başına ayrı `UserWatch` satırı oluşur.
- `GET /watches?userId=` — kullanıcının takip listesini `WatchGroup`'un güncel `LastKnownStatus`/`LastCheckedAt` bilgisiyle birlikte döner (Faz 3.2'deki poller bu alanları güncelleyecek).
- `DELETE /watches/{id}?userId=` — `id`, `UserWatch.Id`'dir (WatchGroup değil). `userId` sahiplik doğrulaması için zorunlu query param; eşleşmezse `404` döner (kayıt bulunamadı ile başkasına ait olma durumu ayrıştırılmaz, bilgi sızıntısı önlenir). Silme yalnızca o kullanıcının takibini kaldırır — `WatchGroup` başka kullanıcılar tarafından hâlâ takip ediliyor olabileceğinden silinmez, orphan `WatchGroup` bilinçli olarak temizlenmiyor (Faz 3.2 poller'ı boşuna kontrol eder ama veri kaybı riski yaratmaz; gerekirse ileride bir cleanup job'ı eklenebilir).
- **Dedup DB seviyesinde değil, application seviyesinde**: `WatchGroups(ProductCode, Size, StoreId)` üzerinde unique olmayan bir index var — `StoreId` nullable olduğundan Postgres'te unique constraint NULL'ları ayrı kayıt sayardı (iki online-only watch aynı ürün/beden için farklı satır açardı), bu yüzden dedup `WatchService.CreateWatchAsync` içinde "find or create" ile yapılıyor. `UserWatches(UserId, WatchGroupId)` ise gerçekten unique — aynı kullanıcının aynı gruba iki kez eklenmesi DB seviyesinde engelleniyor.
- **Gerçek Postgres'e karşı bulunan hata**: `GetWatchesAsync` ilk implementasyonda `OrderByDescending`'i DTO projeksiyonundan sonra uyguluyordu. InMemory test provider'ı (unit testlerde kullanılan) bunu client-eval ile sessizce çalıştırdı, ama gerçek Npgsql sağlayıcısı projeksiyon (DTO constructor çağrısı) içindeki alanla sıralamayı SQL'e çeviremeyip `InvalidOperationException` fırlattı — `dotnet test` yeşildi ama gerçek servis `GET /watches` çağrısında 500 dönüyordu. Docker Postgres'e karşı uçtan uca smoke-test sırasında yakalanıp düzeltildi (`OrderByDescending` artık `Join`'den önce, ham `UserWatches` üzerinde çalışıyor) — bu, "unit testler InMemory provider ile SQL çevirisi hatalarını yakalayamayabilir" örneği olarak not edildi.
- Gerçek Docker Postgres'e (`subscription_db`) karşı uçtan uca doğrulandı: iki farklı kullanıcının aynı ürün/beden/mağazasız kombinasyonu takip etmesi tek `WatchGroup`'a dedup edildi, sahiplik dışı silme denemesi `404`, sahibinin silmesi `204` ve takip listesini boşalttı — sonrasında test verisi temizlendi.
- Unit testler `tests/StockTracker.Subscription.Tests` — 9 test (yeni `WatchGroup` oluşturma, iki farklı kullanıcının aynı gruba dedup edilmesi, aynı kullanıcının tekrar eklemesinin duplicate yaratmaması, farklı `StoreId`'lerin ayrı grup açması, kullanıcı bazlı listeleme, `LastKnownStatus`/`LastCheckedAt` alanlarının doğru taşınması, sahiplik kontrollü silme senaryoları dahil).
- **İç iletişim (Faz 3.3)**: `GET /internal/watchers?productCode=&size=&storeId=` — gateway bypass, direkt HTTP. Notification Service, bir restock event'inde kime bildirim gideceğini (o `WatchGroup`'u takip eden tüm `UserId`'ler) buradan çözer.

### Stock Poller (Faz 3.2 — Tamamlandı)

- **Scheduler**: Quartz.NET (`Quartz.Extensions.Hosting`) — Hangfire yerine tercih edildi çünkü ek bir dashboard/depolama veritabanı gerektirmiyor, `AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true)` ile process-in-process çalışıyor. `StockPollerJob` (`IJob`), `appsettings.json` > `Poller:IntervalSeconds`'ta (varsayılan 60 sn) tanımlı sabit bir `SimpleSchedule` tetikleyicisiyle tekrar tekrar çalışır; job içindeki hata loglanıp yutulur (bir döngüdeki geçici hata sonraki tetiklemeleri etkilemesin diye).
- **Aday seçimi — roadmap'in ötesine bilinçli genişletme**: Faz 3.1 roadmap'i "`LastKnownStatus = OutOfStock` olan WatchGroup'ları çeken job" diyordu, ama gerçek implementasyon `LastKnownStatus IS NULL OR LastKnownStatus <> InStock` filtresini kullanıyor (yani OutOfStock + Unknown + hiç kontrol edilmemiş). Gerekçe: `POST /watches` ile yeni oluşturulan bir `WatchGroup`'un `LastKnownStatus`'u **null**'dur (henüz hiç kontrol edilmedi) — yalnızca literal "OutOfStock" filtrelenseydi bu grup asla ilk kontrolünü almaz, özellik uçtan uca hiç çalışmazdı. `InStock` olan gruplar bilinçli olarak atlanıyor — kullanıcı zaten haberdar edilmiş sayılır (Faz 3.3), tekrar kontrol o ana kadar gereksiz.
- **Önceliklendirme**: `StockPollerService.ResolveCheckInterval(watcherCount)`, o `WatchGroup`'a bağlı `UserWatches` sayısına göre 3 kademeli bir kontrol aralığı döner (varsayılan: ≥5 kullanıcı → 5 dk, ≥2 kullanıcı → 15 dk, aksi halde → 60 dk; hepsi `Poller` config bölümünden okunur). `StockPollerService.IsDue(lastCheckedAt, interval, now)` saf bir fonksiyon — `lastCheckedAt` null'sa (hiç kontrol edilmemiş) veya aradan geçen süre kademenin aralığını aştıysa `true` döner.
- **`CheckStockCommand` (V2) publish akışı**: kontrol zamanı gelen her `WatchGroup` için önce `IProductServiceClient.LookupAsync(productCode)` ile marka/`BrandId`/`ScraperQueueName`/`ProductUrl` çözülür (marka çözülemezse o döngüde atlanır, `LastCheckedAt` **güncellenmez** — bir sonraki döngüde tekrar denenir). `WatchGroup.StoreId` doluysa, `IStoreReferenceServiceClient.GetStoreByIdAsync(storeId)` ile `BrandSpecificStoreId` çözülür — bunun için Store Reference Service'e yeni bir `GET /stores/{id}` endpoint'i eklendi (mevcut `GET /stores` yalnızca liste/filtre destekliyordu, tekil ID sorgusu yoktu). `City`/`District` poller akışında `null` gönderilir — `WatchGroup` bu alanları hiç saklamıyor ve `BershkaStockCheckService`'in yönlendirme mantığı zaten bunlara bakmıyor (yalnızca `ProductUrl`/`StoreId`/`BrandSpecificStoreId` kullanılıyor).
- **Yayın sonrası `LastCheckedAt` güncellemesi — optimistic**: komut publish edilir edilmez `WatchGroup.LastCheckedAt = now` olarak güncellenir (henüz scraper'dan gerçek sonuç gelmeden). Bu, poll interval'i scraper'ın gerçek yanıt süresinden kısaysa aynı grubun art arda döngülerde tekrar tekrar kuyruğa gönderilmesini önlemek için bilinçli bir tasarım kararı — gerçek sonuç geldiğinde (`StockResultEvent`) `LastCheckedAt` zaten event'in kendi `CheckedAt` zaman damgasıyla tekrar (daha doğru şekilde) güncellenir.
- **Kapanan döngü — `StockResultEventConsumer`**: `StockResultEvent` (fanout, `Messages.V1`) yeni bir consumer (`StockResultEventConsumer` → `IWatchGroupStatusUpdater.UpdateFromStockResultAsync`) tarafından tüketilir; gelen event'in `ProductCode`+`Size`+`StoreId` üçlüsüyle eşleşen `WatchGroup` bulunup `LastKnownStatus`/`LastCheckedAt` güncellenir (eşleşme yoksa — ör. bu ürünü henüz kimse takip etmiyor, sadece anlık bir Search Orchestrator sorgusundan gelen sonuç — sessizce yok sayılır). Bu consumer olmadan poller'ın `OutOfStock`/`null` filtresi hiçbir zaman gerçek veriyle beslenmez, döngü hiç kapanmazdı. Receive endpoint adı: `subscription-stock-result-events` (fanout exchange'e bağlı, kendi bağımsız kuyruğu — Notification Service Faz 3.3'te aynı exchange'e kendi kuyruğuyla bağlanacak, ikisi de aynı mesajın bağımsız birer kopyasını alır).
- **Gerçek altyapıya karşı uçtan uca doğrulama**: gerçek Docker Postgres/RabbitMQ + çalışan Product/BrandDetection/StoreReference servislerine karşı yapıldı. Bershka regex'iyle (`^\d{11}$`) otomatik çözülen bir test ürün kodu için `POST /watches` çağrıldı (`LastKnownStatus=null`); 10 saniyelik poller interval'iyle çalışan Subscription servisi bir sonraki döngüde bu grubu aday olarak yakalayıp Product Service'i sorguladı ve `stock.check.bershka` kuyruğuna doğru `CheckStockCommand` V2 payload'ını gönderdi — mesajın gerçek içeriği RabbitMQ Management API üzerinden doğrulandı (doğru `productCode`/`brandName`/`size`, `productUrl: null` çünkü regex-only çözüm). Ardından RabbitMQ Management API ile elle yayınlanan bir `StockResultEvent` (`status: InStock`), `subscription-stock-result-events` kuyruğundaki consumer tarafından tüketilip `WatchGroup.LastKnownStatus`'u `InStock`'a çevirdi; bir sonraki poll döngüsünde bu grubun artık aday listesine girmediği (log: "aday: 0") doğrulandı — döngünün uçtan uca kapandığı kanıtlandı. Sonrasında test verisi (`subscription_db`, `product_db.ProductBrandMaps`, ilgili Redis cache key'i, RabbitMQ'daki test mesajı) temizlendi.
- Unit testler `tests/StockTracker.Subscription.Tests` — `StockPollerServiceTests` (önceliklendirme kademeleri, `IsDue` sınır durumları, InStock/hiç-kontrol-edilmemiş/OutOfStock filtre davranışı, marka çözülemediğinde publish edilmemesi ve `LastCheckedAt`'in değişmemesi, `StoreId`→`BrandSpecificStoreId` çözümü) ve `WatchGroupStatusUpdaterTests` (eşleşen/eşleşmeyen `WatchGroup`, aynı ürün+beden için farklı `StoreId`'li iki grubun birbirinden bağımsız güncellenmesi) — toplam 16 yeni test.

### Notification Service (Faz 3.3 — Tamamlandı, gerçek Firebase/SendGrid hesabı hariç)

- **`StockResultEvent` consumer**: `StockResultEventConsumer` (fanout, `Messages.V1`) kendi bağımsız `notification-stock-result-events` kuyruğuyla dinler — Subscription Service'in Faz 3.2'de eklediği `subscription-stock-result-events` kuyruğundan tamamen ayrı; aynı fanout exchange'in iki bağımsız kopyası, her ikisi de her event'i kendi başına işler.
- **Neden Subscription Service'e "önceki durum neydi" diye sorulmuyor**: ilk tasarım fikri, Notification'ın restock'u tespit etmek için Subscription'daki `WatchGroup.LastKnownStatus`'u okuması olabilirdi, ama bu bir yarış durumu yaratır — aynı `StockResultEvent`'i AYNI ANDA tüketen iki bağımsız consumer var (Subscription'ın kendi `WatchGroupStatusUpdater`'ı ve Notification'ın kendisi); Notification, Subscription'a sorduğu anda Subscription zaten kendi durumunu YENİ değere güncellemiş olabilir, böylece "önceki durum" sorusuna hep yanlış (zaten güncel) cevap alınır. Çözüm: Notification Service, `WatchGroupNotificationState` adında **kendi bağımsız durumunu** tutuyor (Subscription'ın `WatchGroup`'undan tamamen ayrı bir tablo, aynı doğal anahtarla — `ProductCode`+`Size`+`StoreId` — ama farklı bir veritabanında, database-per-service prensibiyle tutarlı). Her event işlendiğinde önce bu tablodaki mevcut durum okunur (= "önceki durum"), sonra event'in durumuna güncellenir — hepsi tek bir `SaveChangesAsync` içinde, aynı consume çağrısında.
- **Geçiş tespiti**: yalnızca `previousStatus == OutOfStock && yeniStatus == InStock` ise bildirim tetiklenir (`NotificationProcessingService.ProcessAsync`). İlk kontrol (previousStatus `null`, yani hiç bilinmiyordu) `InStock` gelse bile bildirim **göndermez** — kullanıcı hiçbir zaman "yok" olduğunu öğrenmediği için bu bir "tekrar stokta" haberi sayılmaz, roadmap'in "sadece yok→var" ifadesiyle birebir örtüşüyor.
- **Kime bildirim gidecek**: Subscription Service'e yeni eklenen `GET /internal/watchers?productCode=&size=&storeId=` (gateway bypass, direkt HTTP) ile o `WatchGroup`'u takip eden tüm `UserId`'ler çözülür.
- **Email (SendGrid) — gerçekten çalışır durumda**: `SendGridEmailSender`, SendGrid v3 Mail Send API'sine (`api.sendgrid.com/v3/mail/send`, `Authorization: Bearer <SENDGRID_API_KEY>`) gerçek bir HTTP isteği atar. Kullanıcının email adresi, Identity Service'e yeni eklenen `GET /internal/users/{id}` (gateway bypass) ile gerçek zamanlı çözülür. `SENDGRID_API_KEY` henüz gerçek bir hesaptan alınmadığı için (kullanıcı kararı — bkz. altta) `.env`'de placeholder (`REPLACE_WITH_ENV`) — bu durumda `SendGridEmailSender` HTTP isteği hiç atmadan `false` döner ve loglar, servis çökmez. Gerçek bir anahtar girildiğinde kod değişikliği gerekmeden çalışır.
- **Push (FCM) — altyapı hazır, bu fazda kullanılmıyor**: `FcmPushSender`, FCM'in legacy HTTP API'sine (`fcm.googleapis.com/fcm/send`, `Authorization: key=<FCM_SERVER_KEY>`, roadmap'in "Server Key al" ifadesiyle birebir eşleşen, OAuth2/service-account gerektirmeyen basit yöntem) gerçek bir HTTP client olarak yazıldı. Ama pratikte **hiç çağrılmıyor**: sistemde hiçbir yerde kullanıcı başına bir push/cihaz token'ı saklanmıyor (mobil uygulama henüz yok — token kaydı Faz 5.4'ün kapsamı: "FCM push notification token kaydı"). `IUserDeviceTokenProvider`'ın şu anki tek implementasyonu `NoOpDeviceTokenProvider`, her zaman `null` döner; `NotificationProcessingService` bunu görünce push kanalını sessizce (ama loglayarak) atlar, `NotificationLog`'a hiç kayıt açmaz (gerçekten denenmedi). Faz 5.4'te gerçek bir token kaynağına bağlanan bir implementasyonla değiştirilecek.
- **Idempotency**: `NotificationLog` üzerinde `(CommandId, UserId, Channel)` unique index + gönderim öncesi `AnyAsync` kontrolü — MassTransit'in at-least-once teslimat garantisiyle aynı event iki kez tüketilse bile (ör. broker yeniden başlatma sonrası redelivery) aynı kullanıcıya aynı kanaldan ikinci bir bildirim gitmez. Ayrıca durum geçişi mantığının kendisi de doğal bir idempotency katmanı sağlıyor: `WatchGroupNotificationState` bir kez `InStock`'a güncellendikten sonra aynı event'in tekrar işlenmesi zaten "restock" olarak görülmez (`previousStatus` artık `InStock`).
- **Kullanıcı kararı — gerçek Firebase/SendGrid hesabı bu fazda kurulmadı**: bu fazın roadmap'i gerçek bir Firebase projesi/FCM Server Key ve SendGrid/Postmark hesabı gerektiriyordu; kullanıcıyla görüşülüp koddan bağımsız bu adımın ertelenmesine karar verildi — HTTP entegrasyonları gerçek API'lere göre tam yazıldı (`SendGridEmailSender`, `FcmPushSender`), env var'lar (`SENDGRID_API_KEY`, `FCM_SERVER_KEY`) placeholder (`REPLACE_WITH_ENV`) olarak `.env`'de duruyor, gerçek anahtarlar girildiğinde kod değişikliği gerekmez.
- **Gerçek altyapıya karşı uçtan uca doğrulama**: gerçek Docker Postgres/RabbitMQ + çalışan Identity/Subscription/Notification servislerine karşı yapıldı. Identity'de gerçek bir kullanıcı kaydedildi, Subscription'da bu kullanıcı adına bir `WatchGroup` (`POST /watches`) oluşturuldu. RabbitMQ Management API ile elle önce `OutOfStock` sonra `InStock` `StockResultEvent`'i yayınlandı — Notification Service `GET /internal/watchers` ile doğru kullanıcıyı, `GET /internal/users/{id}` ile gerçek email adresini çözdü, `SendGridEmailSender` (anahtar yapılandırılmadığı için) `false` döndü ve bu `NotificationLog`'a (`Channel=Email, Success=false`, doğru `CommandId`) doğru şekilde yazıldı; push kanalının token yokluğu nedeniyle hiç `NotificationLog` satırı açmadan atlandığı loglardan doğrulandı. Aynı restock event'i tekrar yayınlandığında (durum zaten `InStock` olduğu için) ikinci bir `NotificationLog` satırı açılmadığı (toplam kayıt sayısı 1'de sabit kaldı) doğrulandı — sonrasında test verisi (`notification_db`, `subscription_db`, Identity'deki test kullanıcısı) temizlendi.
- Unit testler `tests/StockTracker.Notification.Tests` — 7 test: ilk kontrolde (`previousStatus=null`) bildirim gitmemesi, `OutOfStock→OutOfStock` ve `InStock→OutOfStock` geçişlerinde bildirim gitmemesi, watcher yoksa hiçbir gönderici çağrılmaması, restock'ta her watcher'a email gönderilip `NotificationLog`'a doğru işlenmesi, push token yoksa `IPushSender`'ın hiç çağrılmadan atlanması, idempotency (aynı `CommandId` ile tekrar işlemede duplicate email gitmemesi) dahil.

## Ölçeklenme Riski — Çoklu Marka Scraping ve Bot Tespiti

Faz 6.1'de Zara ve Pull&Bear scraper'ları eklenince, hem marka sayısı hem de (Faz 3.2 Stock Poller devreye girince) tekrarlanan periyodik istek hacmi artacak. Bershka Scraper'da doğrulandığı gibi (bkz. yukarı) hedef siteler en azından User-Agent bazlı temel bot tespiti yapıyor; istek hacmi arttıkça bu tespit muhtemelen sıkılaşacak (rate-limit, 429/403, IP bazlı engelleme). Bu, tek bir servisin problemi değil — her yeni marka scraper'ının tekrar tekrar çözmemesi gereken, paylaşılan bir altyapı sorunu. Faz 2.6 olarak plana eklendi (bkz. `.claude/ROADMAP.md`):

### Faz 2.6 — Tamamlanan önlemler (`StockTracker.Shared.Scraping/Http`)

Hepsi paylaşılan projeye taşındı/eklendi — Zara/Pull&Bear geldiğinde kopyala-yapıştır yerine doğrudan kullanılabilir:

- **`HostRateLimitingHandler`**: her hedef host için token-bucket ile dakika başına istek bütçesi (varsayılan 60/dk) — `ScraperEtiquetteHandler`'daki istekler-arası rastgele gecikme yalnızca art arda İKİ isteği yavaşlatıyordu, toplam bütçe tanımlamıyordu. Süreç içi `static` bir dictionary'de host→bucket eşlemesi tutuluyor (birden fazla eşzamanlı stok kontrolü de aynı bütçeyi paylaşır).
- **`ScraperEtiquetteHandler`** (taşındı, güçlendirildi): artık User-Agent'ı TEK BAŞINA değil, `BrowserProfile` (UA + tutarlı `Accept-Language` + varsa `sec-ch-ua`/`sec-ch-ua-platform`/`sec-ch-ua-mobile`) olarak birlikte rotasyonluyor. Yalnızca Chromium tabanlı profillerde `sec-ch-ua*` dolu — Firefox/Safari gerçek tarayıcılarda bu header'ları hiç göndermez, bu yüzden o profillerde bilinçli olarak boş bırakıldı (aksi halde motor/tarayıcı tutarsızlığı kendi başına bir parmak izi sinyali olurdu).
- **`ScraperResiliencePolicies.AddScraperResilience()`**: üç katmanlı Polly zinciri —
  1. `RetryWithRetryAfterAwareness`: 5xx/408/network hatalarına ek olarak **429'u da** kapsar (eski `AddTransientHttpErrorPolicy` 429'u hiç kapsamıyordu — `HandleTransientHttpError()` predicate'i yalnızca 5xx/408). Sunucu `Retry-After` header'ı verdiyse ona uyulur, vermediyse exponential backoff'a düşülür (`ComputeRetryDelay` — ayrı, saf bir fonksiyon olarak test edilebilir).
  2. `TransientErrorCircuitBreaker`: 5xx için "normal" devre kesici (5 hata → 30 sn, eskisiyle aynı).
  3. `BotDetectionCircuitBreaker`: **403'e özel, ayrı ve daha agresif** bir devre kesici (2 hata → 2 dk) — 403'ün "sunucu arızalı" değil "bizi bot olarak işaretledi" anlamına gelme ihtimali yüksek olduğu için, aynı hızda tekrar denemek durumu kötüleştirebilir.
- Gerçek `api.inditex.com`'a karşı doğrulandı: yeni handler zinciri (rate limiting + header profili + 3 katmanlı Polly) gerçek bir online + mağaza stok sorgusunu sorunsuz tamamladı, health-log'a (Faz 2.5) doğru şekilde işlendi.
- 15 yeni unit test (`tests/StockTracker.Shared.Scraping.Tests`): host bazlı bucket izolasyonu, bucket tükendiğinde gerçekten bekleme (cancellation ile kanıtlandı, gerçek 60 sn beklenmeden), `ComputeRetryDelay`'in Retry-After'ı doğru önceliklendirmesi, 429'un retry-yapılabilir olması (eski davranışa göre regresyon testi), 403 devre kesicisinin eşik sonrası gerçekten `BrokenCircuitException` fırlatması, Chromium/Firefox/Safari profillerinin `sec-ch-ua` tutarlılığı dahil.

### Faz 2.6 — kapsam dışı bırakılan madde

- **Proxy/IP rotasyonu**: kullanıcıyla değerlendirildi — gerçek bir IP rotasyonu ya ücretli bir proxy sağlayıcısı (Bright Data, Oxylabs vb.) ya da kendi çok-bölgeli sunucu altyapımızı gerektiriyor; ücretsiz/public proxy listeleri güvenlik riski taşıyor ve genelde zaten bloklanmış, Tor ise Akamai gibi sistemlerce datacenter IP'lerinden bile agresif tespit ediliyor. Kullanıcı kararıyla **Faz 7**'ye (en son faz, sağlayıcı kararı verilene kadar pasif) ertelendi — bkz. `.claude/ROADMAP.md`.
- **Headless browser tespiti (Playwright'a özgü, kalıcı risk)**: Bershka Scraper PDP'leri Playwright ile çekiyor (bkz. yukarı) — bu, Akamai'nin korumasını geçmenin tek yolu ama kalıcı bir risk taşıyor. **Somut olarak doğrulandı**: Playwright'ın bundled Chromium'u (varsayılan), UA/`navigator.webdriver` maskeleme gibi standart önlemlere rağmen Akamai'den anında "Access Denied" aldı — yalnızca gerçek Chrome kanalını (`Channel = "chrome"`) sürmek bu engeli aştı. Yani mevcut çözüm bile fiziksel olarak gerçek Chrome kurulu olan makinelere bağımlı (headless Chromium yeterli değil) — bu, hacim arttıkça (özellikle Faz 3.2 poller devreye girince) başarı oranının düşebileceğine dair somut bir erken sinyal. Redis cache-aside (productUrl başına 15 dk TTL) bunu *geciktirir* (Playwright'ı seyrekleştirir) ama ortadan kaldırmaz — bu risk yukarıdaki host bazlı rate limiting/circuit breaker önlemleriyle *azaltılıyor*, proxy/IP rotasyonu olmadan tamamen ortadan kalkmıyor.

Not: bu önlemler istek nezaketi/güvenilirlik amaçlıdır (rate limiting, backoff, header gerçekçiliği) — CAPTCHA çözme veya bot-tespitini aktif olarak atlatma gibi teknikler kapsam dışıdır; `.claude/SECURITY.md`'de kabul edilen ToS riski bilinçli ve sınırlı tutulmalıdır.

## Billing — App Store / Play Store In-App Purchase (Faz 4 — planlanıyor)

**Karar**: ödeme, ayrı bir sanal pos/ödeme sağlayıcısı (iyzico, Paddle vb.) üzerinden değil, mobil uygulamanın (Faz 5.4) App Store ve Play Store'daki yerleşik abonelik satın alma (in-app purchase) akışı üzerinden alınacak. Billing Service kart bilgisiyle hiçbir zaman karşılaşmaz — yalnızca (1) mobil client'ın tamamladığı satın almayı Apple/Google'ın **server-to-server** API'lerine karşı doğrular, (2) abonelik yaşam döngüsü event'lerini (yenileme, iptal, ödeme başarısız, refund) her iki store'un kendi webhook mekanizmasından dinler.

**Gerekçe**:
- PCI-DSS yükü tamamen Apple/Google'a devroluyor — ayrı bir ödeme sağlayıcı entegrasyonu, sözleşmesi veya webhook imza doğrulaması altyapısı (iyzico/Paddle'a özgü) kurmaya gerek kalmıyor.
- App Store/Play Store politikaları, dijital/abonelik ürünler için zaten kendi IAP sistemlerinin kullanılmasını **zorunlu tutuyor** — üçüncü taraf bir ödeme sağlayıcı kullanılsaydı uygulamanın store'dan reddedilme riski olurdu. Bu karar hem teknik hem operasyonel riski aynı anda azaltıyor.
- Fiyatlandırma store konsollarında (App Store Connect / Play Console) tanımlanır — `Plans` tablosunda `Price` alanı **tutulmaz**, yalnızca her store'daki ürün ID'si (`AppStoreProductId`/`PlayStoreProductId`) referans olarak saklanır.

**Akış (planlanan)**:
1. Mobil client, App Store/Play Store'un native satın alma akışını başlatır (kullanıcı Apple ID/Google hesabıyla öder — Billing Service bu adımda hiç devrede değil).
2. Satın alma tamamlanınca client, aldığı receipt/purchase token'ı `POST /billing/verify-purchase`'a gönderir.
3. Billing Service, ilgili store'un server API'sine karşı doğrular (Apple: App Store Server API; Google: Play Developer API) ve `Subscriptions` kaydını oluşturur/günceller.
4. Abonelik sonrası tüm yaşam döngüsü event'leri (otomatik yenileme, iptal, ödeme başarısız, refund) client'tan bağımsız olarak **webhook** ile gelir: Apple → App Store Server Notifications V2 (JWS-signed payload, Apple public key'iyle doğrulanır), Google → Real-time Developer Notifications (Cloud Pub/Sub push subscription, OIDC token doğrulaması). Her ikisi de `PaymentEvents` tablosuna normalize edilerek yazılır, `(Provider, EventId)` unique constraint'iyle idempotent işlenir.

**Sıralama bağımlılığı**: gerçek bir satın almanın uçtan uca (gerçek cihazda, gerçek Apple ID/Google hesabıyla) doğrulanması mobil client gerektiriyor — Faz 5.4 bu fazdan **sonra** geliyor. Faz 4'te doğrulama/webhook mantığı Apple/Google'ın sağladığı sandbox/örnek payload'larla test edilecek; "gerçek satın alma" testi Faz 5.4 tamamlandıktan sonra mümkün olacak. Bu, projenin genelindeki "canlı altyapıya karşı doğrula" prensibinden bilinçli, dokümante edilmiş bir sapma.

## Bilinen Mimari Kararlar ve Riskler

| Karar | Açıklama |
|---|---|
| Servisler arası iletişim gateway'den geçmez | Performans ve dayanıklılık için — iç HTTP direkt yapılır |
| Database-per-service | Cross-service foreign key yok; ID referansları ve event'lerle tutarlılık sağlanır |
| Scraping ToS riski | Hedef sitelerin ToS'una aykırılık riski bilinçli olarak kabul edilmiştir |
| PostgreSQL `CREATE DATABASE IF NOT EXISTS` yok | Init script'te her DB için ayrı `CREATE DATABASE` kullanıldı |
| YARP transform çakışması | `PathPattern` + `PathRemovePrefix` ayrı transform bloklarına bölündü |
| Mac → Docker init script permission sorunu | `git update-index --chmod=+x` ile çözülür |
| MassTransit sürüm kararı | v9+ ticari lisans gerektirdiği için son açık kaynak sürüm olan 8.5.5'e sabitlendi |
| FluentAssertions sürüm kararı | v8+ ticari lisans (Xceed) gerektirdiği için son Apache 2.0 sürüm olan 7.2.0'a sabitlendi (`tests/*.csproj`) |
| Ödeme: App Store/Play Store IAP, ayrı sanal pos yok | Kullanıcı kararı — PCI kapsamı store'lara devroluyor, store politikaları zaten kendi IAP'lerini zorunlu tutuyor (bkz. yukarı, Billing bölümü) |