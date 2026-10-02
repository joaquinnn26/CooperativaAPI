-- Unifica el archivo de socios y empleados en DocumentoPersona y fija
-- vencimiento de 24 horas para rutas todavía abiertas.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.TipoDocumentoPersona','U') IS NULL OR OBJECT_ID('dbo.DocumentoPersona','U') IS NULL
    THROW 51010, 'Primero debe aplicarse 20260928_VistasLaborales.sql.', 1;

-- Migra cualquier documento anterior que todavía no tenga correspondencia.
IF OBJECT_ID('dbo.DocumentoSocioLaboral','U') IS NOT NULL
   AND OBJECT_ID('dbo.TipoDocumentoSocio','U') IS NOT NULL
BEGIN
    INSERT dbo.TipoDocumentoPersona(Codigo,Nombre,Grupo)
    SELECT CONCAT('LEG_',t.idTipoDocumento),t.Nombre,N'Histórico de socio'
    FROM dbo.TipoDocumentoSocio t
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TipoDocumentoPersona p WHERE p.Codigo=CONCAT('LEG_',t.idTipoDocumento));

    INSERT dbo.DocumentoPersona(idPersona,idTipoDocumentoPersona,NombreArchivo,TipoArchivo,Contenido,Estado,MotivoRechazo,FechaCargaUtc,idDocumentoSocioOrigen)
    SELECT s.idPersona,p.idTipoDocumentoPersona,d.Nombre,d.Tipo,d.Contenido,
           CASE WHEN d.Estado IN ('Aprobado','Rechazado') THEN d.Estado ELSE 'Pendiente' END,
           LEFT(d.Motivo,100),d.FechaUtc,d.idDocumento
    FROM dbo.DocumentoSocioLaboral d
    JOIN dbo.Socio s ON s.idSocio=d.idSocio
    JOIN dbo.TipoDocumentoPersona p ON p.Codigo=CONCAT('LEG_',d.idTipoDocumento)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DocumentoPersona x WHERE x.idDocumentoSocioOrigen=d.idDocumento);

    -- Mantiene enlazadas a sus documentos migrados las tareas antiguas.
    IF OBJECT_ID('dbo.TareaLaboral','U') IS NOT NULL
        UPDATE task SET OrigenId=doc.idDocumentoPersona
        FROM dbo.TareaLaboral task
        JOIN dbo.DocumentoPersona doc ON doc.idDocumentoSocioOrigen=task.OrigenId
        WHERE task.Categoria='Documentacion'
          AND NOT EXISTS (SELECT 1 FROM dbo.TareaLaboral other
                          WHERE other.idTarea<>task.idTarea AND other.Area=task.Area
                            AND other.Categoria=task.Categoria AND other.OrigenId=doc.idDocumentoPersona);

    -- No borrar las tablas de origen aqui. Se conservan para rollback y para
    -- verificar referencias externas antes de una migraci?n de limpieza separada.
END;

-- El vencimiento se fija al crear la ruta. Las ya existentes conservan su
-- plazo original calculado desde FechaCreacion; las finalizadas/canceladas no cambian.
IF COL_LENGTH('dbo.RutaMedicion','FechaCaducidadUtc') IS NULL
    ALTER TABLE dbo.RutaMedicion ADD FechaCaducidadUtc datetime2(3) NULL;
GO

UPDATE dbo.RutaMedicion
SET FechaCaducidadUtc=DATEADD(HOUR,24,FechaCreacion)
WHERE FechaCaducidadUtc IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.RutaMedicion') AND name='FechaCaducidadUtc' AND is_nullable=1)
    ALTER TABLE dbo.RutaMedicion ALTER COLUMN FechaCaducidadUtc datetime2(3) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id=dc.object_id
               WHERE dc.parent_object_id=OBJECT_ID('dbo.RutaMedicion') AND c.name='FechaCaducidadUtc')
    ALTER TABLE dbo.RutaMedicion ADD CONSTRAINT DF_RutaMedicion_FechaCaducidadUtc
        DEFAULT (DATEADD(HOUR,24,SYSUTCDATETIME())) FOR FechaCaducidadUtc;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.RutaMedicion') AND name='CK_RutaMedicion_Estado')
    ALTER TABLE dbo.RutaMedicion DROP CONSTRAINT CK_RutaMedicion_Estado;
ALTER TABLE dbo.RutaMedicion WITH CHECK ADD CONSTRAINT CK_RutaMedicion_Estado
    CHECK (Estado IN ('Pendiente','EnCurso','Finalizada','Cancelada','TiempoCaducado'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.RutaMedicion') AND name='IX_RutaMedicion_Caducidad')
    CREATE INDEX IX_RutaMedicion_Caducidad ON dbo.RutaMedicion(FechaCaducidadUtc,Estado) INCLUDE(idRutaMedicion);

EXEC(N'CREATE OR ALTER PROCEDURE dbo.ExpirarRutasMedicion @idRutaMedicion int = NULL AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RutaMedicion
       SET Estado=''TiempoCaducado''
     WHERE Estado IN (''Pendiente'',''EnCurso'')
       AND FechaCaducidadUtc<=SYSUTCDATETIME()
       AND (@idRutaMedicion IS NULL OR idRutaMedicion=@idRutaMedicion);
END');

SET QUOTED_IDENTIFIER ON;
EXEC dbo.ExpirarRutasMedicion;

COMMIT;
