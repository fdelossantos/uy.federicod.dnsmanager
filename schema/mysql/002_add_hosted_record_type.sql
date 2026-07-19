SET @hosted_record_type_exists = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Domains'
      AND COLUMN_NAME = 'HostedRecordType'
);

SET @add_hosted_record_type = IF(
    @hosted_record_type_exists = 0,
    'ALTER TABLE Domains ADD COLUMN HostedRecordType varchar(5) NULL AFTER DelegationType',
    'SELECT 1'
);

PREPARE add_hosted_record_type_statement FROM @add_hosted_record_type;
EXECUTE add_hosted_record_type_statement;
DEALLOCATE PREPARE add_hosted_record_type_statement;

UPDATE Domains
SET HostedRecordType = 'A'
WHERE DelegationType = 'Hosted'
  AND HostedRecordType IS NULL;

UPDATE Domains
SET HostedRecordType = NULL
WHERE DelegationType = 'Delegated';
