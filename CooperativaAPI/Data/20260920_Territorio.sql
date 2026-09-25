-- Ejecutar sobre la misma base de CooperativaEntities, con respaldo previo.
-- Aditivo: no elimina ni inventa datos. Todo el esquema se crea en una transacción.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Zona', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.Zona (
  idZona INT IDENTITY PRIMARY KEY,
  Nombre NVARCHAR(100) COLLATE Latin1_General_CI_AI NOT NULL UNIQUE,
  Activo BIT NOT NULL DEFAULT 1,
  Version ROWVERSION NOT NULL
 );
END;
IF OBJECT_ID('dbo.Barrio', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.Barrio (
  idBarrio INT IDENTITY PRIMARY KEY,
  Nombre NVARCHAR(100) COLLATE Latin1_General_CI_AI NOT NULL UNIQUE,
  idZona INT NOT NULL REFERENCES dbo.Zona(idZona),
  Activo BIT NOT NULL DEFAULT 1,
  Version ROWVERSION NOT NULL
 );
 CREATE INDEX IX_Barrio_Zona ON dbo.Barrio(idZona);
END;
IF OBJECT_ID('dbo.UbicacionServicio', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.UbicacionServicio (
  idServicioSocio INT PRIMARY KEY REFERENCES dbo.ServicioSocio(idServicioSocio),
  idBarrio INT NULL REFERENCES dbo.Barrio(idBarrio),
  Direccion NVARCHAR(250) NOT NULL,
  Version ROWVERSION NOT NULL
 );
 CREATE INDEX IX_UbicacionServicio_Barrio ON dbo.UbicacionServicio(idBarrio);
END;
IF OBJECT_ID('dbo.HistorialTerritorial', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.HistorialTerritorial (
  idHistorial BIGINT IDENTITY PRIMARY KEY,
  FechaUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
  idUsuarioSistema INT NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
  Usuario NVARCHAR(200) NOT NULL,
  Entidad NVARCHAR(30) NOT NULL,
  idEntidad INT NOT NULL,
  Accion NVARCHAR(30) NOT NULL,
  Motivo NVARCHAR(300) NOT NULL,
  Anterior NVARCHAR(MAX) NULL,
  Nuevo NVARCHAR(MAX) NOT NULL
 );
 CREATE INDEX IX_HistorialTerritorial_Entidad ON dbo.HistorialTerritorial(Entidad,idEntidad,FechaUtc);
 CREATE INDEX IX_HistorialTerritorial_Usuario ON dbo.HistorialTerritorial(idUsuarioSistema,FechaUtc);
END;
COMMIT;
