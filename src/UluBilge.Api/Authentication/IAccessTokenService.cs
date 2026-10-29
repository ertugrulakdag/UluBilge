namespace UluBilge.Api.Authentication
{
    public interface IAccessTokenService
    {
        AccessTokenResponse Create(AuthenticatedUser user, Guid securityStamp, Guid familyId);
    }

    public sealed record AccessTokenResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAtUtc);
}
