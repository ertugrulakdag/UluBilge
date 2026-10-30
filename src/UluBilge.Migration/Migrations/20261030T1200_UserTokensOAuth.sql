SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.UserTokens', N'SecurityStamp') IS NULL ALTER TABLE [dbo].[UserTokens] ADD [SecurityStamp] uniqueidentifier NULL;

    IF COL_LENGTH(N'dbo.UserTokens', N'OAuthClientId') IS NULL ALTER TABLE [dbo].[UserTokens] ADD [OAuthClientId] nvarchar(128) NULL;

    IF COL_LENGTH(N'dbo.UserTokens', N'OAuthResource') IS NULL ALTER TABLE [dbo].[UserTokens] ADD [OAuthResource] nvarchar(2048) NULL;

    IF COL_LENGTH(N'dbo.UserTokens', N'OAuthScope') IS NULL    ALTER TABLE [dbo].[UserTokens] ADD [OAuthScope] nvarchar(256) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
