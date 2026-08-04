# Bekleyen Girdiler / Kararlar

Bu dosya, geliştirme sırasında "gerçek veri/hesap yok, kod placeholder ile hazır" diye bilinçli olarak ertelenen ve **kullanıcının doldurması/karar vermesi gereken** tüm noktaların tek listesi. Her madde işaretlendiğinde (`[x]`) ilgili `.env` değeri gerçek değerle güncellenir ve gerekirse kısa bir doğrulama yapılır — kod tarafında ek değişiklik gerekmez (hepsi zaten gerçek API'lere göre yazıldı).

Frontend süreci (Faz 5) tasarım bekliyor; bu liste o zamana kadar backend'in "gerçek dünyaya bağlanma" borcunu takip etmek için var.

## Nasıl kullanılır

1. Bir madde için gerçek değeri elde edince `.env` dosyasındaki ilgili `REPLACE_WITH_ENV` placeholder'ını gerçek değerle değiştir.
2. Bu dosyada ilgili checkbox'ı işaretle.
3. "Doğrulama" sütununda önerilen hızlı testi çalıştırıp gerçekten çalıştığını teyit et (opsiyonel ama önerilir).

---

## Faz 3.3 — Notification Service

- [ ] **SendGrid hesabı + `SENDGRID_API_KEY`** — email bildirimleri için. Şu an: anahtar yoksa `SendGridEmailSender` gerçek istek atmadan `false` döner, `NotificationLog.Success=false` yazar, servis çökmez.
  - Doğrulama: `.env`'e gerçek anahtarı gir, Notification servisini yeniden başlat, bir restock event'i tetikle (bkz. `.claude/ARCHITECTURE.md` → Notification Service, uçtan uca doğrulama bölümündeki curl/RabbitMQ komutları), gerçek bir email'in ulaştığını kontrol et.
- [ ] **`NOTIFICATION_FROM_EMAIL`** — şu an `notifications@stocktracker.local` (gerçek bir domain değil). SendGrid genelde doğrulanmış bir gönderici domaini ister — kendi domaininizle değiştirin.
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

- [ ] Her marka için regex/site-search keşfi, gerçek site erişimiyle canlı doğrulama (Bershka'da yapıldığı gibi).

## Faz 7 — Proxy/IP Rotasyonu (bilinçli olarak ertelendi)

- [ ] Proxy sağlayıcı kararı (Bright Data / Oxylabs / Smartproxy vb. — ücretli). Karar verilene kadar bu faz pasif bekliyor.
