using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace StockTracker.Billing.Tests;

// Apple'ın kendi-imzalı (x5c-in-header) JWS formatını taklit eden test yardımcı sınıfı — gerçek Apple
// anahtarı olmadığından kendi self-signed sertifikamızla imzalıyoruz. AppleJwsVerifier yalnızca "imza,
// header'daki sertifikayla eşleşiyor mu" diye baktığı için (kasıtlı kapsam sınırlaması, bkz.
// AppleJwsVerifier üstündeki not) bu, gerçek doğrulama mantığını tam olarak test edebiliyor.
public static class TestJwsBuilder
{
    public static string CreateSignedJws(object payload, out X509Certificate2 certificate)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certRequest = new CertificateRequest("CN=Test Apple JWS", ecdsa, HashAlgorithmName.SHA256);
        var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        certificate = cert;

        var headerJson = JsonSerializer.Serialize(new { alg = "ES256", x5c = new[] { Convert.ToBase64String(cert.RawData) } });
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        using var privateKey = cert.GetECDsaPrivateKey()!;
        var signature = privateKey.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = Base64UrlEncode(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
