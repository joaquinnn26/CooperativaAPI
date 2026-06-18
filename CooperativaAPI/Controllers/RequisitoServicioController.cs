using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Filters;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/requisitoservicio")]
    public class RequisitoServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
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
                        r.idServicio,
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

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear([FromBody] RequisitoServicio requisito)
        {
            try
            {
                var error = ValidarRequisito(requisito);
                if (error != null) return BadRequest(error);
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
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Editar(int id, [FromBody] RequisitoServicio requisitoEditado)
        {
            try
            {
                var requisito = db.RequisitoServicio.Find(id);

                if (requisito == null)
                    return NotFound();

                var error = ValidarRequisito(requisitoEditado);
                if (error != null) return BadRequest(error);

                requisito.Nombre = requisitoEditado.Nombre;
                requisito.Descripcion = requisitoEditado.Descripcion;
                requisito.Obligatorio = requisitoEditado.Obligatorio;
                requisito.idServicio = requisitoEditado.idServicio;

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
        [JwtAuthorize(Roles = "Administrador")]
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

        private string ValidarRequisito(RequisitoServicio requisito)
        {
            if (requisito == null) return "Los datos del requisito son obligatorios.";
            if (string.IsNullOrWhiteSpace(requisito.Nombre)) return "El nombre es obligatorio.";
            if (requisito.Nombre.Trim().Length > 120) return "El nombre no puede superar 120 caracteres.";
            if (!db.Servicio.Any(s => s.idServicio == requisito.idServicio))
                return "El servicio no existe.";
            return null;
        }
    }
}
