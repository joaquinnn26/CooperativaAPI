using System;
using System.Linq;
using System.Web.Http;
using System.Net;
using System.Security.Claims;
using CooperativaAPI.Filters;


namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/reclamo")]
    public class ReclamoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();
        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetReclamosPorSocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar reclamos de otro socio.");

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
        [JwtAuthorize(Roles = "Administrador,Socio")]
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
        [JwtAuthorize(Roles = "Administrador")]
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
        [JwtAuthorize(Roles = "Socio")]
        public IHttpActionResult Crear([FromBody] Reclamo nuevo)
        {
            try
            {
                if (nuevo == null)
                    return BadRequest("No se recibió ningún reclamo.");

                var idSocioActual = ObtenerClaimEntero("idSocio");
                if (!idSocioActual.HasValue) return Unauthorized();
                nuevo.idSocio = idSocioActual.Value;

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
        [JwtAuthorize(Roles = "Administrador")]
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

                    if (!TransicionReclamoValida(reclamo.idEstadoReclamo, datos.idEstadoReclamo))
                        return BadRequest("La transicion de estado del reclamo no es valida.");

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
        [JwtAuthorize(Roles = "Administrador")]
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

        private static bool TransicionReclamoValida(int actual, int siguiente)
        {
            if (actual == siguiente) return true;
            if (actual == 1) return siguiente == 2 || siguiente == 4;
            if (actual == 2) return siguiente == 3 || siguiente == 4;
            return false;
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
