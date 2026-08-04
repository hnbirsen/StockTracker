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
| `billing_db` | Billing Service | 🔜 Planlanıyor |
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

### Scraper Health Monitoring — Redis, tüm marka scraper'ları arasında paylaşılan (kendi veritabanı yok)

`scraper:health:{scraperName}:log` → her scraper denemesinin (`source`, `success`, `httpStatusCode`, `errorMessage`, `context` — hangi ürün URL'i/mağaza/partnumber üzerinde olduğu, `durationMs`, `timestamp`) `LPUSH` ile eklendiği, `LTRIM` ile son 500 kayıtla sınırlanan capped-list. Bilinçli olarak ayrı bir Postgres DB (`bershka_scraper_db` gibi) yerine bu kullanıldı — bkz. `.claude/ARCHITECTURE.md` → Bershka Scraper → Scraper Health Monitoring, "Tasarım kararı" notu. `StockTracker.Shared.Scraping` projesindeki `IScraperHealthLogService` üzerinden okunur/yazılır; Zara/Pull&Bear gibi gelecek scraper'lar aynı servisi kendi `scraperName`'leriyle çağırarak sıfır ek altyapıyla kullanabilir.

### brand_db

| Tablo | Alanlar |
|---|---|
| `BrandCodeSignatures` | Id, BrandId, BrandName, RegexPattern, Confidence, IsActive, CreatedAt |

**Seed data:**

| Marka | Pattern | Confidence |
|---|---|---|
| Bershka | `^\d{11}$` | High |
| Zara | `^\d{5}/\d{3}/\d{2,3}$` | High |
| Pull&Bear | `^\d{8}$` | Low |

> Bershka'nın pattern'i, gerçek bershka.com ürün sayfaları üzerinden doğrulandı (Faz 2.4): önde sıfır + 4 haneli model + 3 haneli varyant + 3 haneli renk kodu (ör. REF `2891/054/426` → `02891054426`), `BershkaStockApiClient`'ın stok API'sine gönderdiği `productCode` ile birebir aynı format. Eski `^\d{7,9}$` deseni doğrulanmamış bir tahmindi.

### store_db

| Tablo | Alanlar |
|---|---|
| `Stores` | Id, BrandId, BrandName, City, District, StoreName, BrandSpecificStoreId, IsActive, CreatedAt |

`BrandId`, `product_db.Brands.Id` ile eşleşen convention-based referans (FK değil). `BrandSpecificStoreId`, markanın kendi sitesi/API'sinde bu fiziksel mağazayı tanımlayan kod — scraper (Faz 2.4) bunu kullanacak.

**Seed data:** Bershka — 4 mağaza:

| Şehir | İlçe | Mağaza | BrandSpecificStoreId |
|---|---|---|---|
| Istanbul | Kadikoy | City's Kozyatağı | `16884` |
| Istanbul | Sisli | Cevahir AVM | `8359` |
| Ankara | Cankaya | Kentpark | `6943` |
| Izmir | Bornova | Forum Bornova | `8426` |

> `BrandSpecificStoreId` değerleri artık Bershka'nın gerçek mağaza bulucu API'sinden (`itxrest/2/bam/store/{chainId}/physical-store`) dönen gerçek `physicalStoreId` değerleri (Faz 2.4) — `BershkaStockApiClient.CheckStoreStockAsync` bunları doğrudan stok API'sinin `physicalStoreId` parametresine geçiriyor. Her mağaza, ilgili il/ilçeye en yakın gerçek Bershka mağazasıdır (Kadıköy için Kozyatağı — Kadıköy ilçesi sınırları içinde bir mahalle; Şişli/Bornova için isim birebir eşleşiyor; Çankaya için adreste "ÇANKAYA" geçen mağaza seçildi, çünkü en yakın 5 sonuç arasında marka adı "Armada" olan bir mağaza yoktu). Detay için bkz. `.claude/ARCHITECTURE.md` → Bershka Scraper.

`GET /stores?brandId=&city=&district=` — tüm filtreler opsiyonel, `city`/`district` karşılaştırması case-insensitive, sadece `IsActive=true` kayıtlar döner. `GET /stores/{id}` (Faz 3.2) — tek mağaza, Subscription Service'in Stock Poller'ı `BrandSpecificStoreId` çözmek için kullanır.

### subscription_db

| Tablo | Alanlar |
|---|---|
| `WatchGroups` | Id, ProductCode, Size, StoreId (nullable), LastCheckedAt, LastKnownStatus (`StockStatus?`, `Shared.Contracts.Messages.V1` ile aynı enum), CreatedAt |
| `UserWatches` | Id, UserId, WatchGroupId (FK, cascade delete), CreatedAt |

**Dedup mantığı**: aynı `ProductCode`+`Size`+`StoreId` kombinasyonunu takip eden tüm kullanıcılar tek bir `WatchGroup`'a bağlanır — `UserWatches` N:1 ayrımı bunun için var. `WatchGroups` üzerinde `(ProductCode, Size, StoreId)` index'i var ama **unique değil**: `StoreId` nullable olduğundan Postgres unique constraint'te NULL'ları birbirinden farklı sayar, bu yüzden dedup DB constraint'i yerine `WatchService.CreateWatchAsync` içinde application-level "find or create" ile yapılıyor. `UserWatches` üzerinde `(UserId, WatchGroupId)` **unique** index var — aynı kullanıcının aynı gruba iki kez eklenmesini DB seviyesinde engelliyor.

`POST /watches` `{userId, productCode, size, storeId?}` alır, mevcut/yeni `WatchGroup`'a bağlı `UserWatch` döner (201). `GET /watches?userId=` kullanıcının takip listesini `WatchGroup` verisiyle (LastKnownStatus/LastCheckedAt) birlikte döner. `DELETE /watches/{id}?userId=` yalnızca `UserWatch`'ı siler (WatchGroup, başka kullanıcılar takip ediyor olabileceği için silinmez); `userId` sahiplik kontrolü için zorunlu — eşleşmezse `404` (kayıt var/yok bilgisi sızdırılmaz).

> Not: `GetWatchesAsync` sorgusunda `OrderByDescending` DTO projeksiyonundan **önce** uygulanıyor — projeksiyon sonrası sıralama InMemory test provider'ında client-eval ile sorunsuz çalışıyor ama gerçek Npgsql sağlayıcısında SQL'e çevrilemiyor (`InvalidOperationException`). Gerçek Postgres'e karşı uçtan uca test sırasında yakalandı, unit testler (InMemory) bunu yakalayamadı.

**Stock Poller (Faz 3.2)**: `WatchGroups.LastKnownStatus`/`LastCheckedAt`, artık iki yoldan güncelleniyor — (1) `StockPollerService`, Quartz.NET ile periyodik çalışıp henüz `InStock` olduğu doğrulanmamış grupları (`LastKnownStatus IS NULL OR <> InStock`) `UserWatches` sayısına göre önceliklendirilmiş bir sıklıkla (varsayılan: ≥5 kullanıcı/5 dk, ≥2 kullanıcı/15 dk, aksi halde/60 dk — `appsettings.json > Poller`) tekrar kontrole gönderir (`CheckStockCommand` V2 publish + optimistic `LastCheckedAt` güncellemesi); (2) `StockResultEventConsumer` → `WatchGroupStatusUpdater`, scraper'lardan gelen `StockResultEvent`'i (fanout) `ProductCode`+`Size`+`StoreId` ile eşleştirip gerçek sonucu yazar. Detay ve uçtan uca doğrulama için bkz. `.claude/ARCHITECTURE.md` → Stock Poller.

### notification_db

| Tablo | Alanlar |
|---|---|
| `NotificationLogs` | Id, UserId, ProductCode, Size, StoreId (nullable), Channel (`Push`/`Email`), CommandId, Success, ErrorMessage, SentAt |
| `WatchGroupNotificationStates` | Id, ProductCode, Size, StoreId (nullable), LastKnownStatus (`StockStatus?`), UpdatedAt |

`NotificationLogs(CommandId, UserId, Channel)` **unique** — idempotency guard'ı: aynı `StockResultEvent` iki kez tüketilse (MassTransit at-least-once) bile aynı kullanıcıya aynı kanaldan ikinci bir bildirim gitmez. `WatchGroupNotificationStates(ProductCode, Size, StoreId)` unique — Subscription Service'in `WatchGroups` tablosundan **bilerek bağımsız**: aynı `StockResultEvent`'i paralel tüketen iki servisten biri diğerine "önceki durum neydi" diye sorarsa yarış durumu oluşur, bu yüzden Notification kendi geçmişini kendi tutuyor (bkz. `.claude/ARCHITECTURE.md` → Notification Service).

### billing_db (planlanan — App Store/Play Store IAP, bkz. `.claude/ARCHITECTURE.md` → Billing)
- `Plans` (Id, Name, MaxTrackedProducts, CheckFrequencyMinutes, AppStoreProductId, PlayStoreProductId) — **`Price` yok**: fiyat App Store Connect/Play Console'da tanımlanır, DB yalnızca store ürün ID referanslarını tutar
- `Subscriptions` (Id, UserId, PlanId, Platform (`Apple`/`Google`), Status, StoreTransactionId/PurchaseToken, CurrentPeriodEnd)
- `PaymentEvents` (Id, SubscriptionId, Provider (`Apple`/`Google`), EventType, EventId, RawPayload, ReceivedAt) — `(Provider, EventId)` unique, webhook idempotency için

> Planlanan şema taslaktır; Faz 4 başladığında ilgili servisin migration'ı ile netleştirilecektir.

## Cross-Service Veri Referansları

- Foreign key **yok** (farklı veritabanları). Yerine `UserId`, `BrandId` gibi ID referansları tutulur.
- `BrandId` değerleri `product_db.Brands` tablosundaki ID'lerle eşleşir — bu bir convention, FK constraint değil.
- Tutarlılık event-driven güncellemelerle (RabbitMQ) veya senkron API doğrulamasıyla sağlanır.