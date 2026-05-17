using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/requisitoservicio")]
    public class RequisitoServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var requisitos = (
                    from r in db.RequisitoServicio
                    join s in db.Servicio
                        on r.idServicio equals s.idServicio
                    where r.Activo == true
                    select new
                    {
                        r.idRequisitoServicio,
                        r.Nombre,
                        r.Descripcion,
                        r.Obligatorio,
                        Servicio = s.Nombre
                    }
                ).ToList();

                return Ok(requisitos);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("")]
        public IHttpActionResult Crear([FromBody] RequisitoServicio requisito)
        {
            try
            {
                requisito.Activo = true;

                db.RequisitoServicio.Add(requisito);
                db.SaveChanges();

                return Ok("Requisito creado correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("{id}")]
        public IHttpActionResult Editar(int id, [FromBody] RequisitoServicio requisitoEditado)
        {
            try
            {
                var requisito = db.RequisitoServicio.Find(id);

                if (requisito == null)
                    return NotFound();

                requisito.Nombre = requisitoEditado.Nombre;
                requisito.Descripcion = requisitoEditado.Descripcion;
                requisito.Obligatorio = requisitoEditado.Obligatorio;

                db.SaveChanges();

                return Ok("Requisito actualizado.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("estado/{id}")]
        public IHttpActionResult CambiarEstado(int id)
        {
            try
            {
                var requisito = db.RequisitoServicio.Find(id);

                if (requisito == null)
                    return NotFound();

                requisito.Activo = !requisito.Activo;

                db.SaveChanges();

                return Ok("Estado actualizado.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}