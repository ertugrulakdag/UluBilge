using Microsoft.EntityFrameworkCore; // OAuth deposu icin Entity Framework Core tiplerini kullanir.

namespace UluBilge.Api.Authentication.OAuth; // OAuth verilerini kimlik dogrulama altinda toplar.

public sealed class OAuthDbContext(DbContextOptions<OAuthDbContext> options) : DbContext(options); // OpenIddict tablolarini ayri ve kalici SQLite veritabaninda yonetir.
