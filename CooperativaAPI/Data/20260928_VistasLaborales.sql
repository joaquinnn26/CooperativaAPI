SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.AreaLaboral') IS NULL
BEGIN
 CREATE TABLE dbo.AreaLaboral(Codigo nvarchar(30) NOT NULL PRIMARY KEY,Nombre nvarchar(120) NOT NULL,Operativa bit NOT NULL,Version rowversion);
 INSERT dbo.AreaLaboral(Codigo,Nombre,Operativa) VALUES
 ('ADMINISTRACION',N'Administración',0),('AGUA',N'Redes de Agua Potable',1),('ENERGIA',N'Redes de Energía',1),('INTERNET',N'Redes de Internet',1),('GAS',N'Atención de Gas Natural por convenio',0),('GERENCIA',N'Gerencia',0);
 CREATE TABLE dbo.AreaServicio(idServicio int NOT NULL PRIMARY KEY REFERENCES dbo.Servicio(idServicio),Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo));
 INSERT dbo.AreaServicio SELECT idServicio,CASE Nombre WHEN 'Luz' THEN 'ENERGIA' WHEN 'Agua Potable' THEN 'AGUA' WHEN 'Internet' THEN 'INTERNET' END FROM dbo.Servicio WHERE Nombre IN ('Luz','Agua Potable','Internet');
 CREATE TABLE dbo.EmpleadoArea(idEmpleadoArea int IDENTITY PRIMARY KEY,idEmpleado int NOT NULL REFERENCES dbo.Empleado(idEmpleado),Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),DesdeUtc datetime2(3) NOT NULL,HastaUtc datetime2(3) NULL,Version rowversion,CONSTRAINT CK_EmpleadoArea_Fechas CHECK(HastaUtc IS NULL OR HastaUtc>=DesdeUtc));
 CREATE UNIQUE INDEX UX_EmpleadoArea_Vigente ON dbo.EmpleadoArea(idEmpleado,Area) WHERE HastaUtc IS NULL;
 CREATE TABLE dbo.FuncionLaboral(Codigo nvarchar(30) NOT NULL PRIMARY KEY,Nombre nvarchar(100) NOT NULL);
 INSERT dbo.FuncionLaboral VALUES('OPERARIO',N'Trabajos asignados'),('LECTURISTA',N'Hojas de ruta y lecturas'),('ATENCION',N'Atención administrativa'),('FACTURACION',N'Facturación de su área'),('CONSULTA_GENERAL',N'Consulta de gerencia');
 CREATE TABLE dbo.FuncionArea(Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),Funcion nvarchar(30) NOT NULL REFERENCES dbo.FuncionLaboral(Codigo),PRIMARY KEY(Area,Funcion));
 INSERT dbo.FuncionArea VALUES('ADMINISTRACION','ATENCION'),('ADMINISTRACION','FACTURACION'),('AGUA','OPERARIO'),('AGUA','LECTURISTA'),('ENERGIA','OPERARIO'),('ENERGIA','LECTURISTA'),('INTERNET','OPERARIO'),('INTERNET','FACTURACION'),('GERENCIA','CONSULTA_GENERAL');
 CREATE TABLE dbo.EmpleadoFuncion(idEmpleadoFuncion int IDENTITY PRIMARY KEY,idEmpleadoArea int NOT NULL REFERENCES dbo.EmpleadoArea(idEmpleadoArea),Funcion nvarchar(30) NOT NULL REFERENCES dbo.FuncionLaboral(Codigo),DesdeUtc datetime2(3) NOT NULL,HastaUtc datetime2(3) NULL,Version rowversion,CONSTRAINT CK_EmpleadoFuncion_Fechas CHECK(HastaUtc IS NULL OR HastaUtc>=DesdeUtc));
 CREATE UNIQUE INDEX UX_EmpleadoFuncion_Vigente ON dbo.EmpleadoFuncion(idEmpleadoArea,Funcion) WHERE HastaUtc IS NULL;
 CREATE TABLE dbo.EncargadoArea(idEncargadoArea int IDENTITY PRIMARY KEY,Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),idEmpleado int NOT NULL REFERENCES dbo.Empleado(idEmpleado),DesdeUtc datetime2(3) NOT NULL,HastaUtc datetime2(3) NULL,Motivo nvarchar(500) NOT NULL,Version rowversion,CONSTRAINT CK_EncargadoArea_Fechas CHECK(HastaUtc IS NULL OR HastaUtc>=DesdeUtc));
 CREATE UNIQUE INDEX UX_EncargadoArea_Vigente ON dbo.EncargadoArea(Area) WHERE HastaUtc IS NULL;
 CREATE TABLE dbo.MotivoLaboral(idMotivo int IDENTITY PRIMARY KEY,Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),Accion nvarchar(30) NOT NULL CHECK(Accion IN ('Rechazar','Devolver','CorregirLectura')),Nombre nvarchar(150) NOT NULL,Activo bit NOT NULL DEFAULT 1,Version rowversion,UNIQUE(Area,Accion,Nombre));
 CREATE TABLE dbo.TareaLaboral(idTarea int IDENTITY PRIMARY KEY,Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),Categoria nvarchar(30) NOT NULL CHECK(Categoria IN ('Trabajo','Solicitud','Instalacion','Reclamo','Documentacion','Ruta')),OrigenId int NULL,idSocio int NULL REFERENCES dbo.Socio(idSocio),idServicioSocio int NULL REFERENCES dbo.ServicioSocio(idServicioSocio),idEmpleadoAsignado int NULL REFERENCES dbo.Empleado(idEmpleado),Titulo nvarchar(200) NOT NULL,Estado nvarchar(30) NOT NULL CHECK(Estado IN ('PendienteAsignacion','Asignada','EnCurso','EnRevision','Observada','Aprobada','Rechazada','Cerrada')),Resultado nvarchar(2000) NULL,DatosResultado nvarchar(max) NULL,FechaCreacionUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),FechaActualizacionUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),FechaCierreUtc datetime2(3) NULL,Version rowversion);
 CREATE UNIQUE INDEX UX_TareaLaboral_Origen ON dbo.TareaLaboral(Area,Categoria,OrigenId) WHERE OrigenId IS NOT NULL;
 CREATE INDEX IX_TareaLaboral_Equipo ON dbo.TareaLaboral(Area,idEmpleadoAsignado,Estado);
 CREATE TABLE dbo.AuditoriaLaboral(idAuditoria bigint IDENTITY PRIMARY KEY,idTarea int NULL REFERENCES dbo.TareaLaboral(idTarea),Area nvarchar(30) NULL REFERENCES dbo.AreaLaboral(Codigo),idUsuarioSistema int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),idEmpleado int NULL REFERENCES dbo.Empleado(idEmpleado),FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),Accion nvarchar(60) NOT NULL,EstadoAnterior nvarchar(30) NULL,EstadoNuevo nvarchar(30) NULL,Motivo nvarchar(1000) NULL,Datos nvarchar(max) NULL);
 CREATE INDEX IX_AuditoriaLaboral_Tarea ON dbo.AuditoriaLaboral(idTarea,idAuditoria);
 CREATE TABLE dbo.EvidenciaLaboral(idEvidencia int IDENTITY PRIMARY KEY,idTarea int NOT NULL REFERENCES dbo.TareaLaboral(idTarea),idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),Nombre nvarchar(150) NOT NULL,Tipo nvarchar(50) NOT NULL,Contenido varbinary(max) NOT NULL);
 CREATE TABLE dbo.TipoDocumentoSocio(idTipoDocumento int IDENTITY PRIMARY KEY,Nombre nvarchar(100) NOT NULL UNIQUE,Activo bit NOT NULL DEFAULT 1);
 CREATE TABLE dbo.DocumentoSocioLaboral(idDocumento int IDENTITY PRIMARY KEY,idSocio int NOT NULL REFERENCES dbo.Socio(idSocio),idTipoDocumento int NOT NULL REFERENCES dbo.TipoDocumentoSocio(idTipoDocumento),Nombre nvarchar(150) NOT NULL,Tipo nvarchar(50) NOT NULL,Contenido varbinary(max) NOT NULL,FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),Estado nvarchar(30) NOT NULL DEFAULT 'Pendiente',Motivo nvarchar(1000) NULL);
 CREATE TABLE dbo.ConfirmacionFactura(idFactura int NOT NULL PRIMARY KEY REFERENCES dbo.Factura(idFactura),Estado nvarchar(30) NOT NULL CHECK(Estado IN ('Pagada','Pendiente','Anulada')),FechaActualizacionUtc datetime2(3) NOT NULL,Fuente nvarchar(100) NOT NULL,Referencia nvarchar(200) NOT NULL);
 CREATE TABLE dbo.CorreccionLecturaLaboral(idCorreccion int IDENTITY PRIMARY KEY,idMedicion int NOT NULL REFERENCES dbo.Medicion(idMedicion),Area nvarchar(30) NOT NULL REFERENCES dbo.AreaLaboral(Codigo),idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),idMotivo int NOT NULL REFERENCES dbo.MotivoLaboral(idMotivo),Anterior nvarchar(max) NOT NULL,Nuevo nvarchar(max) NOT NULL);
 CREATE TABLE dbo.EvidenciaLecturaLaboral(idEvidencia int IDENTITY PRIMARY KEY,idMedicion int NOT NULL REFERENCES dbo.Medicion(idMedicion),idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),Nombre nvarchar(150) NOT NULL,Tipo nvarchar(50) NOT NULL,Contenido varbinary(max) NOT NULL);
END;
IF OBJECT_ID('dbo.EvidenciaLecturaLaboral') IS NULL
 CREATE TABLE dbo.EvidenciaLecturaLaboral(idEvidencia int IDENTITY PRIMARY KEY,idMedicion int NOT NULL REFERENCES dbo.Medicion(idMedicion),idUsuarioSistema int NOT NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),FechaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),Nombre nvarchar(150) NOT NULL,Tipo nvarchar(50) NOT NULL,Contenido varbinary(max) NOT NULL);
IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('dbo.UsuarioSistema') AND name='UQ_UsuarioSistema_IdSocio')
 ALTER TABLE dbo.UsuarioSistema DROP CONSTRAINT UQ_UsuarioSistema_IdSocio;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.UsuarioSistema') AND name='idSocio' AND is_nullable=0)
 ALTER TABLE dbo.UsuarioSistema ALTER COLUMN idSocio int NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.UsuarioSistema') AND name='UX_UsuarioSistema_SocioRol')
 CREATE UNIQUE INDEX UX_UsuarioSistema_SocioRol ON dbo.UsuarioSistema(idSocio,idRol) WHERE idSocio IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.UsuarioSistema') AND name='UX_UsuarioSistema_EmpleadoRol')
 CREATE UNIQUE INDEX UX_UsuarioSistema_EmpleadoRol ON dbo.UsuarioSistema(idEmpleado,idRol) WHERE idEmpleado IS NOT NULL;
-- Recupera la cuenta personal que antes se convertía en laboral. No duplica Persona/Socio.
-- Se conserva la credencial existente; cada cuenta puede cambiar su contraseña independientemente.
INSERT dbo.UsuarioSistema(Usuario,Contrasenia,idRol,idSocio,idEmpleado)
SELECT CONCAT('socio-',s.idSocio,'-personal'),u.Contrasenia,r.idRol,s.idSocio,NULL
FROM dbo.UsuarioSistema u JOIN dbo.Rol laboral ON laboral.idRol=u.idRol AND laboral.NombreRol='Empleado'
JOIN dbo.Empleado e ON e.idEmpleado=u.idEmpleado JOIN dbo.Socio s ON s.idSocio=u.idSocio AND s.idPersona=e.idPersona
CROSS JOIN dbo.Rol r WHERE r.NombreRol='Socio'
AND NOT EXISTS(SELECT 1 FROM dbo.UsuarioSistema p WHERE p.idSocio=s.idSocio AND p.idRol=r.idRol);
IF NOT EXISTS(SELECT 1 FROM dbo.AuditoriaLaboral WHERE Accion='MigracionCuentasSeparadas')
 INSERT dbo.AuditoriaLaboral(Accion,Motivo) VALUES('MigracionCuentasSeparadas',N'Cuentas personales recuperadas desde relaciones Persona/Socio/Empleado existentes. Áreas, funciones y encargados sin asignar.');
COMMIT;

-- Catálogo inicial controlado para registrar motivos normalizados y producir estadísticas.
-- El INSERT es idempotente; conserva los cambios de estado realizados por Administración.
DECLARE @Motivos TABLE(Area nvarchar(30),Accion nvarchar(30),Nombre nvarchar(150));
INSERT @Motivos VALUES
 ('ADMINISTRACION','Rechazar',N'Solicitud duplicada'),('ADMINISTRACION','Rechazar',N'Fuera del área de cobertura'),('ADMINISTRACION','Rechazar',N'No cumple los requisitos del trámite'),('ADMINISTRACION','Rechazar',N'Deuda pendiente de regularización'),('ADMINISTRACION','Rechazar',N'Información del titular no verificable'),('ADMINISTRACION','Rechazar',N'No corresponde a un servicio de la cooperativa'),
 ('ADMINISTRACION','Devolver',N'Falta documentación obligatoria'),('ADMINISTRACION','Devolver',N'Documentación ilegible o incompleta'),('ADMINISTRACION','Devolver',N'Corregir datos del titular'),('ADMINISTRACION','Devolver',N'Completar datos de contacto'),('ADMINISTRACION','Devolver',N'Falta seleccionar el servicio solicitado'),
 ('AGUA','Rechazar',N'Conexión fuera del área de cobertura'),('AGUA','Rechazar',N'No cumple condiciones técnicas'),('AGUA','Rechazar',N'Solicitud duplicada'),('AGUA','Rechazar',N'Deuda pendiente de regularización'),('AGUA','Devolver',N'Falta documentación obligatoria'),('AGUA','Devolver',N'Falta evidencia de la visita'),('AGUA','Devolver',N'Completar resultado técnico'),('AGUA','Devolver',N'Revisar datos de la conexión'),('AGUA','CorregirLectura',N'Error de transcripción'),('AGUA','CorregirLectura',N'Lectura asociada a una conexión incorrecta'),('AGUA','CorregirLectura',N'Valor no coincide con la fotografía'),('AGUA','CorregirLectura',N'Fotografía ilegible o insuficiente'),('AGUA','CorregirLectura',N'Lectura duplicada'),
 ('ENERGIA','Rechazar',N'Conexión fuera del área de cobertura'),('ENERGIA','Rechazar',N'No cumple condiciones técnicas'),('ENERGIA','Rechazar',N'Solicitud duplicada'),('ENERGIA','Rechazar',N'Deuda pendiente de regularización'),('ENERGIA','Devolver',N'Falta documentación obligatoria'),('ENERGIA','Devolver',N'Falta evidencia de la visita'),('ENERGIA','Devolver',N'Completar resultado técnico'),('ENERGIA','Devolver',N'Revisar datos de la conexión'),('ENERGIA','CorregirLectura',N'Error de transcripción'),('ENERGIA','CorregirLectura',N'Lectura asociada a una conexión incorrecta'),('ENERGIA','CorregirLectura',N'Valor no coincide con la fotografía'),('ENERGIA','CorregirLectura',N'Fotografía ilegible o insuficiente'),('ENERGIA','CorregirLectura',N'Lectura duplicada'),
 ('INTERNET','Rechazar',N'Conexión fuera del área de cobertura'),('INTERNET','Rechazar',N'No hay factibilidad técnica'),('INTERNET','Rechazar',N'Solicitud duplicada'),('INTERNET','Rechazar',N'Deuda pendiente de regularización'),('INTERNET','Devolver',N'Falta documentación obligatoria'),('INTERNET','Devolver',N'Falta evidencia de la visita'),('INTERNET','Devolver',N'Completar resultado técnico'),('INTERNET','Devolver',N'Revisar datos de la conexión'),
 ('GAS','Rechazar',N'Fuera del alcance del convenio'),('GAS','Rechazar',N'No cumple los requisitos del trámite'),('GAS','Devolver',N'Falta documentación obligatoria'),('GAS','Devolver',N'Documentación ilegible o incompleta'),
 ('GERENCIA','Rechazar',N'No corresponde a una consulta de gestión'),('GERENCIA','Devolver',N'Falta información para completar la consulta');
INSERT dbo.MotivoLaboral(Area,Accion,Nombre)
SELECT m.Area,m.Accion,m.Nombre FROM @Motivos m
WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoLaboral x WHERE x.Area=m.Area AND x.Accion=m.Accion AND x.Nombre=m.Nombre);

-- Archivo de persona compartido por sus cuentas de socio y empleado; los tipos son sugeridos, no obligatorios.
IF OBJECT_ID('dbo.TipoDocumentoPersona') IS NULL
 CREATE TABLE dbo.TipoDocumentoPersona(idTipoDocumentoPersona int IDENTITY PRIMARY KEY,Codigo nvarchar(40) NOT NULL UNIQUE,Nombre nvarchar(120) NOT NULL,Grupo nvarchar(50) NOT NULL,Activo bit NOT NULL DEFAULT 1,Version rowversion);
IF OBJECT_ID('dbo.DocumentoPersona') IS NULL
 CREATE TABLE dbo.DocumentoPersona(idDocumentoPersona int IDENTITY PRIMARY KEY,idPersona int NOT NULL REFERENCES dbo.Persona(idPersona),idTipoDocumentoPersona int NOT NULL REFERENCES dbo.TipoDocumentoPersona(idTipoDocumentoPersona),NombreArchivo nvarchar(150) NOT NULL,TipoArchivo nvarchar(50) NOT NULL,Contenido varbinary(max) NOT NULL,Estado nvarchar(20) NOT NULL DEFAULT 'Pendiente' CHECK(Estado IN ('Pendiente','Aprobado','Rechazado')),MotivoRechazo nvarchar(100) NULL,FechaCargaUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),FechaRevisionUtc datetime2(3) NULL,idUsuarioRevisor int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),idUsuarioCarga int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),Version rowversion);
IF COL_LENGTH('dbo.DocumentoPersona','idDocumentoSocioOrigen') IS NULL
 ALTER TABLE dbo.DocumentoPersona ADD idDocumentoSocioOrigen int NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.DocumentoPersona') AND name='UX_DocumentoPersona_Legacy')
 CREATE UNIQUE INDEX UX_DocumentoPersona_Legacy ON dbo.DocumentoPersona(idDocumentoSocioOrigen) WHERE idDocumentoSocioOrigen IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.DocumentoPersona') AND name='IX_DocumentoPersona_Persona')
 CREATE INDEX IX_DocumentoPersona_Persona ON dbo.DocumentoPersona(idPersona,FechaCargaUtc DESC);
IF OBJECT_ID('dbo.MotivoRechazoAltaSocio') IS NULL
 CREATE TABLE dbo.MotivoRechazoAltaSocio(idMotivo int IDENTITY PRIMARY KEY,Codigo nvarchar(40) NOT NULL UNIQUE,Nombre nvarchar(150) NOT NULL UNIQUE,Activo bit NOT NULL DEFAULT 1);
IF OBJECT_ID('dbo.AltaSocio') IS NULL
 CREATE TABLE dbo.AltaSocio(idSocio int NOT NULL PRIMARY KEY REFERENCES dbo.Socio(idSocio),EsTitular bit NOT NULL,Estado nvarchar(30) NOT NULL CHECK(Estado IN ('PendienteDocumentacion','EnRevision','Rechazada','Aprobada')),idMotivoRechazo int NULL REFERENCES dbo.MotivoRechazoAltaSocio(idMotivo),FechaEnvioUtc datetime2(3) NULL,FechaDecisionUtc datetime2(3) NULL,idUsuarioRevisor int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),Version rowversion);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.AltaSocio') AND name='IX_AltaSocio_Estado')
 CREATE INDEX IX_AltaSocio_Estado ON dbo.AltaSocio(Estado,idSocio);
-- Socios que ya existían antes del flujo se consideran aprobados y no pierden el acceso actual.
INSERT dbo.AltaSocio(idSocio,EsTitular,Estado)
SELECT s.idSocio,1,'Aprobada' FROM dbo.Socio s WHERE NOT EXISTS(SELECT 1 FROM dbo.AltaSocio a WHERE a.idSocio=s.idSocio);
DECLARE @MotivosAlta TABLE(Codigo nvarchar(40),Nombre nvarchar(150));
INSERT @MotivosAlta VALUES('DOC_ILEGIBLE',N'Documento ilegible'),('DOC_VENCIDO',N'Documento vencido'),('DNI_FRENTE',N'DNI frente incorrecto'),('DNI_DORSO',N'DNI dorso incorrecto'),('DOMICILIO',N'Constancia de domicilio incorrecta'),('DATOS_NO_COINCIDEN',N'Datos no coinciden con la documentación'),('FALTA_DOCUMENTO',N'Falta documentación'),('DOC_INCOMPLETA',N'Documentación incompleta');
INSERT dbo.MotivoRechazoAltaSocio(Codigo,Nombre) SELECT m.Codigo,m.Nombre FROM @MotivosAlta m WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoRechazoAltaSocio x WHERE x.Codigo=m.Codigo);
IF OBJECT_ID('dbo.MotivoDocumentoPersona') IS NULL
 CREATE TABLE dbo.MotivoDocumentoPersona(idMotivo int IDENTITY PRIMARY KEY,Codigo nvarchar(40) NOT NULL UNIQUE,Nombre nvarchar(150) NOT NULL UNIQUE,Activo bit NOT NULL DEFAULT 1);
DECLARE @MotivosDocumento TABLE(Codigo nvarchar(40),Nombre nvarchar(150));
INSERT @MotivosDocumento VALUES('ILEGIBLE',N'Archivo ilegible'),('INCOMPLETO',N'Documento incompleto'),('INCORRECTO',N'Archivo incorrecto para el tipo seleccionado'),('VENCIDO',N'Documento vencido'),('DATOS_NO_COINCIDEN',N'Los datos no coinciden'),('NO_CORRESPONDE',N'El documento no corresponde al legajo'),('DUPLICADO',N'Documento duplicado'),('FALTA_FIRMA',N'Falta firma o validaciÃ³n requerida');
INSERT dbo.MotivoDocumentoPersona(Codigo,Nombre) SELECT m.Codigo,m.Nombre FROM @MotivosDocumento m WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoDocumentoPersona x WHERE x.Codigo=m.Codigo);
DECLARE @TiposPersona TABLE(Codigo nvarchar(40),Nombre nvarchar(120),Grupo nvarchar(50));
INSERT @TiposPersona VALUES
 ('Domicilio',N'Constancia de domicilio',N'Identidad y contacto'),('DniFrente',N'DNI — frente',N'Identidad y contacto'),('DniDorso',N'DNI — dorso',N'Identidad y contacto'),('CUIL',N'Constancia de CUIL',N'Identidad y contacto'),('CONTACTO_EMERGENCIA',N'Datos de contacto de emergencia',N'Identidad y contacto'),
 ('CV',N'Currículum vitae',N'Antecedentes y formación'),('TITULOS',N'Títulos y certificados',N'Antecedentes y formación'),('MATRICULAS',N'Matrículas o habilitaciones',N'Antecedentes y formación'),('LICENCIA_CONDUCIR',N'Licencia de conducir',N'Antecedentes y formación'),('CAPACITACIONES',N'Constancias de capacitación',N'Antecedentes y formación'),
 ('DESIGNACION',N'Contrato o acto de designación',N'Vínculo laboral'),('ALTA_TEMPRANA',N'Constancia de alta laboral',N'Vínculo laboral');
INSERT dbo.TipoDocumentoPersona(Codigo,Nombre,Grupo)
SELECT t.Codigo,t.Nombre,t.Grupo FROM @TiposPersona t
WHERE NOT EXISTS(SELECT 1 FROM dbo.TipoDocumentoPersona x WHERE x.Codigo=t.Codigo);
-- Migra también el archivo documental anterior sin borrar su origen ni perder su categoría original.
INSERT dbo.TipoDocumentoPersona(Codigo,Nombre,Grupo)
SELECT CONCAT('LEG_',t.idTipoDocumento),t.Nombre,N'Histórico de socio'
FROM dbo.TipoDocumentoSocio t
WHERE NOT EXISTS(SELECT 1 FROM dbo.TipoDocumentoPersona x WHERE x.Codigo=CONCAT('LEG_',t.idTipoDocumento));
INSERT dbo.DocumentoPersona(idPersona,idTipoDocumentoPersona,NombreArchivo,TipoArchivo,Contenido,Estado,MotivoRechazo,FechaCargaUtc,idDocumentoSocioOrigen)
SELECT s.idPersona,pt.idTipoDocumentoPersona,d.Nombre,d.Tipo,d.Contenido,
 CASE WHEN d.Estado IN ('Aprobado','Rechazado') THEN d.Estado ELSE 'Pendiente' END,
 LEFT(d.Motivo,100),d.FechaUtc,d.idDocumento
FROM dbo.DocumentoSocioLaboral d
JOIN dbo.Socio s ON s.idSocio=d.idSocio
JOIN dbo.TipoDocumentoPersona pt ON pt.Codigo=CONCAT('LEG_',d.idTipoDocumento)
WHERE NOT EXISTS(SELECT 1 FROM dbo.DocumentoPersona x WHERE x.idDocumentoSocioOrigen=d.idDocumento);
