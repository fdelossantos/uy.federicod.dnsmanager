CREATE TABLE [dbo].[Accounts] (
    [AccountId]   NVARCHAR (256) NOT NULL,
    [DisplayName] NVARCHAR (256) NOT NULL,
    [Created]     DATETIME       NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([AccountId] ASC)
);

