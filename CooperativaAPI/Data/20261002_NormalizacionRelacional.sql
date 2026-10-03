-- Normalizacion segura de catalogos y restricciones de unicidad.
-- Deduplica requisitos idénticos, reubica sus referencias y protege claves de negocio.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.RequisitoServicio',N'U') IS NULL
    THROW 51030, 'No existe dbo.RequisitoServicio; se cancela la migracion.', 1;

DECLARE @Duplicados TABLE(idDuplicado int PRIMARY KEY,idConservar int NOT NULL);
;WITH D AS (
    SELECT idRequisitoServicio,idServicio,Nombre,
           MIN(idRequisitoServicio) OVER(PARTITION BY idServicio,Nombre) AS idConservar,
           ROW_NUMBER() OVER(PARTITION BY idServicio,Nombre ORDER BY idRequisitoServicio) AS rn
    FROM dbo.RequisitoServicio
)
INSERT @Duplicados(idDuplicado,idConservar)
SELECT idRequisitoServicio,idConservar FROM D WHERE rn>1;

-- Conserva el requisito con el menor identificador y no pierde documentos asociados.
UPDATE ds SET idRequisitoServicio=d.idConservar
FROM dbo.DocumentoSolicitud ds
JOIN @Duplicados d ON d.idDuplicado=ds.idRequisitoServicio;

DELETE rs FROM dbo.RequisitoServicio rs
JOIN @Duplicados d ON d.idDuplicado=rs.idRequisitoServicio;

IF EXISTS(SELECT 1 FROM dbo.RequisitoServicio GROUP BY idServicio,Nombre HAVING COUNT(*)>1)
    THROW 51031, 'Persisten requisitos repetidos por servicio; revisar antes de continuar.', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RequisitoServicio') AND name=N'UX_RequisitoServicio_Servicio_Nombre')
    CREATE UNIQUE INDEX UX_RequisitoServicio_Servicio_Nombre ON dbo.RequisitoServicio(idServicio,Nombre);

-- Los nombres de estos catálogos son claves de negocio usadas por la API y los reportes.
IF EXISTS(SELECT 1 FROM dbo.Rol GROUP BY NombreRol HAVING COUNT(*)>1)
    THROW 51032, 'Hay roles duplicados; resolver referencias antes de crear unicidad.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Rol') AND name=N'UX_Rol_Nombre')
    CREATE UNIQUE INDEX UX_Rol_Nombre ON dbo.Rol(NombreRol);

IF EXISTS(SELECT 1 FROM dbo.Cargo GROUP BY NombreCargo HAVING COUNT(*)>1)
    THROW 51033, 'Hay cargos duplicados; resolver referencias antes de crear unicidad.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Cargo') AND name=N'UX_Cargo_Nombre')
    CREATE UNIQUE INDEX UX_Cargo_Nombre ON dbo.Cargo(NombreCargo);

IF EXISTS(SELECT 1 FROM dbo.TipoLectura GROUP BY Nombre HAVING COUNT(*)>1)
    THROW 51034, 'Hay tipos de lectura duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TipoLectura') AND name=N'UX_TipoLectura_Nombre')
    CREATE UNIQUE INDEX UX_TipoLectura_Nombre ON dbo.TipoLectura(Nombre);

IF EXISTS(SELECT 1 FROM dbo.TipoMedidor GROUP BY Nombre HAVING COUNT(*)>1)
    THROW 51035, 'Hay tipos de medidor duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TipoMedidor') AND name=N'UX_TipoMedidor_Nombre')
    CREATE UNIQUE INDEX UX_TipoMedidor_Nombre ON dbo.TipoMedidor(Nombre);

IF EXISTS(SELECT 1 FROM dbo.TipoTarifa GROUP BY NombreTipo HAVING COUNT(*)>1)
    THROW 51036, 'Hay tipos de tarifa duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TipoTarifa') AND name=N'UX_TipoTarifa_Nombre')
    CREATE UNIQUE INDEX UX_TipoTarifa_Nombre ON dbo.TipoTarifa(NombreTipo);

IF EXISTS(SELECT 1 FROM dbo.TipoReclamo GROUP BY NombreReclamo HAVING COUNT(*)>1)
    THROW 51037, 'Hay tipos de reclamo duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TipoReclamo') AND name=N'UX_TipoReclamo_Nombre')
    CREATE UNIQUE INDEX UX_TipoReclamo_Nombre ON dbo.TipoReclamo(NombreReclamo);

IF EXISTS(SELECT 1 FROM dbo.EstadoReclamo GROUP BY NombreEstado HAVING COUNT(*)>1)
    THROW 51038, 'Hay estados de reclamo duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.EstadoReclamo') AND name=N'UX_EstadoReclamo_Nombre')
    CREATE UNIQUE INDEX UX_EstadoReclamo_Nombre ON dbo.EstadoReclamo(NombreEstado);

IF EXISTS(SELECT 1 FROM dbo.Servicio GROUP BY Nombre HAVING COUNT(*)>1)
    THROW 51039, 'Hay servicios duplicados; revisar catalogo.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Servicio') AND name=N'UX_Servicio_Nombre')
    CREATE UNIQUE INDEX UX_Servicio_Nombre ON dbo.Servicio(Nombre);

COMMIT TRANSACTION;

-- Validaciones posteriores.
SELECT idServicio,Nombre,COUNT(*) AS Cantidad
FROM dbo.RequisitoServicio GROUP BY idServicio,Nombre HAVING COUNT(*)>1;
SELECT N'RequisitoServicio' AS Tabla,COUNT(*) AS Filas FROM dbo.RequisitoServicio;
