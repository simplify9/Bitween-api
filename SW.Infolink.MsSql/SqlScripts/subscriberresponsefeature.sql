ALTER TABLE [infolink].[Xchanges] ADD [ResponseXchangeId] int NOT NULL DEFAULT 0;

GO

ALTER TABLE [infolink].[Subscribers] ADD [ResponseSubscriberId] int NOT NULL DEFAULT 0;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20191114103701_subscriberresponsefeature', N'2.2.6-servicing-10079');

GO

