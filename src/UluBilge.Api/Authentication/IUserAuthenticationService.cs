namespace UluBilge.Api.Authentication
{
    public interface IUserAuthenticationService
    {
        Task<TokenResponse?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);
        Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
        Task LogoutAsync(UserSession session, CancellationToken cancellationToken = default);
        Task<bool> IsSessionValidAsync(UserSession session, CancellationToken cancellationToken = default);
    }

    public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc);
}
