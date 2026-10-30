using System.Globalization; // Token claimlerini kulturden bagimsiz bicimlendirir.
using System.Security.Claims; // Dogrulanmis kullanici claimlerini okur.
using System.Security.Cryptography; // Bilinmeyen kullanicilar icin de parola kontrolu yapilmasini saglar.
using Microsoft.AspNetCore.Identity; // Mevcut guvenli parola hash bicimini kullanir.
using Microsoft.Extensions.Options; // Parola hash maliyetini ayarlar.
using Microsoft.IdentityModel.JsonWebTokens; // JWT erisim tokenini olusturur.
using Microsoft.IdentityModel.Tokens; // Tokeni gizli anahtarla imzalar.
using UluBilge.Core.Entities; // Mevcut kullanici modelini kullanir.
using UluBilge.Core.Enums; // Kullanici rollerini kullanir.
using UluBilge.Data.Abstract; // Mevcut repository ve is birimini kullanir.

namespace UluBilge.Api.Authentication; // Giris ve token islemlerini tek yerde toplar.

public sealed class AuthService(IUserRepository users, IUnitOfWork unitOfWork, JwtOptions settings, SigningCredentials signingCredentials) // Parola dogrulama ve JWT uretimini yonetir.
{ // Kimlik servisini baslatir.
    public const string RoleClaimType = "role"; // Standart rol claim adini belirler.
    private static readonly PasswordHasher<User> Passwords = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 })); // Mevcut hashlerle uyumlu parola dogrulayicisini hazirlar.
    private static readonly User DummyUser = new(); // Bilinmeyen kullanici icin de hash kontrolu yapilmasini saglar.
    private static readonly string DummyHash = Passwords.HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))); // Bilinmeyen kullanicinin zaman farkiyla ayirt edilmesini azaltir.
    private static readonly RoleEnum KnownRoles = Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role); // Tanimli rol bitlerini birlestirir.

    public Task<TokenResponse?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default) => // Dogrulanan kullaniciya kisa omurlu token verir.
        unitOfWork.ExecuteCommandAsync<TokenResponse?>(async ct => // Giris sayaclarini ayni veritabani isleminde gunceller.
        { // Giris islemini baslatir.
            var user = await users.GetByNormalizedUserNameForUpdateAsync(userName.Trim().ToUpperInvariant(), ct); // Kullanici satirini kilitleyerek okur.
            var verification = VerifyPassword(user, password); // Parola hashini guvenli bicimde dogrular.
            var now = DateTime.UtcNow; // Hesap kilidi ve token icin UTC zamanini alir.
            if (user is null || !user.IsActive || user.LockoutEndUtc > now || (user.RoleEnum & ~KnownRoles) != 0) return null; // Kapali, kilitli veya gecersiz hesabi reddeder.
            if (verification == PasswordVerificationResult.Failed) // Hatali parola denemesini ele alir.
            { // Hesap kilidi denetimini baslatir.
                var failedCount = user.LockoutEndUtc.HasValue ? 1 : Math.Min(user.AccessFailedCount, 4) + 1; // Suresi biten kilitten sonra sayaci yeniden baslatir.
                await users.RecordFailedLoginAsync(user.Id, failedCount, failedCount >= 5 ? now.AddMinutes(15) : null, now, ct); // Bes hatali denemede hesabi on bes dakika kilitler.
                return null; // Hatali parola icin token vermez.
            } // Hesap kilidi denetimini tamamlar.
            if (verification == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Passwords.HashPassword(user, password); // Gerekirse eski parola hashini gunceller.
            await users.RecordSuccessfulLoginAsync(user, now, ct); // Girisi kaydedip hata sayacini sifirlar.
            return CreateToken(user, now); // Oturum veya refresh token kaydi olusturmadan JWT uretir.
        }, cancellationToken); // Istek iptalini veritabanina iletir.

    private TokenResponse CreateToken(User user, DateTime now) // Dogrulanmis kullanicinin JWT tokenini uretir.
    { // Token uretimini baslatir.
        var expiresAt = now.AddMinutes(settings.AccessTokenLifetimeMinutes); // Tokenin bitis zamanini hesaplar.
        var roles = Enum.GetValues<RoleEnum>().Where(role => role != RoleEnum.None && user.RoleEnum.HasFlag(role)).Select(role => role.ToString()).ToArray(); // Rol maskesini standart rol adlarina cevirir.
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor // Imzali JWT alanlarini hazirlar.
        { // JWT alanlarini baslatir.
            Issuer = settings.Issuer, // Tokeni veren uygulamayi belirtir.
            Audience = settings.Audience, // Tokenin hedef API'sini belirtir.
            IssuedAt = now, NotBefore = now, Expires = expiresAt, // Tokenin gecerlilik zamanlarini belirtir.
            SigningCredentials = signingCredentials, // Tokeni sunucunun gizli anahtariyla imzalar.
            Claims = new Dictionary<string, object> // Yalnizca gerekli kullanici bilgilerini tasir.
            { // Kullanici claimlerini baslatir.
                ["sub"] = user.Id.ToString(CultureInfo.InvariantCulture), // Kullanici numarasini tasir.
                ["name"] = user.UserName, // Kullanici adini tasir.
                [RoleClaimType] = roles // Standart yetkilendirme icin rol adlarini tasir.
            } // Kullanici claimlerini tamamlar.
        }); // Imzali token metnini olusturur.
        return new TokenResponse(token, "Bearer", settings.AccessTokenLifetimeMinutes * 60, new DateTimeOffset(expiresAt, TimeSpan.Zero)); // Mevcut access token yanit alanlarini korur.
    } // Token uretimini tamamlar.

    public static AuthenticatedUser? GetUser(ClaimsPrincipal principal) // Kullanici bilgisini dogrulanmis claimlerden okur.
    { // Kullanici bilgisini okumayi baslatir.
        if (!int.TryParse(principal.FindFirstValue("sub"), out var id) || id <= 0 || string.IsNullOrWhiteSpace(principal.Identity?.Name)) return null; // Eksik kimlik bilgisini reddeder.
        var roles = principal.FindAll(RoleClaimType).Aggregate(RoleEnum.None, (mask, claim) => Enum.TryParse<RoleEnum>(claim.Value, out var role) ? mask | role : mask); // Rol adlarini mevcut sayisal yanit bicimine cevirir.
        return new AuthenticatedUser(id, principal.Identity.Name, roles); // Mevcut me yanitini korur.
    } // Kullanici bilgisini okumayi tamamlar.

    private static PasswordVerificationResult VerifyPassword(User? user, string password) // Bozuk parola hashlerini guvenli bicimde reddeder.
    { // Parola dogrulamasini baslatir.
        try { return Passwords.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, password); } // Identity parola hasher'ini kullanir.
        catch (FormatException) { return PasswordVerificationResult.Failed; } // Bozuk hash icin basarisiz giris dondurur.
    } // Parola dogrulamasini tamamlar.
} // Kimlik servisini tamamlar.

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAtUtc); // Yalnizca erisim tokeni bilgilerini tasir.
public sealed record AuthenticatedUser(int Id, string UserName, RoleEnum RoleEnum); // Kullanici yanitini ek servis katmani olmadan tanimlar.
