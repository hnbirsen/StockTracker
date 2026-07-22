public record RegisterRequest 
(
    string Email,
    string Password,
    string? FirstName,
    string? LastName
);

public record LoginRequest(
    string Email,
    string Password
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    UserDto User
);

public record UserDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool IsEmailVerified
);