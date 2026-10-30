namespace UluBilge.McpServer.Contracts;

internal sealed record LoginRequest(string UserName, string Password);
internal sealed record RefreshRequest(string RefreshToken);

internal sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset ExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
