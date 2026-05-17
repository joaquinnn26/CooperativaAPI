using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/servicio")]
    public class ServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        // GET
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var servicios = db.Servicio
                    .Select(s => new
                    {
                        s.idServicio,
                        s.Nombre,
                        s.Descripcion,
                        s.idTipoTarifa,
                        TipoTarifa = s.TipoTarifa.NombreTipo,
                        s.TarifaBase
                    })
                    .ToList();

                return Ok(servicios);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al listar servicios: " + ex.Message);
            }
        }

        // POST
        [HttpPost]
        [Route("")]
        public IHttpActionResult Crear([FromBody] Servicio servicio)
        {
            try
            {
                if (servicio == null)
                    return BadRequest("Datos inválidos.");

                if (string.IsNullOrWhiteSpace(servicio.Nombre))
                    return BadRequest("El nombre es obligatorio.");

                db.Servicio.Add(servicio);
                db.SaveChanges();

                return Ok(new
                {
                    message = "Servicio creado correctamente."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al crear servicio: " + ex.Message);
            }
        }

        // PUT
        [HttpPut]
        [Route("{id:int}")]
        public IHttpActionResult Editar(int id, [FromBody] Servicio servicioEditado)
        {
            try
            {
                var servicio = db.Servicio.Find(id);

                if (servicio == null)
                    return NotFound();

                servicio.Nombre = servicioEditado.Nombre;
                servicio.Descripcion = servicioEditado.Descripcion;
                servicio.idTipoTarifa = servicioEditado.idTipoTarifa;
                servicio.TarifaBase = servicioEditado.TarifaBase;

                db.SaveChanges();

                return Ok(new
                {
                    message = "Servicio actualizado correctamente."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al editar servicio: " + ex.Message);
            }
        }

        // DELETE
        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var servicio = db.Servicio.Find(id);

                if (servicio == null)
                    return NotFound();

                db.Servicio.Remove(servicio);
                db.SaveChanges();

                return Ok(new
                {
                    message = "Servicio eliminado correctamente."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al eliminar servicio: " + ex.Message);
            }
        }
    }
}