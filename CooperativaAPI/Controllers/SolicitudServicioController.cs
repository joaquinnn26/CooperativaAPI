using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;
using System.Security.Claims;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/solicitudservicio")]
    public class SolicitudServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        private string GenerarNumeroMedidor()
        {
            int ultimoId = 0;

            if (db.Medidor.Any())
            {
                ultimoId = db.Medidor.Max(m => m.idMedidor);
            }

            int siguiente = ultimoId + 1;
            return "MED-" + siguiente.ToString("D4");
        }

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var solicitudes = db.SolicitudServicio
                    .Select(ss => new
                    {
                        ss.idSolicitudServicio,
                        ss.idSocio,
                        ss.idServicio,
                        Socio = ss.Socio.Persona.Nombre + " " + ss.Socio.Persona.Apellido,
                        Documento = ss.Socio.Persona.Documento,
                        Servicio = ss.Servicio.Nombre,
                        Estado = ss.EstadoSolicitudServicio.NombreEstado,
                        ss.FechaSolicitud,
                        ss.Observaciones,
                        ss.MotivoRechazo,
                        Documentos = ss.DocumentoSolicitud.Select(d => new
                        {
                            d.idDocumentoSolicitud,
                            d.idRequisitoServicio,
                            d.NombreArchivo,
                            d.RutaArchivo,
                            d.TipoArchivo,
                            d.FechaSubida
                        }).ToList()
                    })
                    .ToList();

                return Ok(solicitudes);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(System.Net.HttpStatusCode.Forbidden, "No puedes consultar solicitudes de otro socio.");

                var solicitudes = db.SolicitudServicio
                    .Where(ss => ss.idSocio == idSocio)
                    .Select(ss => new
                    {
                        ss.idSolicitudServicio,
                        ss.idSocio,
                        ss.idServicio,
                        Socio = ss.Socio.Persona.Nombre + " " + ss.Socio.Persona.Apellido,
                        Documento = ss.Socio.Persona.Documento,
                        Servicio = ss.Servicio.Nombre,
                        Estado = ss.EstadoSolicitudServicio.NombreEstado,
                        ss.FechaSolicitud,
                        ss.Observaciones,
                        ss.MotivoRechazo,
                        Documentos = ss.DocumentoSolicitud.Select(d => new
                        {
                            d.idDocumentoSolicitud,
                            d.idRequisitoServicio,
                            d.NombreArchivo,
                            d.RutaArchivo,
                            d.TipoArchivo,
                            d.FechaSubida
                        }).ToList()
                    })
                    .ToList();

                return Ok(solicitudes);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Socio")]
        public IHttpActionResult Crear([FromBody] SolicitudServicio solicitud)
        {
            try
            {
                if (solicitud == null)
                    return BadRequest("Datos inválidos.");

                var idSocioActual = ObtenerClaimEntero("idSocio");
                if (!idSocioActual.HasValue) return Unauthorized();
                solicitud.idSocio = idSocioActual.Value;

                if (!db.Socio.Any(s => s.idSocio == solicitud.idSocio && s.Activo == true))
                    return BadRequest("El socio no existe o esta inactivo.");

                if (!db.Servicio.Any(s => s.idServicio == solicitud.idServicio))
                    return BadRequest("El servicio no existe.");

                var solicitudActiva = db.SolicitudServicio.Any(s =>
                    s.idSocio == solicitud.idSocio &&
                    s.idServicio == solicitud.idServicio &&
                    s.FechaResolucion == null);
                if (solicitudActiva)
                    return BadRequest("Ya existe una solicitud activa para este servicio.");

                solicitud.FechaSolicitud = DateTime.Now;

                var estadoPendiente = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Pendiente");

                if (estadoPendiente == null)
                    return BadRequest("No existe el estado Pendiente.");

                solicitud.idEstadoSolicitud = estadoPendiente.idEstadoSolicitud;

                db.SolicitudServicio.Add(solicitud);
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Solicitud creada correctamente.",
                    solicitud.idSolicitudServicio
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("aprobar/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Aprobar(int id)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var solicitud = db.SolicitudServicio.Find(id);
                    if (solicitud == null)
                        return NotFound();

                    var estadoActual = solicitud.EstadoSolicitudServicio?.NombreEstado;
                    if (estadoActual != "Pendiente" && estadoActual != "En Revision")
                        return BadRequest("Solo se pueden aprobar solicitudes pendientes o en revision.");

                    var estado = db.EstadoSolicitudServicio
                        .FirstOrDefault(e => e.NombreEstado == "Aprobada");

                    if (estado == null)
                        return BadRequest("No existe el estado Aprobada.");

                    var servicio = db.Servicio.Find(solicitud.idServicio);
                    if (servicio == null)
                        return BadRequest("El servicio no existe.");

                    solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                    solicitud.FechaResolucion = DateTime.Now;
                    solicitud.MotivoRechazo = null;

                    var servicioSocio = db.ServicioSocio.FirstOrDefault(ss =>
                        ss.idSocio == solicitud.idSocio &&
                        ss.idServicio == solicitud.idServicio &&
                        ss.Activo == true);

                    if (servicioSocio == null)
                    {
                        servicioSocio = new ServicioSocio
                        {
                            idSocio = solicitud.idSocio,
                            idServicio = solicitud.idServicio,
                            FechaAlta = DateTime.Now,
                            Activo = true
                        };

                        db.ServicioSocio.Add(servicioSocio);
                        db.SaveChanges();
                    }

                    bool esServicioLuz = servicio.Nombre != null &&
                                         servicio.Nombre.Trim().ToLower() == "luz";

                    bool medidorCreado = false;
                    string numeroMedidor = null;

                    if (esServicioLuz)
                    {
                        bool yaTieneMedidor = db.Medidor.Any(m =>
                            m.idServicioSocio == servicioSocio.idServicioSocio &&
                            m.Activo == true);

                        if (!yaTieneMedidor)
                        {
                            var tipoMedidorDefault = db.TipoMedidor.FirstOrDefault();

                            if (tipoMedidorDefault == null)
                                return BadRequest("No existe un tipo de medidor para asignar al servicio de luz.");

                            var nuevoMedidor = new Medidor
                            {
                                idServicioSocio = servicioSocio.idServicioSocio,
                                idTipoMedidor = tipoMedidorDefault.idTipoMedidor,
                                Numero = GenerarNumeroMedidor(),
                                FechaInstalacion = DateTime.Now,
                                Activo = true
                            };

                            db.Medidor.Add(nuevoMedidor);
                            medidorCreado = true;
                            numeroMedidor = nuevoMedidor.Numero;
                        }
                    }

                    db.SaveChanges();
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Solicitud aprobada.",
                        servicio = servicio.Nombre,
                        idServicioSocio = servicioSocio.idServicioSocio,
                        medidorCreado = medidorCreado,
                        numeroMedidor = numeroMedidor
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        [HttpPut]
        [Route("rechazar/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Rechazar(int id, [FromBody] dynamic data)
        {
            try
            {
                var solicitud = db.SolicitudServicio.Find(id);
                if (solicitud == null)
                    return NotFound();

                var estadoActual = solicitud.EstadoSolicitudServicio?.NombreEstado;
                if (estadoActual != "Pendiente" && estadoActual != "En Revision")
                    return BadRequest("Solo se pueden rechazar solicitudes pendientes o en revision.");

                var motivo = Convert.ToString(data?.motivo)?.Trim();
                if (string.IsNullOrWhiteSpace(motivo))
                    return BadRequest("El motivo de rechazo es obligatorio.");
                if (motivo.Length > 500)
                    return BadRequest("El motivo de rechazo no puede superar 500 caracteres.");

                var estado = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Rechazada");

                if (estado == null)
                    return BadRequest("No existe el estado Rechazada.");

                solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                solicitud.MotivoRechazo = motivo;
                solicitud.FechaResolucion = DateTime.Now;

                db.SaveChanges();

                return Ok("Solicitud rechazada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
