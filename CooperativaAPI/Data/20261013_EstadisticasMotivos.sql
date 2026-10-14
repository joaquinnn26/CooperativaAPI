/* Historial normalizado para decisiones documentales y referencias a motivos.
   No se migran textos históricos: su equivalencia requiere revisión explícita. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.DocumentoPersona','idMotivoDocumento') IS NULL
    ALTER TABLE dbo.DocumentoPersona ADD idMotivoDocumento int NULL;
IF COL_LENGTH('dbo.DocumentoPersona','idMotivoAltaSocio') IS NULL
    ALTER TABLE dbo.DocumentoPersona ADD idMotivoAltaSocio int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DocumentoPersona_MotivoDocumentoPersona')
    ALTER TABLE dbo.DocumentoPersona ADD CONSTRAINT FK_DocumentoPersona_MotivoDocumentoPersona FOREIGN KEY(idMotivoDocumento) REFERENCES dbo.MotivoDocumentoPersona(idMotivo);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_DocumentoPersona_MotivoRechazoAltaSocio')
    ALTER TABLE dbo.DocumentoPersona ADD CONSTRAINT FK_DocumentoPersona_MotivoRechazoAltaSocio FOREIGN KEY(idMotivoAltaSocio) REFERENCES dbo.MotivoRechazoAltaSocio(idMotivo);

IF OBJECT_ID('dbo.HistorialDocumentoPersona','U') IS NULL
BEGIN
    CREATE TABLE dbo.HistorialDocumentoPersona(
        idHistorialDocumentoPersona bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_HistorialDocumentoPersona PRIMARY KEY,
        idDocumentoPersona int NOT NULL CONSTRAINT FK_HistorialDocumentoPersona_DocumentoPersona REFERENCES dbo.DocumentoPersona(idDocumentoPersona),
        EstadoAnterior nvarchar(20) NULL,
        EstadoNuevo nvarchar(20) NOT NULL,
        idMotivoDocumento int NULL CONSTRAINT FK_HistorialDocumentoPersona_MotivoDocumentoPersona REFERENCES dbo.MotivoDocumentoPersona(idMotivo),
        idMotivoAltaSocio int NULL CONSTRAINT FK_HistorialDocumentoPersona_MotivoAltaSocio REFERENCES dbo.MotivoRechazoAltaSocio(idMotivo),
        idUsuarioResponsable int NOT NULL CONSTRAINT FK_HistorialDocumentoPersona_UsuarioSistema REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        FechaUtc datetime2(3) NOT NULL CONSTRAINT DF_HistorialDocumentoPersona_FechaUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_HistorialDocumentoPersona_UnMotivo CHECK(idMotivoDocumento IS NULL OR idMotivoAltaSocio IS NULL)
    );
    CREATE INDEX IX_HistorialDocumentoPersona_FechaEstado ON dbo.HistorialDocumentoPersona(FechaUtc,EstadoNuevo) INCLUDE(idDocumentoPersona,idMotivoDocumento,idMotivoAltaSocio,idUsuarioResponsable);
END;

IF COL_LENGTH('dbo.AuditoriaLaboral','idMotivoLaboral') IS NULL
    ALTER TABLE dbo.AuditoriaLaboral ADD idMotivoLaboral int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_AuditoriaLaboral_MotivoLaboral')
    ALTER TABLE dbo.AuditoriaLaboral ADD CONSTRAINT FK_AuditoriaLaboral_MotivoLaboral FOREIGN KEY(idMotivoLaboral) REFERENCES dbo.MotivoLaboral(idMotivo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AuditoriaLaboral') AND name='IX_AuditoriaLaboral_Motivo_Fecha')
    CREATE INDEX IX_AuditoriaLaboral_Motivo_Fecha ON dbo.AuditoriaLaboral(idMotivoLaboral,FechaUtc) INCLUDE(Area,Accion,EstadoAnterior,EstadoNuevo,idUsuarioSistema,idTarea);

COMMIT TRANSACTION;
GO
CREATE OR ALTER TRIGGER dbo.TR_DocumentoPersona_HistorialEstado
ON dbo.DocumentoPersona
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.HistorialDocumentoPersona(idDocumentoPersona,EstadoAnterior,EstadoNuevo,idMotivoDocumento,idMotivoAltaSocio,idUsuarioResponsable)
    SELECT i.idDocumentoPersona,d.Estado,i.Estado,i.idMotivoDocumento,i.idMotivoAltaSocio,COALESCE(i.idUsuarioRevisor,i.idUsuarioCarga)
    FROM inserted i LEFT JOIN deleted d ON d.idDocumentoPersona=i.idDocumentoPersona
    WHERE (d.idDocumentoPersona IS NULL OR ISNULL(d.Estado,N'')<>ISNULL(i.Estado,N''))
      AND COALESCE(i.idUsuarioRevisor,i.idUsuarioCarga) IS NOT NULL;
END;
GO
