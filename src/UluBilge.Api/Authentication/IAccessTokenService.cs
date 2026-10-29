namespace UluBilge.Api.Authentication
{
    public interface IAccessTokenService
    {
        AccessTokenResponse Create(AuthenticatedUser user);
    }

    public sealed record AccessTokenResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAtUtc);
}
