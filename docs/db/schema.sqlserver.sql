CREATE TABLE [Addresses] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [FullName] nvarchar(max) NOT NULL,
    [Phone] nvarchar(max) NOT NULL,
    [Line1] nvarchar(max) NOT NULL,
    [Ward] nvarchar(max) NOT NULL,
    [District] nvarchar(max) NOT NULL,
    [City] nvarchar(max) NOT NULL,
    [Country] nvarchar(max) NOT NULL,
    [IsDefault] bit NOT NULL,
    CONSTRAINT [PK_Addresses] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Carts] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [CheckedOut] bit NOT NULL,
    CONSTRAINT [PK_Carts] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Categories] (
    [Id] uniqueidentifier NOT NULL,
    [Slug] nvarchar(120) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [ParentId] uniqueidentifier NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [ExchangeRates] (
    [Currency] nvarchar(3) NOT NULL,
    [Rate] decimal(18,8) NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ExchangeRates] PRIMARY KEY ([Currency])
);
GO


CREATE TABLE [InventoryMovements] (
    [Id] bigint NOT NULL IDENTITY,
    [VariantId] uniqueidentifier NOT NULL,
    [Type] nvarchar(20) NOT NULL,
    [Quantity] int NOT NULL,
    [Reference] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_InventoryMovements] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Notifications] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NULL,
    [Recipient] nvarchar(max) NOT NULL,
    [Channel] nvarchar(max) NOT NULL,
    [Subject] nvarchar(max) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [IsRead] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Orders] (
    [Id] uniqueidentifier NOT NULL,
    [Number] nvarchar(32) NOT NULL,
    [UserId] uniqueidentifier NULL,
    [Email] nvarchar(256) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [PaymentMethod] nvarchar(20) NOT NULL,
    [Currency] nvarchar(3) NOT NULL,
    [ExchangeRate] decimal(18,8) NOT NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    [Discount] decimal(18,2) NOT NULL,
    [ShippingFee] decimal(18,2) NOT NULL,
    [Total] decimal(18,2) NOT NULL,
    [PromotionCode] nvarchar(50) NULL,
    [ShippingAddress_FullName] nvarchar(max) NOT NULL,
    [ShippingAddress_Phone] nvarchar(max) NOT NULL,
    [ShippingAddress_Line1] nvarchar(max) NOT NULL,
    [ShippingAddress_Ward] nvarchar(max) NOT NULL,
    [ShippingAddress_District] nvarchar(max) NOT NULL,
    [ShippingAddress_City] nvarchar(max) NOT NULL,
    [ShippingAddress_Country] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Outbox] (
    [Id] uniqueidentifier NOT NULL,
    [Type] nvarchar(100) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [OccurredAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [Attempts] int NOT NULL,
    [Error] nvarchar(max) NULL,
    CONSTRAINT [PK_Outbox] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Payments] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [Method] nvarchar(20) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [ProviderReference] nvarchar(450) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Promotions] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(50) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Type] nvarchar(20) NOT NULL,
    [Value] decimal(18,2) NOT NULL,
    [MinSubtotal] decimal(18,2) NOT NULL,
    [MaxDiscount] decimal(18,2) NULL,
    [StartsAt] datetime2 NOT NULL,
    [EndsAt] datetime2 NULL,
    [UsageLimit] int NULL,
    [UsedCount] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Promotions] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [RefreshTokens] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [TokenHash] nvarchar(128) NOT NULL,
    [FamilyId] uniqueidentifier NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [RevokedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Shipments] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [Carrier] nvarchar(max) NOT NULL,
    [TrackingNumber] nvarchar(450) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Shipments] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [FullName] nvarchar(200) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [PreferredLocale] nvarchar(10) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [WebhookEvents] (
    [Id] uniqueidentifier NOT NULL,
    [Source] nvarchar(50) NOT NULL,
    [EventId] nvarchar(100) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [ReceivedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_WebhookEvents] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Products] (
    [Id] uniqueidentifier NOT NULL,
    [Slug] nvarchar(160) NOT NULL,
    [Name] nvarchar(300) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Brand] nvarchar(120) NOT NULL,
    [CategoryId] uniqueidentifier NOT NULL,
    [MinPrice] decimal(18,2) NOT NULL,
    [RatingAverage] float NOT NULL,
    [RatingCount] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [OrderHistory] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [From] nvarchar(20) NOT NULL,
    [To] nvarchar(20) NOT NULL,
    [Note] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_OrderHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderHistory_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [OrderItems] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [VariantId] uniqueidentifier NOT NULL,
    [Sku] nvarchar(max) NOT NULL,
    [ProductName] nvarchar(max) NOT NULL,
    [VariantName] nvarchar(max) NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Reviews] (
    [Id] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Rating] int NOT NULL,
    [Comment] nvarchar(2000) NOT NULL,
    [VerifiedPurchase] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Reviews] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Reviews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [ProductImages] (
    [Id] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [Url] nvarchar(1000) NOT NULL,
    [Alt] nvarchar(max) NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_ProductImages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [ProductTranslations] (
    [Id] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [Locale] nvarchar(10) NOT NULL,
    [Name] nvarchar(300) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ProductTranslations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProductTranslations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Variants] (
    [Id] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [Sku] nvarchar(64) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Color] nvarchar(50) NULL,
    [Size] nvarchar(20) NULL,
    [Price] decimal(18,2) NOT NULL,
    [CompareAtPrice] decimal(18,2) NULL,
    CONSTRAINT [PK_Variants] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Variants_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Wishlist] (
    [UserId] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Wishlist] PRIMARY KEY ([UserId], [ProductId]),
    CONSTRAINT [FK_Wishlist_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [CartItems] (
    [Id] uniqueidentifier NOT NULL,
    [CartId] uniqueidentifier NOT NULL,
    [VariantId] uniqueidentifier NOT NULL,
    [Quantity] int NOT NULL,
    CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CartItems_Carts_CartId] FOREIGN KEY ([CartId]) REFERENCES [Carts] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_CartItems_Variants_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [Variants] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Inventory] (
    [VariantId] uniqueidentifier NOT NULL,
    [OnHand] int NOT NULL,
    [Reserved] int NOT NULL,
    CONSTRAINT [PK_Inventory] PRIMARY KEY ([VariantId]),
    CONSTRAINT [FK_Inventory_Variants_VariantId] FOREIGN KEY ([VariantId]) REFERENCES [Variants] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_Addresses_UserId] ON [Addresses] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_CartItems_CartId_VariantId] ON [CartItems] ([CartId], [VariantId]);
GO


CREATE INDEX [IX_CartItems_VariantId] ON [CartItems] ([VariantId]);
GO


CREATE INDEX [IX_Carts_UserId] ON [Carts] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_Categories_Slug] ON [Categories] ([Slug]);
GO


CREATE INDEX [IX_InventoryMovements_VariantId_CreatedAt] ON [InventoryMovements] ([VariantId], [CreatedAt]);
GO


CREATE INDEX [IX_Notifications_UserId_CreatedAt] ON [Notifications] ([UserId], [CreatedAt]);
GO


CREATE INDEX [IX_OrderHistory_OrderId] ON [OrderHistory] ([OrderId]);
GO


CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);
GO


CREATE UNIQUE INDEX [IX_Orders_Number] ON [Orders] ([Number]);
GO


CREATE INDEX [IX_Orders_Status_CreatedAt] ON [Orders] ([Status], [CreatedAt]);
GO


CREATE INDEX [IX_Orders_UserId_CreatedAt] ON [Orders] ([UserId], [CreatedAt]);
GO


CREATE INDEX [IX_Outbox_ProcessedAt_OccurredAt] ON [Outbox] ([ProcessedAt], [OccurredAt]);
GO


CREATE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);
GO


CREATE INDEX [IX_Payments_ProviderReference] ON [Payments] ([ProviderReference]);
GO


CREATE INDEX [IX_ProductImages_ProductId] ON [ProductImages] ([ProductId]);
GO


CREATE INDEX [IX_Products_CategoryId_IsActive_MinPrice] ON [Products] ([CategoryId], [IsActive], [MinPrice]);
GO


CREATE INDEX [IX_Products_CreatedAt] ON [Products] ([CreatedAt]);
GO


CREATE UNIQUE INDEX [IX_Products_Slug] ON [Products] ([Slug]);
GO


CREATE UNIQUE INDEX [IX_ProductTranslations_ProductId_Locale] ON [ProductTranslations] ([ProductId], [Locale]);
GO


CREATE UNIQUE INDEX [IX_Promotions_Code] ON [Promotions] ([Code]);
GO


CREATE INDEX [IX_RefreshTokens_FamilyId] ON [RefreshTokens] ([FamilyId]);
GO


CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
GO


CREATE INDEX [IX_Reviews_ProductId_CreatedAt] ON [Reviews] ([ProductId], [CreatedAt]);
GO


CREATE UNIQUE INDEX [IX_Reviews_ProductId_UserId] ON [Reviews] ([ProductId], [UserId]);
GO


CREATE INDEX [IX_Reviews_UserId] ON [Reviews] ([UserId]);
GO


CREATE INDEX [IX_Shipments_OrderId] ON [Shipments] ([OrderId]);
GO


CREATE UNIQUE INDEX [IX_Shipments_TrackingNumber] ON [Shipments] ([TrackingNumber]);
GO


CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
GO


CREATE INDEX [IX_Variants_ProductId] ON [Variants] ([ProductId]);
GO


CREATE UNIQUE INDEX [IX_Variants_Sku] ON [Variants] ([Sku]);
GO


CREATE UNIQUE INDEX [IX_WebhookEvents_Source_EventId] ON [WebhookEvents] ([Source], [EventId]);
GO


CREATE INDEX [IX_Wishlist_ProductId] ON [Wishlist] ([ProductId]);
GO


