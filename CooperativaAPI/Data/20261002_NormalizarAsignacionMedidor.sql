-- El socio de una instalacion se obtiene por ServicioSocio; no se almacena dos veces.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.AsignacionMedidor',N'idSocio') IS NOT NULL
BEGIN
    IF EXISTS(
        SELECT 1
        FROM dbo.AsignacionMedidor a
        LEFT JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=a.idServicioSocio
        WHERE ss.idServicioSocio IS NULL OR a.idSocio<>ss.idSocio
    )
        THROW 51040, 'AsignacionMedidor contiene socios que no coinciden con su ServicioSocio; se cancela sin perder datos.', 1;

    DECLARE @dropSocioFks nvarchar(max)=N'';
    SELECT @dropSocioFks=@dropSocioFks+N'ALTER TABLE dbo.AsignacionMedidor DROP CONSTRAINT '+QUOTENAME(f.name)+N';'
    FROM sys.foreign_keys f
    WHERE f.parent_object_id=OBJECT_ID(N'dbo.AsignacionMedidor')
      AND EXISTS(
          SELECT 1 FROM sys.foreign_key_columns fc
          JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
          WHERE fc.constraint_object_id=f.object_id AND c.name=N'idSocio'
      );
    IF @dropSocioFks<>N'' EXEC sys.sp_executesql @dropSocioFks;

    ALTER TABLE dbo.AsignacionMedidor DROP COLUMN idSocio;
END;

COMMIT TRANSACTION;

SELECT c.name AS Columna
FROM sys.columns c
WHERE c.object_id=OBJECT_ID(N'dbo.AsignacionMedidor') AND c.name=N'idSocio';
