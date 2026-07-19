CREATE TABLE IF NOT EXISTS Accounts (
    AccountId varchar(256) NOT NULL,
    DisplayName varchar(256) NOT NULL,
    Created datetime NOT NULL,
    PRIMARY KEY (AccountId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS Zones (
    ZoneId varchar(50) NOT NULL,
    ZoneName varchar(256) NOT NULL,
    Enabled tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (ZoneId),
    UNIQUE KEY UX_Zones_ZoneName (ZoneName)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS Domains (
    DomainName varchar(64) NOT NULL,
    ZoneId varchar(50) NOT NULL,
    AccountId varchar(256) NOT NULL,
    DelegationType varchar(10) NOT NULL,
    HostedRecordType varchar(5) NULL,
    PRIMARY KEY (DomainName, ZoneId),
    KEY IX_Domains_AccountId (AccountId),
    CONSTRAINT FK_Domains_Zones FOREIGN KEY (ZoneId) REFERENCES Zones (ZoneId),
    CONSTRAINT FK_Domains_Accounts FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS DomainNameservers (
    DomainName varchar(64) NOT NULL,
    ZoneId varchar(50) NOT NULL,
    Nameserver varchar(256) NOT NULL,
    CreatedBy varchar(256) NULL,
    CreatedOn datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (DomainName, ZoneId, Nameserver),
    KEY IX_DomainNameservers_CreatedBy (CreatedBy)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS Records (
    DomainName varchar(64) NOT NULL,
    ZoneId varchar(50) NOT NULL,
    AccountId varchar(256) NOT NULL,
    RecordContent text NULL,
    Name varchar(512) NULL,
    Proxied varchar(10) NULL,
    Type varchar(20) NULL,
    Comment varchar(512) NULL,
    CreatedOn datetime NULL,
    Id varchar(128) NULL,
    Lockef varchar(10) NULL,
    ModifiedOn datetime NULL,
    Proxiable varchar(10) NULL,
    TTL int NULL,
    ZonaName varchar(256) NULL,
    PRIMARY KEY (DomainName, ZoneId),
    KEY IX_Records_AccountId (AccountId),
    KEY IX_Records_Id (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO Zones (ZoneId, ZoneName, Enabled) VALUES
    ('099ae4a9f6afab475a7510ba27fb90c5', 'tda.lat', 1),
    ('f457c045341701560794460ceab90cf7', 'marketplace.uy', 1),
    ('f371303144d35b6cd49a195f4ac18dd3', 'therealcake.com', 1)
ON DUPLICATE KEY UPDATE
    ZoneName = VALUES(ZoneName),
    Enabled = VALUES(Enabled);
