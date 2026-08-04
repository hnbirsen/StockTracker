# Bekleyen Girdiler / Kararlar

Bu dosya, geliştirme sırasında "gerçek veri/hesap yok, kod placeholder ile hazır" diye bilinçli olarak ertelenen ve **kullanıcının doldurması/karar vermesi gereken** tüm noktaların tek listesi. Her madde işaretlendiğinde (`[x]`) ilgili `.env` değeri gerçek değerle güncellenir ve gerekirse kısa bir doğrulama yapılır — kod tarafında ek değişiklik gerekmez (hepsi zaten gerçek API'lere göre yazıldı).

Frontend süreci (Faz 5) tasarım bekliyor; bu liste o zamana kadar backend'in "gerçek dünyaya bağlanma" borcunu takip etmek için var.

## Nasıl kullanılır

1. Bir madde için gerçek değeri elde edince `.env` dosyasındaki ilgili `REPLACE_WITH_ENV` placeholder'ını gerçek değerle değiştir.
2. Bu dosyada ilgili checkbox'ı işaretle.
3. "Doğrulama" sütununda önerilen hızlı testi çalıştırıp gerçekten çalıştığını teyit et (opsiyonel ama önerilir).

---

## Faz 3.3 — Notification Service

- [ ] **Kendi SMTP sunucumuz/relay'imiz + `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_USE_SSL`** — email bildirimleri için. **Kullanıcı kararıyla ilk süreçte 3. taraf bir email sağlayıcısı kullanılmıyor** — kendi mail sunucunuz/relay'iniz gerekiyor (kurumsal SMTP, kendi barındırdığınız bir mail sunucusu, veya bir hosting sağlayıcının SMTP'si — üçüncü taraf bir "transactional email API"si değil, düz SMTP protokolü). Şu an: `SMTP_HOST` yoksa `SmtpEmailSender` (MailKit) bağlantı denemeden `false` döner, `NotificationLog.Success=false` yazar, servis çökmez. `SMTP_USERNAME`/`SMTP_PASSWORD` opsiyonel — bazı relay'ler kimlik doğrulaması istemez.
  - Doğrulama: `.env`'i doldurup Notification servisini yeniden başlat, bir restock event'i tetikle (bkz. `.claude/ARCHITECTURE.md` → Notification Service, uçtan uca doğrulama bölümündeki curl/RabbitMQ komutları), gerçek bir email'in ulaştığını kontrol et. Gerçek sunucu olmadan yerel test için: Docker'da `mailhog/mailhog` çalıştırıp `SMTP_HOST=localhost`, `SMTP_PORT=1025`, `SMTP_USE_SSL=false` ile denenebilir — bu proje bu şekilde canlı doğrulandı.
- [ ] **`NOTIFICATION_FROM_EMAIL`** — şu an `notifications@stocktracker.local` (gerçek bir domain değil). Kendi domaininizin SPF/DKIM kayıtlarını doğru kurmazsanız gönderdiğiniz mailler spam'e düşebilir — yalnızca bir env var değişikliği değil, DNS tarafında da iş gerektirir.
- [ ] **(Planlanan — sonraki aşama) Amazon SES'e geçiş**: kullanıcı kararı — ilk süreçte kendi SMTP sunucumuz kullanılacak, ancak ileride email gönderimi Amazon SES'e taşınacak (bkz. AWS'in projede nerede faydalı olacağına dair önceki değerlendirme — SES, kendi SMTP altyapısını yönetme yükünü ortadan kaldırıp deliverability/ölçek avantajı sağlıyor). Kod tarafında bu geçiş küçük bir iş: `IEmailSender` zaten soyutlanmış olduğundan SES için ya SMTP arayüzü (host: `email-smtp.<region>.amazonaws.com`, mevcut `SmtpEmailSender`/MailKit'i neredeyse hiç değiştirmeden kullanılabilir) ya da AWS SDK (`AWSSDK.SimpleEmailV2`) tabanlı yeni bir `IEmailSender` implementasyonu eklenir. Gerekecek girdiler: AWS hesabı, SES'te domain doğrulama + üretim erişimi (sandbox'tan çıkış), IAM kimlik bilgileri (SMTP kullanıcı adı/şifre ya da access key/secret).
- [ ] **Firebase projesi + `FCM_SERVER_KEY`** — push bildirimleri için. Şu an: anahtar olsa bile **hiç çağrılmıyor**, çünkü cihaz push token'ı saklayan hiçbir mekanizma yok (`NoOpDeviceTokenProvider` her zaman `null` döner). Bu satırın gerçek anlam kazanması için Faz 5.4'te (mobil uygulama) token kayıt akışının da yazılması gerekiyor — sadece anahtarı girmek yeterli değil.

## Faz 4.2 — Apple (App Store In-App Purchase)

- [ ] **Apple Developer Program hesabı** (yıllık ücretli).
- [ ] **App Store Connect'te abonelik ürünü tanımlama** — ürün ID'leri, `Plans.AppStoreProductId` alanına yazılacak (şu an `null`).
- [ ] **`APPLE_ISSUER_ID`, `APPLE_KEY_ID`, `APPLE_PRIVATE_KEY_BASE64`** — App Store Server API anahtarı (App Store Connect → Users and Access → Keys → In-App Purchase). `.p8` dosyasının tamamını `base64 -i AuthKey_XXXX.p8` ile tek satıra çevirip `APPLE_PRIVATE_KEY_BASE64`'e yapıştır.
- [ ] **`APPLE_BUNDLE_ID`** — mobil uygulamanın bundle identifier'ı (Faz 5.4'te Expo/React Native projesi kurulunca netleşir).
- [ ] `APPLE_STORE_ENVIRONMENT` zaten `Sandbox` — gerçek yayına geçilene kadar değiştirmeyin.
- [ ] Doğrulama: `.env`'i doldurup Billing servisini yeniden başlat, gerçek bir Sandbox Tester hesabıyla mobil (veya App Store Connect test aracıyla) bir satın alma yap, `POST /verify-purchase`'a gerçek transactionId gönder, `UserSubscriptions`/`UserPlans`'ın güncellendiğini kontrol et.
- [ ] ⚠️ **Prodüksiyona geçmeden önce zorunlu kod değişikliği** (bu bir "veri doldurma" değil, kod işi — ayrıca not ediyorum ki unutulmasın): `AppleJwsVerifier` şu an yalnızca JWS imzasının mesajın kendi header'ındaki sertifikayla eşleştiğini doğruluyor, o sertifikanın **gerçekten Apple'a ait olduğunu** (Apple'ın Root CA'sına kadar zincir) doğrulamıyor. Gerçek bir Apple hesabı/anahtarı elinize geçtiğinde bu iş listeye alınmalı — bkz. `.claude/SECURITY.md` → Ödeme Güvenliği.

## Faz 4.2 — Google (Play Store In-App Purchase)

- [ ] **Google Play Console hesabı** (tek seferlik ücretli kayıt).
- [ ] **Play Console'da abonelik ürünü tanımlama** — ürün ID'leri, `Plans.PlayStoreProductId` alanına yazılacak (şu an `null`).
- [ ] **Play Developer API için service account oluşturma** (Google Cloud Console) + **`GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64`** — service account JSON dosyasının tamamını `base64 -i service-account.json` ile tek satıra çevirip yapıştır.
- [ ] **`GOOGLE_PLAY_PACKAGE_NAME`** — mobil uygulamanın Android package name'i (Faz 5.4'te netleşir).
- [ ] **Cloud Pub/Sub push subscription kurulumu** (Real-time Developer Notifications) + **`GOOGLE_PLAY_PUSH_AUDIENCE`** — webhook URL'iniz (`https://.../api/billing/webhooks/google`) belirlenince Pub/Sub tarafında push subscription'a bu URL tanımlanır, audience değeri de aynı URL olur. Şu an boşsa Google webhook'u her zaman `401` döner (güvenli varsayılan — hiç yapılandırılmamışken hiçbir isteği kabul etmiyor).
- [ ] Doğrulama: `.env`'i doldurup Billing servisini yeniden başlat, Play Console "License Testing" ile bir test satın alması yap, `POST /verify-purchase` ve gerçek bir RTDN webhook event'inin işlendiğini kontrol et.

## Genel / Prodüksiyon Öncesi (SECURITY.md)

Bunlar veri değil, "prodüksiyona geçmeden önce yapılmalı" işleri — ayrı bir faz olarak planlanmadı, burada tek yerde toplandı:

- [ ] Secret manager (Azure Key Vault / AWS Secrets Manager / Doppler) — şu an tüm secret'lar `.env` dosyasında.
- [ ] Servisler arası HTTP trafiği için mTLS veya network izolasyonu — şu an düz HTTP.
- [ ] Gateway'de rate limiting (`/auth/login`, `/auth/register` — brute-force önleme) — henüz eklenmedi.
- [ ] CORS politikası — şu an `AllowAll`, production'da bilinen frontend origin'lerine kısıtlanmalı.
- [ ] HTTPS zorunluluğu — production'da zorunlu hale getirilmeli.
- [ ] Refresh token'ların DB'de hash'lenmesi — şu an düz metin.
- [ ] Token reuse detection — henüz uygulanmadı.
- [ ] CI'a `dotnet list package --vulnerable` adımı eklenmesi.
- [ ] Kod TODO: `StockTracker.Identity/Services/AuthService.cs` — email zaten alınmışsa 409 Conflict yerine `null` dönüyor, düzeltilmedi.

## Legal / KVKK

- [ ] Açık rıza metni ve veri saklama politikası — henüz yazılmadı (kullanıcı email, konum, bildirim tercihi tutuluyor).
- [ ] Ticaret/veri hukuku avukatına danışma — özellikle ödeme (Faz 4) ve scraping (Faz 2) riskleri için öneriliyor, henüz yapılmadı.

## Faz 5 — Frontend (tasarım bekliyor, şimdilik atlandı)

- [ ] Figma tasarım dosyası/erişimi — Faz 5.2 için gerekecek.

## Faz 5.4 — Mobil Uygulama (yukarıdaki Apple/Google hesaplarına ek olarak)

- [ ] Gerçek cihaz test erişimi (iOS + Android).
- [ ] FCM push token kayıt akışının yazılması — yukarıdaki `FCM_SERVER_KEY`'i gerçek anlamlı kılan asıl iş.

## Faz 6.1 — Yeni Marka Onboarding (Zara, Pull&Bear)

- [x] Zara için regex/site-search keşfi + gerçek site erişimiyle canlı doğrulama — tamamlandı (bkz. `.claude/ARCHITECTURE.md` > Zara Scraper). Kod tarafında bekleyen bir girdi yok, ek `.env` değişkeni gerekmiyor.
- [x] **10 ürün × farklı beden × farklı il/ilçe canlı testi** — tamamlandı (kullanıcı talebiyle, gerçek Chrome tarayıcı oturumu üzerinden üretim mantığının birebir aynısı test edildi; bu ortamda gerçek Chrome kanalı kurulu olmadığı için derlenmiş servis değil, aynı endpoint/parsing mantığı doğrulandı). 3 gerçek bulgu koda yansıtıldı: `"low_on_stock"` durumu artık stokta sayılıyor, `productId` artık öncelikle PDP'nin `colors[].productId` alanından çözülüyor (URL `v1`'e daha az bağımlı), ve Akamai'nin hız-bazlı bloklamasının **kalıcı** olduğu (basit bir bekleme ile geçmediği) doğrulandı. Detay: `.claude/ARCHITECTURE.md` > Zara Scraper.
- [x] **Zara için derlenmiş üretim kodunun (`PlaywrightZaraFetcher`+`ZaraStockApiClient`) gerçek Chrome kanalıyla çalıştırılması** — bu makinede gerçek Chrome kurulu olduğu anlaşılınca (ilk kontrol Mac'te yanlış komutla yapılmıştı) tamamlandı: küçük bir harness (RabbitMQ hariç, gerçek Redis + gerçek Playwright) ile aynı 10 ürün tekrar test edildi. Online stok 10/10 doğru. Detay: `.claude/ARCHITECTURE.md` > Zara Scraper (üçüncü tur).
- [ ] **RabbitMQ üzerinden tam uçtan uca smoke-test** — hâlâ eksik olan tek adım: `dotnet run --project StockTracker.ZaraScraper` ile TAM servisi (consumer dahil) ayağa kaldırıp RabbitMQ'ya elle bir `CheckStockCommand` (V2, gerçek bir Zara `ProductUrl`'iyle) gönderip dönen `StockResultEvent`'in doğruluğu kontrol edilmeli — Bershka'nın 5 turluk sürecindeki son adım.
- [ ] **(DÜZELTİLMİŞ VE KESİNLEŞTİRİLMİŞ BULGU) Zara mağaza sorgusu bloklaması IP bazlı DEĞİL — otomasyon (Playwright/Selenium) tespitine dayalı; Faz 7 (proxy/IP rotasyonu) bunu ÇÖZMEZ**: kullanıcının doğrudan itirazı üzerine ("browser'dan sağlayabiliyorum, sen neden sağlayamıyorsun") yapılan ek testlerle netleşti:
  - Kullanıcının GERÇEK tarayıcı profiliyle (`claude-in-chrome`), aynı endpoint'e aynı anda atılan istek `200` başarılı oldu — Playwright'ın (kalıcı profil + organik gezinme simülasyonuyla bile) `403` aldığı TAM O SIRADA. Aynı IP, aynı an, farklı sonuç → IP bazlı bir blok değil.
  - Alternatif olarak Selenium+ChromeDriver (farklı bir otomasyon protokolü, CDP değil) denendi — O DA başarısız oldu (403, hatta PDP navigasyonu da tam veri dönmedi).
  - Sonuç: engelleme muhtemelen Akamai'nin herhangi bir tarayıcı-otomasyon aracını (ChromeDriver'ın enjekte ettiği `$cdc_` değişkenleri, headless Chrome'a özgü sinyaller, `navigator.webdriver` vb.) tespit etmesinden kaynaklanıyor — IP/ağ değil, otomasyonun KENDİSİ tespit ediliyor. **Bu nedenle Faz 7'deki proxy/IP rotasyonu bu sorunu ÇÖZMEYECEK.**
  - Daha derin çözümler (canvas fingerprint sahteciliği, "undetected-chromedriver" tarzı CDP/WebDriver izi gizleme yamaları) kasıtlı olarak denenmedi — bunlar `.claude/ARCHITECTURE.md`'de zaten bilinçli olarak kapsam dışı bırakılan "bot-tespitini aktif atlatma" sınırını aşar.
  - **Mevcut durum kabul edildi**: kod güvenli davranıyor (403 → `Unknown`, exception yok), online stok sorgusu (mağaza sorgusundan farklı olarak) sorunsuz çalışıyor. Zara'nın mağaza bazlı stok özelliği şu an için bilinen, kalıcı bir kısıtlama — gelecekte yalnızca tamamen farklı bir mimari (ör. gerçek bir kullanıcı tarayıcısında çalışan bir uzantı üzerinden veri toplama) bu özelliği etkinleştirebilir, ki bu ayrı ve büyük bir faz gerektirir.
- [ ] Pull&Bear için regex/site-search keşfi, gerçek site erişimiyle canlı doğrulama (Bershka/Zara'da yapıldığı gibi).

## Faz 7 — Proxy/IP Rotasyonu (bilinçli olarak ertelendi)

- [ ] Proxy sağlayıcı kararı (Bright Data / Oxylabs / Smartproxy vb. — ücretli). Karar verilene kadar bu faz pasif bekliyor.
