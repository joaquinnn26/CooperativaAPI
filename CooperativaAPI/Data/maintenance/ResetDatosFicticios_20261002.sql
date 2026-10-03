-- Reinicio de datos de prueba. Conserva schema, catalogos, servicios y el login Administrador.
-- Ejecutado primero en CooperativaResetTest_20261002 y luego en Cooperativa.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF DB_NAME() NOT IN (N'Cooperativa',N'CooperativaResetTest_20261002')
    THROW 51100, 'Base de datos no autorizada para este reinicio.', 1;
IF OBJECT_ID(N'dbo.UsuarioSistema',N'U') IS NULL OR OBJECT_ID(N'dbo.Rol',N'U') IS NULL
    THROW 51101, 'No se reconoce el esquema de Cooperativa.', 1;
IF (SELECT COUNT(*) FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol WHERE r.NombreRol=N'Administrador')<>1
    THROW 51102, 'Se esperaba exactamente una cuenta Administrador; no se realizo ningun cambio.', 1;
IF EXISTS (SELECT 1 FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol WHERE r.NombreRol=N'Administrador' AND u.idEmpleado IS NOT NULL)
    THROW 51103, 'La cuenta Administrador tiene un empleado asociado; revisar el acceso antes del reinicio.', 1;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @idAdmin int;
    SELECT @idAdmin=u.idUsuarioSistema
    FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol
    WHERE r.NombreRol=N'Administrador';

    -- Mantener la cuenta de acceso, pero desvincularla del socio ficticio.
    UPDATE dbo.UsuarioSistema SET idSocio=NULL WHERE idUsuarioSistema=@idAdmin;

    -- Evidencias, auditorias y datos derivados primero.
    DELETE FROM dbo.EvidenciaLecturaLaboral;
    DELETE FROM dbo.EvidenciaLaboral;
    DELETE FROM dbo.CorreccionLecturaLaboral;
    DELETE FROM dbo.AuditoriaLaboral;
    DELETE FROM dbo.HistorialTerritorial;
    DELETE FROM dbo.ContextoRutaMedidor;
    DELETE FROM dbo.EventoMedidor;

    -- Operaciones de lectura, ruta e inventario de equipos.
    DELETE FROM dbo.RutaMedicionDetalle;
    DELETE FROM dbo.Medicion;
    DELETE FROM dbo.RutaMedicion;
    DELETE FROM dbo.AsignacionMedidor;
    DELETE FROM dbo.FichaMedidor;
    DELETE FROM dbo.MedidorEquipo;

    -- Solicitudes, tramites, documentos y tareas.
    DELETE FROM dbo.DocumentoSolicitud;
    DELETE FROM dbo.HistorialEstadoSolicitud;
    DELETE FROM dbo.SolicitudConexion;
    DELETE FROM dbo.AltaSocio;
    DELETE FROM dbo.DocumentoPersona;
    DELETE FROM dbo.TareaLaboral;
    DELETE FROM dbo.SolicitudServicio;

    -- Facturacion, reclamos, turnos y anuncios de prueba.
    DELETE FROM dbo.ConfirmacionFactura;
    DELETE FROM dbo.Factura;
    DELETE FROM dbo.Reclamo;
    DELETE FROM dbo.Turnos;
    DELETE FROM dbo.Anuncio;

    -- Ubicaciones y vinculaciones operativas de socios.
    DELETE FROM dbo.UbicacionServicio;
    DELETE FROM dbo.ServicioSocio;

    -- Accesos ficticios y relaciones laborales; la cuenta Administrador queda.
    DELETE FROM dbo.EmpleadoFuncion;
    DELETE FROM dbo.EncargadoArea;
    DELETE FROM dbo.EmpleadoArea;
    DELETE FROM dbo.UsuarioSistema WHERE idUsuarioSistema<>@idAdmin;
    DELETE FROM dbo.Empleado;
    DELETE FROM dbo.Socio;
    DELETE FROM dbo.Persona;

    -- Reiniciar identificadores solo en tablas operativas vaciadas.
    DECLARE @tabla sysname, @sql nvarchar(4000);
    DECLARE reset_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT v.Tabla
        FROM (VALUES
            (N'AltaSocio'),(N'Anuncio'),(N'AsignacionMedidor'),(N'AuditoriaLaboral'),
            (N'ContextoRutaMedidor'),(N'CorreccionLecturaLaboral'),(N'DocumentoPersona'),
            (N'DocumentoSolicitud'),(N'Empleado'),(N'EmpleadoArea'),(N'EmpleadoFuncion'),
            (N'EncargadoArea'),(N'EventoMedidor'),(N'EvidenciaLaboral'),(N'EvidenciaLecturaLaboral'),
            (N'Factura'),(N'HistorialEstadoSolicitud'),(N'HistorialTerritorial'),(N'Medicion'),
            (N'MedidorEquipo'),(N'Persona'),(N'Reclamo'),(N'RutaMedicion'),(N'RutaMedicionDetalle'),
            (N'ServicioSocio'),(N'SolicitudConexion'),(N'SolicitudServicio'),(N'TareaLaboral'),
            (N'Turnos'),(N'UsuarioSistema')
        ) v(Tabla)
        JOIN sys.tables t ON t.name=v.Tabla AND SCHEMA_NAME(t.schema_id)=N'dbo'
        JOIN sys.identity_columns ic ON ic.object_id=t.object_id;
    OPEN reset_cursor;
    FETCH NEXT FROM reset_cursor INTO @tabla;
    WHILE @@FETCH_STATUS=0
    BEGIN
        IF @tabla<>N'UsuarioSistema'
        BEGIN
            SET @sql=N'DBCC CHECKIDENT (N''dbo.'+REPLACE(@tabla,N'''',N'''''')+N''', RESEED, 0) WITH NO_INFOMSGS;';
            EXEC sys.sp_executesql @sql;
        END;
        FETCH NEXT FROM reset_cursor INTO @tabla;
    END;
    CLOSE reset_cursor;
    DEALLOCATE reset_cursor;

    IF (SELECT COUNT(*) FROM dbo.UsuarioSistema)<>1 OR NOT EXISTS
       (SELECT 1 FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol
        WHERE u.idUsuarioSistema=@idAdmin AND r.NombreRol=N'Administrador' AND u.idEmpleado IS NULL AND u.idSocio IS NULL)
        THROW 51104, 'La cuenta Administrador no quedo preservada correctamente.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Persona) OR EXISTS (SELECT 1 FROM dbo.Socio) OR EXISTS (SELECT 1 FROM dbo.Empleado)
       OR EXISTS (SELECT 1 FROM dbo.ServicioSocio) OR EXISTS (SELECT 1 FROM dbo.Medidor)
       OR EXISTS (SELECT 1 FROM dbo.Medicion) OR EXISTS (SELECT 1 FROM dbo.Reclamo) OR EXISTS (SELECT 1 FROM dbo.Turnos)
        THROW 51105, 'Quedaron datos operativos; se revierte todo el reinicio.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','reset_cursor')>=0 CLOSE reset_cursor;
    IF CURSOR_STATUS('local','reset_cursor')>-3 DEALLOCATE reset_cursor;
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- Validacion resumida: servicios/catalogos siguen cargados; datos ficticios vaciados.
SELECT N'Servicio' Tabla,COUNT(*) Filas FROM dbo.Servicio
UNION ALL SELECT N'RequisitoServicio',COUNT(*) FROM dbo.RequisitoServicio
UNION ALL SELECT N'AreaServicio',COUNT(*) FROM dbo.AreaServicio
UNION ALL SELECT N'Socio',COUNT(*) FROM dbo.Socio
UNION ALL SELECT N'Empleado',COUNT(*) FROM dbo.Empleado
UNION ALL SELECT N'Persona',COUNT(*) FROM dbo.Persona
UNION ALL SELECT N'ServicioSocio',COUNT(*) FROM dbo.ServicioSocio
UNION ALL SELECT N'UsuarioSistema',COUNT(*) FROM dbo.UsuarioSistema;
