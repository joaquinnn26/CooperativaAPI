-- Migración aditiva. Ejecutar después del respaldo verificado, en Cooperativa.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.UbicacionServicio','Localidad') IS NULL
BEGIN
 ALTER TABLE dbo.UbicacionServicio ADD Localidad nvarchar(100) NULL, Ambito nvarchar(10) NULL,
 Ruta nvarchar(30) NULL, Folio nvarchar(30) NULL;
END;
IF COL_LENGTH('dbo.Socio','NumeroSocio') IS NULL
 ALTER TABLE dbo.Socio ADD NumeroSocio nvarchar(40) NULL;
-- Los identificadores internos NO se convierten en números oficiales de socio.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Socio_NumeroOficial' AND object_id=OBJECT_ID('dbo.Socio'))
 EXEC(N'CREATE UNIQUE INDEX UX_Socio_NumeroOficial ON dbo.Socio(NumeroSocio) WHERE NumeroSocio IS NOT NULL');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Ubicacion_Ambito')
 EXEC(N'ALTER TABLE dbo.UbicacionServicio ADD CONSTRAINT CK_Ubicacion_Ambito CHECK(Ambito IS NULL OR Ambito IN (''Urbano'',''Rural''))');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Ubicacion_RutaFolio')
 EXEC(N'ALTER TABLE dbo.UbicacionServicio ADD CONSTRAINT CK_Ubicacion_RutaFolio CHECK((Ruta IS NULL AND Folio IS NULL) OR (LEN(LTRIM(RTRIM(Ruta)))>0 AND LEN(LTRIM(RTRIM(Folio)))>0 AND Ruta IS NOT NULL AND Folio IS NOT NULL))');
IF OBJECT_ID('dbo.SolicitudConexion') IS NULL
 CREATE TABLE dbo.SolicitudConexion(idSolicitudServicio int PRIMARY KEY REFERENCES dbo.SolicitudServicio(idSolicitudServicio),idServicioSocio int NOT NULL UNIQUE REFERENCES dbo.ServicioSocio(idServicioSocio));

IF OBJECT_ID('dbo.FichaMedidor') IS NULL
BEGIN
 ALTER TABLE dbo.Medidor ALTER COLUMN idServicioSocio int NULL;
 CREATE TABLE dbo.FichaMedidor(
  idMedidor int NOT NULL PRIMARY KEY REFERENCES dbo.Medidor(idMedidor),
  Marca nvarchar(100) NULL, Modelo nvarchar(100) NULL,
  IdentidadVerificada bit NOT NULL DEFAULT 0,
  EstadoFisico nvarchar(30) NOT NULL CHECK(EstadoFisico IN ('Operativo','PendienteRevision','Averiado','EnReparacion','BajaDefinitiva')),
  NumeroAnterior nvarchar(100) NULL,
  Version rowversion NOT NULL
 );
 CREATE TABLE dbo.MotivoMedidor(
  idMotivo int IDENTITY PRIMARY KEY, Nombre nvarchar(100) NOT NULL,
  Accion nvarchar(30) NOT NULL CHECK(Accion IN ('Alta','Verificar','Instalar','Retirar','Falla','Reparacion','Revision','BajaDefinitiva')),
  Activo bit NOT NULL DEFAULT 1, Version rowversion NOT NULL,
  CONSTRAINT UQ_MotivoMedidor UNIQUE(Accion,Nombre)
 );
 CREATE TABLE dbo.AsignacionMedidor(
  idAsignacion int IDENTITY PRIMARY KEY,
  idMedidor int NOT NULL REFERENCES dbo.Medidor(idMedidor),
  idServicioSocio int NOT NULL REFERENCES dbo.ServicioSocio(idServicioSocio),
  idSocio int NOT NULL REFERENCES dbo.Socio(idSocio),
  FechaInicio datetime2 NULL, FechaFin datetime2 NULL,
  Cerrada bit NOT NULL DEFAULT 0,
  LecturaInicial decimal(18,2) NULL, LecturaFinal decimal(18,2) NULL,
  Origen nvarchar(30) NOT NULL,
  PendienteConciliacion bit NOT NULL DEFAULT 0,
  CHECK(PendienteConciliacion=0 OR Origen='MigracionSinVerificar'),
  CHECK(FechaFin IS NULL OR FechaInicio IS NULL OR FechaFin>=FechaInicio),
  CHECK(Cerrada=1 OR FechaFin IS NULL),
  CHECK(Origen='MigracionSinVerificar' OR Cerrada=0 OR FechaFin IS NOT NULL),
  CHECK(LecturaInicial IS NULL OR LecturaInicial>=0),
  CHECK(LecturaFinal IS NULL OR LecturaFinal>=ISNULL(LecturaInicial,0))
 );
 CREATE UNIQUE INDEX UX_AsignacionMedidor_Activa ON dbo.AsignacionMedidor(idMedidor) WHERE Cerrada=0;
 CREATE UNIQUE INDEX UX_AsignacionConexion_Activa ON dbo.AsignacionMedidor(idServicioSocio) WHERE Cerrada=0 AND PendienteConciliacion=0;
 CREATE TABLE dbo.EventoMedidor(
  idEvento bigint IDENTITY PRIMARY KEY,
  idMedidor int NOT NULL REFERENCES dbo.Medidor(idMedidor),
  FechaUtc datetime2 NOT NULL, FechaRegistroUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
  Accion nvarchar(30) NOT NULL,
  idUsuarioSistema int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
  idMotivo int NULL REFERENCES dbo.MotivoMedidor(idMotivo),
  Motivo nvarchar(100) NOT NULL, Detalle nvarchar(1000) NULL,
  idAsignacion int NULL REFERENCES dbo.AsignacionMedidor(idAsignacion),
  ReferenciaTrabajo nvarchar(100) NULL,
  Anterior nvarchar(max) NULL, Nuevo nvarchar(max) NOT NULL
 );
 CREATE INDEX IX_EventoMedidor_Cronologia ON dbo.EventoMedidor(idMedidor,FechaUtc,idEvento);
 INSERT dbo.FichaMedidor(idMedidor,EstadoFisico,NumeroAnterior)
 SELECT idMedidor,'PendienteRevision',Numero FROM dbo.Medidor;
 INSERT dbo.AsignacionMedidor(idMedidor,idServicioSocio,idSocio,FechaInicio,Origen,Cerrada,PendienteConciliacion)
 SELECT m.idMedidor,m.idServicioSocio,s.idSocio,m.FechaInstalacion,'MigracionSinVerificar',CASE WHEN m.Activo=1 THEN 0 ELSE 1 END,
 CASE WHEN (SELECT COUNT(*) FROM dbo.Medidor otro WHERE otro.idServicioSocio=m.idServicioSocio AND otro.Activo=1)>1 THEN 1 ELSE 0 END
 FROM dbo.Medidor m JOIN dbo.ServicioSocio s ON s.idServicioSocio=m.idServicioSocio;
 INSERT dbo.EventoMedidor(idMedidor,FechaUtc,Accion,Motivo,Detalle,Nuevo)
 SELECT idMedidor,SYSUTCDATETIME(),'Migracion','Registro anterior sin verificar',
 'No acredita identidad física ni reconstruye instalaciones anteriores. Se conservan lecturas y referencias originales.',
 '{"IdentidadVerificada":false,"EstadoFisico":"PendienteRevision"}' FROM dbo.Medidor;
END;
-- Las rutas anteriores conservan la conexión que tenían antes de un retiro o reutilización.
IF OBJECT_ID('dbo.ContextoRutaMedidor') IS NULL
BEGIN
 CREATE TABLE dbo.ContextoRutaMedidor(
 idRutaMedicionDetalle int PRIMARY KEY REFERENCES dbo.RutaMedicionDetalle(idRutaMedicionDetalle),
 idSocio int NULL, SocioNombre nvarchar(250) NULL, Documento nvarchar(50) NULL,
 ServicioNombre nvarchar(100) NULL, NumeroMedidor nvarchar(100) NULL);
 INSERT dbo.ContextoRutaMedidor
 SELECT d.idRutaMedicionDetalle,s.idSocio,CONCAT(p.Nombre,' ',p.Apellido),p.Documento,se.Nombre,m.Numero
 FROM dbo.RutaMedicionDetalle d JOIN dbo.Medidor m ON m.idMedidor=d.idMedidor
 LEFT JOIN dbo.ServicioSocio s ON s.idServicioSocio=m.idServicioSocio
 LEFT JOIN dbo.Socio so ON so.idSocio=s.idSocio LEFT JOIN dbo.Persona p ON p.idPersona=so.idPersona
 LEFT JOIN dbo.Servicio se ON se.idServicio=s.idServicio;
END;
COMMIT;
GO
CREATE OR ALTER TRIGGER dbo.TR_ContextoRutaMedidor ON dbo.RutaMedicionDetalle AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.ContextoRutaMedidor
 SELECT d.idRutaMedicionDetalle,s.idSocio,CONCAT(p.Nombre,' ',p.Apellido),p.Documento,se.Nombre,m.Numero
 FROM inserted d JOIN dbo.Medidor m ON m.idMedidor=d.idMedidor
 LEFT JOIN dbo.ServicioSocio s ON s.idServicioSocio=m.idServicioSocio
 LEFT JOIN dbo.Socio so ON so.idSocio=s.idSocio LEFT JOIN dbo.Persona p ON p.idPersona=so.idPersona
 LEFT JOIN dbo.Servicio se ON se.idServicio=s.idServicio;
END;
GO
CREATE OR ALTER TRIGGER dbo.TR_AsignacionMedidor_Conflictos ON dbo.AsignacionMedidor AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.AsignacionMedidor a ON a.idServicioSocio=i.idServicioSocio AND a.idAsignacion<>i.idAsignacion
 WHERE i.Cerrada=0 AND a.Cerrada=0 AND i.PendienteConciliacion=0)
 THROW 51001,'La conexión ya tiene otra instalación o una asignación heredada pendiente de conciliación.',1;
END;
GO
