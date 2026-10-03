-- La instalacion vigente vive solo en AsignacionMedidor.
-- dbo.Medidor queda como vista de compatibilidad para el modelo EF heredado.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Medidor',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.MedidorEquipo',N'U') IS NULL
BEGIN
    IF EXISTS(
        SELECT 1 FROM dbo.Medidor m
        WHERE m.idServicioSocio IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM dbo.AsignacionMedidor a WHERE a.idMedidor=m.idMedidor AND a.Cerrada=0 AND a.idServicioSocio=m.idServicioSocio)
    )
        THROW 51050, 'Hay vinculos de medidor sin asignacion vigente coincidente; se cancela para conservar el historial.', 1;

    DECLARE @dropMedidorFks nvarchar(max)=N'';
    SELECT @dropMedidorFks=@dropMedidorFks+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(f.parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(f.parent_object_id))+
        N' DROP CONSTRAINT '+QUOTENAME(f.name)+N';'
    FROM sys.foreign_keys f
    WHERE f.parent_object_id=OBJECT_ID(N'dbo.Medidor')
      AND EXISTS(
          SELECT 1 FROM sys.foreign_key_columns fc
          JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
          WHERE fc.constraint_object_id=f.object_id AND c.name=N'idServicioSocio'
      );
    IF @dropMedidorFks<>N'' EXEC sys.sp_executesql @dropMedidorFks;

    EXEC sys.sp_rename N'dbo.Medidor',N'MedidorEquipo';

    DECLARE @dropColumnDefaults nvarchar(max)=N'';
    SELECT @dropColumnDefaults=@dropColumnDefaults+N'ALTER TABLE dbo.MedidorEquipo DROP CONSTRAINT '+QUOTENAME(dc.name)+N';'
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID(N'dbo.MedidorEquipo') AND c.name IN (N'FechaInstalacion',N'Activo');
    IF @dropColumnDefaults<>N'' EXEC sys.sp_executesql @dropColumnDefaults;

    IF COL_LENGTH(N'dbo.MedidorEquipo',N'idServicioSocio') IS NOT NULL
        ALTER TABLE dbo.MedidorEquipo DROP COLUMN idServicioSocio;
    IF COL_LENGTH(N'dbo.MedidorEquipo',N'FechaInstalacion') IS NOT NULL
        ALTER TABLE dbo.MedidorEquipo DROP COLUMN FechaInstalacion;
    IF COL_LENGTH(N'dbo.MedidorEquipo',N'Activo') IS NOT NULL
        ALTER TABLE dbo.MedidorEquipo DROP COLUMN Activo;
END;

IF OBJECT_ID(N'dbo.Medidor',N'V') IS NULL
BEGIN
    EXEC sys.sp_executesql N'CREATE VIEW dbo.Medidor AS
        SELECT m.idMedidor,m.Numero,a.idServicioSocio,
               CAST(CASE WHEN a.idAsignacion IS NOT NULL AND f.EstadoFisico=N''Operativo'' THEN 1 ELSE 0 END AS bit) AS Activo,
               m.idTipoMedidor,a.FechaInicio AS FechaInstalacion
        FROM dbo.MedidorEquipo m
        JOIN dbo.FichaMedidor f ON f.idMedidor=m.idMedidor
        LEFT JOIN dbo.AsignacionMedidor a ON a.idMedidor=m.idMedidor AND a.Cerrada=0;';
END;

COMMIT TRANSACTION;

IF OBJECT_ID(N'dbo.Medidor',N'V') IS NULL OR OBJECT_ID(N'dbo.MedidorEquipo',N'U') IS NULL
    THROW 51051, 'No se creo correctamente la tabla normalizada y su vista de compatibilidad.', 1;

SELECT name,type_desc FROM sys.objects WHERE object_id IN (OBJECT_ID(N'dbo.Medidor'),OBJECT_ID(N'dbo.MedidorEquipo')) ORDER BY name;
