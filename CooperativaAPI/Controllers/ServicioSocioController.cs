using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/serviciosocio")]
    public class ServicioSocioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpPost]
        [Route("{idSocio}")]
        public IHttpActionResult ActualizarServicios(int idSocio, [FromBody] List<int> serviciosSeleccionados)
        {
            if (serviciosSeleccionados == null)
                serviciosSeleccionados = new List<int>();

            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var socio = db.Socio.Find(idSocio);
                    if (socio == null)
                        return NotFound();

                    var actuales = db.ServicioSocio.Where(ss => ss.idSocio == idSocio).ToList();
                    if (actuales.Any())
                    {
                        db.ServicioSocio.RemoveRange(actuales);
                        db.SaveChanges();
                    }

                    foreach (var idServicio in serviciosSeleccionados)
                    {
                        var nuevo = new ServicioSocio
                        {
                            idSocio = idSocio,
                            idServicio = idServicio
                        };
                        db.ServicioSocio.Add(nuevo);
                    }

                    db.SaveChanges();
                    trans.Commit();
                    return Ok("Servicios actualizados correctamente.");
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest("Error al actualizar servicios: " + ex.Message);
                }
            }
        }

        [HttpGet]
        [Route("{idSocio}")]
        public IHttpActionResult GetServiciosPorSocio(int idSocio)
        {
            try
            {
                var servicios = (
                    from ss in db.ServicioSocio
                    join s in db.Servicio on ss.idServicio equals s.idServicio
                    where ss.idSocio == idSocio
                    select new
                    {
                        ss.idServicioSocio,
                        ss.idSocio,
                        ss.idServicio,
                        Servicio = s.Nombre,
                        s.Descripcion,
                        s.TarifaBase
                    }
                ).ToList();

                return Ok(servicios);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener servicios del socio: " + ex.Message);
            }
        }
    }
}