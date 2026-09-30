using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/turnos")]
    public class TurnosController : ApiController
    {
        private const string ConnectionStringName = "DefaultConnection";

        private static readonly string[] HorariosPermitidos =
        {
            "08:00", "08:30", "09:00", "09:30",
            "10:00", "10:30", "11:00", "11:30",
            "12:00", "12:30", "13:00", "13:30"
        };

        private static readonly int[] EstadosQueOcupanHorario = { 1, 2, 3, 6 };

        [HttpGet]
        [Route("disponibilidad")]
        [JwtAuthorize(Roles = "Administrador,Socio,Empleado")]
        public IHttpActionResult ObtenerDisponibilidad(string desde = null, int dias = 12)
        {
            try
            {
                DateTime fechaInicio;
                if (!ResolverFechaInicio(desde, out fechaInicio))
                {
                    return BadRequest("La fecha desde no es valida.");
                }

                if (fechaInicio < DateTime.Today)
                {
                    fechaInicio = DateTime.Today;
                }

                if (dias < 1) dias = 1;
                if (dias > 30) dias = 30;

                var fechaHasta = fechaInicio.AddDays(dias * 4);
                var ocupados = ObtenerHorariosOcupados(fechaInicio, fechaHasta, null);

                var resultado = new List<object>();
                var fecha = fechaInicio;
                var guard = 0;

                while (resultado.Count < dias && guard < dias * 4)
                {
                    if (EsDiaHabil(fecha))
                    {
                        var horarios = HorariosPermitidos
                            .Where(h => !HorarioPasado(fecha, TimeSpan.Parse(h)))
                            .Where(h => !ocupados.Contains(ClaveHorario(fecha, TimeSpan.Parse(h))))
                            .ToList();

                        resultado.Add(new
                        {
                            fecha = fecha.ToString("yyyy-MM-dd"),
                            disponible = horarios.Any(),
                            horarios = horarios
                        });
                    }

                    fecha = fecha.AddDays(1);
                    guard++;
                }

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Socio,Empleado,Administrador")]
        public IHttpActionResult CrearTurno([FromBody] CrearTurnoRequest request)
        {
            try
            {
                var idSocioActual = ObtenerClaimEntero("idSocio");
                if(User.IsInRole("Socio")) {
                    if (!idSocioActual.HasValue) return Unauthorized();
                    if (request != null) request.IdSocio = idSocioActual.Value;
                } else {
                    using(var laboral=new CooperativaEntities()) {
                        if(!LaboralAccess.Atencion(laboral,(ClaimsPrincipal)User)) return StatusCode(HttpStatusCode.Forbidden);
                        if(request==null || !request.IdSocio.HasValue || !laboral.Socio.Any(x=>x.idSocio==request.IdSocio && x.Activo==true))return BadRequest("Seleccioná un socio activo.");
                    }
                    if(!new[]{"Telefono","Ventanilla"}.Contains(request.Canal))return BadRequest("Seleccioná el canal de atención.");
                }

                DateTime fecha;
                TimeSpan hora;
                var error = ValidarTurnoNuevo(request, out fecha, out hora);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return BadRequest(error);
                }

                int idTurno;

                using (var connection = CrearConexion())
                {
                    connection.Open();

                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        if (!HorarioDisponible(connection, transaction, fecha, hora, null))
                        {
                            transaction.Rollback();
                            return Content(HttpStatusCode.Conflict, new { message = "El horario seleccionado ya no esta disponible." });
                        }

                        using (var command = new SqlCommand(@"
INSERT INTO dbo.Turnos
    (IdSocio, SocioNombre, SocioDocumento, Fecha, Hora, Motivo, IdEstadoTurno, FechaCreacion)
OUTPUT inserted.IdTurno
VALUES
    (@IdSocio, @SocioNombre, @SocioDocumento, @Fecha, @Hora, @Motivo, 1, SYSUTCDATETIME());", connection, transaction))
                        {
                            command.Parameters.Add("@IdSocio", SqlDbType.Int).Value = request.IdSocio.Value;
                            command.Parameters.Add("@SocioNombre", SqlDbType.NVarChar, 160).Value = ValorDb(StringOrNull(request.SocioNombre));
                            command.Parameters.Add("@SocioDocumento", SqlDbType.NVarChar, 50).Value = ValorDb(StringOrNull(request.SocioDocumento));
                            command.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
                            command.Parameters.Add("@Hora", SqlDbType.Time).Value = hora;
                            command.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = request.Motivo.Trim();

                            idTurno = Convert.ToInt32(command.ExecuteScalar());
                        }

                        AuditarTurno(connection,transaction,idTurno,null,"Pendiente","RecibirTurno",User.IsInRole("Socio")?"App":request.Canal);
                        transaction.Commit();
                    }
                }

                return Ok(ObtenerTurnoDto(idTurno));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio,Empleado")]
        public IHttpActionResult ObtenerTurnosPorSocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar turnos de otro socio.");

                using (var connection = CrearConexion())
                using (var command = new SqlCommand(QueryTurnosBase + @"
WHERE t.IdSocio = @IdSocio
ORDER BY t.Fecha DESC, t.Hora DESC;", connection))
                {
                    command.Parameters.Add("@IdSocio", SqlDbType.Int).Value = idSocio;
                    connection.Open();

                    return Ok(LeerTurnos(command));
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("{idTurno:int}/cancelar")]
        [JwtAuthorize(Roles = "Administrador,Socio,Empleado")]
        public IHttpActionResult CancelarTurno(int idTurno)
        {
            try
            {
                var turnoActual = ObtenerTurnoPorId(idTurno);
                if (turnoActual == null) return NotFound();
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != turnoActual.IdSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes cancelar turnos de otro socio.");
                if (turnoActual.IdEstadoTurno == 5)
                    return Ok(ObtenerTurnoDto(idTurno));
                if (turnoActual.IdEstadoTurno == 4)
                    return Content(HttpStatusCode.Conflict, "Un turno rechazado no puede cancelarse.");

                using (var connection = CrearConexion())
                {
                    connection.Open();

                    using (var command = new SqlCommand(@"
UPDATE dbo.Turnos
SET IdEstadoTurno = 5,
    FechaPropuesta = NULL,
    HoraPropuesta = NULL,
    FechaActualizacion = SYSUTCDATETIME()
WHERE IdTurno = @IdTurno
  AND IdEstadoTurno NOT IN (4, 5);", connection))
                    {
                        command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;

                        int filasAfectadas = command.ExecuteNonQuery();

                        if (filasAfectadas == 0)
                            return Content(HttpStatusCode.Conflict, "El turno no se puede cancelar en su estado actual.");
                    }
                }

                return Ok(ObtenerTurnoDto(idTurno));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("{idTurno:int}/aceptar-propuesta")]
        [JwtAuthorize(Roles = "Socio")]
        public IHttpActionResult AceptarPropuesta(int idTurno)
        {
            try
            {
                TurnoRow turno;

                using (var connection = CrearConexion())
                {
                    connection.Open();

                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        turno = ObtenerTurnoRow(connection, transaction, idTurno);
                        if (turno == null)
                        {
                            transaction.Rollback();
                            return NotFound();
                        }

                        if (ObtenerClaimEntero("idSocio") != turno.IdSocio)
                        {
                            transaction.Rollback();
                            return Content(HttpStatusCode.Forbidden, "No puedes aceptar propuestas de otro socio.");
                        }

                        if (turno.IdEstadoTurno != 3)
                        {
                            transaction.Rollback();
                            return Content(HttpStatusCode.Conflict, "Solo se puede aceptar un turno en estado Propuesto.");
                        }

                        if (!turno.FechaPropuesta.HasValue || !turno.HoraPropuesta.HasValue)
                        {
                            transaction.Rollback();
                            return BadRequest("El turno no tiene una propuesta pendiente.");
                        }

                        var fechaPropuesta = turno.FechaPropuesta.Value.Date;
                        var horaPropuesta = turno.HoraPropuesta.Value;

                        string mensaje;
                        if (!FechaYHoraValidas(fechaPropuesta, horaPropuesta, out mensaje))
                        {
                            transaction.Rollback();
                            return BadRequest(mensaje);
                        }

                        if (!HorarioDisponible(connection, transaction, fechaPropuesta, horaPropuesta, idTurno))
                        {
                            transaction.Rollback();
                            return Content(HttpStatusCode.Conflict, new { message = "La propuesta ya no esta disponible." });
                        }

                        using (var command = new SqlCommand(@"
UPDATE dbo.Turnos
SET Fecha = @Fecha,
    Hora = @Hora,
    IdEstadoTurno = 6,
    FechaPropuesta = NULL,
    HoraPropuesta = NULL
WHERE IdTurno = @IdTurno;", connection, transaction))
                        {
                            command.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaPropuesta;
                            command.Parameters.Add("@Hora", SqlDbType.Time).Value = horaPropuesta;
                            command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }

                return Ok(ObtenerTurnoDto(idTurno));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpDelete]
        [Route("{idTurno:int}")]
        [JwtAuthorize(Roles = "Socio")]
        public IHttpActionResult EliminarTurno(int idTurno)
        {
            return CancelarTurno(idTurno);
        }
        [HttpDelete]
        [Route("~/api/admin/turnos/{idTurno:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult EliminarTurnoAdmin(int idTurno)
        {
            return CancelarTurno(idTurno);
        }
        [HttpGet]
        [Route("~/api/admin/turnos")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult ObtenerTurnosAdmin()
        {
            try
            {
                using (var connection = CrearConexion())
                using (var command = new SqlCommand(QueryTurnosBase + @"
ORDER BY
    CASE WHEN t.IdEstadoTurno IN (1, 3) THEN 0 ELSE 1 END,
    t.Fecha,
    t.Hora;", connection))
                {
                    connection.Open();
                    return Ok(LeerTurnos(command));
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("~/api/admin/turnos/{idTurno:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult ActualizarTurnoAdmin(int idTurno, [FromBody] ActualizarTurnoAdminRequest request)
        {
            try
            {
                var estadoId = ResolverEstadoId(request);
                if (!estadoId.HasValue)
                {
                    return BadRequest("Estado de turno no valido.");
                }

                using (var connection = CrearConexion())
                {
                    connection.Open();

                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        var turno = ObtenerTurnoRow(connection, transaction, idTurno);
                        if (turno == null)
                        {
                            transaction.Rollback();
                            return NotFound();
                        }

                        if (!TransicionTurnoValida(turno.IdEstadoTurno, estadoId.Value))
                        {
                            transaction.Rollback();
                            return Content(HttpStatusCode.Conflict, "La transicion de estado del turno no es valida.");
                        }

                        if (estadoId.Value == 4) {
                            using(var motivo = new SqlCommand("SELECT Nombre FROM dbo.MotivoLaboral WHERE idMotivo=@id AND Area='ADMINISTRACION' AND Accion='Rechazar' AND Activo=1",connection,transaction)) {
                                motivo.Parameters.AddWithValue("@id",(object)request.idMotivo??DBNull.Value);
                                var nombre=motivo.ExecuteScalar() as string;
                                if(nombre==null)return BadRequest("Seleccioná un motivo vigente de rechazo.");
                                request.ObservacionAdmin=nombre;
                            }
                        }
                        if (estadoId.Value == 2)
                        {
                            string mensaje;
                            if (!FechaYHoraValidas(turno.Fecha, turno.Hora, out mensaje))
                            {
                                transaction.Rollback();
                                return BadRequest(mensaje);
                            }

                            if (!HorarioDisponible(connection, transaction, turno.Fecha, turno.Hora, idTurno))
                            {
                                transaction.Rollback();
                                return Content(HttpStatusCode.Conflict, new { message = "El horario solicitado ya no esta disponible." });
                            }

                            ActualizarComoAprobado(connection, transaction, idTurno, request);
                        }
                        else if (estadoId.Value == 3)
                        {
                            DateTime fechaPropuesta;
                            TimeSpan horaPropuesta;
                            var error = ValidarPropuesta(request, out fechaPropuesta, out horaPropuesta);

                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                transaction.Rollback();
                                return BadRequest(error);
                            }

                            if (!HorarioDisponible(connection, transaction, fechaPropuesta, horaPropuesta, idTurno))
                            {
                                transaction.Rollback();
                                return Content(HttpStatusCode.Conflict, new { message = "El horario propuesto ya no esta disponible." });
                            }

                            ActualizarComoPropuesto(connection, transaction, idTurno, fechaPropuesta, horaPropuesta, request);
                        }
                        else
                        {
                            ActualizarEstadoSimple(connection, transaction, idTurno, estadoId.Value, request);
                        }

                        if(estadoId.Value==4 && string.IsNullOrWhiteSpace(request.ObservacionAdmin))return BadRequest("Indicá el motivo del rechazo.");
                        AuditarTurno(connection,transaction,idTurno,turno.IdEstadoTurno.ToString(),estadoId.Value.ToString(),"DecidirTurno",request.ObservacionAdmin);
                        transaction.Commit();
                    }
                }

                return Ok(ObtenerTurnoDto(idTurno));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private void AuditarTurno(SqlConnection c,SqlTransaction tx,int id,string antes,string despues,string accion,string motivo) {
            using(var cmd=new SqlCommand(@"INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoAnterior,EstadoNuevo,Motivo,Datos) VALUES('ADMINISTRACION',@u,@e,@a,@antes,@nuevo,@motivo,@datos)",c,tx)) {
                cmd.Parameters.AddWithValue("@u",ObtenerClaimEntero("idUsuarioSistema"));cmd.Parameters.AddWithValue("@e",(object)ObtenerClaimEntero("idEmpleado")??DBNull.Value);
                cmd.Parameters.AddWithValue("@a",accion);cmd.Parameters.AddWithValue("@antes",(object)antes??DBNull.Value);cmd.Parameters.AddWithValue("@nuevo",(object)despues??DBNull.Value);cmd.Parameters.AddWithValue("@motivo",(object)motivo??DBNull.Value);cmd.Parameters.AddWithValue("@datos",Newtonsoft.Json.JsonConvert.SerializeObject(new{idTurno=id}));cmd.ExecuteNonQuery();
            }
        }
        private const string QueryTurnosBase = @"
SELECT
    t.IdTurno,
    t.IdSocio,
    t.SocioNombre,
    t.SocioDocumento,
    t.Fecha,
    t.Hora,
    t.Motivo,
    t.IdEstadoTurno,
    t.FechaPropuesta,
    t.HoraPropuesta,
    t.ObservacionAdmin,
    t.FechaCreacion,
    t.FechaActualizacion,
    e.Nombre AS Estado
FROM dbo.Turnos t
INNER JOIN dbo.EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno
";

        private SqlConnection CrearConexion()
        {
            var settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                throw new InvalidOperationException("No existe la connection string DefaultConnection.");
            }

            return new SqlConnection(settings.ConnectionString);
        }

        private List<object> LeerTurnos(SqlCommand command)
        {
            var turnos = new List<object>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    turnos.Add(MapTurno(LeerTurno(reader)));
                }
            }

            return turnos;
        }

        private object ObtenerTurnoDto(int idTurno)
        {
            using (var connection = CrearConexion())
            using (var command = new SqlCommand(QueryTurnosBase + @"
WHERE t.IdTurno = @IdTurno;", connection))
            {
                command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;
                connection.Open();

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return MapTurno(LeerTurno(reader));
                }
            }
        }

        private TurnoRow ObtenerTurnoRow(SqlConnection connection, SqlTransaction transaction, int idTurno)
        {
            using (var command = new SqlCommand(QueryTurnosBase + @"
WHERE t.IdTurno = @IdTurno;", connection, transaction))
            {
                command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return LeerTurno(reader);
                }
            }
        }

        private TurnoRow LeerTurno(SqlDataReader reader)
        {
            return new TurnoRow
            {
                IdTurno = Convert.ToInt32(reader["IdTurno"]),
                IdSocio = Convert.ToInt32(reader["IdSocio"]),
                SocioNombre = LeerString(reader, "SocioNombre"),
                SocioDocumento = LeerString(reader, "SocioDocumento"),
                Fecha = Convert.ToDateTime(reader["Fecha"]).Date,
                Hora = LeerHora(reader, "Hora").Value,
                Motivo = LeerString(reader, "Motivo"),
                IdEstadoTurno = Convert.ToInt32(reader["IdEstadoTurno"]),
                FechaPropuesta = LeerFechaNullable(reader, "FechaPropuesta"),
                HoraPropuesta = LeerHora(reader, "HoraPropuesta"),
                ObservacionAdmin = LeerString(reader, "ObservacionAdmin"),
                FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                FechaActualizacion = LeerFechaNullable(reader, "FechaActualizacion"),
                Estado = LeerString(reader, "Estado")
            };
        }

        private object MapTurno(TurnoRow turno)
        {
            var socioNombre = string.IsNullOrWhiteSpace(turno.SocioNombre)
                ? "Socio " + turno.IdSocio
                : turno.SocioNombre;

            return new
            {
                id = turno.IdTurno,
                idTurno = turno.IdTurno,
                idSocio = turno.IdSocio,
                socio = socioNombre,
                socioNombre = socioNombre,
                documentoSocio = turno.SocioDocumento,
                socioDocumento = turno.SocioDocumento,
                fecha = turno.Fecha.ToString("yyyy-MM-dd"),
                hora = FormatHora(turno.Hora),
                motivo = turno.Motivo,
                idEstadoTurno = turno.IdEstadoTurno,
                estado = turno.Estado,
                fechaPropuesta = turno.FechaPropuesta.HasValue ? turno.FechaPropuesta.Value.ToString("yyyy-MM-dd") : null,
                horaPropuesta = turno.HoraPropuesta.HasValue ? FormatHora(turno.HoraPropuesta.Value) : null,
                observacionAdmin = turno.ObservacionAdmin,
                fechaCreacion = turno.FechaCreacion,
                fechaActualizacion = turno.FechaActualizacion
            };
        }

        private string ValidarTurnoNuevo(CrearTurnoRequest request, out DateTime fecha, out TimeSpan hora)
        {
            fecha = DateTime.MinValue;
            hora = TimeSpan.Zero;

            if (request == null)
            {
                return "La solicitud es obligatoria.";
            }

            if (!request.IdSocio.HasValue || request.IdSocio.Value <= 0)
            {
                return "El socio es obligatorio.";
            }

            if (!request.Fecha.HasValue)
            {
                return "La fecha es obligatoria.";
            }

            if (string.IsNullOrWhiteSpace(request.Hora))
            {
                return "La hora es obligatoria.";
            }

            if (string.IsNullOrWhiteSpace(request.Motivo))
            {
                return "El motivo es obligatorio.";
            }

            if (request.Motivo.Trim().Length > 500)
            {
                return "El motivo no puede superar 500 caracteres.";
            }

            fecha = request.Fecha.Value.Date;

            if (!TryParseHora(request.Hora, out hora))
            {
                return "La hora no es valida.";
            }

            string mensaje;
            if (!FechaYHoraValidas(fecha, hora, out mensaje))
            {
                return mensaje;
            }

            return null;
        }

        private string ValidarPropuesta(ActualizarTurnoAdminRequest request, out DateTime fechaPropuesta, out TimeSpan horaPropuesta)
        {
            fechaPropuesta = DateTime.MinValue;
            horaPropuesta = TimeSpan.Zero;

            if (request == null || !request.FechaPropuesta.HasValue || string.IsNullOrWhiteSpace(request.HoraPropuesta))
            {
                return "Para proponer otro horario debes enviar fechaPropuesta y horaPropuesta.";
            }

            fechaPropuesta = request.FechaPropuesta.Value.Date;

            if (!TryParseHora(request.HoraPropuesta, out horaPropuesta))
            {
                return "La hora propuesta no es valida.";
            }

            string mensaje;
            if (!FechaYHoraValidas(fechaPropuesta, horaPropuesta, out mensaje))
            {
                return mensaje;
            }

            return null;
        }

        private bool FechaYHoraValidas(DateTime fecha, TimeSpan hora, out string mensaje)
        {
            if (fecha.Date < DateTime.Today)
            {
                mensaje = "La fecha debe ser desde hoy en adelante.";
                return false;
            }

            if (!EsDiaHabil(fecha))
            {
                mensaje = "Solo se pueden solicitar turnos de lunes a viernes.";
                return false;
            }

            if (!HorariosPermitidos.Contains(FormatHora(hora)))
            {
                mensaje = "El horario no esta dentro de la disponibilidad permitida.";
                return false;
            }

            if (HorarioPasado(fecha, hora))
            {
                mensaje = "El horario seleccionado ya paso.";
                return false;
            }

            mensaje = null;
            return true;
        }

        private bool HorarioDisponible(SqlConnection connection, SqlTransaction transaction, DateTime fecha, TimeSpan hora, int? excluirIdTurno)
        {
            var clave = ClaveHorario(fecha, hora);
            var ocupados = ObtenerHorariosOcupados(connection, transaction, fecha.Date, fecha.Date, excluirIdTurno);

            return !ocupados.Contains(clave);
        }

        private HashSet<string> ObtenerHorariosOcupados(DateTime desde, DateTime hasta, int? excluirIdTurno)
        {
            using (var connection = CrearConexion())
            {
                connection.Open();
                return ObtenerHorariosOcupados(connection, null, desde, hasta, excluirIdTurno);
            }
        }

        private HashSet<string> ObtenerHorariosOcupados(SqlConnection connection, SqlTransaction transaction, DateTime desde, DateTime hasta, int? excluirIdTurno)
        {
            var ocupados = new HashSet<string>();
            var lockHint = transaction == null ? string.Empty : " WITH (UPDLOCK, HOLDLOCK)";
            var estados = string.Join(",", EstadosQueOcupanHorario.Select(e => e.ToString(CultureInfo.InvariantCulture)).ToArray());

            using (var command = new SqlCommand(@"
SELECT Fecha, Hora
FROM dbo.Turnos" + lockHint + @"
WHERE Fecha >= @Desde
  AND Fecha <= @Hasta
  AND IdEstadoTurno IN (" + estados + @")
  AND (@ExcluirIdTurno IS NULL OR IdTurno <> @ExcluirIdTurno)

UNION ALL

SELECT FechaPropuesta AS Fecha, HoraPropuesta AS Hora
FROM dbo.Turnos" + lockHint + @"
WHERE IdEstadoTurno = 3
  AND FechaPropuesta IS NOT NULL
  AND HoraPropuesta IS NOT NULL
  AND FechaPropuesta >= @Desde
  AND FechaPropuesta <= @Hasta
  AND (@ExcluirIdTurno IS NULL OR IdTurno <> @ExcluirIdTurno);", connection, transaction))
            {
                command.Parameters.Add("@Desde", SqlDbType.Date).Value = desde.Date;
                command.Parameters.Add("@Hasta", SqlDbType.Date).Value = hasta.Date;
                command.Parameters.Add("@ExcluirIdTurno", SqlDbType.Int).Value = ValorDb(excluirIdTurno);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var fecha = Convert.ToDateTime(reader["Fecha"]).Date;
                        var hora = LeerHora(reader, "Hora");

                        if (hora.HasValue)
                        {
                            ocupados.Add(ClaveHorario(fecha, hora.Value));
                        }
                    }
                }
            }

            return ocupados;
        }

        private void ActualizarComoAprobado(SqlConnection connection, SqlTransaction transaction, int idTurno, ActualizarTurnoAdminRequest request)
        {
            using (var command = new SqlCommand(@"
UPDATE dbo.Turnos
SET IdEstadoTurno = 2,
    FechaPropuesta = NULL,
    HoraPropuesta = NULL,
    ObservacionAdmin = COALESCE(@ObservacionAdmin, ObservacionAdmin)
WHERE IdTurno = @IdTurno;", connection, transaction))
            {
                command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;
                command.Parameters.Add("@ObservacionAdmin", SqlDbType.NVarChar, 500).Value = ValorDb(StringOrNull(request == null ? null : request.ObservacionAdmin));
                command.ExecuteNonQuery();
            }
        }

        private void ActualizarComoPropuesto(SqlConnection connection, SqlTransaction transaction, int idTurno, DateTime fechaPropuesta, TimeSpan horaPropuesta, ActualizarTurnoAdminRequest request)
        {
            using (var command = new SqlCommand(@"
UPDATE dbo.Turnos
SET IdEstadoTurno = 3,
    FechaPropuesta = @FechaPropuesta,
    HoraPropuesta = @HoraPropuesta,
    ObservacionAdmin = @ObservacionAdmin
WHERE IdTurno = @IdTurno;", connection, transaction))
            {
                command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;
                command.Parameters.Add("@FechaPropuesta", SqlDbType.Date).Value = fechaPropuesta.Date;
                command.Parameters.Add("@HoraPropuesta", SqlDbType.Time).Value = horaPropuesta;
                command.Parameters.Add("@ObservacionAdmin", SqlDbType.NVarChar, 500).Value = ValorDb(StringOrNull(request == null ? null : request.ObservacionAdmin));
                command.ExecuteNonQuery();
            }
        }

        private void ActualizarEstadoSimple(SqlConnection connection, SqlTransaction transaction, int idTurno, int estadoId, ActualizarTurnoAdminRequest request)
        {
            using (var command = new SqlCommand(@"
UPDATE dbo.Turnos
SET IdEstadoTurno = @IdEstadoTurno,
    FechaPropuesta = NULL,
    HoraPropuesta = NULL,
    ObservacionAdmin = COALESCE(@ObservacionAdmin, ObservacionAdmin)
WHERE IdTurno = @IdTurno;", connection, transaction))
            {
                command.Parameters.Add("@IdTurno", SqlDbType.Int).Value = idTurno;
                command.Parameters.Add("@IdEstadoTurno", SqlDbType.Int).Value = estadoId;
                command.Parameters.Add("@ObservacionAdmin", SqlDbType.NVarChar, 500).Value = ValorDb(StringOrNull(request == null ? null : request.ObservacionAdmin));
                command.ExecuteNonQuery();
            }
        }

        private int? ResolverEstadoId(ActualizarTurnoAdminRequest request)
        {
            if (request == null) return null;
            if (request.EstadoId.HasValue && EstadoValido(request.EstadoId.Value)) return request.EstadoId.Value;
            if (request.IdEstadoTurno.HasValue && EstadoValido(request.IdEstadoTurno.Value)) return request.IdEstadoTurno.Value;

            var estado = NormalizarTexto(request.Estado);

            switch (estado)
            {
                case "pendiente":
                    return 1;
                case "aprobado":
                case "aprobada":
                    return 2;
                case "propuesto":
                case "propuesta":
                    return 3;
                case "rechazado":
                case "rechazada":
                    return 4;
                case "cancelado":
                case "cancelada":
                    return 5;
                case "aceptado":
                case "aceptada":
                    return 6;
                default:
                    return null;
            }
        }

        private static bool EstadoValido(int estadoId)
        {
            return estadoId >= 1 && estadoId <= 6;
        }

        private static bool TransicionTurnoValida(int actual, int siguiente)
        {
            if (actual == siguiente) return true;
            if (actual == 1) return siguiente == 2 || siguiente == 3 || siguiente == 4 || siguiente == 5;
            if (actual == 3) return siguiente == 3 || siguiente == 4 || siguiente == 5;
            if (actual == 2 || actual == 6) return siguiente == 5;
            return false;
        }

        private TurnoRow ObtenerTurnoPorId(int idTurno)
        {
            using (var connection = CrearConexion())
            {
                connection.Open();
                return ObtenerTurnoRow(connection, null, idTurno);
            }
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }

        private static bool ResolverFechaInicio(string desde, out DateTime fechaInicio)
        {
            fechaInicio = DateTime.Today;

            if (string.IsNullOrWhiteSpace(desde))
            {
                return true;
            }

            if (DateTime.TryParseExact(desde.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fechaInicio))
            {
                fechaInicio = fechaInicio.Date;
                return true;
            }

            if (DateTime.TryParse(desde, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out fechaInicio))
            {
                fechaInicio = fechaInicio.Date;
                return true;
            }

            return false;
        }

        private static bool EsDiaHabil(DateTime fecha)
        {
            return fecha.DayOfWeek != DayOfWeek.Saturday &&
                   fecha.DayOfWeek != DayOfWeek.Sunday;
        }

        private static bool HorarioPasado(DateTime fecha, TimeSpan hora)
        {
            return fecha.Date.Add(hora) <= DateTime.Now;
        }

        private static bool TryParseHora(string value, out TimeSpan hora)
        {
            hora = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out hora) ||
                   TimeSpan.TryParse(value.Trim(), CultureInfo.InvariantCulture, out hora);
        }

        private static TimeSpan? LeerHora(SqlDataReader reader, string columnName)
        {
            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            if (value is TimeSpan)
            {
                return (TimeSpan)value;
            }

            if (value is DateTime)
            {
                return ((DateTime)value).TimeOfDay;
            }

            TimeSpan hora;
            return TryParseHora(Convert.ToString(value, CultureInfo.InvariantCulture), out hora) ? hora : (TimeSpan?)null;
        }

        private static DateTime? LeerFechaNullable(SqlDataReader reader, string columnName)
        {
            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            return Convert.ToDateTime(value).Date;
        }

        private static string LeerString(SqlDataReader reader, string columnName)
        {
            var value = reader[columnName];
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            return Convert.ToString(value);
        }

        private static string ClaveHorario(DateTime fecha, TimeSpan hora)
        {
            return fecha.ToString("yyyy-MM-dd") + "|" + FormatHora(hora);
        }

        private static string FormatHora(TimeSpan hora)
        {
            return hora.ToString(@"hh\:mm");
        }

        private static string StringOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static object ValorDb(object value)
        {
            return value ?? DBNull.Value;
        }

        private static string NormalizarTexto(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private sealed class TurnoRow
        {
            public int IdTurno { get; set; }
            public int IdSocio { get; set; }
            public string SocioNombre { get; set; }
            public string SocioDocumento { get; set; }
            public DateTime Fecha { get; set; }
            public TimeSpan Hora { get; set; }
            public string Motivo { get; set; }
            public int IdEstadoTurno { get; set; }
            public DateTime? FechaPropuesta { get; set; }
            public TimeSpan? HoraPropuesta { get; set; }
            public string ObservacionAdmin { get; set; }
            public DateTime FechaCreacion { get; set; }
            public DateTime? FechaActualizacion { get; set; }
            public string Estado { get; set; }
        }
    }

    public class CrearTurnoRequest
    {
        public string Canal { get; set; }
        public int? IdSocio { get; set; }
        public string SocioNombre { get; set; }
        public string SocioDocumento { get; set; }
        public DateTime? Fecha { get; set; }
        public string Hora { get; set; }
        public string Motivo { get; set; }
    }

    public class ActualizarTurnoAdminRequest
    {
        public int? idMotivo { get; set; }
        public int? EstadoId { get; set; }
        public int? IdEstadoTurno { get; set; }
        public string Estado { get; set; }
        public DateTime? FechaPropuesta { get; set; }
        public string HoraPropuesta { get; set; }
        public string ObservacionAdmin { get; set; }
    }
}
