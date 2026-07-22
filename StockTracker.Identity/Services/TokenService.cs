using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    TokenService.JwtSettings GetJwtSettings();
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private JwtSettings _jwtSettings;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        GetJwtSettings();
    }

    public string GenerateAccessToken(User user)
    {
        var secretKey = _jwtSettings.SecretKey ?? throw new InvalidOperationException("JWT secret key is not configured.");
        var issuer = _jwtSettings.Issuer ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var audience = _jwtSettings.Audience ?? throw new InvalidOperationException("JWT audience is not configured.");
        var expiryMinutes = double.Parse(_jwtSettings.AccessTokenExpiryMinutes ?? throw new InvalidOperationException("JWT access token expiry is not configured."));

        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public record JwtSettings
    (
        string SecretKey,
        string Issuer,
        string Audience,
        string AccessTokenExpiryMinutes,
        string RefreshTokenExpiryDays
    );

    private JwtSettings BindJwtSettings()
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        _jwtSettings = jwtSettings.Get<JwtSettings>() ?? throw new InvalidOperationException("JWT settings are not configured.");
        return _jwtSettings;
    }

    public JwtSettings GetJwtSettings()
    {
        if (_jwtSettings == null)
            return BindJwtSettings();

        return _jwtSettings;
    }
}