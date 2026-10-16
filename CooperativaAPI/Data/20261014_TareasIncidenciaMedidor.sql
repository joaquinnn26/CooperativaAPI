-- Vinculo explicito entre las incidencias y los trabajos para atenderlas.
-- Conserva el historial existente; no infiere relaciones entre eventos antiguos.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.IncidenciaMedidor','U') IS NULL
 THROW 51001,'Aplicar primero la migracion de incidencias de medidor.',1;
IF OBJECT_ID('dbo.IncidenciaMedidorTarea','U') IS NULL
BEGIN
 CREATE TABLE dbo.IncidenciaMedidorTarea(
  idTarea int NOT NULL PRIMARY KEY REFERENCES dbo.TareaLaboral(idTarea),
  idIncidencia bigint NOT NULL REFERENCES dbo.IncidenciaMedidor(idIncidencia),
  FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
  idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema)
 );
 CREATE INDEX IX_IncidenciaMedidorTarea_Incidencia ON dbo.IncidenciaMedidorTarea(idIncidencia,idTarea);
END;
COMMIT TRANSACTION;
