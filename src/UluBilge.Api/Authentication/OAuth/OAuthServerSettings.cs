namespace UluBilge.Api.Authentication.OAuth; // OAuth yapilandirmasini ayri bir ad alaninda toplar.

public sealed record OAuthServerSettings(Uri Issuer, string DatabasePath, bool AllowDynamicClientRegistration) // Dogrulanmis OAuth ayarlarini servislerle paylasir.
{ // OAuth ayar modelini baslatir.
    public const string Scope = "mcp:tools"; // MCP araclarina erisim icin kullanilan izni tanimlar.
    public const string BearerScheme = "UluBilge.McpBearer"; // Eski JWT ile OAuth token dogrulamasini secen semayi adlandirir.
    public Uri Resource => new(Issuer, "mcp"); // Tokenin kullanilabilecegi MCP kaynaginin sabit adresini verir.
} // OAuth ayar modelini tamamlar.
