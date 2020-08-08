DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[SubscriberSchedules]') AND [c].[name] = N'On');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[SubscriberSchedules] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [infolink].[SubscriberSchedules] ALTER COLUMN [On] time NOT NULL;

GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[ReceiverSchedules]') AND [c].[name] = N'On');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[ReceiverSchedules] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [infolink].[ReceiverSchedules] ALTER COLUMN [On] time NOT NULL;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20191117114800_scheduletimespanchange', N'2.2.6-servicing-10079');

GO

