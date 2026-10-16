USE Cooperativa;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Medicion','VersionRevision') IS NULL
 ALTER TABLE dbo.Medicion ADD VersionRevision rowversion;
IF OBJECT_ID('dbo.AprobacionLecturaLaboral','U') IS NULL
BEGIN
 CREATE TABLE dbo.AprobacionLecturaLaboral(
  idAprobacion bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
  idMedicion int NOT NULL REFERENCES dbo.Medicion(idMedicion),
  VersionLectura binary(8) NOT NULL,
  idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
  FechaUtc datetime2 NOT NULL CONSTRAINT DF_AprobacionLectura_Fecha DEFAULT SYSUTCDATETIME(),
  ValorLectura decimal(10,2) NOT NULL,
  ConsumoCalculado decimal(10,2) NULL,
  CONSTRAINT UQ_AprobacionLectura_Version UNIQUE(idMedicion,VersionLectura)
 );
END;
-- Las lecturas históricas quedan sin aprobación; nunca se infiere una revisión.
COMMIT;
