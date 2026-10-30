namespace UluBilge.App.Authentication
{
    // Yalnizca sunucu tarafindaki OAuth endpoint'leri tarafindan olusturulur.
    public sealed record OAuthTokenContext(string Issuer, string Resource, string ClientId, string? Scope);
}
