DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Adapters]') AND [c].[name] = N'Hash');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Adapters] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [infolink].[Adapters] DROP COLUMN [Hash];

GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Adapters]') AND [c].[name] = N'Package');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Adapters] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [infolink].[Adapters] DROP COLUMN [Package];

GO

ALTER TABLE [infolink].[Adapters] ADD [ServerlessId] varchar(200) NULL;

GO