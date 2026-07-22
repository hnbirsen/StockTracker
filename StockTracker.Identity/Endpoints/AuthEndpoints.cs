public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        // POST /auth/register
        group.MapPost("/register", async (RegisterRequest request, IAuthService authService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { Message = "Email and password are required." });

            if (request.Password.Length < 8)
                return Results.BadRequest(new { Message = "Password must be at least 8 characters long." });

            var response = await authService.RegisterAsync(request);
            if (response is null)
                return Results.BadRequest(new { Message = "Registration failed." });

            return Results.Ok(response);
        });

        // POST /auth/login
        group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
        {
            var response = await authService.LoginAsync(request);
            if (response is null)
                return Results.Unauthorized();

            return Results.Ok(response);
        });

        // POST /auth/refresh-token
        group.MapPost("/refresh-token", async (RefreshTokenRequest request, IAuthService authService) =>
        {
            var response = await authService.RefreshTokenAsync(request.RefreshToken);
            if (response is null)
                return Results.Unauthorized();

            return Results.Ok(response);
        });

        // POST /auth/logout
        group.MapPost("/logout", async (RefreshTokenRequest request, IAuthService authService) =>
        {
            await authService.RevokeRefreshTokenAsync(request.RefreshToken);
            return Results.Ok();
        });

        // GET /health
        app.MapGet("/health", () => Results.Ok());
    }
}