# API Konvansiyonları

Tüm servislerde tutarlılık sağlamak için ortak kurallar.

## URL Yapısı

- Gateway üzerinden: `https://api.stocktracker.com/api/{servis}/{kaynak}`
- Versiyonlama: `/api/v1/{servis}/{kaynak}` (v1 ile başla, ileride breaking change olursa v2)

## HTTP Metodları

| Metod | Kullanım |
|---|---|
| GET | Veri okuma, yan etkisiz |
| POST | Yeni kayıt oluşturma, tetikleyici aksiyonlar (login, refresh) |
| PUT | Tam güncelleme |
| PATCH | Kısmi güncelleme |
| DELETE | Silme |

## Standart Response Zarfı

Başarılı yanıt:
```json
{
  "success": true,
  "data": { },
  "meta": { "requestId": "..." }
}
```

Hata yanıtı:
```json
{
  "success": false,
  "error": {
    "code": "PRODUCT_NOT_FOUND",
    "message": "Ürün bulunamadı",
    "details": null
  },
  "meta": { "requestId": "..." }
}
```

## HTTP Durum Kodları

- `200` — başarılı okuma/güncelleme
- `201` — başarılı oluşturma
- `204` — başarılı, içerik yok (ör. delete)
- `400` — validasyon hatası
- `401` — kimlik doğrulama başarısız/eksik
- `403` — yetkisiz erişim
- `404` — kaynak bulunamadı
- `409` — çakışma (ör. e-posta zaten kayıtlı)
- `429` — rate limit aşıldı
- `500` — beklenmeyen sunucu hatası

## Kimlik Doğrulama

- Header: `Authorization: Bearer <access_token>`
- Access token ömrü: kısa (ör. 15 dk)
- Refresh token: rotation'lı, her kullanımda invalidate edilip yenisi verilir
- Refresh endpoint'i: `POST /api/identity/refresh` — body'de eski refresh token

## Sayfalama

Query parametreleri: `?page=1&pageSize=20`

```json
{
  "data": [ ],
  "pagination": { "page": 1, "pageSize": 20, "totalItems": 143, "totalPages": 8 }
}
```

## Filtreleme (Ürün/Stok Arama)

```
GET /api/products/search?barcode=123456789012
GET /api/stock/availability?productId=...&size=M&city=Istanbul&district=Kadikoy
```

## Idempotency

Ödeme ve bildirim gibi kritik POST işlemlerinde `Idempotency-Key` header'ı zorunlu tutulur (çift işlem önleme).

## Hata Kod Öneki Kuralı

Her servis kendi hata kodlarını üretir, servis adıyla prefix'lenir:
- `IDENTITY_INVALID_CREDENTIALS`
- `PRODUCT_NOT_FOUND`
- `SUBSCRIPTION_PAYMENT_FAILED`

## Loglama / İzlenebilirlik

- Her request'e `X-Request-Id` atanır, Gateway seviyesinde üretilir, tüm servislere propagate edilir
- Structured logging (JSON) tercih edilir, ileride merkezi log toplama (ör. Seq/ELK) eklenebilir
