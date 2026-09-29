using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/serviciosocio")]
    public class ServicioSocioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpPost]
        [Route("{idSocio}")]
        [JwtAuthorize(Roles = "Administrador")]
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

                    var idsSeleccionados = serviciosSeleccionados.Distinct().ToList();
                    if (idsSeleccionados.Any(id => !db.Servicio.Any(s => s.idServicio == id)))
                        return BadRequest("La seleccion contiene servicios inexistentes.");

                    var actuales = db.ServicioSocio.Where(ss => ss.idSocio == idSocio).ToList();
                    if(actuales.GroupBy(s=>s.idServicio).Any(g=>g.Count()>1))
                    {
                        if(new HashSet<int>(actuales.Where(s=>s.Activo==true).Select(s=>s.idServicio)).SetEquals(idsSeleccionados))
                        { trans.Commit(); return Ok("Las conexiones existentes se conservaron sin cambios."); }
                        return BadRequest("El socio tiene varias conexiones del mismo servicio. No se puede modificar su estado mediante una selección global de servicios.");
                    }
                    foreach (var actual in actuales)
                        actual.Activo = idsSeleccionados.Contains(actual.idServicio);

                    foreach (var idServicio in idsSeleccionados)
                    {
                        var existente = actuales.FirstOrDefault(ss => ss.idServicio == idServicio);
                        if (existente != null)
                        {
                            existente.Activo = true;
                            continue;
                        }

                        db.ServicioSocio.Add(new ServicioSocio
                        {
                            idSocio = idSocio,
                            idServicio = idServicio,
                            FechaAlta = DateTime.Now,
                            Activo = true
                        });
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
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetServiciosPorSocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar servicios de otro socio.");

                var servicios = (
                    from ss in db.ServicioSocio
                    join s in db.Servicio on ss.idServicio equals s.idServicio
                    where ss.idSocio == idSocio && ss.Activo == true
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

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
