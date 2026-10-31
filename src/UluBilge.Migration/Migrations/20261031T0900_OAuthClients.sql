SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Hesaplanan kolondaki indeks için gerekli SQL Server oturum ayarları.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Dinamik OAuth istemci kayıtlarını API yeniden başlatıldığında da korur.
    IF OBJECT_ID(N'dbo.OAuthClients', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[OAuthClients]
        (
            [ClientId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL, -- İstemciye kayıt sırasında verilen kalıcı kimlik. Büyük/küçük harf ayrımı korunur.
            [DisplayName] nvarchar(128) NOT NULL, -- Bağlantı onay ekranında gösterilen istemci adı.
            [IsActive] bit NOT NULL CONSTRAINT [DF_OAuthClients_IsActive] DEFAULT (1), -- İstemci kaydını silmeden yeni yetkilendirme ve token işlemlerine kapatmak için kullanılır. Varsayılan 1.
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OAuthClients_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()), -- İstemci kaydının oluşturulma zamanı (UTC).

            CONSTRAINT [PK_OAuthClients] PRIMARY KEY CLUSTERED ([ClientId])
        );
    END;

    -- Bir istemcinin birden fazla izin verilen OAuth dönüş adresi olabilir.
    IF OBJECT_ID(N'dbo.OAuthClientRedirectUris', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[OAuthClientRedirectUris]
        (
            [ClientId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL, -- Dönüş adresinin ait olduğu OAuthClients.ClientId kaydına foreign key.
            [RedirectUri] nvarchar(2048) COLLATE Latin1_General_100_BIN2 NOT NULL, -- İstemci için kayıtlı tam dönüş adresi. API'nin 2048 karakter sınırıyla uyumludur; büyük/küçük harf ayrımı korunur.
            [RedirectUriHash] AS CONVERT(binary(32), HASHBYTES('SHA2_256', [RedirectUri])) PERSISTED NOT NULL, -- SQL tarafından hesaplanan SHA-256 özeti; uzun URI'leri indeks sınırını aşmadan istemci bazında benzersiz tutar.

            CONSTRAINT [PK_OAuthClientRedirectUris] PRIMARY KEY CLUSTERED ([ClientId], [RedirectUriHash]),
            CONSTRAINT [FK_OAuthClientRedirectUris_OAuthClients] FOREIGN KEY ([ClientId]) REFERENCES [dbo].[OAuthClients] ([ClientId])
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
