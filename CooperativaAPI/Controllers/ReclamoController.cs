using System;
using System.Linq;
using System.Web.Http;
using System.Net;


namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/reclamo")]
    public class ReclamoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();
        [HttpGet]
        [Route("socio/{idSocio:int}")]
        public IHttpActionResult GetReclamosPorSocio(int idSocio)
        {
            try
            {
                var reclamos = db.Reclamo
                    .Where(r => r.idSocio == idSocio && r.Activo == true)
                    .Select(r => new
                    {
                        r.idReclamo,
                        r.Descripcion,
                        r.Fecha,
                        r.Imagen,
                        Estado = r.EstadoReclamo.NombreEstado,
                        Tipo = r.TipoReclamo.NombreReclamo
                    })
                    .OrderByDescending(r => r.idReclamo)
                    .ToList();

                return Ok(reclamos);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener los reclamos del socio: " + ex.Message);
            }
        }
        [HttpGet]
        [Route("tipoReclamo")]
        public IHttpActionResult GetTipoReclamo()
        {
            try
            {
                var tipos = db.TipoReclamo
                    .Select(t => new
                    {
                        t.idTipoReclamo,
                        t.NombreReclamo
                    })
                    .ToList();

                return Ok(tipos);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener tipos de reclamo: " + ex.Message);
            }
        }

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var reclamos = db.Reclamo
                    .Where(r => r.Activo == true)
                    .Select(r => new
                    {
                        r.idReclamo,
                        r.Descripcion,
                        r.Fecha,
                        r.Imagen,
                        Estado = r.EstadoReclamo.NombreEstado,
                        Tipo = r.TipoReclamo.NombreReclamo,
                        Socio = new
                        {
                            r.Socio.idSocio,
                            r.Socio.Persona.Nombre,
                            r.Socio.Persona.Apellido,
                            r.Socio.Persona.Documento
                        }
                    })
                    .OrderByDescending(r => r.idReclamo)
                    .ToList();

                return Ok(reclamos);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al listar reclamos: " + ex.Message);
            }
        }


        [HttpPost]
        [Route("")]
        public IHttpActionResult Crear([FromBody] Reclamo nuevo)
        {
            try
            {
                if (nuevo == null)
                    return BadRequest("No se recibió ningún reclamo.");

                if (string.IsNullOrWhiteSpace(nuevo.Descripcion))
                    return BadRequest("La descripción no puede estar vacía.");


                var socioExiste = db.Socio.Any(s => s.idSocio == nuevo.idSocio && s.Activo == true);
                if (!socioExiste)
                    return BadRequest($"El socio con ID {nuevo.idSocio} no existe o está inactivo.");


                var tipoExiste = db.TipoReclamo.Any(t => t.idTipoReclamo == nuevo.idTipoReclamo);
                if (!tipoExiste)
                    return BadRequest($"El tipo de reclamo con ID {nuevo.idTipoReclamo} no existe.");


                if (nuevo.idEstadoReclamo == 0)
                    nuevo.idEstadoReclamo = 1;

                var estadoExiste = db.EstadoReclamo.Any(e => e.idEstadoReclamo == nuevo.idEstadoReclamo);
                if (!estadoExiste)
                    return BadRequest($"El estado de reclamo con ID {nuevo.idEstadoReclamo} no existe.");


                nuevo.Fecha = DateTime.Now;
                nuevo.Activo = true;

                if (!string.IsNullOrEmpty(nuevo.Imagen))
                {
                    nuevo.Imagen = nuevo.Imagen;
                }

                db.Reclamo.Add(nuevo);
                db.SaveChanges();

                return Ok(new
                {
                    message = "Reclamo creado correctamente.",
                    idReclamo = nuevo.idReclamo,
                    imagenGuardada = !string.IsNullOrEmpty(nuevo.Imagen)
                });
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.BadRequest, new
                {
                    mensaje = ex.InnerException?.Message ?? ex.Message
                });
            }
        }


        [HttpPut]
        [Route("{id:int}")]
        public IHttpActionResult Editar(int id, [FromBody] Reclamo datos)
        {
            try
            {
                var reclamo = db.Reclamo.FirstOrDefault(r => r.idReclamo == id && r.Activo == true);
                if (reclamo == null)
                    return NotFound();

                if (!string.IsNullOrWhiteSpace(datos.Descripcion))
                    reclamo.Descripcion = datos.Descripcion;

                if (datos.idEstadoReclamo != 0)
                {
                    var estadoValido = db.EstadoReclamo.Any(e => e.idEstadoReclamo == datos.idEstadoReclamo);
                    if (!estadoValido)
                        return BadRequest("El estado de reclamo ingresado no existe.");

                    reclamo.idEstadoReclamo = datos.idEstadoReclamo;
                }

                db.SaveChanges();
                return Ok(new { message = "Reclamo actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al actualizar reclamo: " + ex.Message);
            }
        }

        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var reclamo = db.Reclamo.FirstOrDefault(r => r.idReclamo == id && r.Activo == true);
                if (reclamo == null)
                    return NotFound();

                reclamo.Activo = false;
                db.SaveChanges();

                return Ok(new { message = "Reclamo dado de baja correctamente." });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al eliminar reclamo: " + ex.Message);
            }
        }
    }
}
