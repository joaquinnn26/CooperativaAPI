-- Base comun, aditiva, para clasificar y consultar eventos operativos.
-- No sustituye ni borra los historiales de cada modulo.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.MotivoEventoOperativo','U') IS NULL
BEGIN
    CREATE TABLE dbo.MotivoEventoOperativo(
        idMotivoEvento int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MotivoEventoOperativo PRIMARY KEY,
        Contexto nvarchar(60) NOT NULL,
        Codigo nvarchar(80) NOT NULL,
        Nombre nvarchar(150) NOT NULL,
        NombreNormalizado AS UPPER(LTRIM(RTRIM(Nombre))) PERSISTED,
        Orden int NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Orden DEFAULT(0),
        Activo bit NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Activo DEFAULT(1),
        ConfirmadoCooperativa bit NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Confirmado DEFAULT(0),
        RequiereDetalle bit NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Detalle DEFAULT(0),
        Origen nvarchar(40) NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Origen DEFAULT('Configurado'),
        FechaCreacionUtc datetime2(3) NOT NULL CONSTRAINT DF_MotivoEventoOperativo_Fecha DEFAULT(SYSUTCDATETIME()),
        idUsuarioCreador int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        Version rowversion NOT NULL,
        CONSTRAINT UQ_MotivoEventoOperativo_Codigo UNIQUE(Contexto,Codigo),
        CONSTRAINT UQ_MotivoEventoOperativo_Nombre UNIQUE(Contexto,NombreNormalizado)
    );
END;

IF OBJECT_ID('dbo.EventoOperativo','U') IS NULL
BEGIN
    CREATE TABLE dbo.EventoOperativo(
        idEventoOperativo bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EventoOperativo PRIMARY KEY,
        Contexto nvarchar(60) NOT NULL,
        CodigoEvento nvarchar(80) NOT NULL,
        Entidad nvarchar(60) NOT NULL,
        idEntidad nvarchar(80) NOT NULL,
        FechaEventoUtc datetime2(3) NOT NULL CONSTRAINT DF_EventoOperativo_Fecha DEFAULT(SYSUTCDATETIME()),
        idUsuarioSistema int NULL REFERENCES dbo.UsuarioSistema(idUsuarioSistema),
        ActorSistema bit NOT NULL CONSTRAINT DF_EventoOperativo_ActorSistema DEFAULT(0),
        idEmpleado int NULL REFERENCES dbo.Empleado(idEmpleado),
        idEmpleadoActor int NULL REFERENCES dbo.Empleado(idEmpleado),
        Area nvarchar(30) NULL,
        idSocio int NULL,
        idServicio int NULL,
        idServicioSocio int NULL,
        idMedidor int NULL,
        idRutaMedicion int NULL,
        Folio nvarchar(50) NULL,
        EstadoAnterior nvarchar(60) NULL,
        EstadoNuevo nvarchar(60) NULL,
        idMotivoEvento int NULL REFERENCES dbo.MotivoEventoOperativo(idMotivoEvento),
        MotivoRegistrado nvarchar(150) NULL,
        MotivoDesconocido bit NOT NULL CONSTRAINT DF_EventoOperativo_MotivoDesconocido DEFAULT(0),
        OperacionId uniqueidentifier NULL,
        Resultado nvarchar(80) NULL,
        Detalle nvarchar(1000) NULL,
        Datos nvarchar(max) NULL,
        ClaveOrigen nvarchar(256) NULL,
        CONSTRAINT CK_EventoOperativo_Actor CHECK((ActorSistema=1 AND idUsuarioSistema IS NULL) OR ActorSistema=0),
        CONSTRAINT CK_EventoOperativo_Motivo CHECK(NOT(MotivoDesconocido=1 AND idMotivoEvento IS NOT NULL))
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.EventoOperativo') AND name='UX_EventoOperativo_ClaveOrigen')
    CREATE UNIQUE INDEX UX_EventoOperativo_ClaveOrigen ON dbo.EventoOperativo(ClaveOrigen) WHERE ClaveOrigen IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.EventoOperativo') AND name='IX_EventoOperativo_ContextoFecha')
    CREATE INDEX IX_EventoOperativo_ContextoFecha ON dbo.EventoOperativo(Contexto,FechaEventoUtc DESC) INCLUDE(CodigoEvento,Entidad,idEntidad,idMotivoEvento,Area,idEmpleado);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.EventoOperativo') AND name='IX_EventoOperativo_AreaFecha')
    CREATE INDEX IX_EventoOperativo_AreaFecha ON dbo.EventoOperativo(Area,FechaEventoUtc DESC) INCLUDE(Contexto,CodigoEvento,Entidad,idEntidad,idMotivoEvento);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.EventoOperativo') AND name='IX_EventoOperativo_Operacion')
    CREATE INDEX IX_EventoOperativo_Operacion ON dbo.EventoOperativo(OperacionId) INCLUDE(Contexto,CodigoEvento,idMedidor) WHERE OperacionId IS NOT NULL;

DECLARE @Oficial TABLE(Contexto nvarchar(60),Codigo nvarchar(80),Nombre nvarchar(150),Orden int);
INSERT @Oficial VALUES
 ('LECTURA_NO_REALIZADA','MEDIDOR_OBSTRUIDO',N'Medidor tapado u obstruido',1),
 ('LECTURA_NO_REALIZADA','MEDIDOR_AVERIADO',N'Medidor averiado',2),
 ('LECTURA_NO_REALIZADA','RIESGO_EMPLEADO',N'Riesgo para el empleado',3),
 ('LECTURA_NO_REALIZADA','DOMICILIO_CERRADO',N'Domicilio cerrado o sin acceso',4),
 ('LECTURA_NO_REALIZADA','ACCESO_DENEGADO',N'Acceso denegado por el ocupante',5),
 ('LECTURA_NO_REALIZADA','MEDIDOR_NO_LOCALIZADO',N'Medidor no localizado en el domicilio',6),
 ('LECTURA_NO_REALIZADA','MEDIDOR_ILEGIBLE',N'Visor o numeracion ilegible',7),
 ('LECTURA_NO_REALIZADA','DIRECCION_NO_COINCIDE',N'La direccion no coincide con la conexion',8),
 ('MEDIDOR_FALLA','SIN_REGISTRO',N'El medidor no registra consumo',1),
 ('MEDIDOR_FALLA','CONTADOR_DETENIDO',N'Contador detenido o trabado',2),
 ('MEDIDOR_FALLA','VISOR_ILEGIBLE',N'Visor o numeracion ilegible',3),
 ('MEDIDOR_FALLA','LECTURA_IRREGULAR',N'Lectura irregular o inestable',4),
 ('MEDIDOR_FALLA','DANO_FISICO',N'Dano en la carcasa o las conexiones',5),
 ('MEDIDOR_FALLA','POSIBLE_MANIPULACION',N'Posible manipulacion del equipo',6),
 ('MEDIDOR_REEMPLAZO','BAJA_SERVICIO',N'Baja del servicio',1),
 ('MEDIDOR_REEMPLAZO','MEDIDOR_AVERIADO',N'Medidor averiado',2),
 ('MEDIDOR_REEMPLAZO','MEDIDOR_OBSOLETO',N'Medidor obsoleto',3),
 ('MEDIDOR_REEMPLAZO','LECTURA_INCORRECTA',N'Lectura incorrecta',4),
 ('INSTALACION_NO_REALIZADA','FALTA_AUTORIZACION',N'Falta de autorización',1),
 ('INSTALACION_NO_REALIZADA','SIN_FACTIBILIDAD',N'Falta de factibilidad técnica',2),
 ('INSTALACION_NO_REALIZADA','RIESGO_ELECTRICO_ESTRUCTURAL',N'Riesgo eléctrico o estructural',3),
 ('INSTALACION_NO_REALIZADA','CLIMA_ADVERSO',N'Malas condiciones climáticas',4),
 ('SOLICITUD_SERVICIO_RECHAZO','DOC_INCOMPLETA',N'Documentación incompleta',1),
 ('SOLICITUD_SERVICIO_RECHAZO','DATOS_INCORRECTOS',N'Datos incorrectos',2),
 ('SOLICITUD_SERVICIO_RECHAZO','DEUDA_PENDIENTE',N'Deuda pendiente',3),
 ('SOLICITUD_SERVICIO_RECHAZO','NO_AUTORIZADO',N'Solicitante no autorizado',4),
 ('SOLICITUD_SERVICIO_RECHAZO','SIN_AUTORIZACION_PROPIETARIO',N'Falta de autorización del propietario',5),
 ('SOLICITUD_SERVICIO_RECHAZO','FUERA_COBERTURA',N'Fuera de cobertura',6),
 ('SOLICITUD_SERVICIO_RECHAZO','SIN_FACTIBILIDAD',N'Falta de factibilidad técnica',7),
 ('SOLICITUD_SERVICIO_RECHAZO','DOMICILIO_INCORRECTO',N'Domicilio incorrecto',8),
 ('SOLICITUD_SERVICIO_RECHAZO','ENERGIA_REQUISITO_PILAR',N'Energía: incumplimiento de requisitos del pilar',9);

INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Orden,Activo,ConfirmadoCooperativa,Origen)
SELECT o.Contexto,o.Codigo,o.Nombre,o.Orden,1,1,'Cuestionario'
FROM @Oficial o
WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoEventoOperativo m WHERE m.Contexto=o.Contexto AND m.Codigo=o.Codigo);

-- Catalogos preexistentes se conservan y quedan visibles como pendientes de validacion.
IF OBJECT_ID('dbo.MotivoMedidor','U') IS NOT NULL
    INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,Origen)
    SELECT CONCAT('MEDIDOR_',UPPER(m.Accion)),CONCAT('LEGACY_MOTIVOMEDIDOR_',m.idMotivo),m.Nombre,m.Activo,0,'MigradoSinValidar'
    FROM dbo.MotivoMedidor m
    WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoEventoOperativo x WHERE x.Contexto=CONCAT('MEDIDOR_',UPPER(m.Accion)) AND x.NombreNormalizado=UPPER(LTRIM(RTRIM(m.Nombre))));

IF OBJECT_ID('dbo.MotivoLaboral','U') IS NOT NULL
    INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,Origen)
    SELECT CASE WHEN m.Accion='CorregirLectura' THEN 'LECTURA_CORRECCION' ELSE CONCAT('LABORAL_',UPPER(m.Area),'_',UPPER(m.Accion)) END,
           CONCAT('LEGACY_MOTIVOLABORAL_',m.idMotivo),m.Nombre,m.Activo,0,'MigradoSinValidar'
    FROM (SELECT m.*,ROW_NUMBER() OVER(PARTITION BY CASE WHEN m.Accion='CorregirLectura' THEN 'LECTURA_CORRECCION' ELSE CONCAT('LABORAL_',UPPER(m.Area),'_',UPPER(m.Accion)) END,UPPER(LTRIM(RTRIM(m.Nombre))) ORDER BY m.idMotivo) rn FROM dbo.MotivoLaboral m) m
    WHERE m.rn=1 AND NOT EXISTS(SELECT 1 FROM dbo.MotivoEventoOperativo x WHERE x.Contexto=CASE WHEN m.Accion='CorregirLectura' THEN 'LECTURA_CORRECCION' ELSE CONCAT('LABORAL_',UPPER(m.Area),'_',UPPER(m.Accion)) END AND x.NombreNormalizado=UPPER(LTRIM(RTRIM(m.Nombre))));

IF OBJECT_ID('dbo.MotivoDocumentoPersona','U') IS NOT NULL
    INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,Origen)
    SELECT 'DOCUMENTO_PERSONA_RECHAZO',CONCAT('LEGACY_MOTIVODOCUMENTO_',m.idMotivo),m.Nombre,m.Activo,0,'MigradoSinValidar'
    FROM dbo.MotivoDocumentoPersona m
    WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoEventoOperativo x WHERE x.Contexto='DOCUMENTO_PERSONA_RECHAZO' AND x.NombreNormalizado=UPPER(LTRIM(RTRIM(m.Nombre))));

IF OBJECT_ID('dbo.MotivoRechazoAltaSocio','U') IS NOT NULL
    INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,Origen)
    SELECT 'ALTA_SOCIO_RECHAZO',CONCAT('LEGACY_ALTA_SOCIO_',m.idMotivo),m.Nombre,m.Activo,0,'MigradoSinValidar'
    FROM dbo.MotivoRechazoAltaSocio m
    WHERE NOT EXISTS(SELECT 1 FROM dbo.MotivoEventoOperativo x WHERE x.Contexto='ALTA_SOCIO_RECHAZO' AND x.NombreNormalizado=UPPER(LTRIM(RTRIM(m.Nombre))));

-- Importa trazas existentes sin inventar actor, estado previo ni motivo desconocido.
IF OBJECT_ID('dbo.EventoMedidor','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,Area,idEmpleadoActor,idMedidor,EstadoAnterior,EstadoNuevo,idMotivoEvento,MotivoRegistrado,Detalle,Datos,OperacionId,ClaveOrigen,MotivoDesconocido)
    SELECT CONCAT('MEDIDOR_',UPPER(COALESCE(oldMotivo.Accion,e.Accion))),e.Accion,'Medidor',CONVERT(nvarchar(80),e.idMedidor),e.FechaUtc,e.idUsuarioSistema,
           CASE WHEN t.Nombre='Agua' THEN 'AGUA' WHEN t.Nombre IN ('Electrico','Eléctrico') THEN 'ENERGIA' END,u.idEmpleado,e.idMedidor,NULL,NULL,mot.idMotivoEvento,oldMotivo.Nombre,e.Detalle,
           (SELECT e.Anterior Anterior,e.Nuevo Nuevo,e.idAsignacion idAsignacion,e.ReferenciaTrabajo ReferenciaTrabajo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
           e.OperacionId,CONCAT('EventoMedidor:',e.idEvento),CASE WHEN e.idMotivo IS NOT NULL AND mot.idMotivoEvento IS NULL THEN 1 ELSE 0 END
    FROM dbo.EventoMedidor e JOIN dbo.Medidor m ON m.idMedidor=e.idMedidor JOIN dbo.TipoMedidor t ON t.idTipoMedidor=m.idTipoMedidor
    LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=e.idUsuarioSistema
    LEFT JOIN dbo.MotivoMedidor oldMotivo ON oldMotivo.idMotivo=e.idMotivo
    LEFT JOIN dbo.MotivoEventoOperativo mot ON mot.Contexto=CONCAT('MEDIDOR_',UPPER(COALESCE(oldMotivo.Accion,e.Accion))) AND mot.NombreNormalizado=UPPER(LTRIM(RTRIM(oldMotivo.Nombre)))
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo x WHERE x.ClaveOrigen=CONCAT('EventoMedidor:',e.idEvento));

IF OBJECT_ID('dbo.AuditoriaLaboral','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,idEmpleado,idEmpleadoActor,Area,idSocio,idServicioSocio,EstadoAnterior,EstadoNuevo,idMotivoEvento,MotivoRegistrado,Detalle,Datos,ClaveOrigen,MotivoDesconocido)
    SELECT CONCAT('LABORAL_',UPPER(COALESCE(a.Area,t.Area,'GENERAL')),'_',UPPER(a.Accion)),a.Accion,
           CASE WHEN t.Categoria IS NULL THEN 'AuditoriaLaboral' ELSE t.Categoria END,
           CONVERT(nvarchar(80),COALESCE(t.idTarea,t.OrigenId,a.idAuditoria)),a.FechaUtc,a.idUsuarioSistema,COALESCE(t.idEmpleadoAsignado,a.idEmpleado),a.idEmpleado,
           COALESCE(a.Area,t.Area),t.idSocio,t.idServicioSocio,a.EstadoAnterior,a.EstadoNuevo,mot.idMotivoEvento,mot.Nombre,a.Motivo,a.Datos,
           CONCAT('AuditoriaLaboral:',a.idAuditoria),CASE WHEN a.Accion IN ('Rechazar','Devolver','RechazarInstalacion','Asignar','ReabrirEtapa') AND a.Motivo IS NOT NULL AND mot.idMotivoEvento IS NULL THEN 1 ELSE 0 END
    FROM dbo.AuditoriaLaboral a LEFT JOIN dbo.TareaLaboral t ON t.idTarea=a.idTarea
    LEFT JOIN dbo.MotivoEventoOperativo mot ON mot.Contexto=CONCAT('LABORAL_',UPPER(COALESCE(a.Area,t.Area,'GENERAL')),'_',UPPER(a.Accion)) AND mot.NombreNormalizado=UPPER(LTRIM(RTRIM(a.Motivo)))
    WHERE a.Accion<>'CorregirLectura'
      AND NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo x WHERE x.ClaveOrigen=CONCAT('AuditoriaLaboral:',a.idAuditoria));

IF OBJECT_ID('dbo.CorreccionLecturaLaboral','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,idEmpleadoActor,Area,idServicioSocio,idMedidor,EstadoAnterior,EstadoNuevo,idMotivoEvento,MotivoRegistrado,Datos,ClaveOrigen,MotivoDesconocido)
    SELECT 'LECTURA_CORRECCION','CorregirLectura','Medicion',CONVERT(nvarchar(80),c.idMedicion),c.FechaUtc,c.idUsuarioSistema,u.idEmpleado,c.Area,me.idServicioSocio,me.idMedidor,
           'Registrada','Corregida',mot.idMotivoEvento,ml.Nombre,
           (SELECT c.Anterior Anterior,c.Nuevo Nuevo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('CorreccionLecturaLaboral:',c.idCorreccion),CASE WHEN mot.idMotivoEvento IS NULL THEN 1 ELSE 0 END
     FROM dbo.CorreccionLecturaLaboral c JOIN dbo.Medicion me ON me.idMedicion=c.idMedicion LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=c.idUsuarioSistema
    LEFT JOIN dbo.MotivoLaboral ml ON ml.idMotivo=c.idMotivo
    LEFT JOIN dbo.MotivoEventoOperativo mot ON mot.Contexto='LECTURA_CORRECCION' AND mot.NombreNormalizado=UPPER(LTRIM(RTRIM(ml.Nombre)))
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo x WHERE x.ClaveOrigen=CONCAT('CorreccionLecturaLaboral:',c.idCorreccion));

IF OBJECT_ID('dbo.HistorialEstadoSolicitud','U') IS NOT NULL
BEGIN
    ;WITH H AS(
      SELECT h.idHistorialEstadoSolicitud,h.idSolicitudServicio,h.idEmpleado,h.FechaCambio,h.Observaciones,e.NombreEstado,
        LAG(e.NombreEstado) OVER(PARTITION BY h.idSolicitudServicio ORDER BY h.FechaCambio,h.idHistorialEstadoSolicitud) EstadoAnterior
      FROM dbo.HistorialEstadoSolicitud h JOIN dbo.EstadoSolicitudServicio e ON e.idEstadoSolicitud=h.idEstadoSolicitud
    )
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idEmpleadoActor,Area,idSocio,idServicio,EstadoAnterior,EstadoNuevo,Detalle,ClaveOrigen,MotivoDesconocido)
    SELECT 'SOLICITUD_SERVICIO','CambioEstado','SolicitudServicio',CONVERT(nvarchar(80),h.idSolicitudServicio),CONVERT(datetime2(3),h.FechaCambio),h.idEmpleado,a.Area,s.idSocio,s.idServicio,h.EstadoAnterior,h.NombreEstado,h.Observaciones,
           CONCAT('HistorialEstadoSolicitud:',h.idHistorialEstadoSolicitud),CASE WHEN h.NombreEstado='Rechazada' THEN 1 ELSE 0 END
    FROM H h JOIN dbo.SolicitudServicio s ON s.idSolicitudServicio=h.idSolicitudServicio LEFT JOIN dbo.AreaServicio a ON a.idServicio=s.idServicio
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo x WHERE x.ClaveOrigen=CONCAT('HistorialEstadoSolicitud:',h.idHistorialEstadoSolicitud));
END;

IF OBJECT_ID('dbo.HistorialTerritorial','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,EstadoNuevo,Detalle,Datos,ClaveOrigen,MotivoDesconocido)
    SELECT CONCAT('TERRITORIO_',UPPER(h.Entidad)),h.Accion,h.Entidad,CONVERT(nvarchar(80),h.idEntidad),h.FechaUtc,h.idUsuarioSistema,h.Accion,h.Motivo,
           (SELECT h.Anterior Anterior,h.Nuevo Nuevo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('HistorialTerritorial:',h.idHistorial),CASE WHEN h.Motivo IS NULL THEN 0 ELSE 1 END
    FROM dbo.HistorialTerritorial h
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo x WHERE x.ClaveOrigen=CONCAT('HistorialTerritorial:',h.idHistorial));

IF OBJECT_ID('dbo.RutaMedicion','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,idEmpleado,Area,idRutaMedicion,EstadoNuevo,ActorSistema,Resultado,Detalle,Datos,ClaveOrigen)
    SELECT 'HOJA_RUTA','TiempoCaducado','RutaMedicion',CONVERT(nvarchar(80),r.idRutaMedicion),r.idEmpleado,
      (SELECT TOP 1 a.Area FROM dbo.RutaMedicionDetalle d JOIN dbo.ContextoRutaMedidor c ON c.idRutaMedicionDetalle=d.idRutaMedicionDetalle
       JOIN dbo.Servicio se ON se.Nombre=c.ServicioNombre JOIN dbo.AreaServicio a ON a.idServicio=se.idServicio WHERE d.idRutaMedicion=r.idRutaMedicion ORDER BY a.Area),
      r.idRutaMedicion,'TiempoCaducado',1,'TiempoCaducado',N'Caducidad aplicada durante la actualización del sistema; fecha de expiración según el plazo configurado.',
      (SELECT r.FechaCaducidadUtc FechaCaducidadUtc FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('RutaCaducidad:',r.idRutaMedicion)
    FROM dbo.RutaMedicion r
    WHERE r.Estado='TiempoCaducado' AND NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo e WHERE e.ClaveOrigen=CONCAT('RutaCaducidad:',r.idRutaMedicion));

IF OBJECT_ID('dbo.RutaMedicion','U') IS NOT NULL AND OBJECT_ID('dbo.ExpirarRutasMedicion','P') IS NOT NULL
    EXEC(N'CREATE OR ALTER PROCEDURE dbo.ExpirarRutasMedicion @idRutaMedicion int = NULL AS
    BEGIN
        SET NOCOUNT ON;
        DECLARE @expiradas TABLE(idRutaMedicion int PRIMARY KEY,idEmpleado int,EstadoAnterior nvarchar(60),FechaCaducidadUtc datetime2(3));
        UPDATE r SET Estado=''TiempoCaducado''
        OUTPUT inserted.idRutaMedicion,inserted.idEmpleado,deleted.Estado,inserted.FechaCaducidadUtc INTO @expiradas
        FROM dbo.RutaMedicion r
        WHERE r.Estado IN (''Pendiente'',''EnCurso'') AND r.FechaCaducidadUtc<=SYSUTCDATETIME()
          AND (@idRutaMedicion IS NULL OR r.idRutaMedicion=@idRutaMedicion);

        INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,idEmpleado,Area,idRutaMedicion,EstadoAnterior,EstadoNuevo,ActorSistema,Resultado,Detalle,Datos,ClaveOrigen)
        SELECT ''HOJA_RUTA'',''TiempoCaducado'',''RutaMedicion'',CONVERT(nvarchar(80),x.idRutaMedicion),x.idEmpleado,
          (SELECT TOP 1 a.Area FROM dbo.RutaMedicionDetalle d JOIN dbo.ContextoRutaMedidor c ON c.idRutaMedicionDetalle=d.idRutaMedicionDetalle
           JOIN dbo.Servicio se ON se.Nombre=c.ServicioNombre JOIN dbo.AreaServicio a ON a.idServicio=se.idServicio WHERE d.idRutaMedicion=x.idRutaMedicion ORDER BY a.Area),
          x.idRutaMedicion,x.EstadoAnterior,''TiempoCaducado'',1,''TiempoCaducado'',N''Venció el plazo de 24 horas.'',
          (SELECT x.FechaCaducidadUtc FechaCaducidadUtc FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT(''RutaCaducidad:'',x.idRutaMedicion)
        FROM @expiradas x
        WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo e WHERE e.ClaveOrigen=CONCAT(''RutaCaducidad:'',x.idRutaMedicion));
    END');

-- Incorpora las altas de socios existentes para que el tablero no comience vacío.
IF OBJECT_ID('dbo.Socio','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,ActorSistema,Area,idSocio,EstadoNuevo,Datos,ClaveOrigen)
    SELECT 'SOCIO_ALTA','SocioRegistrado','Socio',CONVERT(nvarchar(80),s.idSocio),CONVERT(datetime2(3),s.FechaAlta),1,'ADMINISTRACION',s.idSocio,
           CASE WHEN s.Activo=1 THEN 'Activo' ELSE 'Pendiente o inactivo' END,
           (SELECT s.Activo Activo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('SocioAlta:',s.idSocio)
    FROM dbo.Socio s
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo e WHERE e.ClaveOrigen IN (CONCAT('SocioAlta:',s.idSocio),CONCAT('API:SocioAlta:',s.idSocio)));

-- Conserva también decisiones históricas de altas y sus motivos seleccionados.
IF OBJECT_ID('dbo.AltaSocioRevision','U') IS NOT NULL AND OBJECT_ID('dbo.MotivoRechazoAltaSocio','U') IS NOT NULL
    INSERT dbo.EventoOperativo(Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,ActorSistema,idEmpleadoActor,Area,idSocio,EstadoNuevo,idMotivoEvento,MotivoRegistrado,MotivoDesconocido,Datos,ClaveOrigen)
    SELECT CASE WHEN r.Estado='Rechazada' THEN 'ALTA_SOCIO_RECHAZO' ELSE 'ALTA_SOCIO_REVISION' END,
           CASE WHEN r.Estado='Rechazada' THEN 'AltaSocioRechazada' ELSE 'AltaSocioAprobada' END,
           'AltaSocio',CONVERT(nvarchar(80),r.idSocio),r.FechaUtc,r.idUsuarioRevisor,0,u.idEmpleado,'ADMINISTRACION',r.idSocio,r.Estado,
           m.idMotivoEvento,COALESCE(m.Nombre,mr.Nombre),CASE WHEN r.Estado='Rechazada' AND m.idMotivoEvento IS NULL THEN 1 ELSE 0 END,
           (SELECT r.idRevision FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('AltaSocioRevision:',r.idRevision)
    FROM dbo.AltaSocioRevision r
    LEFT JOIN dbo.MotivoRechazoAltaSocio mr ON mr.idMotivo=r.idMotivo
    LEFT JOIN dbo.MotivoEventoOperativo m ON m.Contexto='ALTA_SOCIO_RECHAZO' AND m.NombreNormalizado=UPPER(LTRIM(RTRIM(mr.Nombre)))
    LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=r.idUsuarioRevisor
    WHERE NOT EXISTS(SELECT 1 FROM dbo.EventoOperativo e WHERE e.ClaveOrigen IN (CONCAT('AltaSocioRevision:',r.idRevision),CONCAT('API:AltaSocioRevision:',r.idRevision)));

COMMIT;
