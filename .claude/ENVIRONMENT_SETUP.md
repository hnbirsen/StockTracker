# Geliştirme Ortamı Kurulumu

Mac ve Windows PC arasında tutarlı çalışabilmek için ortam kurulum notları.

## Gereksinimler

- .NET SDK 10
- Docker Desktop (Mac ve Windows'ta)
- Git
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`
- VS Code veya Rider (Windows'ta Visual Studio da kullanılabilir)

## Repo Klonlama ve İlk Kurulum

```bash
git clone <repo-url>
cd StockTracker
cp ".env example" .env   # değerleri doldur
docker compose up -d
dotnet restore StockTracker.slnx
```

## Environment Variable Yönetimi

Tüm secret'lar `.env` dosyasından okunur, `appsettings.json`'a gömülmez.

Mevcut `.env` key'leri:

```bash
# Docker Compose altyapısı
POSTGRES_USER=
POSTGRES_PASSWORD=
RABBITMQ_USER=
RABBITMQ_PASSWORD=

# Identity Service
IDENTITY_DB_CONNECTION=Host=localhost;Port=5432;Database=identity_db;Username=...;Password=...
JWT_SECRET_KEY=
JWT_ISSUER=StockTracker
JWT_AUDIENCE=StockTracker.Clients

# Product Service
PRODUCT_DB_CONNECTION=Host=localhost;Port=5432;Database=product_db;Username=...;Password=...
REDIS_CONNECTION=localhost:6379

# Brand Detection Service
BRAND_DB_CONNECTION=Host=localhost;Port=5432;Database=brand_db;Username=...;Password=...
PRODUCT_SERVICE_URL=http://localhost:5002

# RabbitMQ mesajlaşma (MassTransit) — tüm servislerde ortak
RABBITMQ_HOST=localhost

# Search Orchestrator
BRAND_DETECTION_SERVICE_URL=http://localhost:5003
STORE_REFERENCE_SERVICE_URL=http://localhost:5004
# PRODUCT_SERVICE_URL ve REDIS_CONNECTION zaten yukarıda tanımlı, Search Orchestrator da kullanır

# Store Reference Service
STORE_DB_CONNECTION=Host=localhost;Port=5432;Database=store_db;Username=...;Password=...

# Bershka Scraper — gerçek endpoint'ler (bkz. .claude/ARCHITECTURE.md > Bershka Scraper)
BERSHKA_STOCK_API_BASE_URL=https://api.inditex.com
# REDIS_CONNECTION zaten yukarıda tanımlı, Bershka Scraper da kullanır (PDP cache-aside)

# Zara Scraper — ek bir env var GEREKMİYOR. Bershka'nın aksine ayrı bir stok API host'u yok;
# hem online hem mağaza stoğu doğrudan www.zara.com'a karşı Playwright ile okunuyor (bkz.
# .claude/ARCHITECTURE.md > Zara Scraper). REDIS_CONNECTION ve yukarıdaki paylaşılan RabbitMQ
# değişkenleri zaten yeterli.
```

`.env` dosyası `.gitignore`'da olmalı — repoya commit'lenmez. Sadece `.env example` (değersiz, key listesi) repoda tutulur.

## Bershka/Zara Scraper — Playwright/Chrome Kurulumu (tek seferlik, manuel adım)

`StockTracker.BershkaScraper` ve `StockTracker.ZaraScraper`, ürün sayfalarını (ikisi de Akamai Bot Manager'ın arkasında — Zara'da ayrıca `store-product-availability` endpoint'i de aynı korumaya sahip, bkz. `.claude/ARCHITECTURE.md` > Zara Scraper) Playwright ile **gerçek Chrome kanalını** çalıştırarak okur. Bu, `.NET` paket restore'unun (`dotnet restore`) **kapsamadığı** ayrı bir adım — NuGet paketi sadece Playwright'ın .NET API'sini indirir, tarayıcı binary'sini indirmez. Kurulum makine/tarayıcı-cache seviyesinde olduğu için (aşağıya bkz.) her iki servis için ayrı ayrı yapmaya gerek yok — biri için kurulduktan sonra diğeri de aynı önbellekten kullanır; yine de her iki projeyi de en az bir kez build etmek gerekir (`playwright.ps1`/`cli.js` script'i `bin/` altına oradan düşer).

**Neden `chrome` kanalı, bundled `chromium` değil:** gerçek verilerle doğrulandı — Playwright'ın varsayılan (bundled) Chromium'u, standart stealth önlemlerine (UA maskeleme, `navigator.webdriver` gizleme) rağmen Akamai'den anında "Access Denied" alıyor. Gerçek Chrome kanalını sürmek bu engeli aşıyor (bkz. `.claude/ARCHITECTURE.md` > Bershka Scraper > Playwright + Redis cache-aside). Bu yüzden `chromium` değil `chrome` kurulmalı.

**Neden `git clone` sonrası bunu fark etmiyorsun:** kurulum script'i (`playwright.ps1` / `.playwright/package/cli.js`) proje build edilince `bin/` altına düşer, ama `bin/` `.gitignore`'da — yani repoyu klonlayan biri bu adımı hiçbir dosyada görmez, ancak build alıp servisi ilk kez `dotnet run` ile çalıştırdığında (veya testleri Playwright'a bağlı bir senaryoyla çalıştırdığında) fark eder. Bu yüzden burada açıkça belgeleniyor.

**Kurulum (her geliştirici makinesinde bir kez):**

```bash
dotnet build StockTracker.BershkaScraper --configuration Release

# PowerShell (pwsh) kuruluysa — resmi/önerilen yol:
pwsh StockTracker.BershkaScraper/bin/Release/net10.0/playwright.ps1 install chrome

# pwsh yoksa (Mac'te `brew install --cask powershell` ile kurulabilir), Node.js zaten kuruluysa alternatif:
cd StockTracker.BershkaScraper/bin/Release/net10.0
node .playwright/package/cli.js install chrome
```

`chrome` kanalı, makinede zaten kurulu bir Google Chrome varsa onu kullanır; yoksa Playwright kendi indirir. Test ettiğimiz makinede (Mac, gerçek Chrome kurulu) bu sorunsuz çalıştı.

**`dotnet clean` bunu siler mi? Hayır.** İndirilen tarayıcı, `bin/` içinde değil — kullanıcı seviyesinde, proje dışı bir önbellek klasöründe tutulur (Mac: `~/Library/Caches/ms-playwright/`, Linux: `~/.cache/ms-playwright/`, Windows: `%USERPROFILE%\AppData\Local\ms-playwright\`). `dotnet clean`/`bin`+`obj` silme, sadece küçük installer script'ini (`.playwright/`) siler — bu, bir sonraki `dotnet build`'de otomatik geri gelir ve zaten önbellekte olanı tekrar indirmeden kullanır. Yani bu kurulumu makine başına yalnızca bir kez yapman yeterli.

## Servisleri Çalıştırma

Her servis ayrı terminal sekmesinde çalıştırılır:

```bash
dotnet run --project StockTracker.Gateway          # :8000
dotnet run --project StockTracker.Identity         # :5001
dotnet run --project StockTracker.Product          # :5002
dotnet run --project StockTracker.BrandDetection   # :5003
dotnet run --project StockTracker.StoreReference   # :5004
dotnet run --project StockTracker.SearchOrchestrator # :5005
dotnet run --project StockTracker.Subscription     # :5006
dotnet run --project StockTracker.Billing          # :5007
dotnet run --project StockTracker.Notification     # :5008
dotnet run --project StockTracker.BershkaScraper   # :5009
dotnet run --project StockTracker.ZaraScraper      # :5010
```

Sadece belirli bir servis üzerinde çalışıyorsan, altyapıyı ayağa kaldırıp o servisi IDE'den debug edebilirsin:

```bash
docker compose up -d   # postgres, redis, rabbitmq ayağa kalkar
# ardından IDE'den ilgili servisi Debug modda başlat
```

## Mac ↔ Windows Arası Dikkat Edilmesi Gerekenler

1. **Init script permission sorunu (çözüldü):** Mac'te `.sh` dosyaları Docker'a geçişte execute izni kaybeder. Bu proje `.sql` dosyası kullandığı için bu sorun geçerli değil. Yeni `.sh` dosyası eklenirse `git update-index --chmod=+x` ile izin verilmeli.

2. **Satır sonu karakterleri (CRLF/LF):** `.gitattributes` ile `.sh` dosyaları LF'e zorlanmalı. Shell script'lerde CRLF `env: bash\r: No such file or directory` hatasına yol açar.

3. **Docker volume path'leri:** `docker-compose.yml`'de relative path kullanıldığı için Mac/Windows arasında taşınabilirlik sorun çıkarmaz.

## Docker Compose Sağlık Kontrolü

```bash
docker compose ps
docker compose logs postgres
docker compose logs -f identity-service
```

## Veritabanı Kontrol

```bash
# Tüm DB'leri listele
docker exec -it stocktracker-postgres psql -U stocktracker -l

# Belirli bir DB'ye bağlan
docker exec -it stocktracker-postgres psql -U stocktracker -d identity_db
```

## Redis Kontrol

```bash
docker exec -it stocktracker-redis redis-cli ping   # → PONG
docker exec -it stocktracker-redis redis-cli keys "product:*"  # cache key'leri listele
```

## RabbitMQ Yönetim Paneli

```
http://localhost:15672
Kullanıcı adı / şifre: .env'deki RABBITMQ_USER / RABBITMQ_PASSWORD
```

## CI ile Paritenin Korunması

GitHub Actions pipeline her push'ta:
- `dotnet restore` + `dotnet build` + `dotnet test`
- `docker compose config` (YAML doğrulama)

Local'de "bende çalışıyor" sorunlarını önlemek için PR açmadan önce CI loglarını kontrol et.
