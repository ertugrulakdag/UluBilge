using Microsoft.EntityFrameworkCore; // OAuth veritabanini hazirlama islemlerini kullanir.

namespace UluBilge.Api.Authentication.OAuth; // OAuth baslangic islemlerini ayri tutar.

public sealed class OAuthDatabaseInitializer(IServiceProvider services, OAuthServerSettings settings) : IHostedService // Sunucu baslamadan OAuth deposunu hazirlar.
{ // Baslangic servisini baslatir.
    public async Task StartAsync(CancellationToken cancellationToken) // Uygulama acilirken kalici depoyu olusturur.
    { // Depo hazirligini baslatir.
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!); // Veritabani dosyasinin klasorunu hazirlar.
        await using var scope = services.CreateAsyncScope(); // Baslangic icin bagimsiz bir servis kapsami acar.
        var database = scope.ServiceProvider.GetRequiredService<OAuthDbContext>(); // OAuth veritabani baglamini alir.
        await database.Database.EnsureCreatedAsync(cancellationToken); // Eksik OAuth tablolarini yeni ve ayri depoda olusturur.
    } // Depo hazirligini tamamlar.

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask; // Kapatmada ek bir islem gerekmedigini belirtir.
} // Baslangic servisini tamamlar.
