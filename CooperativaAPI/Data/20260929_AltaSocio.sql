SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.AltaSocio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AltaSocio (
        idUsuarioSistema int NOT NULL PRIMARY KEY REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        EsTitular bit NOT NULL,
        Estado varchar(24) NOT NULL DEFAULT 'PendienteDocumentacion',
        Motivo nvarchar(1000) NULL,
        FechaRegistro datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        FechaRevision datetime2 NULL,
        RevisadoPor int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        Version int NOT NULL DEFAULT 1,
        CONSTRAINT CK_AltaSocio_Estado CHECK (Estado IN ('PendienteDocumentacion','EnRevision','Rechazada','Aprobada'))
    );
    CREATE TABLE dbo.AltaSocioDocumento (
        idDocumento int IDENTITY PRIMARY KEY,
        idUsuarioSistema int NOT NULL REFERENCES dbo.AltaSocio(idUsuarioSistema),
        Tipo varchar(20) NOT NULL,
        Nombre nvarchar(180) NOT NULL,
        Mime varchar(40) NOT NULL,
        Contenido varbinary(max) NOT NULL,
        Fecha datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Vigente bit NOT NULL DEFAULT 1,
        CONSTRAINT CK_AltaSocioDocumento_Tipo CHECK (Tipo IN ('Domicilio','DniFrente','DniDorso')),
        CONSTRAINT CK_AltaSocioDocumento_Tamano CHECK (DATALENGTH(Contenido) BETWEEN 1 AND 2097152)
    );
    CREATE UNIQUE INDEX UX_AltaSocioDocumento_Vigente ON dbo.AltaSocioDocumento(idUsuarioSistema, Tipo) WHERE Vigente=1;
    CREATE TABLE dbo.AltaSocioRevision (
        idRevision int IDENTITY PRIMARY KEY,
        idUsuarioSistema int NOT NULL REFERENCES dbo.AltaSocio(idUsuarioSistema),
        Estado varchar(24) NOT NULL,
        Motivo nvarchar(1000) NULL,
        RevisadoPor int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        Fecha datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
COMMIT;
