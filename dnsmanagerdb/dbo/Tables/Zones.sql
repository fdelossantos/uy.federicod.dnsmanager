CREATE TABLE [dbo].[Zones] (
    [ZoneId]   VARCHAR (50)  NOT NULL,
    [ZoneName] VARCHAR (256) NOT NULL,
    [Enabled]  BIT           CONSTRAINT [DF_Zones_Enabled] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_Zones] PRIMARY KEY CLUSTERED ([ZoneId] ASC)
);

