using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

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
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
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
        public IHttpActionResult Crear([FromBody] SolicitudServicio solicitud)
        {
            try
            {
                if (solicitud == null)
                    return BadRequest("Datos inválidos.");

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
        public IHttpActionResult Aprobar(int id)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var solicitud = db.SolicitudServicio.Find(id);
                    if (solicitud == null)
                        return NotFound();

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
        public IHttpActionResult Rechazar(int id, [FromBody] dynamic data)
        {
            try
            {
                var solicitud = db.SolicitudServicio.Find(id);
                if (solicitud == null)
                    return NotFound();

                var estado = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Rechazada");

                if (estado == null)
                    return BadRequest("No existe el estado Rechazada.");

                solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                solicitud.MotivoRechazo = data?.motivo;
                solicitud.FechaResolucion = DateTime.Now;

                db.SaveChanges();

                return Ok("Solicitud rechazada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}