# Veritabanı Dokümanı

## Prensip: Database-per-Service

Her servisin kendi PostgreSQL veritabanı vardır, başka bir servisin veritabanına doğrudan erişim yasaktır. Servisler arası veri paylaşımı API çağrısı veya RabbitMQ event'i üzerinden yapılır.

## Veritabanları

| DB Adı | Servis | Durum |
|---|---|---|
| `identity_db` | Identity Service | ✅ Migration uygulandı |
| `product_db` | Product Service | ✅ Migration uygulandı |
| `brand_db` | Brand Detection Service | ✅ Migration uygulandı |
| `store_db` | Store Reference Service | ✅ Migration uygulandı |
| `subscription_db` | Subscription Service | ✅ Migration uygulandı |
| `billing_db` | Billing Service | ✅ Migration uygulandı (Faz 4.1 + 4.2 + 4.3) |
| `notification_db` | Notification Service | ✅ Migration uygulandı |

## Init Script Kuralları

- PostgreSQL `CREATE DATABASE IF NOT EXISTS` **desteklemez** — geliştirme sürecinde karşılaşılan hata. Çözüm: her database için ayrı `CREATE DATABASE` çağrısı kullanıldı.
- Init script `docker/postgres-init/init-multiple-dbs.sh` olarak `.sh` formatında çalışıyor. `POSTGRES_MULTIPLE_DATABASES` ortam değişkeninden virgülle ayrılmış database adlarını okur, döngüyle her birini oluşturur.
- Mac → Docker geçişinde `.sh` init script'lerinin execute (`+x`) izni kaybolabilir. Sorun yaşanırsa: `git update-index --chmod=+x docker/postgres-init/init-multiple-dbs.sh`

## Migration Stratejisi

- EF Core Code-First migration'lar kullanılıyor
- Her servisin kendi migration geçmişi ve kendi DbContext'i var
- Migration'lar uygulama başlangıcında `db.Database.MigrateAsync()` ile otomatik uygulanır
- CI pipeline'da `docker compose config` ile YAML doğrulaması yapılır

## Mevcut Şemalar

### identity_db

| Tablo | Alanlar |
|---|---|
| `Users` | Id, Email, PasswordHash (BCrypt), FirstName, LastName, IsEmailVerified, CreatedAt |
| `RefreshTokens` | Id, UserId, Token, ExpiresAt, IsRevoked, CreatedAt |

`GET /internal/users/{id}` (Faz 3.3) — gateway bypass, direkt HTTP. Notification Service'in email çözmesi için.

`UserRegisteredEvent` (Faz 4.1) — `POST /auth/register` başarılı olduğunda fanout olarak publish edilir (kayıt işlemini bloklamayan fire-and-forget). Billing Service tüketip otomatik Free plan atar.

### product_db

| Tablo | Alanlar |
|---|---|
| `Brands` | Id, Name, ScraperQueueName, SearchEndpoint, IsActive, CreatedAt |
| `ProductBrandMaps` | Id, ProductCode, BrandId, ResolvedVia (enum), Confidence (enum), ProductUrl, ResolvedAt |

**Seed data:** Bershka (`ScraperQueueName: "bershka"`)

**Enum: ResolvedVia** — FormatMatch=1, SiteSearch=2, SearchEngine=3, Manual=4

**Enum: ConfidenceLevel** — Low=1, Medium=2, High=3

**Redis cache:** `product:lookup:{productCode}` → 24 saatlik TTL, cache-aside pattern. Hit/miss sayaçları `cache:metrics:hits` / `cache:metrics:misses` key'lerinde tutulur (`CacheMetricsService`, `GET /cache/metrics` ile okunur). Mapping değiştiğinde (`SaveMappingAsync`) ilgili cache key otomatik silinir; manuel temizlik için `DELETE /cache/{code}` var.

**Not:** `Brands.ScraperQueueName` alanı, RabbitMQ kuyruk isimlendirme kuralıyla birebir eşleşir — `QueueNaming.StockCheckQueue(ScraperQueueName)` → `stock.check.{ScraperQueueName}` (bkz. `.claude/ARCHITECTURE.md` — Mesajlaşma Katmanı).

### Search Orchestrator — Redis throttle (kendi veritabanı yok)

`search:throttle:{userId}:{productCode}:{size}` → `SETNX`, 30 saniyelik TTL. Aynı kullanıcı aynı ürün/beden için tekrar arama isteği attığında `429` döner (bkz. `.claude/ARCHITECTURE.md` — Search Orchestrator).

### Bershka Scraper — Redis PDP cache-aside (kendi veritabanı yok)

`bershka:pdp-sizes:{productUrl}` → ürün sayfasından (Playwright ile) okunan tüm bedenlerin listesi (`Name`, `Stock`, `PartNumber`, `MastersSizeId`, `ColorId`), 15 dakikalık TTL. Aynı ürüne yapılan art arda online/mağaza stok kontrolleri Playwright'ı tekrar tetiklemez (bkz. `.claude/ARCHITECTURE.md` — Bershka Scraper). Boş sonuç cache'lenmez.

### Zara Scraper — Redis PDP cache-aside (kendi veritabanı yok)

`zara:pdp-sizes:{productUrl}` → ürün sayfasının SSR verisinden (`window.zara.viewPayload`) okunan tüm bedenlerin listesi (`Name`, `Availability`, `ColorId`, `Sku`), 15 dakikalık TTL — yalnızca ONLINE stok için. Mağaza bazlı stok (`store-product-availability`) hiç önbelleklenmiyor, her zaman canlı sorgulanıyor (fiziksel stok daha hızlı değişiyor; Akamai hız-bazlı bloklamasına karşı zaten `PlaywrightZaraFetcher` içinde ayrı bir hız sınırlaması var — bkz. `.claude/ARCHITECTURE.md` → Zara Scraper).

### Mango Scraper — Redis PDP cache-aside (kendi veritabanı yok)

`mango:pdp-sizes:{productUrl}` → ürün sayfasının Next.js RSC akışından okunan tüm bedenlerin listesi (`Name`, `Available`, `ColorId`), 15 dakikalık TTL — yalnızca ONLINE stok için. Mağaza bazlı stok (`store-finder/v2/stores/stock`) hiç önbelleklenmiyor. Zara'dan farklı olarak bu scraper'da Playwright/Akamai bloklaması YOK (bkz. `.claude/ARCHITECTURE.md` → Mango Scraper) — Redis önbelleği yalnızca gereksiz tekrar isteklerden kaçınmak için, bot tespitinden kaçınmak için değil.

### Massimo Dutti Scraper — Redis PDP cache-aside (kendi veritabanı yok)

`massimodutti:pdp-sizes:{productUrl}` → ürün sayfasının `#mdfrontw-state` Angular SSR state'inden okunan tüm bedenlerin listesi (`Name`, `ColorId`, `CatEntryId`, `MastersSizeId`, `IsBuyable`, `BackSoon`), 15 dakikalık TTL — online stok İÇİN, ama aynı zamanda mağaza stok sorgusunun ihtiyaç duyduğu `CatEntryId`/`MastersSizeId` değerlerini de taşıyor. Mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`) hiç önbelleklenmiyor — Akamai korumasız ve ucuz olduğu için her zaman canlı sorgulanıyor (bkz. `.claude/ARCHITECTURE.md` → Massimo Dutti Scraper).

### Beymen Scraper — Redis ürün özeti cache-aside (kendi veritabanı yok)

`beymen:product-summary:{productCode}` → `sf-api/api/product/{id}/productsummary` yanıtının tamamı (her beden için `inStock`, `stockQuantity`, `variantBarcode`), 15 dakikalık TTL — hem online stok hem mağaza sorgusunun ihtiyaç duyduğu barkod eşlemesi için kullanılıyor. Mağaza stok API'si (`api/store/getstorestock/{barcode}`) hiç önbelleklenmiyor — korumasız ve ucuz olduğu için her zaman canlı sorgulanıyor (bkz. `.claude/ARCHITECTURE.md` → Beymen Scraper). Bu scraper'da Playwright/PDP fetch hiç yok, tüm veri bu iki düz API çağrısından geliyor.

### Pull&Bear Scraper — Redis PDP cache-aside (kendi veritabanı yok)

`pullbear:pdp-sizes:{productUrl}` → ürün sayfasının `<product-modular>.__product` state'inden okunan tüm bedenlerin listesi (`Name`, `ColorId`, `CatEntryId`, `MastersSizeId`, `IsBuyable`, `BackSoon`), 15 dakikalık TTL — Massimo Dutti ile birebir aynı önbellekleme deseni (online stok + mağaza sorgusunun ihtiyaç duyduğu `CatEntryId`/`MastersSizeId` değerleri). Mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`) hiç önbelleklenmiyor — Akamai korumasız ve ucuz olduğu için her zaman canlı sorgulanıyor.

### Scraper Health Monitoring — Redis, tüm marka scraper'ları arasında paylaşılan (kendi veritabanı yok)

`scraper:health:{scraperName}:log` → her scraper denemesinin (`source`, `success`, `httpStatusCode`, `errorMessage`, `context` — hangi ürün URL'i/mağaza/partnumber üzerinde olduğu, `durationMs`, `timestamp`) `LPUSH` ile eklendiği, `LTRIM` ile son 500 kayıtla sınırlanan capped-list. Bilinçli olarak ayrı bir Postgres DB (`bershka_scraper_db` gibi) yerine bu kullanıldı — bkz. `.claude/ARCHITECTURE.md` → Bershka Scraper → Scraper Health Monitoring, "Tasarım kararı" notu. `StockTracker.Shared.Scraping` projesindeki `IScraperHealthLogService` üzerinden okunur/yazılır; her yeni scraper aynı servisi kendi `scraperName`'iyle çağırarak sıfır ek altyapıyla kullanabilir.

### brand_db

| Tablo | Alanlar |
|---|---|
| `BrandCodeSignatures` | Id, BrandId, BrandName, RegexPattern, Confidence, IsActive, CreatedAt |

**Seed data:**

| Marka | Pattern | Confidence |
|---|---|---|
| Bershka | `^\d{11}$` | High |
| Zara | `^\d{4}/\d{3}/\d{3}$` | High |
| Pull&Bear | `^\d{8}$` | Low |
| Mango | `^\d{8}/\d{2}$` | Medium |
| H&M | `^\d{7}/\d{3}$` | Medium |
| Massimo Dutti | `^\d{8}/\d{3}$` | Medium |
| Beymen | `^\d{7}$` | Medium |
| Pull&Bear | `^\d{8}/\d{3}$` | Medium |

> Bershka'nın pattern'i, gerçek bershka.com ürün sayfaları üzerinden doğrulandı (Faz 2.4): önde sıfır + 4 haneli model + 3 haneli varyant + 3 haneli renk kodu (ör. REF `2891/054/426` → `02891054426`), `BershkaStockApiClient`'ın stok API'sine gönderdiği `productCode` ile birebir aynı format. Eski `^\d{7,9}$` deseni doğrulanmamış bir tahmindi.
>
> Zara'nın pattern'i, gerçek zara.com ürün sayfaları üzerinden doğrulandı (Faz 6.1): 4 haneli `displayReference` + 3 haneli varyant + 3 haneli `colors[].id` (ör. `5063/821/802`), çoklu gerçek örnekle (9083/479, 5372/323, 0962/307, ...) doğrulandı. Eski `^\d{5}/\d{3}/\d{2,3}$` deseni doğrulanmamış bir tahmindi, ilk grup 5 değil 4 rakamdı.
>
> Mango'nun pattern'i, gerçek shop.mango.com API'sinden (`online-orchestrator.mango.com/v4/products` → `"reference":"37013869"`) ve ürün URL yapısından (`.../37013869/56/00`) doğrulandı: 8 haneli temel referans + 2 haneli `colors[].id`. **Medium** tutuldu (Zara/Bershka'nın High'ının aksine) — temel 8 haneli referans kesin doğrulandı ama fiziksel üründeki TAM görünen format (ayraç dahil) doğrulanamadı. Bilinçli olarak `/` ayraçlı bileşik kod seçildi — yalnızca `^\d{8}$` olsaydı Pull&Bear'ın (henüz doğrulanmamış) deseniyle birebir çakışırdı.
>
> H&M'nin pattern'i, gerçek www2.hm.com ürün sayfalarından (`__NEXT_DATA__` → `productId`/`artId`, ürün URL yapısı `productpage.{productId}{artId}.html`) doğrulandı: 7 haneli temel `productId` + 3 haneli `artId` (renk varyantı). **Medium** tutuldu (Zara/Bershka'nın High'ının aksine) — temel format canlı API/URL verisiyle doğrulandı ama fiziksel üründeki etiket formatı (ayraç dahil) henüz çapraz doğrulanmadı, Mango ile aynı gerekçe. `/` ayraçlı bileşik kod, diğer markaların regex'leriyle çakışmayı önlemek için bilinçli olarak seçildi (7+3 hane deseni Bershka'nın 11 haneli ayraçsız deseniyle veya Mango'nun 8+2 deseniyle çakışmıyor).
>
> Massimo Dutti'nin pattern'i, gerçek massimodutti.com ürün sayfalarının `#mdfrontw-state` SSR verisinden doğrulandı: `reference` alanı (`"06244810-I2026"`) 8 haneli temel referansı, `colors[].reference` alanı (`"C06244810251-I2026"`) 3 haneli renk kodunu (`colors[].id`) veriyor. **Medium** tutuldu (Zara/Bershka'nın High'ının aksine) — temel format SSR verisiyle kesin ama fiziksel üründeki etiket formatı (ayraç dahil) henüz çapraz doğrulanmadı, Mango/H&M ile aynı gerekçe. 8+3 hane deseni H&M'in 7+3 deseniyle ÇAKIŞMIYOR (ilk grup hane sayısı farklı, regex bunu ayırt ediyor).
>
> Beymen'in pattern'i, diğer markalardan FARKLI olarak ayraçsız — renk/varyant `productCode`'un bir parçası değil, tamamen ayrı bir ürün ID'si (`otherColorList` alanı bunu gösteriyor). Gerçek `sf-api/api/product/{id}/productsummary` API'sinden ve ürün URL yapısından (`/tr/p_{slug}_{id}`) doğrulandı, birden fazla gerçek örnekle (1661415, 1884189, 2049912, 1652585, 1937139, ...) 7 hane teyit edildi. **Medium** tutuldu — tek marka örneği üzerinden genellendiği için High'a çıkarılmadı, ama 7 hane diğer markaların (Bershka 11, Pull&Bear 8+3) desenleriyle ÇAKIŞMIYOR.
>
> Pull&Bear'ın pattern'i, gerçek pullandbear.com'un `<product-modular>` custom element'inin `__product.detail` verisinden doğrulandı: `reference` alanı (`"07460338-I2026"`) 8 haneli temel referansı, `colors[].reference` alanı (`"C07460338250-I2026"`) 3 haneli renk kodunu veriyor. **⚠️ BİLİNÇLİ, BELGELENEN ÇAKIŞMA**: bu desen Massimo Dutti'ninkiyle (`^\d{8}/\d{3}$`) BİREBİR AYNI — iki marka aynı alt-yapıyı (aynı platform) paylaştığı için. Saf regex tabanlı eşleşme bu iki markayı ayırt edemez; bir kod her ikisiyle de eşleşecek ve BrandDetection Service'in zaten var olan "birden fazla aday → manuel çözüm" akışı devreye girecek — bu bir hata değil, gerçek bir platform-paylaşımı sonucu (bkz. `.claude/PENDING_INPUTS.md`). Medium tutuldu (Massimo Dutti ile aynı gerekçe — fiziksel etiket formatı ayrıca doğrulanmadı; ilginç bir şekilde `displayReference` alanı Zara tarzı `7460/338` formatında görünüyor, ama bu yalnızca görünen etiket, sorgu için kullanılan gerçek kod hâlâ 8+3).

### store_db

| Tablo | Alanlar |
|---|---|
| `Stores` | Id, BrandId, BrandName, City, District, StoreName, BrandSpecificStoreId, Latitude, Longitude, IsActive, CreatedAt |

`BrandId`, `product_db.Brands.Id` ile eşleşen convention-based referans (FK değil). `BrandSpecificStoreId`, markanın kendi sitesi/API'sinde bu fiziksel mağazayı tanımlayan kod — scraper (Faz 2.4) bunu kullanacak. `Latitude`/`Longitude` (nullable) Faz 6.1'de Mango için eklendi, H&M de aynı alanları kullanıyor — ikisinin de mağaza stok API'si mağaza ID'si değil enlem/boylam ile "yakındaki mağazalar" sorgusu yapıyor (bkz. `.claude/ARCHITECTURE.md` → Mango Scraper / H&M Scraper); Bershka/Zara/Massimo Dutti'de mağaza sorgusu doğrudan `BrandSpecificStoreId` ile çalıştığı için bu alanlar yalnızca mağaza keşfi amacıyla dolduruldu, çalışma zamanında kullanılmıyor.

**Seed data:** Bershka — 4 mağaza:

| Şehir | İlçe | Mağaza | BrandSpecificStoreId |
|---|---|---|---|
| Istanbul | Kadikoy | City's Kozyatağı | `16884` |
| Istanbul | Sisli | Cevahir AVM | `8359` |
| Ankara | Cankaya | Kentpark | `6943` |
| Izmir | Bornova | Forum Bornova | `8426` |

> `BrandSpecificStoreId` değerleri artık Bershka'nın gerçek mağaza bulucu API'sinden (`itxrest/2/bam/store/{chainId}/physical-store`) dönen gerçek `physicalStoreId` değerleri (Faz 2.4) — `BershkaStockApiClient.CheckStoreStockAsync` bunları doğrudan stok API'sinin `physicalStoreId` parametresine geçiriyor. Her mağaza, ilgili il/ilçeye en yakın gerçek Bershka mağazasıdır (Kadıköy için Kozyatağı — Kadıköy ilçesi sınırları içinde bir mahalle; Şişli/Bornova için isim birebir eşleşiyor; Çankaya için adreste "ÇANKAYA" geçen mağaza seçildi, çünkü en yakın 5 sonuç arasında marka adı "Armada" olan bir mağaza yoktu). Detay için bkz. `.claude/ARCHITECTURE.md` → Bershka Scraper.

**Seed data:** Zara — 4 mağaza (Faz 6.1, Bershka'nın mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId |
|---|---|---|---|
| Istanbul | Kadikoy | Bağdat Caddesi | `3231` |
| Istanbul | Sisli | Cevahir AVM | `12692` |
| Ankara | Cankaya | Kentpark | `251` |
| Izmir | Bornova | Forum Bornova | `3643` |

> `BrandSpecificStoreId` değerleri Zara'nın kendi mağaza listesi sayfasından (`z-maazalar-st1404.html` → `window.zara.viewPayload.physicalStoresList`) okunan gerçek `physicalStoreId` değerleri (Faz 6.1) — `store-product-availability` endpoint'inin `physicalStoreIds` parametresine doğrudan geçiriliyor. Kentpark ve Forum Bornova, Bershka'nın seçtiği AVM'lerle birebir aynı (isim eşleşmesiyle doğrulandı). Detay için bkz. `.claude/ARCHITECTURE.md` → Zara Scraper.

**Seed data:** Mango — 4 mağaza + koordinat (Faz 6.1, Bershka/Zara'nın mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId | Latitude | Longitude |
|---|---|---|---|---|---|
| Istanbul | Kadikoy | Bağdat Caddesi (Suadiye) | `10389` | 40.959937009724 | 29.080951331352 |
| Istanbul | Sisli | Cevahir AVM | `10277` | 41.06278401465 | 28.992832831243 |
| Ankara | Cankaya | CEPA AVM | `10403` | 39.90971454185 | 32.778216907751 |
| Izmir | Bornova | Forum Bornova | `10711` | 38.450381582438 | 27.209401193083 |

> `BrandSpecificStoreId` ve koordinatlar, Mango'nun kendi `store-finder/v2/stores/stock` API'sinin döndürdüğü gerçek mağaza kayıtlarından (Faz 6.1) — bu API belirli bir mağaza ID'siyle değil enlem/boylam ile "yakındaki mağazalar" sorgusu yaptığı için, koordinatlar `productId`/`colorId` ile birlikte doğrudan sorguya geçiriliyor (bkz. `.claude/ARCHITECTURE.md` → Mango Scraper). Cevahir AVM ve Forum Bornova, Bershka/Zara'nın seçtiği AVM'lerle birebir aynı; Çankaya için Kentpark'ta Mango mağazası çıkmadığından en yakın gerçek eşleşme (CEPA AVM) kullanıldı.

**Seed data:** H&M — 4 mağaza + koordinat (Faz 6.1, Bershka/Zara/Mango'nun mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId | Latitude | Longitude |
|---|---|---|---|---|---|
| Istanbul | Kadikoy | Bağdat Caddesi | `TR0030` | 40.96030285769096 | 29.08093315025326 |
| Istanbul | Sisli | Özdilek Park AVM | `TR0028` | 41.07764537422764 | 29.01283722778317 |
| Ankara | Cankaya | CEPA AVM | `TR0007` | 39.90859342561093 | 32.77851787102509 |
| Izmir | Bornova | Optimum AVM | `TR0075` | 38.338445 | 27.135329 |

> `BrandSpecificStoreId` (H&M'in kendi `storeCode` formatı, ör. `TR0030`) ve koordinatlar, H&M'in `/tr_tr/sis/tr/{productId}/{artId}` mağaza stok API'sinin döndürdüğü gerçek mağaza kayıtlarından (Faz 6.1) — Mango gibi bu API de belirli bir mağaza ID'siyle değil enlem/boylam ile "yakındaki mağazalar" sorgusu yapıyor (bkz. `.claude/ARCHITECTURE.md` → H&M Scraper). Kadıköy için Bağdat Caddesi'ndeki gerçek H&M mağazası kullanıldı; Şişli için Cevahir'de H&M çıkmadığından en yakın gerçek eşleşme (Özdilek Park AVM, Esentepe/Şişli); Çankaya için CEPA AVM (Mango'nunkiyle birebir aynı mağaza); Bornova için Forum Bornova'da H&M çıkmadığından en yakın gerçek eşleşme (İzmir Optimum AVM, gerçekte de Bornova ilçesinde).

**Seed data:** Massimo Dutti — 4 mağaza + koordinat (Faz 6.1, Bershka/Zara/Mango/H&M'in mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId | Latitude | Longitude |
|---|---|---|---|---|---|
| Istanbul | Kadikoy | Hilltown AVM | `12013` | 40.953106 | 29.121725 |
| Istanbul | Sisli | Cevahir AVM | `4483` | 41.063595 | 28.992115 |
| Ankara | Cankaya | Kentpark AVM | `4009` | 39.909011 | 32.77629 |
| Izmir | Bornova | Karşıyaka Rönesans AVM | `12840` | 38.4784351 | 27.0743432 |

> `BrandSpecificStoreId`, Massimo Dutti'nin kendi mağaza bulucu API'sinin (`itxrest/2/bam/store/{storeId}/physical-store`) döndürdüğü gerçek mağaza ID'lerinden (Faz 6.1) — bu API yalnızca mağaza KEŞFİ için kullanıldı (enlem/boylam ile "yakındaki mağazalar" araması yapıyor), gerçek stok sorgusu ise farklı ve daha basit bir API'ye (`api/storefront/1/stores/{storeId}/products/{catEntryId}/available-sizes`) doğrudan bu `BrandSpecificStoreId` ile gidiyor — enlem/boylam çalışma zamanında GEREKMİYOR (Zara'daki gibi, bkz. `.claude/ARCHITECTURE.md` → Massimo Dutti Scraper). Şişli için Cevahir AVM ve Çankaya için Kentpark AVM, Bershka/Zara/H&M'in seçtiği AVM'lerle birebir aynı mağaza; Kadıköy'de ve Bornova'da gerçek bir Massimo Dutti mağazası çıkmadığından, en yakın gerçek eşleşmeler kullanıldı (sırasıyla Hilltown AVM/Maltepe ve Karşıyaka Rönesans AVM). Mağaza bazlı stok sorgusu gerçek sayısal verilerle (`stock` alanı) canlı doğrulandı.

**Seed data:** Beymen — 4 mağaza + koordinat (Faz 6.1, diğer markaların mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId | Latitude | Longitude |
|---|---|---|---|---|---|
| Istanbul | Kadikoy | Beymen Suadiye | `Beymen Suadiye` | 40.957216 | 29.087570 |
| Istanbul | Sisli | Beymen Nişantaşı | `Beymen Nişantaşı` | 41.049605 | 28.992227 |
| Ankara | Cankaya | Beymen Panora | `Beymen Panora` | 39.84796 | 32.832813 |
| Izmir | Bornova | Beymen Hilltown İzmir | `Beymen Hilltown İzmir` | 38.478249 | 27.073872 |

> `BrandSpecificStoreId`, diğer markalardan FARKLI olarak sayısal bir ID değil, mağazanın kendi `Name` alanı — Beymen'in `GET /api/store/getstorestock/{barcode}` API'sinde ayrı bir sayısal mağaza ID'si yok, mağazalar isimleriyle döndürülüyor (bkz. `.claude/ARCHITECTURE.md` → Beymen Scraper). Latitude/Longitude API'nin kendi `Coordinate` alanından bilgi amaçlı dolduruldu, çalışma zamanında kullanılmıyor (mağaza sorgusu doğrudan barkod + mağaza adıyla çalışıyor). Kadıköy ve Şişli için gerçek Beymen mağazaları birebir ilçe eşleşmesiyle bulundu; Çankaya için Panora seçildi (Kavaklıdere de bir seçenekti); Bornova'da gerçek bir mağaza çıkmadığından İzmir'deki en yakın gerçek eşleşme Beymen Hilltown İzmir (Karşıyaka — Massimo Dutti'nin Bornova için seçtiğiyle aynı mağaza).

**Seed data:** Pull&Bear — 4 mağaza + koordinat (Faz 6.1, diğer markaların mevcut 4 iliyle eşleşecek şekilde):

| Şehir | İlçe | Mağaza | BrandSpecificStoreId | Latitude | Longitude |
|---|---|---|---|---|---|
| Istanbul | Kadikoy | City's Kozyatağı AVM | `16941` | 40.9800391 | 29.0993434 |
| Istanbul | Sisli | Cevahir AVM | `5287` | 41.063595 | 28.992115 |
| Ankara | Cankaya | Kentpark AVM | `6370` | 39.909011 | 32.77629 |
| Izmir | Bornova | Forum Bornova AVM | `5334` | 38.45034027 | 27.2086791 |

> `BrandSpecificStoreId`, Pull&Bear'ın (Massimo Dutti ile aynı platform) mağaza bulucu API'sinin (`itxrest/2/bam/store/{storeId}/physical-store`, yalnızca mağaza keşfi için kullanıldı) döndürdüğü gerçek mağaza ID'lerinden — gerçek stok sorgusu doğrudan bu ID ile çalışıyor, enlem/boylam çalışma zamanında GEREKMİYOR. Kadıköy için Kozyatağı'ndaki City's AVM (Bershka'nın seçtiği mahalleyle aynı gerekçe); Şişli için Cevahir AVM ve Çankaya için Kentpark AVM (diğer markalarla birebir aynı mağaza); Bornova için Forum Bornova AVM — bu markada GERÇEK bir Bornova eşleşmesi bulundu (Massimo Dutti/Beymen'de en yakın eşleşmeye gidilmesi gerekmişti).

`GET /stores?brandId=&city=&district=` — tüm filtreler opsiyonel, `city`/`district` karşılaştırması case-insensitive, sadece `IsActive=true` kayıtlar döner. `GET /stores/{id}` (Faz 3.2) — tek mağaza, Subscription Service'in Stock Poller'ı `BrandSpecificStoreId` (ve Mango/H&M için `Latitude`/`Longitude`) çözmek için kullanır.

### subscription_db

| Tablo | Alanlar |
|---|---|
| `WatchGroups` | Id, ProductCode, Size, StoreId (nullable), LastCheckedAt, LastKnownStatus (`StockStatus?`, `Shared.Contracts.Messages.V1` ile aynı enum), CreatedAt |
| `UserWatches` | Id, UserId, WatchGroupId (FK, cascade delete), CreatedAt |

**Dedup mantığı**: aynı `ProductCode`+`Size`+`StoreId` kombinasyonunu takip eden tüm kullanıcılar tek bir `WatchGroup`'a bağlanır — `UserWatches` N:1 ayrımı bunun için var. `WatchGroups` üzerinde `(ProductCode, Size, StoreId)` index'i var ama **unique değil**: `StoreId` nullable olduğundan Postgres unique constraint'te NULL'ları birbirinden farklı sayar, bu yüzden dedup DB constraint'i yerine `WatchService.CreateWatchAsync` içinde application-level "find or create" ile yapılıyor. `UserWatches` üzerinde `(UserId, WatchGroupId)` **unique** index var — aynı kullanıcının aynı gruba iki kez eklenmesini DB seviyesinde engelliyor.

`POST /watches` `{userId, productCode, size, storeId?}` alır, mevcut/yeni `WatchGroup`'a bağlı `UserWatch` döner (201). Kullanıcı zaten aynı `WatchGroup`'u takip etmiyorsa (Faz 4.3), yeni bir `UserWatch` açılmadan önce Billing Service'in `GET /limits/{userId}`'i sorulur — limit aşılmışsa `403` + `WATCH_LIMIT_EXCEEDED` döner (bkz. altta ve `.claude/ARCHITECTURE.md` → Billing → Limit Kontrol Middleware'i). `GET /watches?userId=` kullanıcının takip listesini `WatchGroup` verisiyle (LastKnownStatus/LastCheckedAt) birlikte döner. `DELETE /watches/{id}?userId=` yalnızca `UserWatch`'ı siler (WatchGroup, başka kullanıcılar takip ediyor olabileceği için silinmez); `userId` sahiplik kontrolü için zorunlu — eşleşmezse `404` (kayıt var/yok bilgisi sızdırılmaz).

> Not: `GetWatchesAsync` sorgusunda `OrderByDescending` DTO projeksiyonundan **önce** uygulanıyor — projeksiyon sonrası sıralama InMemory test provider'ında client-eval ile sorunsuz çalışıyor ama gerçek Npgsql sağlayıcısında SQL'e çevrilemiyor (`InvalidOperationException`). Gerçek Postgres'e karşı uçtan uca test sırasında yakalandı, unit testler (InMemory) bunu yakalayamadı.

**Stock Poller (Faz 3.2)**: `WatchGroups.LastKnownStatus`/`LastCheckedAt`, artık iki yoldan güncelleniyor — (1) `StockPollerService`, Quartz.NET ile periyodik çalışıp henüz `InStock` olduğu doğrulanmamış grupları (`LastKnownStatus IS NULL OR <> InStock`) `UserWatches` sayısına göre önceliklendirilmiş bir sıklıkla (varsayılan: ≥5 kullanıcı/5 dk, ≥2 kullanıcı/15 dk, aksi halde/60 dk — `appsettings.json > Poller`) tekrar kontrole gönderir (`CheckStockCommand` V2 publish + optimistic `LastCheckedAt` güncellemesi); (2) `StockResultEventConsumer` → `WatchGroupStatusUpdater`, scraper'lardan gelen `StockResultEvent`'i (fanout) `ProductCode`+`Size`+`StoreId` ile eşleştirip gerçek sonucu yazar. Detay ve uçtan uca doğrulama için bkz. `.claude/ARCHITECTURE.md` → Stock Poller.

### notification_db

| Tablo | Alanlar |
|---|---|
| `NotificationLogs` | Id, UserId, ProductCode, Size, StoreId (nullable), Channel (`Push`/`Email`), CommandId, Success, ErrorMessage, SentAt |
| `WatchGroupNotificationStates` | Id, ProductCode, Size, StoreId (nullable), LastKnownStatus (`StockStatus?`), UpdatedAt |

`NotificationLogs(CommandId, UserId, Channel)` **unique** — idempotency guard'ı: aynı `StockResultEvent` iki kez tüketilse (MassTransit at-least-once) bile aynı kullanıcıya aynı kanaldan ikinci bir bildirim gitmez. `WatchGroupNotificationStates(ProductCode, Size, StoreId)` unique — Subscription Service'in `WatchGroups` tablosundan **bilerek bağımsız**: aynı `StockResultEvent`'i paralel tüketen iki servisten biri diğerine "önceki durum neydi" diye sorarsa yarış durumu oluşur, bu yüzden Notification kendi geçmişini kendi tutuyor (bkz. `.claude/ARCHITECTURE.md` → Notification Service).

### billing_db (App Store/Play Store IAP, bkz. `.claude/ARCHITECTURE.md` → Billing)

| Tablo | Alanlar |
|---|---|
| `Plans` | Id, Name, MaxTrackedProducts, CheckFrequencyMinutes, AppStoreProductId (nullable), PlayStoreProductId (nullable), IsActive, CreatedAt |
| `UserPlans` | Id, UserId (unique), PlanId (FK, Restrict), AssignedAt |
| `UserSubscriptions` | Id, UserId (unique), PlanId, Platform (`Apple`/`Google`), StoreTransactionId (nullable, Apple), PurchaseToken (nullable, Google), Status (`Active`/`GracePeriod`/`Cancelled`/`Expired`/`Refunded`/`Unknown`), CurrentPeriodEnd (nullable), CreatedAt, UpdatedAt |
| `PaymentEvents` | Id, SubscriptionId (nullable FK), Provider (`Apple`/`Google`), EventId, EventType, RawPayload, ReceivedAt |

**`Price` yok**: fiyat App Store Connect/Play Console'da tanımlanır, DB yalnızca store ürün ID referanslarını tutar. **Seed data**: `Free` (`FreePlanId` sabit Guid, 3 ürün/60 dk) ve `Premium` (`PremiumPlanId` sabit Guid, 50 ürün/5 dk) — `AppStoreProductId`/`PlayStoreProductId` gerçek store ürünleri oluşturulana kadar `null`.

**Otomatik Free plan atama (Faz 4.1)**: Identity Service, `POST /auth/register` başarılı olduğunda `UserRegisteredEvent`'i (fanout, `Messages.V1`) publish eder; Billing Service kendi bağımsız kuyruğuyla (`billing-user-registered-events`) tüketip `UserPlans`'a idempotent şekilde Free plan satırı ekler. `GET /plans` ve `GET /users/{userId}/plan` ile sorgulanabilir.

**IAP doğrulama + webhook (Faz 4.2)**: `PaymentEvents(Provider, EventId)` unique — idempotency guard'ı, aynı Apple/Google webhook event'i iki kez teslim edilirse ikinci deneme buna çarpar. `UserSubscriptions(UserId)` unique — MVP kapsamında kullanıcı başına tek abonelik. `POST /verify-purchase`, `POST /webhooks/apple`, `POST /webhooks/google` (servisin kendi route'ları — Gateway `/api/billing` prefix'ini soyduğundan önekSİZ, bkz. `.claude/ARCHITECTURE.md` → Billing → routing hatası notu) — detay ve gerçek altyapıya karşı uçtan uca doğrulama için bkz. `.claude/ARCHITECTURE.md` → Billing → Store IAP Doğrulama + Webhook.

**Limit kontrolü (Faz 4.3)**: `GET /limits/{userId}` — kullanıcının henüz bir planı olmadığı durumda bile (Free'ye düşerek) her zaman bir limit döner, 404 vermez. Subscription Service, yeni bir `UserWatch` oluşturmadan önce bunu çağırır; Billing'e ulaşılamazsa fail-open (izin verir) — detay için bkz. `.claude/ARCHITECTURE.md` → Billing → Limit Kontrol Middleware'i.

> Faz 4.2 şeması taslaktır; ilgili migration'la netleştirilecektir.

## Cross-Service Veri Referansları

- Foreign key **yok** (farklı veritabanları). Yerine `UserId`, `BrandId` gibi ID referansları tutulur.
- `BrandId` değerleri `product_db.Brands` tablosundaki ID'lerle eşleşir — bu bir convention, FK constraint değil.
- Tutarlılık event-driven güncellemelerle (RabbitMQ) veya senkron API doğrulamasıyla sağlanır.