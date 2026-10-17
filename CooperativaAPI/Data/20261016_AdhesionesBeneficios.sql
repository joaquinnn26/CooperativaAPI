/* Requiere las migraciones existentes de alta, organización e intercambio contable.
   No convierte solicitudes de atención ni servicios previos en adhesiones. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
/* Importe de Ortopedia indicado por Silvana para este flujo. No sobrescribe catálogos existentes. */
IF NOT EXISTS(SELECT 1 FROM dbo.Servicio WHERE UPPER(LTRIM(RTRIM(Nombre)))='ORTOPEDIA')
BEGIN
 DECLARE @tarifaFija int=(SELECT TOP(1) idTipoTarifa FROM dbo.TipoTarifa WHERE NombreTipo=N'Tarifa Fija Mensual');
 IF @tarifaFija IS NULL THROW 51000,'Configurá el tipo Tarifa Fija Mensual antes de instalar Ortopedia.',1;
 INSERT dbo.Servicio(Nombre,Descripcion,idTipoTarifa,TarifaBase) VALUES(N'Ortopedia',N'Adhesión optativa de cuota fija incluida en otros servicios de la factura eléctrica.',@tarifaFija,1000);
END;
IF OBJECT_ID('dbo.AdhesionBeneficio','U') IS NULL
CREATE TABLE dbo.AdhesionBeneficio(
 idAdhesion int IDENTITY PRIMARY KEY,
 idSocio int NOT NULL REFERENCES dbo.Socio(idSocio),
 idServicio int NOT NULL REFERENCES dbo.Servicio(idServicio),
 Tipo varchar(20) NOT NULL CHECK(Tipo IN ('SEPELIO','ORTOPEDIA')),
 idSolicitudEnergia int NULL REFERENCES dbo.SolicitudServicio(idSolicitudServicio),
 idConexionEnergia int NULL REFERENCES dbo.ServicioSocio(idServicioSocio),
 Direccion nvarchar(200) NOT NULL,
 Estado varchar(20) NOT NULL DEFAULT 'EnRevision' CHECK(Estado IN ('EnRevision','Observada','Activa','Rechazada','Baja')),
 ImporteMensual decimal(12,2) NULL CHECK(ImporteMensual>=0),
 Consentimiento bit NOT NULL CHECK(Consentimiento=1),
 FechaSolicitudUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 FechaActivacionUtc datetime2 NULL,
 FechaBajaUtc datetime2 NULL,
 Motivo nvarchar(500) NULL,
 idUsuarioDecision int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
 Revision int NOT NULL DEFAULT 1,
 Vigente bit NOT NULL DEFAULT 1,
 CONSTRAINT CK_Adhesion_Conexion CHECK(idSolicitudEnergia IS NOT NULL OR idConexionEnergia IS NOT NULL),
 CONSTRAINT CK_Adhesion_Vigencia CHECK((Estado IN ('Rechazada','Baja') AND Vigente=0) OR (Estado IN ('EnRevision','Observada','Activa') AND Vigente=1))
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AdhesionBeneficio') AND name='UX_Adhesion_Vigente')
CREATE UNIQUE INDEX UX_Adhesion_Vigente ON dbo.AdhesionBeneficio(idSocio,Tipo,Direccion) WHERE Vigente=1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AdhesionBeneficio') AND name='UX_Adhesion_Conexion')
CREATE UNIQUE INDEX UX_Adhesion_Conexion ON dbo.AdhesionBeneficio(idConexionEnergia,Tipo) WHERE Vigente=1 AND idConexionEnergia IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AdhesionBeneficio') AND name='UX_Adhesion_Solicitud')
CREATE UNIQUE INDEX UX_Adhesion_Solicitud ON dbo.AdhesionBeneficio(idSolicitudEnergia,Tipo) WHERE Vigente=1 AND idSolicitudEnergia IS NOT NULL;
IF OBJECT_ID('dbo.AdhesionBeneficiario','U') IS NULL
CREATE TABLE dbo.AdhesionBeneficiario(
 idBeneficiario int IDENTITY PRIMARY KEY,
 idAdhesion int NOT NULL REFERENCES dbo.AdhesionBeneficio(idAdhesion),
 Nombre nvarchar(150) NOT NULL,
 Documento nvarchar(20) NOT NULL,
 Vinculo nvarchar(60) NOT NULL,
 MismoDomicilio bit NOT NULL,
 ConservaAdhesion bit NOT NULL DEFAULT 0,
 NombreArchivo nvarchar(150) NOT NULL,
 Mime varchar(50) NOT NULL,
 Contenido varbinary(max) NOT NULL,
 UNIQUE(idAdhesion,Documento)
);
IF COL_LENGTH('dbo.AdhesionBeneficiario','ConservaAdhesion') IS NULL
ALTER TABLE dbo.AdhesionBeneficiario ADD ConservaAdhesion bit NOT NULL DEFAULT 0;
DECLARE @constraint sysname;
SELECT @constraint=name FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.AdhesionBeneficiario') AND definition LIKE '%MismoDomicilio%' AND name<>'CK_Adhesion_Residencia';
IF @constraint IS NOT NULL EXEC('ALTER TABLE dbo.AdhesionBeneficiario DROP CONSTRAINT '+@constraint);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.AdhesionBeneficiario') AND name='CK_Adhesion_Residencia')
EXEC('ALTER TABLE dbo.AdhesionBeneficiario ADD CONSTRAINT CK_Adhesion_Residencia CHECK(MismoDomicilio=1 OR ConservaAdhesion=1)');
IF OBJECT_ID('dbo.AdhesionHistorial','U') IS NULL
CREATE TABLE dbo.AdhesionHistorial(
 idHistorial int IDENTITY PRIMARY KEY,
 idAdhesion int NOT NULL REFERENCES dbo.AdhesionBeneficio(idAdhesion),
 Estado varchar(20) NOT NULL,
 Motivo nvarchar(500) NULL,
 idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
 FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID('dbo.PrestacionBeneficio','U') IS NULL
CREATE TABLE dbo.PrestacionBeneficio(
 idPrestacion int IDENTITY PRIMARY KEY,
 idAdhesion int NOT NULL REFERENCES dbo.AdhesionBeneficio(idAdhesion),
 idBeneficiario int NULL REFERENCES dbo.AdhesionBeneficiario(idBeneficiario),
 Detalle nvarchar(1500) NOT NULL,
 Estado varchar(20) NOT NULL DEFAULT 'Recibida' CHECK(Estado IN ('Recibida','Derivada','Finalizada','Cancelada')),
 ReferenciaPrestador nvarchar(200) NULL,
 Resultado nvarchar(500) NULL,
 Revision int NOT NULL DEFAULT 1,
 FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID('dbo.PrestacionHistorial','U') IS NULL
CREATE TABLE dbo.PrestacionHistorial(
 idHistorial int IDENTITY PRIMARY KEY,
 idPrestacion int NOT NULL REFERENCES dbo.PrestacionBeneficio(idPrestacion),
 Estado varchar(20) NOT NULL,
 Motivo nvarchar(500) NULL,
 idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
 FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF COL_LENGTH('dbo.LoteImportacionFacturaDetalle','ConceptosJson') IS NULL
ALTER TABLE dbo.LoteImportacionFacturaDetalle ADD ConceptosJson nvarchar(max) NULL;
IF OBJECT_ID('dbo.FacturaConcepto','U') IS NULL
CREATE TABLE dbo.FacturaConcepto(
 idConcepto int IDENTITY PRIMARY KEY,
 idFactura int NOT NULL REFERENCES dbo.Factura(idFactura),
 Tipo varchar(20) NOT NULL CHECK(Tipo IN ('SEPELIO','ORTOPEDIA','OTRO')),
 Descripcion nvarchar(200) NOT NULL,
 Importe decimal(12,2) NOT NULL
);
COMMIT;
