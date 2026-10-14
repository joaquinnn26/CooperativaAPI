SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.LoteFacturacionInternet','U') IS NULL
BEGIN
 CREATE TABLE dbo.LoteFacturacionInternet(
  idLote int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoteFacturacionInternet PRIMARY KEY,
  Periodo char(7) NOT NULL,
  Estado nvarchar(20) NOT NULL CONSTRAINT DF_LoteFacturacionInternet_Estado DEFAULT 'Exportado'
   CONSTRAINT CK_LoteFacturacionInternet_Estado CHECK(Estado IN('Exportado','Validado','Aplicado','Anulado')),
  NombreArchivoSalida nvarchar(260) NOT NULL,
  HashSalida char(64) NOT NULL,
  ContenidoSalida nvarchar(max) NOT NULL,
  NombreArchivoRespuesta nvarchar(260) NULL,
  HashRespuesta char(64) NULL,
  ContenidoRespuesta nvarchar(max) NULL,
  Errores nvarchar(max) NULL,
  CantidadFilas int NOT NULL,
  idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
  FechaCreacionUtc datetime2(3) NOT NULL CONSTRAINT DF_LoteFacturacionInternet_Creado DEFAULT SYSUTCDATETIME(),
  FechaValidacionUtc datetime2(3) NULL,
  FechaAplicacionUtc datetime2(3) NULL,
  MotivoAnulacion nvarchar(500) NULL,
  CONSTRAINT CK_LoteFacturacionInternet_Periodo CHECK(Periodo LIKE '[12][0-9][0-9][0-9]-[0-1][0-9]' AND RIGHT(Periodo,2) BETWEEN '01' AND '12')
 );
 CREATE UNIQUE INDEX UX_LoteFacturacionInternet_Periodo ON dbo.LoteFacturacionInternet(Periodo) WHERE Estado<>'Anulado';
END;

IF OBJECT_ID('dbo.LoteFacturacionInternetDetalle','U') IS NULL
BEGIN
 CREATE TABLE dbo.LoteFacturacionInternetDetalle(
  idLote int NOT NULL REFERENCES dbo.LoteFacturacionInternet(idLote),
  idServicioSocio int NOT NULL REFERENCES dbo.ServicioSocio(idServicioSocio),
  idSocio int NOT NULL REFERENCES dbo.Socio(idSocio),
  idServicio int NOT NULL REFERENCES dbo.Servicio(idServicio),
  CodigoSocioExterno nvarchar(80) NOT NULL,
  VigenteDesde date NOT NULL,
  VigenteHasta date NOT NULL,
  ImporteExportado decimal(18,2) NOT NULL,
  Resultado nvarchar(20) NULL CONSTRAINT CK_LoteFacturacionInternetDetalle_Resultado CHECK(Resultado IS NULL OR Resultado IN('FACTURADA','RECHAZADA')),
  ReferenciaFacturaExterna nvarchar(200) NULL,
  NumeroFactura nvarchar(100) NULL,
  FechaEmision date NULL,
  ImporteFactura decimal(18,2) NULL,
  FechaVencimiento date NULL,
  LinkPago nvarchar(500) NULL,
  MotivoRechazo nvarchar(1000) NULL,
  idFactura int NULL REFERENCES dbo.Factura(idFactura),
  CONSTRAINT PK_LoteFacturacionInternetDetalle PRIMARY KEY(idLote,idServicioSocio),
  CONSTRAINT CK_LoteFacturacionInternetDetalle_Fechas CHECK(VigenteHasta>=VigenteDesde),
  CONSTRAINT CK_LoteFacturacionInternetDetalle_Factura CHECK(
   (Resultado IS NULL AND ReferenciaFacturaExterna IS NULL AND idFactura IS NULL) OR
   (Resultado='RECHAZADA' AND MotivoRechazo IS NOT NULL AND idFactura IS NULL) OR
   (Resultado='FACTURADA' AND ReferenciaFacturaExterna IS NOT NULL AND NumeroFactura IS NOT NULL AND FechaEmision IS NOT NULL AND ImporteFactura>0 AND FechaVencimiento IS NOT NULL AND LinkPago IS NOT NULL)
  )
 );
 CREATE UNIQUE INDEX UX_LoteFacturacionInternetDetalle_Referencia ON dbo.LoteFacturacionInternetDetalle(ReferenciaFacturaExterna) WHERE ReferenciaFacturaExterna IS NOT NULL;
 CREATE UNIQUE INDEX UX_LoteFacturacionInternetDetalle_Factura ON dbo.LoteFacturacionInternetDetalle(idFactura)
  WHERE idFactura IS NOT NULL;
END;

COMMIT;
