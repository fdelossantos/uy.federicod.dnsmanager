CREATE TABLE [dbo].[Domains] (
    [DomainName]     NVARCHAR (64)  NOT NULL,
    [ZoneId]         NVARCHAR (50)  NOT NULL,
    [AccountId]      NVARCHAR (256) NOT NULL,
    [DelegationType] VARCHAR (10)   NOT NULL,
    CONSTRAINT [PK_Domains] PRIMARY KEY CLUSTERED ([DomainName] ASC, [ZoneId] ASC)
);

