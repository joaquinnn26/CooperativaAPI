SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.EstadoSolicitudServicio WHERE NombreEstado='Pendiente documentacion')
 INSERT dbo.EstadoSolicitudServicio(NombreEstado) VALUES('Pendiente documentacion');
IF NOT EXISTS(SELECT 1 FROM dbo.EstadoSolicitudServicio WHERE NombreEstado='Pendiente instalacion')
 INSERT dbo.EstadoSolicitudServicio(NombreEstado) VALUES('Pendiente instalacion');
/* Solo altas sin asignar ni iniciar y sin una revision administrativa previa.
   No se supone aprobacion documental para trabajos que ya se realizaron. */
DECLARE @migradas TABLE(idSolicitudServicio int,idEstadoSolicitud int);
UPDATE s SET idEstadoSolicitud=e.idEstadoSolicitud
 OUTPUT INSERTED.idSolicitudServicio,INSERTED.idEstadoSolicitud INTO @migradas
 FROM dbo.SolicitudServicio s
 JOIN dbo.EstadoSolicitudServicio actual ON actual.idEstadoSolicitud=s.idEstadoSolicitud
 CROSS JOIN dbo.EstadoSolicitudServicio e
 WHERE actual.NombreEstado='Pendiente instalacion' AND e.NombreEstado='Pendiente documentacion'
 AND s.FechaResolucion IS NULL
 AND EXISTS(SELECT 1 FROM dbo.TareaLaboral t WHERE t.OrigenId=s.idSolicitudServicio AND t.Categoria='Instalacion' AND t.Estado='PendienteAsignacion' AND t.idEmpleadoAsignado IS NULL)
 AND NOT EXISTS(SELECT 1 FROM dbo.TareaLaboral t WHERE t.OrigenId=s.idSolicitudServicio AND t.Categoria='Solicitud' AND t.Estado='Cerrada')
 AND NOT EXISTS(SELECT 1 FROM dbo.TareaLaboral t JOIN dbo.AuditoriaLaboral a ON a.idTarea=t.idTarea WHERE t.OrigenId=s.idSolicitudServicio AND t.Categoria='Instalacion' AND a.Accion IN('Asignar','Iniciar','NoSePudoInstalar','AprobarDocumentos','EnviarRevision','Aprobar','Cerrar'));
INSERT dbo.HistorialEstadoSolicitud(idSolicitudServicio,idEstadoSolicitud,FechaCambio,Observaciones)
 SELECT idSolicitudServicio,idEstadoSolicitud,GETDATE(),'Revision documental requerida antes de asignar la instalacion. Ajuste del flujo para solicitudes aun sin iniciar.' FROM @migradas;
SELECT COUNT(*) SolicitudesPendientesDeRevision FROM @migradas;
COMMIT TRANSACTION;
