SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.AltaSocioRevision', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AltaSocioRevision (
        idRevision int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        idSocio int NOT NULL REFERENCES dbo.AltaSocio(idSocio),
        Estado nvarchar(30) NOT NULL CHECK (Estado IN ('Aprobada', 'Rechazada')),
        idMotivo int NULL REFERENCES dbo.MotivoRechazoAltaSocio(idMotivo),
        idUsuarioRevisor int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_AltaSocioRevision_Socio
        ON dbo.AltaSocioRevision(idSocio, FechaUtc DESC);
END;

COMMIT;
