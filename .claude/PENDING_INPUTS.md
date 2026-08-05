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

## Faz 6.1 — Yeni Marka Onboarding (Zara, Mango, H&M, Massimo Dutti, Pull&Bear)

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
- [x] **Mango için regex/site-search keşfi + gerçek site erişimiyle uçtan uca canlı doğrulama** — tamamlandı (bkz. `.claude/ARCHITECTURE.md` > Mango Scraper). Zara'nın aksine Mango'da bot koruması yok, bu yüzden derlenmiş üretim kodu (`MangoPdpFetcher`+`MangoStockApiClient`) bu ortamda doğrudan gerçek shop.mango.com'a karşı çalıştırılıp doğrulanabildi (RabbitMQ katmanı hariç) — online stok 4/4, mağaza sorgusu 2/2 doğru gerçek sonuç. Kod tarafında bekleyen bir girdi yok, ek `.env` değişkeni gerekmiyor.
- [ ] **Mango için RabbitMQ üzerinden tam uçtan uca smoke-test** — hâlâ eksik olan tek adım: `dotnet run --project StockTracker.MangoScraper` ile TAM servisi (consumer dahil) ayağa kaldırıp RabbitMQ'ya elle bir `CheckStockCommand` (V2, gerçek bir Mango `ProductUrl`'iyle) gönderip dönen `StockResultEvent`'in doğruluğu kontrol edilmeli.
- [ ] **Mango ürün kodu formatı — fiziksel etiket doğrulaması bekliyor**: `^\d{8}/\d{2}$` deseni gerçek API verisiyle (temel 8 haneli referans + `colors[].id`) doğrulandı ama fiziksel üründeki TAM görünen format (ayraç dahil mi, hangi ayraç) henüz gerçek bir Mango ürün etiketiyle çapraz doğrulanmadı — Medium confidence bu yüzden. Gerçek bir mağazadan/kullanıcıdan bir ürün etiketi fotoğrafı/verisi gelirse High'a çıkarılabilir.
- [x] **H&M için regex/site-search keşfi + kod tamamlandı** — tamamlandı (bkz. `.claude/ARCHITECTURE.md` > H&M Scraper). Online stok mekanizması (`__NEXT_DATA__` içindeki `ssrAvailability`) ve mağaza bazlı mekanizma (`/tr_tr/sis/tr/{productId}/{artId}`, enlem/boylam sorgusu, trafik ışığı G/Y/R) canlı trafikle doğrulandı. Zara/Mango'dan FARKLI olarak H&M'in mağaza yanıtı sparse DEĞİL (yakındaki TÜM mağazaları, stoksuz olanlar dahil, döndürüyor) — bu yüzden hedef mağaza yanıtta yoksa bu durum `OutOfStock` değil `Unknown` (sorgu sorunu) olarak ele alınıyor; bu, koda yansıtılmış bilinçli bir mimari karar. `avaiQty` alanı sadece `0/1000/2000/3000` gibi kova (bucket) değerleri döndürdüğü için gerçek bir sayı OLMADIĞI teyit edildi ve bu yüzden paylaşılan `Quantity` alanına kasıtlı olarak hiç yazılmıyor (kullanıcıyı yanıltmamak için). 21 unit test yazıldı, tümü `IHmPdpFetcher` mock'lanarak geçiyor (gerçek Chrome bağımlılığı yok). Kod tarafında bekleyen bir girdi yok, ek `.env` değişkeni gerekmiyor.
- [ ] **(KRİTİK, ÇÖZÜLMEMİŞ BULGU) H&M'de PDP fetch'in KENDİSİ Playwright ile bloklanıyor — Zara'daki bulgunun aynısı ama DAHA AĞIR**: derlenmiş üretim kodu (`PlaywrightHmFetcher`+`HmStockApiClient`) gerçek Chrome kanalıyla `https://www2.hm.com/tr_tr/productpage.1367091002.html` adresine karşı çalıştırıldığında PDP navigasyonunun kendisi `403` döndü (`__NEXT_DATA__ bulunamadı`) — hem online hem mağaza kontrolü etkilendi (mağaza kontrolü zaten önce PDP'den beden çözümlemesi gerektiriyor). Bu, iki kez tekrarlandı (kalıcı, geçici değil). AYNI ANDA, aynı URL'ye kullanıcının GERÇEK tarayıcı oturumuyla (`claude-in-chrome`, otomasyon bayrağı yok) erişim sorunsuz çalıştı ve sayfa normal göründü.
  - Zara'da yukarıda (76-81. satırlar) zaten ayrıntılı olarak karakterize edilen kök nedenin (IP bazlı DEĞİL, otomasyon/CDP tespiti — `navigator.webdriver`, ChromeDriver'ın `$cdc_` değişkenleri vb.) aynısının burada da geçerli olduğu değerlendirildi; bu yüzden Selenium/Firefox/WebKit ile tam tekrar araştırma bilinçli olarak YAPILMADI (kök neden zaten anlaşıldığı için gereksiz tekrar olurdu).
  - Fark: Zara'da sadece mağaza sorgusu bloklanıyordu (online kontrol çalışıyordu), H&M'de PDP fetch'in kendisi (dolayısıyla hem online HEM mağaza kontrolü) bloklanıyor — Akamai'nin H&M tarafında daha agresif yapılandırıldığı anlamına geliyor.
  - **Mevcut durum kabul edildi**: kod güvenli davranıyor (403 → `Unknown`, exception yok). H&M'in canlı ortamda çalışabilmesi için Zara'nın mağaza-sorgusu kısıtlamasıyla aynı köklü çözümü (ör. gerçek kullanıcı tarayıcısında çalışan bir uzantı mimarisi) bekliyor — ayrı, büyük bir faz. Kullanıcıya bu bulgunun Zara'nın ertelenen otomasyon-tespiti araştırma hattına mı dahil edileceği, yoksa ayrı mı ele alınacağı henüz sorulmadı.
- [x] **Massimo Dutti için regex/site-search keşfi + gerçek site erişimiyle uçtan uca canlı doğrulama** — tamamlandı (bkz. `.claude/ARCHITECTURE.md` > Massimo Dutti Scraper). Zara ile aynı grup (Inditex) ama hibrit bir mimari: ürün sayfası VE `itxrest/2/catalog/.../detail` API'si Akamai korumalı (curl'de sırasıyla `bm-verify` JS-yönlendirme sayfası ve `403 Service Unavailable`), ama gerçek mağaza stok API'si (`api/storefront/1/stores/.../products/.../available-sizes`) AYNI domain'de olmasına rağmen korumasız (curl ile 200 + gerçek, sayısal stok verisi). Bu yüzden derlenmiş üretim kodunun mağaza-stok tarafı (`MassimoDuttiStockApiClient.CheckStoreStockAsync`) bu ortamda doğrudan gerçek massimodutti.com'a karşı çalıştırılıp doğrulanabildi (gerçek `stock` değerleriyle); online stok tarafı ise (Playwright gerektirdiği için) yalnızca canlı tarayıcı trafiğiyle/keşifle doğrulandı, derlenmiş servis olarak henüz gerçek Chrome kanalıyla test edilmedi (Zara/H&M'inkiyle aynı kısıtlama, aşağıya bkz.). Kod tarafında bekleyen bir girdi yok, ek `.env` değişkeni gerekmiyor.
- [x] **(DÜZELTİLMİŞ BULGU) Massimo Dutti mağaza bazlı stok sorgusu GERÇEKTEN ÇALIŞIYOR — ilk keşifte yanlış endpoint test edilmişti**: ilk sürümde mağaza sorgusu için genel mağaza BULUCU API'si (`itxrest/2/bam/store/.../physical-store`, enlem/boylam gerektirir, yalnızca il/ilçe bazlı mağaza listesi döner) kullanılmış ve bu API'nin döndürdüğü, mağaza stoğuyla hiç ilgisi olmayan `receiveStockQuery` bayrağının Türkiye'de her zaman `false` olması "stok sorgusu bu ülke için desteklenmiyor" şeklinde YANLIŞ yorumlanmıştı. Kullanıcı kendi tarayıcısında gerçek bir ürün sayfasında ("MAĞAZA STOK DURUMU" butonu → beden seçimi) her mağaza için gerçek "BEDEN: XX" bilgisi gördüğünü bildirince yeniden araştırıldı: gerçek stok verisi tamamen AYRI bir endpoint'ten geliyor — `api/storefront/1/stores/{storeId}/products/{catEntryId}/available-sizes?physicalStoreIds=...&sizeIds=...` — bu, Zara'daki gibi doğrudan mağaza ID'siyle çalışıyor (enlem/boylam gerekmiyor), sparse bir yanıt veriyor (mağaza dizide yoksa o bedenden yok demek, canlı doğrulandı — Cevahir/Şişli örneği) ve GERÇEK sayısal stok adedi (`stock`, ör. `1`) döndürüyor. Kod (`MassimoDuttiStockApiClient`) bu doğru endpoint'i kullanacak şekilde tamamen yeniden yazıldı; `IMassimoDuttiStockApiClient.CheckStoreStockAsync` artık enlem/boylam yerine `productUrl` alıyor (PDP'den `catEntryId`/`mastersSizeId` çözmek için). 21 unit test (2 yeni: gerçek miktar/son-ürün senaryoları) + mağaza sorgusu curl ile canlı doğrulandı.
- [ ] **Massimo Dutti için derlenmiş üretim kodunun (`PlaywrightMassimoDuttiFetcher`) gerçek Chrome kanalıyla online stok tarafında çalıştırılması** — Zara/H&M'dekiyle aynı kısıtlama: bu ortamda gerçek Chrome kanalı kurulu olmadığı/canlı bir smoke-test yapılmadığı için, `#mdfrontw-state` okuma mantığı yalnızca canlı tarayıcı (`claude-in-chrome`) trafiğiyle keşfedilip doğrulandı, derlenmiş `PlaywrightMassimoDuttiFetcher` üzerinden henüz test edilmedi. Mağaza stok tarafı (Akamai korumasız olduğu için) zaten derlenmiş kodla doğrudan doğrulandı (yukarıya bkz.).
- [ ] **Massimo Dutti için RabbitMQ üzerinden tam uçtan uca smoke-test** — hâlâ eksik olan adım: `dotnet run --project StockTracker.MassimoDuttiScraper` ile TAM servisi (consumer dahil) ayağa kaldırıp RabbitMQ'ya elle bir `CheckStockCommand` (V2, gerçek bir Massimo Dutti `ProductUrl`'iyle) gönderip dönen `StockResultEvent`'in doğruluğu kontrol edilmeli.
- [ ] **Massimo Dutti ürün kodu formatı — fiziksel etiket doğrulaması bekliyor**: `^\d{8}/\d{3}$` deseni gerçek SSR verisiyle (temel 8 haneli referans + 3 haneli `colors[].id`) doğrulandı ama fiziksel üründeki TAM görünen format (ayraç dahil mi, hangi ayraç) henüz gerçek bir Massimo Dutti ürün etiketiyle çapraz doğrulanmadı — Medium confidence bu yüzden. Gerçek bir mağazadan/kullanıcıdan bir ürün etiketi fotoğrafı/verisi gelirse High'a çıkarılabilir.
- [ ] Pull&Bear için regex/site-search keşfi, gerçek site erişimiyle canlı doğrulama (Bershka/Zara/Mango'da yapıldığı gibi).

## Faz 7 — Proxy/IP Rotasyonu (bilinçli olarak ertelendi)

- [ ] Proxy sağlayıcı kararı (Bright Data / Oxylabs / Smartproxy vb. — ücretli). Karar verilene kadar bu faz pasif bekliyor.
