using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web.Http;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/reclamo")]
    public class ReclamoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();
        private const string RespuestaFinalSeparador = "\n\nRespuesta final: ";

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio,Empleado")]
        public IHttpActionResult GetReclamosPorSocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar reclamos de otro socio.");

                var reclamos = db.Reclamo
                    .Where(r => r.idSocio == idSocio && r.Activo == true)
                    .OrderByDescending(r => r.idReclamo)
                    .ToList()
                    .Select(MapReclamoSimple)
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
        [JwtAuthorize(Roles = "Administrador,Socio,Empleado")]
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
                    .OrderByDescending(r => r.idReclamo)
                    .ToList()
                    .Select(MapReclamoAdmin)
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
        [JwtAuthorize(Roles = "Socio,Empleado,Administrador")]
        public IHttpActionResult Crear([FromBody] Reclamo nuevo)
        {
            try
            {
                if (nuevo == null)
                    return ValidationError("No se recibio ningun reclamo.");

                var idSocioActual = ObtenerClaimEntero("idSocio");
                if (User.IsInRole("Socio") && !idSocioActual.HasValue) return Unauthorized();
                if(User.IsInRole("Socio")) nuevo.idSocio = idSocioActual.Value;
                else if(!LaboralAccess.Atencion(db,(ClaimsPrincipal)User))return StatusCode(HttpStatusCode.Forbidden);

                nuevo.Descripcion = Limpiar(nuevo.Descripcion);
                if (nuevo.Descripcion.Length < 10 || nuevo.Descripcion.Length > 800 || !TieneLetra(nuevo.Descripcion))
                    return ValidationError("La descripcion debe tener entre 10 y 800 caracteres e incluir al menos una letra.");

                var socioExiste = db.Socio.Any(s => s.idSocio == nuevo.idSocio && s.Activo == true);
                if (!socioExiste)
                    return BadRequest($"El socio con ID {nuevo.idSocio} no existe o esta inactivo.");

                var tipoExiste = db.TipoReclamo.Any(t => t.idTipoReclamo == nuevo.idTipoReclamo);
                if (!tipoExiste)
                    return BadRequest($"El tipo de reclamo con ID {nuevo.idTipoReclamo} no existe.");

                if (nuevo.idEstadoReclamo == 0)
                    nuevo.idEstadoReclamo = 1;

                if (nuevo.idEstadoReclamo != 1)
                    return Content(HttpStatusCode.Conflict, "Un reclamo nuevo debe iniciar en estado Pendiente.");

                nuevo.Fecha = DateTime.Now;
                nuevo.Activo = true;

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
        public IHttpActionResult Editar(int id, [FromBody] ActualizarReclamoRequest datos)
        {
            try
            {
                if (datos == null)
                    return ValidationError("Los datos del reclamo son obligatorios.");

                var reclamo = db.Reclamo.FirstOrDefault(r => r.idReclamo == id && r.Activo == true);
                if (reclamo == null)
                    return NotFound();

                if (!string.IsNullOrWhiteSpace(datos.Descripcion))
                    reclamo.Descripcion = Limpiar(datos.Descripcion);

                if (datos.idEstadoReclamo != 0)
                {
                    var estadoValido = db.EstadoReclamo.Any(e => e.idEstadoReclamo == datos.idEstadoReclamo);
                    if (!estadoValido)
                        return ValidationError("El estado de reclamo ingresado no existe.");

                    if (!TransicionReclamoValida(reclamo.idEstadoReclamo, datos.idEstadoReclamo))
                        return Content(HttpStatusCode.Conflict, "La transicion de estado del reclamo no es valida.");

                    if (datos.idEstadoReclamo == 3)
                    {
                        var respuesta = Limpiar(datos.RespuestaFinal ?? datos.Solucion ?? datos.ObservacionFinal ?? datos.Observacion);
                        if (respuesta.Length < 10 || respuesta.Length > 500 || !TieneLetra(respuesta))
                            return ValidationError("Para cerrar un reclamo, registra una solucion o respuesta final de entre 10 y 500 caracteres.");

                        reclamo.Descripcion = DescripcionConRespuestaFinal(reclamo.Descripcion, respuesta);
                    }

                    reclamo.idEstadoReclamo = datos.idEstadoReclamo;
                }

                db.SaveChanges();
                return Ok(new { message = "Reclamo actualizado correctamente.", reclamo = MapReclamoAdmin(reclamo) });
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

        private object MapReclamoSimple(Reclamo r)
        {
            var partes = SepararDescripcionRespuesta(r.Descripcion);
            return new
            {
                r.idReclamo,
                Descripcion = partes.descripcion,
                RespuestaFinal = partes.respuestaFinal,
                r.Fecha,
                r.Imagen,
                Estado = r.EstadoReclamo.NombreEstado,
                Tipo = r.TipoReclamo.NombreReclamo
            };
        }

        private object MapReclamoAdmin(Reclamo r)
        {
            var partes = SepararDescripcionRespuesta(r.Descripcion);
            return new
            {
                r.idReclamo,
                r.idSocio,
                Descripcion = partes.descripcion,
                RespuestaFinal = partes.respuestaFinal,
                r.Fecha,
                r.Imagen,
                Estado = r.EstadoReclamo.NombreEstado,
                Tipo = r.TipoReclamo.NombreReclamo,
                Socio = new
                {
                    r.Socio.idSocio,
                    r.Socio.Persona.Nombre,
                    r.Socio.Persona.Apellido,
                    r.Socio.Persona.Documento,
                    r.Socio.Persona.Telefono
                }
            };
        }

        private static bool TransicionReclamoValida(int actual, int siguiente)
        {
            if (actual == siguiente) return true;
            if (actual == 1) return siguiente == 2 || siguiente == 4;
            if (actual == 2) return siguiente == 3 || siguiente == 4;
            return false;
        }

        private static string DescripcionConRespuestaFinal(string descripcion, string respuesta)
        {
            var partes = SepararDescripcionRespuesta(descripcion);
            return partes.descripcion + RespuestaFinalSeparador + respuesta;
        }

        private static dynamic SepararDescripcionRespuesta(string descripcion)
        {
            var text = descripcion ?? "";
            var marker = "Respuesta final:";
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return new { descripcion = text.Trim(), respuestaFinal = "" };

            return new
            {
                descripcion = text.Substring(0, index).Trim(),
                respuestaFinal = text.Substring(index + marker.Length).Trim()
            };
        }

        private static IHttpActionResult ValidationError(string message)
        {
            return new System.Web.Http.Results.ResponseMessageResult(
                new System.Net.Http.HttpResponseMessage((HttpStatusCode)422)
                {
                    Content = new System.Net.Http.StringContent(message)
                });
        }

        private static string Limpiar(string value)
        {
            return Regex.Replace(value ?? "", @"\s+", " ").Trim();
        }

        private static bool TieneLetra(string value)
        {
            return Regex.IsMatch(value ?? "", @"\p{L}");
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }

    public class ActualizarReclamoRequest
    {
        public string Descripcion { get; set; }
        public int idEstadoReclamo { get; set; }
        public string RespuestaFinal { get; set; }
        public string Solucion { get; set; }
        public string ObservacionFinal { get; set; }
        public string Observacion { get; set; }
    }
}
