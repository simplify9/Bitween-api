IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;

GO

IF SCHEMA_ID(N'infolink') IS NULL EXEC(N'CREATE SCHEMA [infolink];');

GO

CREATE TABLE [infolink].[AccessKeySets] (
    [Id] int NOT NULL IDENTITY,
    [Key1] varchar(1024) NOT NULL,
    [Key2] varchar(1024) NOT NULL,
    CONSTRAINT [PK_AccessKeySets] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[Adapters] (
    [Id] int NOT NULL IDENTITY,
    [Type] int NOT NULL,
    [Name] varchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Timeout] int NOT NULL,
    [DocumentId] int NOT NULL,
    [Package] varbinary(max) NULL,
    [Hash] varchar(50) NOT NULL,
    [Properties] nvarchar(max) NULL,
    CONSTRAINT [PK_Adapters] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[Documents] (
    [Id] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [HandlerId] int NOT NULL,
    [DuplicateInterval] int NOT NULL,
    [PromotedProperties] nvarchar(max) NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[Subscribers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [KeySetId] int NOT NULL,
    [DocumentId] int NOT NULL,
    [HandlerId] int NOT NULL,
    [MapperId] int NOT NULL,
    [Temporary] bit NOT NULL,
    [Properties] nvarchar(max) NULL,
    [DocumentFilter] nvarchar(max) NULL,
    [Inactive] bit NOT NULL,
    [SenderSubscriberId] int NOT NULL,
    CONSTRAINT [PK_Subscribers] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[XchangeFiles] (
    [Id] int NOT NULL,
    [Type] tinyint NOT NULL,
    [Content] varbinary(max) NULL,
    CONSTRAINT [PK_XchangeFiles] PRIMARY KEY ([Id], [Type])
);

GO

CREATE TABLE [infolink].[Xchanges] (
    [Id] int NOT NULL IDENTITY,
    [SubscriberId] int NOT NULL,
    [DocumentId] int NOT NULL,
    [HandlerId] int NOT NULL,
    [MapperId] int NOT NULL,
    [References] nvarchar(1024) NULL,
    [OutputFileSize] int NOT NULL,
    [HostName] nvarchar(max) NULL,
    [Status] int NOT NULL,
    [Exception] nvarchar(max) NULL,
    [DeliveredOn] datetime2 NULL,
    [FinishedOn] datetime2 NULL,
    [StartedOn] datetime2 NOT NULL,
    [InputFileName] nvarchar(200) NULL,
    [InputFileSize] int NOT NULL,
    [InputFileHash] varchar(50) NOT NULL,
    [DeliverOn] datetime2 NULL,
    CONSTRAINT [PK_Xchanges] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[SubscriberSchedules] (
    [Id] int NOT NULL IDENTITY,
    [Recurrence] tinyint NOT NULL,
    [On] varchar(1024) NOT NULL,
    [Backwards] bit NOT NULL,
    [SubscriberId] int NOT NULL,
    CONSTRAINT [PK_SubscriberSchedules] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SubscriberSchedules_Subscribers_SubscriberId] FOREIGN KEY ([SubscriberId]) REFERENCES [infolink].[Subscribers] ([Id]) ON DELETE CASCADE
);

GO

CREATE INDEX [IX_SubscriberSchedules_SubscriberId] ON [infolink].[SubscriberSchedules] ([SubscriberId]);

GO

CREATE INDEX [IX_Xchanges_DeliverOn] ON [infolink].[Xchanges] ([DeliverOn]);

GO

CREATE INDEX [IX_Xchanges_DeliveredOn] ON [infolink].[Xchanges] ([DeliveredOn]);

GO

CREATE INDEX [IX_Xchanges_InputFileHash] ON [infolink].[Xchanges] ([InputFileHash]);

GO

CREATE INDEX [IX_Xchanges_SubscriberId] ON [infolink].[Xchanges] ([SubscriberId]);

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20190210154438_init', N'2.2.6-servicing-10079');

GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Subscribers]') AND [c].[name] = N'SenderSubscriberId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Subscribers] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [infolink].[Subscribers] DROP COLUMN [SenderSubscriberId];

GO

ALTER TABLE [infolink].[Subscribers] ADD [Aggregate] bit NOT NULL DEFAULT 0;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20190210160837_subscriberfields', N'2.2.6-servicing-10079');

GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Xchanges]') AND [c].[name] = N'HostName');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Xchanges] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [infolink].[Xchanges] DROP COLUMN [HostName];

GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Xchanges]') AND [c].[name] = N'OutputFileSize');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Xchanges] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [infolink].[Xchanges] DROP COLUMN [OutputFileSize];

GO

CREATE TABLE [infolink].[Receivers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [SubscriberId] int NOT NULL,
    [ReceiverId] int NOT NULL,
    [Properties] nvarchar(max) NULL,
    CONSTRAINT [PK_Receivers] PRIMARY KEY ([Id])
);

GO

CREATE TABLE [infolink].[ReceiverSchedules] (
    [Id] int NOT NULL IDENTITY,
    [Recurrence] tinyint NOT NULL,
    [On] varchar(1024) NOT NULL,
    [Backwards] bit NOT NULL,
    [ReceiverId] int NOT NULL,
    CONSTRAINT [PK_ReceiverSchedules] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ReceiverSchedules_Receivers_ReceiverId] FOREIGN KEY ([ReceiverId]) REFERENCES [infolink].[Receivers] ([Id]) ON DELETE CASCADE
);

GO

CREATE INDEX [IX_ReceiverSchedules_ReceiverId] ON [infolink].[ReceiverSchedules] ([ReceiverId]);

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20190211125534_receiver', N'2.2.6-servicing-10079');

GO

ALTER TABLE [infolink].[AccessKeySets] ADD [Name] varchar(200) NOT NULL DEFAULT N'';

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20190227080032_AccessKeySets_NameField', N'2.2.6-servicing-10079');

GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Receivers]') AND [c].[name] = N'SubscriberId');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Receivers] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [infolink].[Receivers] DROP COLUMN [SubscriberId];

GO

ALTER TABLE [infolink].[Receivers] ADD [ReceiveOn] datetime2 NULL;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20190511080308_receiver2', N'2.2.6-servicing-10079');

GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[infolink].[Documents]') AND [c].[name] = N'HandlerId');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [infolink].[Documents] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [infolink].[Documents] DROP COLUMN [HandlerId];

GO

ALTER TABLE [infolink].[Documents] ADD [BusEnabled] bit NOT NULL DEFAULT 0;

GO

ALTER TABLE [infolink].[Documents] ADD [BusMessageTypeName] varchar(500) NULL;

GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20191015142905_buschanges', N'2.2.6-servicing-10079');

GO

