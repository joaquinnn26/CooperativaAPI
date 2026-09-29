SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.FichaMedidor') IS NULL
    THROW 51000, 'Primero debe estar aplicada la etapa de conexiones y medidores.', 1;
DECLARE @checks nvarchar(max) = N'';
SELECT @checks = @checks + N'ALTER TABLE dbo.MotivoMedidor DROP CONSTRAINT ' + QUOTENAME(name) + N';'
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.MotivoMedidor') AND definition LIKE '%Accion%';
EXEC sp_executesql @checks;
ALTER TABLE dbo.MotivoMedidor WITH CHECK ADD CONSTRAINT CK_MotivoMedidor_Accion
CHECK (Accion IN ('Alta','Verificar','Editar','Instalar','Reemplazar','Retirar','Falla','Reparacion','Revision','BajaDefinitiva'));
IF COL_LENGTH('dbo.EventoMedidor','OperacionId') IS NULL
    ALTER TABLE dbo.EventoMedidor ADD OperacionId uniqueidentifier NULL;
COMMIT;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.EventoMedidor') AND name='IX_EventoMedidor_Operacion')
    CREATE INDEX IX_EventoMedidor_Operacion ON dbo.EventoMedidor(OperacionId,idEvento) INCLUDE(idMedidor) WHERE OperacionId IS NOT NULL;
