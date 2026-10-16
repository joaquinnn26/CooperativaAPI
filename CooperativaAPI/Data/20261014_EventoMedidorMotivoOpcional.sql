-- Alta, instalacion y otras operaciones sin motivo de catalogo conservan NULL.
-- El registro de fallas sigue exigiendo un motivo activo en la API.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.EventoMedidor','U') IS NULL
 THROW 51001,'Falta la tabla EventoMedidor. Aplicar primero la migracion de medidores.',1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.EventoMedidor') AND name='Motivo' AND is_nullable=0)
 ALTER TABLE dbo.EventoMedidor ALTER COLUMN Motivo nvarchar(100) NULL;
COMMIT TRANSACTION;
