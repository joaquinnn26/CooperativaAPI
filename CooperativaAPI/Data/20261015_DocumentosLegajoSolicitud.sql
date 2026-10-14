SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* Referencia documentos de identidad/domicilio ya guardados en el legajo,
   sin duplicar su contenido y conservando quién y cuándo los compartió. */
IF OBJECT_ID('dbo.SolicitudDocumentoLegajo','U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudDocumentoLegajo(
        idSolicitudServicio int NOT NULL,
        idDocumentoPersona int NOT NULL,
        idUsuarioCompartio int NOT NULL,
        FechaUtc datetime2(3) NOT NULL CONSTRAINT DF_SolicitudDocumentoLegajo_FechaUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_SolicitudDocumentoLegajo PRIMARY KEY(idSolicitudServicio,idDocumentoPersona),
        CONSTRAINT FK_SolicitudDocumentoLegajo_Solicitud FOREIGN KEY(idSolicitudServicio) REFERENCES dbo.SolicitudServicio(idSolicitudServicio),
        CONSTRAINT FK_SolicitudDocumentoLegajo_Documento FOREIGN KEY(idDocumentoPersona) REFERENCES dbo.DocumentoPersona(idDocumentoPersona),
        CONSTRAINT FK_SolicitudDocumentoLegajo_Usuario FOREIGN KEY(idUsuarioCompartio) REFERENCES dbo.UsuarioSistema(idUsuarioSistema)
    );
    CREATE INDEX IX_SolicitudDocumentoLegajo_Documento ON dbo.SolicitudDocumentoLegajo(idDocumentoPersona);
END;

COMMIT TRANSACTION;
