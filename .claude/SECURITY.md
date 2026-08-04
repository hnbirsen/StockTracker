# Güvenlik Notları

## Secrets Yönetimi

- Connection string'ler ve JWT secret'ları `appsettings.json`'a **gömülmez**, environment variable olarak sağlanır.
- Her servis `Environment.GetEnvironmentVariable(...)` ile önce env'den, bulamazsa `appsettings.json`'dan okur.
- `appsettings.json`'da tüm hassas değerler `"REPLACE_WITH_ENV"` placeholder'ı ile tutulur.
- `.env` dosyası `.gitignore`'da — repoya commit'lenmez. Sadece `.env example` (değersiz) repoda tutulur.
- Production'da secret manager (Azure Key Vault, AWS Secrets Manager, Doppler) kullanılması önerilir.

## Kimlik Doğrulama

- Şifreler BCrypt ile hash'lenir (salt otomatik dahildir), düz metin asla saklanmaz/loglanmaz.
- JWT access token 60 dakika ömürlü (config'den değiştirilebilir).
- Refresh token rotation uygulanır: her refresh işleminde eski token invalidate edilir.
- Token reuse detection (aynı refresh token'ın iki kez kullanılmaya çalışılması → tüm oturumu sonlandır) henüz uygulanmadı — Faz 1.x'te eklenecek.
- Refresh token'lar veritabanında düz metin olarak tutuluyor — hash'lenmesi Faz 1.x önerisi.

## Servisler Arası İletişim Güvenliği

- Servisler birbirleriyle doğrudan HTTP ile konuşur (gateway bypass). Bu iletişim şu an HTTP — production'da internal network izolasyonu veya mTLS ile güçlendirilmeli.
- Gateway JWT doğrulamasını merkezi olarak yapar; servisler içinde ayrıca token doğrulama yapılmaz.

## Scraping — Yasal ve Teknik Riskler

- Hedef sitelerin ToS'una aykırılık ve anti-bot önlemleriyle karşılaşma riski **bilinçli olarak kabul edilmiştir**.
- Öneriler:
  - Rate limiting ile hedef sitelere aşırı yük bindirme
  - `robots.txt`'i mümkün olduğunca dikkate al
  - Kişisel veri scrape etme (yalnızca ürün/stok verisi)
  - Ölçek büyüdükçe marka bazlı hukuki risk değerlendirmesi yenilenmeli
  - Affiliate programına dahil olmak hem gelir hem meşruiyet sağlar

## Ödeme Güvenliği (App Store / Play Store In-App Purchase)

Ödeme, ayrı bir sanal pos/ödeme sağlayıcısı yerine mobil uygulamanın App Store/Play Store içindeki yerleşik abonelik satın alma akışı üzerinden alınır (bkz. `.claude/ARCHITECTURE.md` → Billing — kullanıcı kararı). Bu, PCI-DSS yükünü ve kart-bilgisi riskini tamamen ortadan kaldırır, ama yeni ve store'a özgü riskler getirir:

- Kart bilgileri **hiçbir zaman** kendi sunucularına uğramaz — kullanıcı ödemeyi doğrudan Apple/Google'a yapar, Billing Service yalnızca satın almanın sonucunu görür.
- **Client'ın gönderdiği receipt/purchase token asla client beyanı olarak güvenilmez** — `POST /billing/verify-purchase` her zaman ilgili store'un **server-to-server** API'sine karşı doğrulama yapar (Apple: App Store Server API; Google: Play Developer API, `PurchaseVerificationService`). Bu doğrulama atlanırsa, sahte/tahrif edilmiş bir token ile ücretsiz premium açılabilir.
- Webhook endpoint'leri (`POST /billing/webhooks/apple`, `POST /billing/webhooks/google`) imza doğrulaması yapar: Apple için JWS imzası isteğin kendi header'ındaki sertifikayla (`AppleJwsVerifier`), Google için Cloud Pub/Sub push isteğinin OIDC token'ı Google'ın canlı JWKS'ine karşı (`GoogleOidcTokenValidator`) doğrulanır — imzasız/geçersiz imzalı istekler `400`/`401` ile reddedilir.
  - ⚠️ **Bilinen, dokümante edilmiş kapsam sınırlaması (Apple)**: `AppleJwsVerifier` yalnızca imzanın JWS'in kendi header'ındaki sertifikayla eşleştiğini doğrular — o sertifikanın **gerçekten Apple'a ait olduğunu** (Apple'ın Root CA'sına kadar güvenilir bir zincir olduğunu) doğrulamaz. Gerçek bir Apple ortamına erişim olmadan (bu proje henüz bir Apple Developer hesabına sahip değil) uydurma bir Root CA sertifikası gömmek riskli olacağından bilinçli olarak ertelendi. **Gerçek bir Apple Developer hesabı/anahtarı edinildiğinde, prodüksiyona geçmeden önce Apple'ın resmi Root CA sertifikasına kadar `X509Chain` doğrulaması eklenmesi zorunludur** — aksi halde, teorik olarak kendi sertifikasıyla imzalanmış sahte bir webhook payload'ı kabul edilebilir. Google tarafında bu risk yok: `GoogleOidcTokenValidator` her zaman Google'ın canlı, resmi JWKS endpoint'ine karşı doğrulama yapar.
- Webhook idempotency: `PaymentEvents` tablosunda `(Provider, EventId)` unique constraint ile aynı event'in iki kez işlenmesi (retry, duplicate delivery) önlenir (`PaymentEventProcessor`).
- Apple/Google'ın server API anahtarları (App Store Server API `.p8` key, Google service account JSON) diğer secret'lar gibi yalnızca `.env`'de tutulur (Base64 ile, tek satıra sığması için), repoya commit'lenmez. Henüz gerçek değerler girilmedi — `REPLACE_WITH_ENV` placeholder'ı yapılandırılmamış sayılır (`AppleStoreSettings`/`GooglePlaySettings.IsConfigured`), bu durumda ilgili client gerçek bir çağrı yapmadan nazikçe başarısız olur (bkz. `.claude/ARCHITECTURE.md` → Billing — bu davranışta canlı testte bulunup düzeltilen bir hata da not edildi).

## API Güvenliği

- Gateway seviyesinde rate limiting eklenecek (özellikle `/auth/login` ve `/auth/register` — brute-force önleme).
- CORS şu an `AllowAll` — production'da yalnızca bilinen frontend origin'lerine kısıtlanmalı.
- Tüm trafik HTTPS üzerinden olmalı (production'da zorunlu, dev'de opsiyonel).

## KVKK

- Kullanıcı e-posta, konum (il/ilçe) ve bildirim tercihleri tutulmaktadır.
- Açık rıza metni ve veri saklama politikası Faz 4 (Billing) öncesinde hazırlanmalı.
- Bir ticaret/veri hukuku avukatına danışılması önerilir.

## Loglama ve PII

- Şifre, token, kart bilgisi gibi hassas veriler loglanmamalı.
- Kullanıcı e-postası gibi PII loglanacaksa maskelenmesi düşünülmeli (örn. `u***@example.com`).

## Bağımlılık Güvenliği

- GitHub Actions CI'a `dotnet list package --vulnerable` adımı eklenmesi önerilir (henüz eklenmedi).
