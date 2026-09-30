SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.AltaSocio', 'Observacion') IS NULL
    ALTER TABLE dbo.AltaSocio ADD Observacion nvarchar(1000) NULL;
IF COL_LENGTH('dbo.AltaSocioRevision', 'Observacion') IS NULL
    ALTER TABLE dbo.AltaSocioRevision ADD Observacion nvarchar(1000) NULL;
COMMIT;
