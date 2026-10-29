SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE [dbo].[Users]
    (
        [Id] int IDENTITY(1, 1) NOT NULL, -- Kullanıcının kalıcı kimliği; JWT içindeki sub claim'ine yazılır.
        [UserName] nvarchar(64) NOT NULL, -- Girişte kullanılan kullanıcı adı; mevcut API sınırıyla uyumlu.
        [NormalizedUserName] nvarchar(64) NOT NULL, -- Tutarlı arama ve kullanıcı adı benzersizliği için normalize edilmiş değer. Üzerinde unique index bulunur.
        [PasswordHash] nvarchar(512) NOT NULL, -- Parolanın güvenli hash'i.
        [Email] nvarchar(512) NOT NULL, -- Kullanıcının e-posta adresi.
        [EmailConfirmed] bit NOT NULL CONSTRAINT [DF_Users_EmailConfirmed] DEFAULT (0), -- E-posta adresinin doğrulanma durumu. Varsayılan 0.
        [AccessFailedCount] int NOT NULL CONSTRAINT [DF_Users_AccessFailedCount] DEFAULT (0), -- Başarısız giriş sayısı. Varsayılan 0.
        [LockoutEndUtc] datetime2(3) NULL, -- Çok sayıda başarısız denemeden sonra hesabın kilidinin açılacağı zaman.
        [SecurityStamp] uniqueidentifier NOT NULL CONSTRAINT [DF_Users_SecurityStamp] DEFAULT (NEWID()), -- Parola değişimi gibi olaylarda değiştirilerek eski oturumların geçerliliğini denetlemek için kullanılır.
        [RoleEnum] bigint NOT NULL CONSTRAINT [DF_Users_RoleEnum] DEFAULT (0), -- UluBilge.Core.Enums.RoleEnum içindeki long tabanlı Flags rollerini tutar. Varsayılan None (0).
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Users_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()), -- Hesabın oluşturulma zamanı (UTC).
        [UpdatedAtUtc] datetime2(3) NULL, -- Son güncellenme zamanı (UTC).
        [LastLoginAtUtc] datetime2(3) NULL, -- Son başarılı giriş zamanı (UTC).
        [IsActive] bit NOT NULL CONSTRAINT [DF_Users_IsActive] DEFAULT (1), -- Hesabı kullanıma açmak veya kapatmak için. Varsayılan 1.

        CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_Users_NormalizedUserName]
        ON [dbo].[Users] ([NormalizedUserName]);

    CREATE TABLE [dbo].[UserTokens]
    (
        [Id] bigint IDENTITY(1, 1) NOT NULL, -- Token kaydının kimliği.
        [UserId] int NOT NULL, -- Users.Id alanına foreign key.
        [TokenHash] binary(32) NOT NULL, -- İstemciye verilen rastgele tokenın SHA-256 özeti. Üzerinde unique index bulunur.
        [FamilyId] uniqueidentifier NOT NULL, -- Aynı girişten başlayan token yenileme zincirini tanımlar.
        [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_UserTokens_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()), -- Oluşturulma zamanı (UTC).
        [ExpiresAtUtc] datetime2(3) NOT NULL, -- Son geçerlilik zamanı (UTC).
        [ConsumedAtUtc] datetime2(3) NULL, -- Tokenın yenileme işleminde kullanıldığı zaman (UTC); başlangıçta NULL.
        [RevokedAtUtc] datetime2(3) NULL, -- İptal zamanı (UTC); başlangıçta NULL.
        [ReplacedByTokenId] bigint NULL, -- Yenileme sırasında üretilen sonraki token kaydı; başlangıçta NULL.

        CONSTRAINT [PK_UserTokens] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_UserTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]),
        CONSTRAINT [FK_UserTokens_UserTokens_ReplacedByTokenId] FOREIGN KEY ([ReplacedByTokenId]) REFERENCES [dbo].[UserTokens] ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_UserTokens_TokenHash]
        ON [dbo].[UserTokens] ([TokenHash]);

    CREATE NONCLUSTERED INDEX [IX_UserTokens_UserId]
        ON [dbo].[UserTokens] ([UserId]);

    CREATE NONCLUSTERED INDEX [IX_UserTokens_FamilyId]
        ON [dbo].[UserTokens] ([FamilyId]);

    -- Örnek kullanıcı: admin / admin. Parola, IdentityV3 PasswordHasher ile 210.000 iterasyonda hash'lenmiştir.
    INSERT INTO [dbo].[Users]
    (
        [UserName],
        [NormalizedUserName],
        [PasswordHash],
        [Email],
        [RoleEnum]
    )
    VALUES
    (
        N'admin',
        N'ADMIN',
        N'AQAAAAIAAzRQAAAAEFDAGz4T8U4SuCl8FIgPEMqj/GSyPfUZI8kJGy40qS7pGduoWr8D0jbb54kEe53+cg==',
        N'admin@example.com',
        1 -- RoleEnum.Admin
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
