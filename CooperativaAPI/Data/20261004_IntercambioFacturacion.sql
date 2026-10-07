/* Intercambio versionado con el sistema contable. Idempotente: aplicar una vez por ambiente. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Factura','idServicioSocio') IS NULL
    ALTER TABLE dbo.Factura ADD idServicioSocio int NULL;
IF COL_LENGTH('dbo.Factura','CodigoExternoContable') IS NULL
    ALTER TABLE dbo.Factura ADD CodigoExternoContable nvarchar(100) NULL;
IF COL_LENGTH('dbo.Factura','Periodo') IS NULL
    ALTER TABLE dbo.Factura ADD Periodo varchar(10) NULL;
IF COL_LENGTH('dbo.Factura','idMedicion') IS NULL
    ALTER TABLE dbo.Factura ADD idMedicion int NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ServicioSocio') AND name='UX_ServicioSocio_IdentidadFactura')
    EXEC(N'CREATE UNIQUE INDEX UX_ServicioSocio_IdentidadFactura ON dbo.ServicioSocio(idServicioSocio,idSocio,idServicio)');
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Factura_ServicioSocio')
    ALTER TABLE dbo.Factura DROP CONSTRAINT FK_Factura_ServicioSocio;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Factura_ServicioSocio_Consistente')
    ALTER TABLE dbo.Factura ADD CONSTRAINT FK_Factura_ServicioSocio_Consistente
        FOREIGN KEY (idServicioSocio,idSocio,idServicio)
        REFERENCES dbo.ServicioSocio(idServicioSocio,idSocio,idServicio);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Medicion') AND name='UX_Medicion_ConexionFactura')
    EXEC(N'CREATE UNIQUE INDEX UX_Medicion_ConexionFactura ON dbo.Medicion(idMedicion,idServicioSocio)');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Factura_Medicion_Conexion')
    ALTER TABLE dbo.Factura ADD CONSTRAINT FK_Factura_Medicion_Conexion
        FOREIGN KEY (idMedicion,idServicioSocio) REFERENCES dbo.Medicion(idMedicion,idServicioSocio);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Factura') AND name='UX_Factura_CodigoExternoContable')
    EXEC(N'CREATE UNIQUE INDEX UX_Factura_CodigoExternoContable ON dbo.Factura(CodigoExternoContable) WHERE CodigoExternoContable IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Factura') AND name='IX_Factura_ServicioSocio_Periodo')
    EXEC(N'CREATE INDEX IX_Factura_ServicioSocio_Periodo ON dbo.Factura(idServicioSocio,Periodo)');

IF OBJECT_ID('dbo.LoteExportacionContable','U') IS NULL
CREATE TABLE dbo.LoteExportacionContable(
    idLoteExportacion int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoteExportacionContable PRIMARY KEY,
    Periodo varchar(10) NOT NULL,
    FormatoVersion varchar(20) NOT NULL CONSTRAINT DF_LoteExportacion_Formato DEFAULT 'SOCIOAPP-1',
    FechaCreacionUtc datetime2(3) NOT NULL CONSTRAINT DF_LoteExportacion_Fecha DEFAULT SYSUTCDATETIME(),
    idUsuarioSistema int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
    CantidadMediciones int NOT NULL CONSTRAINT DF_LoteExportacion_Cantidad DEFAULT 0,
    Estado varchar(20) NOT NULL CONSTRAINT DF_LoteExportacion_Estado DEFAULT 'Generado',
    CONSTRAINT CK_LoteExportacion_Estado CHECK (Estado IN ('Generado','Entregado','Cancelado'))
);

IF OBJECT_ID('dbo.LoteExportacionContableDetalle','U') IS NULL
CREATE TABLE dbo.LoteExportacionContableDetalle(
    idLoteExportacion int NOT NULL REFERENCES dbo.LoteExportacionContable(idLoteExportacion),
    idMedicion int NOT NULL REFERENCES dbo.Medicion(idMedicion),
    idServicioSocio int NOT NULL REFERENCES dbo.ServicioSocio(idServicioSocio),
    DatosExportados nvarchar(max) NOT NULL,
    CONSTRAINT PK_LoteExportacionContableDetalle PRIMARY KEY(idLoteExportacion,idMedicion)
);

IF OBJECT_ID('dbo.LoteImportacionFactura','U') IS NULL
CREATE TABLE dbo.LoteImportacionFactura(
    idLoteImportacion int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoteImportacionFactura PRIMARY KEY,
    NombreArchivo nvarchar(260) NULL,
    HashContenido char(64) NOT NULL,
    FormatoVersion varchar(20) NOT NULL CONSTRAINT DF_LoteImportacion_Formato DEFAULT 'SOCIOAPP-1',
    FechaCreacionUtc datetime2(3) NOT NULL CONSTRAINT DF_LoteImportacion_Fecha DEFAULT SYSUTCDATETIME(),
    idUsuarioSistema int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
    Estado varchar(20) NOT NULL CONSTRAINT DF_LoteImportacion_Estado DEFAULT 'Recibido',
    CantidadFilas int NOT NULL CONSTRAINT DF_LoteImportacion_Cantidad DEFAULT 0,
    CantidadValidas int NOT NULL CONSTRAINT DF_LoteImportacion_Validas DEFAULT 0,
    CantidadAplicadas int NOT NULL CONSTRAINT DF_LoteImportacion_Aplicadas DEFAULT 0,
    CONSTRAINT UQ_LoteImportacion_Hash UNIQUE(HashContenido),
    CONSTRAINT CK_LoteImportacion_Estado CHECK (Estado IN ('Recibido','Validado','ConErrores','Aplicado','Rechazado'))
);

IF OBJECT_ID('dbo.LoteImportacionFacturaDetalle','U') IS NULL
CREATE TABLE dbo.LoteImportacionFacturaDetalle(
    idDetalleImportacion bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoteImportacionFacturaDetalle PRIMARY KEY,
    idLoteImportacion int NOT NULL REFERENCES dbo.LoteImportacionFactura(idLoteImportacion),
    NumeroFila int NOT NULL,
    CodigoFacturaExterno nvarchar(100) NOT NULL,
    NumeroSocio nvarchar(80) NOT NULL,
    idServicioSocio int NULL,
    Periodo varchar(10) NOT NULL,
    Monto decimal(12,2) NOT NULL,
    FechaEmision date NOT NULL,
    FechaVencimiento date NOT NULL,
    LinkPago nvarchar(1000) NULL,
    idMedicion int NULL,
    Estado varchar(20) NOT NULL CONSTRAINT DF_LoteImportacionDetalle_Estado DEFAULT 'Pendiente',
    ErrorValidacion nvarchar(1000) NULL,
    idFactura int NULL REFERENCES dbo.Factura(idFactura),
    CONSTRAINT UQ_LoteImportacionDetalle_Fila UNIQUE(idLoteImportacion,NumeroFila),
    CONSTRAINT CK_LoteImportacionDetalle_Monto CHECK (Monto >= 0),
    CONSTRAINT CK_LoteImportacionDetalle_Fechas CHECK (FechaVencimiento >= FechaEmision),
    CONSTRAINT CK_LoteImportacionDetalle_Estado CHECK (Estado IN ('Pendiente','Valida','Error','Aplicada','Duplicada'))
);
DECLARE @fkMedicion sysname, @dropFk nvarchar(max);
SELECT TOP(1) @fkMedicion=fk.name FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
WHERE fk.parent_object_id=OBJECT_ID('dbo.LoteImportacionFacturaDetalle')
  AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)='idMedicion';
IF @fkMedicion IS NOT NULL
BEGIN
    SET @dropFk=N'ALTER TABLE dbo.LoteImportacionFacturaDetalle DROP CONSTRAINT '+QUOTENAME(@fkMedicion);
    EXEC(@dropFk);
END;
/* En staging se conservan identificadores externos inválidos para informar el error en la fila. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.LoteImportacionFacturaDetalle') AND name='IX_LoteImportacionDetalle_LoteEstado')
    CREATE INDEX IX_LoteImportacionDetalle_LoteEstado ON dbo.LoteImportacionFacturaDetalle(idLoteImportacion,Estado);

COMMIT TRANSACTION;
