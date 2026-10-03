-- Retira el almacenamiento laboral antiguo despues de unificar documentos en Persona.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DocumentoSocioLaboral',N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.DocumentoSocioLaboral)
        THROW 51060, 'Quedan documentos laborales antiguos; deben migrarse a DocumentoPersona antes de eliminarlos.', 1;
    DROP TABLE dbo.DocumentoSocioLaboral;
END;

IF OBJECT_ID(N'dbo.TipoDocumentoSocio',N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.TipoDocumentoSocio)
        THROW 51062, 'El catalogo antiguo de documentos aun contiene datos; migrarlos a TipoDocumentoPersona.', 1;
    DROP TABLE dbo.TipoDocumentoSocio;
END;

IF COL_LENGTH(N'dbo.DocumentoPersona',N'idDocumentoSocioOrigen') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.DocumentoPersona WHERE idDocumentoSocioOrigen IS NOT NULL)
        THROW 51063, 'Quedan referencias de migracion en DocumentoPersona; conservarlas y revisar manualmente.', 1;
    IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DocumentoPersona') AND name=N'UX_DocumentoPersona_Legacy')
        DROP INDEX UX_DocumentoPersona_Legacy ON dbo.DocumentoPersona;
    ALTER TABLE dbo.DocumentoPersona DROP COLUMN idDocumentoSocioOrigen;
END;

COMMIT TRANSACTION;

SELECT OBJECT_ID(N'dbo.DocumentoPersona',N'U') AS DocumentoPersona,
       OBJECT_ID(N'dbo.DocumentoSocioLaboral',N'U') AS DocumentoSocioLaboral,
       OBJECT_ID(N'dbo.TipoDocumentoSocio',N'U') AS TipoDocumentoSocio;
