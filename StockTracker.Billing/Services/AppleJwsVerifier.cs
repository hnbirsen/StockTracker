using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace StockTracker.Billing.Services;

public interface IAppleJwsVerifier
{
    bool TryVerifyAndDecode(string jws, out JsonElement payload);
}

// Apple App Store Server Notifications V2 ve App Store Server API yanıtları, imzayı KENDİ header'ında
// taşıyan bir JWS kullanır (JWT header'ındaki "x5c" alanında sertifika zinciri gömülü) — standart bir
// OAuth/OIDC token gibi önceden bilinen bir signing key ile değil, mesajın kendi içindeki sertifikayla
// doğrulanır. Bu yüzden System.IdentityModel.Tokens.Jwt'nin standart "known issuer signing key" akışı
// yerine burada manuel JWS ayrıştırma/doğrulama yapılıyor.
//
// ⚠️ BİLİNÇLİ, DOKÜMANTE EDİLMİŞ KAPSAM SINIRLAMASI (bkz. .claude/ARCHITECTURE.md > Billing):
// Bu implementasyon yalnızca imzanın JWS içindeki lider (x5c[0]) sertifikayla eşleştiğini doğrular —
// yani mesaj, o sertifikanın özel anahtarıyla imzalanmış ve içerik değiştirilmemiş. AMA o sertifikanın
// gerçekten Apple'a ait olup olmadığını (zincirin Apple'ın gerçek Root CA'sına kadar güvenilir olduğunu)
// DOĞRULAMIYOR. Prodüksiyona geçmeden önce zorunlu bir sertleştirme adımı: Apple'ın resmi Root CA
// sertifikasını (App Store Server Library'nin kullandığı "AppleRootCA-G3") güvenilir depoya ekleyip
// X509Chain ile x5c zincirini o köke kadar doğrulamak. Bu adım, gerçek bir Apple ortamına/anahtarına
// erişim olmadan (bu oturumda yok) uydurma bir sertifika baytı gömmek riskli olacağından bilinçli olarak
// bu fazın dışında bırakıldı — bkz. .claude/SECURITY.md.
public class AppleJwsVerifier : IAppleJwsVerifier
{
    private readonly ILogger<AppleJwsVerifier> _logger;

    public AppleJwsVerifier(ILogger<AppleJwsVerifier> logger)
    {
        _logger = logger;
    }

    public bool TryVerifyAndDecode(string jws, out JsonElement payload)
    {
        payload = default;

        var parts = jws.Split('.');
        if (parts.Length != 3)
        {
            _logger.LogWarning("Geçersiz JWS formatı — 3 segment bekleniyordu, {Count} bulundu.", parts.Length);
            return false;
        }

        try
        {
            var headerJson = Base64UrlDecode(parts[0]);
            using var headerDoc = JsonDocument.Parse(headerJson);
            var header = headerDoc.RootElement;

            if (!header.TryGetProperty("x5c", out var x5cArray) || x5cArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("JWS header'ında x5c (sertifika zinciri) bulunamadı.");
                return false;
            }

            var leafCertBytes = Convert.FromBase64String(x5cArray[0].GetString()!);
            using var leafCert = X509CertificateLoader.LoadCertificate(leafCertBytes);
            using var publicKey = leafCert.GetECDsaPublicKey();

            if (publicKey is null)
            {
                _logger.LogWarning("JWS lider sertifikasından EC public key çözülemedi.");
                return false;
            }

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64UrlDecodeBytes(parts[2]);

            // ES256 — JWS imzası IEEE P1363 (r||s concatenation) formatında, .NET'in varsayılan
            // VerifyData davranışıyla birebir uyumlu (ASN.1 DER'e çevirmeye gerek yok).
            var isValid = publicKey.VerifyData(signingInput, signature, HashAlgorithmName.SHA256);
            if (!isValid)
            {
                _logger.LogWarning("JWS imza doğrulaması başarısız.");
                return false;
            }

            var payloadJson = Base64UrlDecode(parts[1]);
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            payload = payloadDoc.RootElement.Clone();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWS ayrıştırma/doğrulama sırasında hata.");
            return false;
        }
    }

    private static string Base64UrlDecode(string input) => Encoding.UTF8.GetString(Base64UrlDecodeBytes(input));

    private static byte[] Base64UrlDecodeBytes(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
