# Çalışma Kuralları (Solo Geliştirme)

Tek kişilik proje olsa da, tutarlılık ve ileride ekip büyürse kolay onboarding için standartlar.

## Branch Stratejisi

- `main` — her zaman deploy edilebilir durumda
- `feature/{faz}-{kisa-aciklama}` — ör. `feature/faz2.1-rabbitmq-contracts`
- Faz tamamlanınca `main`'e merge, gerekirse squash

## Commit Mesajı Formatı

[Conventional Commits](https://www.conventionalcommits.org/) tarzı:

```
feat(identity): refresh token rotation eklendi
feat(product): redis cache entegrasyonu
feat(brand-detection): regex format imzası katmanı
fix(gateway): PathPattern/PathRemovePrefix çakışması giderildi
chore(docker): init script izin sorunu sql ile çözüldü
docs: mimari dokümanı güncellendi
```

Prefixler: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`.

## Yeni Servis Eklerken Checklist

- [ ] Yeni PostgreSQL veritabanı `docker/postgres-init/01-init-databases.sql`'a eklendi
- [ ] Servis projesi oluşturuldu, `Program.cs` minimal API ile kuruldu
- [ ] EF Core DbContext + ilk migration oluşturuldu
- [ ] Connection string environment variable olarak tanımlandı (`.env example`'a eklendi)
- [ ] YARP Gateway'e route eklendi (`PathRemovePrefix` ayrı transform bloğunda)
- [ ] `appsettings.json`'da hassas değerler `"REPLACE_WITH_ENV"` placeholder'ı
- [ ] `.env` dosyasına yeni key'ler eklendi
- [ ] Servis `http://0.0.0.0:{port}` olarak yapılandırıldı
- [ ] Health check endpoint'i eklendi (`GET /health`)
- [ ] Uygulama başlangıcında `db.Database.MigrateAsync()` çağrısı var
- [ ] Gateway üzerinden health check test edildi
- [ ] `.claude/` dokümanları güncellendi (ROADMAP, DATABASE, ARCHITECTURE)

## Yeni Marka Scraper Eklerken Checklist

- [ ] Markanın ürün kodu formatı incelendi, regex pattern belirlendi
- [ ] `BrandCodeSignatures` tablosuna seed data eklendi
- [ ] `Brands` tablosuna seed data eklendi (`ScraperQueueName` tanımlandı)
- [ ] Chrome DevTools ile iç JSON API araştırıldı
- [ ] Scraper worker servisi oluşturuldu (RabbitMQ consumer)
- [ ] Store Reference tablosuna mağaza listesi eklendi
- [ ] Scraper health monitoring kaydı oluşturuldu
- [ ] Polly retry + circuit breaker yapılandırıldı

## Kod Standartları

- Nullable reference types açık (`<Nullable>enable</Nullable>`)
- Minimal API + `MapGroup` tercih edilir, controller'lar yerine
- DTO'lar (`record`) ile domain entity'leri ayrıştırılır, entity'ler response olarak dışarı sızdırılmaz
- Async/await her I/O işleminde kullanılır
- Environment variable önce, `appsettings.json` fallback:
  ```csharp
  var value = Environment.GetEnvironmentVariable("KEY")
      ?? configuration["Section:Key"]
      ?? throw new InvalidOperationException("...");
  ```

## Servisler Arası İletişim Kuralları

- **Dış trafik** (client → servis): her zaman Gateway üzerinden
- **İç trafik** (servis → servis): doğrudan HTTP, Gateway bypass
- İç servis URL'leri environment variable'dan okunur (örn. `PRODUCT_SERVICE_URL=http://localhost:5002`)
- Typed HttpClient (`builder.Services.AddHttpClient<IClient, Client>(...)`) kullanılır

## Test Stratejisi

- Her servis için en az: unit test (business logic), integration test (endpoint + gerçek/test DB)
- CI pipeline'da `dotnet test` adımı zorunlu, başarısız test merge'i engeller

## Dokümantasyon Güncelleme Kuralı

Yeni bir faz veya önemli değişiklik tamamlandığında şu dosyalar güncellenir:
- `README.md` → durum tablosu ve servis listesi
- `.claude/ROADMAP.md` → ilgili faz checklist'i işaretlenir
- `.claude/DATABASE.md` → yeni tablo/şema bilgisi
- `.claude/ARCHITECTURE.md` → servis durumu tablosu
- `.claude/ENVIRONMENT_SETUP.md` → yeni env variable'lar
