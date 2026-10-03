-- Catálogos analíticos predefinidos y estructura sin versiones innecesarias.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.MotivoMedidor','U') IS NULL
    THROW 51020, 'Falta dbo.MotivoMedidor. Aplicá primero la migración de medidores.', 1;
IF OBJECT_ID('dbo.TipoDocumentoPersona','U') IS NULL
    THROW 51021, 'Falta dbo.TipoDocumentoPersona. Aplicá primero la migración laboral.', 1;

DECLARE @MotivosMedidor TABLE(Accion nvarchar(30),Nombre nvarchar(100));
INSERT @MotivosMedidor(Accion,Nombre) VALUES
 ('Falla',N'No registra consumo'),
 ('Falla',N'Contador detenido o trabado'),
 ('Falla',N'Visor o numeración ilegible'),
 ('Falla',N'Lectura irregular o inestable'),
 ('Falla',N'Daño físico en carcasa o conexiones'),
 ('Falla',N'Posible manipulación del equipo');

-- La baja lógica conserva motivos históricos ya referenciados por eventos.
UPDATE dbo.MotivoMedidor SET Activo=0;
INSERT dbo.MotivoMedidor(Accion,Nombre,Activo)
SELECT m.Accion,m.Nombre,1 FROM @MotivosMedidor m
WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoMedidor x WHERE x.Accion=m.Accion AND x.Nombre=m.Nombre);
UPDATE x SET Activo=1
FROM dbo.MotivoMedidor x JOIN @MotivosMedidor m ON m.Accion=x.Accion AND m.Nombre=x.Nombre;

-- Estos catálogos son fijos desde la aplicación. Se conservan sus claves y datos.
IF COL_LENGTH('dbo.MotivoMedidor','Version') IS NOT NULL
    ALTER TABLE dbo.MotivoMedidor DROP COLUMN Version;
IF COL_LENGTH('dbo.TipoDocumentoPersona','Version') IS NOT NULL
    ALTER TABLE dbo.TipoDocumentoPersona DROP COLUMN Version;
IF COL_LENGTH('dbo.MotivoDocumentoPersona','Version') IS NOT NULL
    ALTER TABLE dbo.MotivoDocumentoPersona DROP COLUMN Version;
IF COL_LENGTH('dbo.MotivoRechazoAltaSocio','Version') IS NOT NULL
    ALTER TABLE dbo.MotivoRechazoAltaSocio DROP COLUMN Version;

COMMIT TRANSACTION;
