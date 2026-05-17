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

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var solicitudes = (
                    from ss in db.SolicitudServicio
                    join s in db.Socio
                        on ss.idSocio equals s.idSocio
                    join p in db.Persona
                        on s.idPersona equals p.idPersona
                    join serv in db.Servicio
                        on ss.idServicio equals serv.idServicio
                    join est in db.EstadoSolicitudServicio
                        on ss.idEstadoSolicitud equals est.idEstadoSolicitud
                    select new
                    {
                        ss.idSolicitudServicio,
                        Socio = p.Nombre + " " + p.Apellido,
                        Servicio = serv.Nombre,
                        Estado = est.NombreEstado,
                        ss.FechaSolicitud,
                        ss.Observaciones
                    }
                ).ToList();

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
                solicitud.FechaSolicitud = DateTime.Now;

                var estadoPendiente = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Pendiente");

                solicitud.idEstadoSolicitud = estadoPendiente.idEstadoSolicitud;

                db.SolicitudServicio.Add(solicitud);
                db.SaveChanges();

                return Ok("Solicitud creada correctamente.");
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

                    solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                    solicitud.FechaResolucion = DateTime.Now;

                    var servicioSocio = new ServicioSocio
                    {
                        idSocio = solicitud.idSocio,
                        idServicio = solicitud.idServicio,
                        FechaAlta = DateTime.Now,
                        Activo = true
                    };

                    db.ServicioSocio.Add(servicioSocio);

                    db.SaveChanges();

                    trans.Commit();

                    return Ok("Solicitud aprobada.");
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

                solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                solicitud.MotivoRechazo = data.motivo;
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