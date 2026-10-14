/* Relaciones explícitas entre una falla y su resolución; sin backfill inferido. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.IncidenciaMedidor','U') IS NULL
BEGIN
    CREATE TABLE dbo.IncidenciaMedidor(
        idIncidencia bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IncidenciaMedidor PRIMARY KEY,
        idMedidor int NOT NULL CONSTRAINT FK_IncidenciaMedidor_Medidor REFERENCES dbo.MedidorEquipo(idMedidor),
        idEventoDeteccion bigint NOT NULL CONSTRAINT FK_IncidenciaMedidor_EventoDeteccion REFERENCES dbo.EventoMedidor(idEvento),
        idEventoUltimaDeteccion bigint NOT NULL CONSTRAINT FK_IncidenciaMedidor_EventoUltimaDeteccion REFERENCES dbo.EventoMedidor(idEvento),
        FechaAperturaUtc datetime2(3) NOT NULL,
        FechaUltimaDeteccionUtc datetime2(3) NOT NULL,
        idMotivo int NULL CONSTRAINT FK_IncidenciaMedidor_Motivo REFERENCES dbo.MotivoMedidor(idMotivo),
        idUsuarioResponsable int NULL CONSTRAINT FK_IncidenciaMedidor_Usuario REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        idEventoResolucion bigint NULL CONSTRAINT FK_IncidenciaMedidor_EventoResolucion REFERENCES dbo.EventoMedidor(idEvento),
        FechaResolucionUtc datetime2(3) NULL,
        TipoResolucion nvarchar(30) NULL,
        idUsuarioResolucion int NULL CONSTRAINT FK_IncidenciaMedidor_UsuarioResolucion REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        CONSTRAINT CK_IncidenciaMedidor_Resolucion CHECK((idEventoResolucion IS NULL AND FechaResolucionUtc IS NULL AND TipoResolucion IS NULL) OR (idEventoResolucion IS NOT NULL AND FechaResolucionUtc IS NOT NULL AND TipoResolucion IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_IncidenciaMedidor_Abierta ON dbo.IncidenciaMedidor(idMedidor) WHERE idEventoResolucion IS NULL;
    CREATE INDEX IX_IncidenciaMedidor_Fecha ON dbo.IncidenciaMedidor(FechaAperturaUtc,FechaResolucionUtc) INCLUDE(idMedidor,idMotivo,TipoResolucion);
END;
COMMIT TRANSACTION;
GO
