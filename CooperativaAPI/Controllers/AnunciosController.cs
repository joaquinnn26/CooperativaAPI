using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/anuncios")]
    public class AnunciosController : ApiController
    {
        private readonly AppDbContext db = new AppDbContext();

        [HttpGet]
        [Route("")]
        public IHttpActionResult ObtenerActivos()
        {
            try
            {
                var ahora = DateTime.UtcNow;
                var anuncios = db.Anuncios
                    .Include(a => a.PrioridadAnuncio)
                    .Where(a => a.Activo)
                    .Where(a => !a.FechaInicio.HasValue || a.FechaInicio <= ahora)
                    .Where(a => !a.FechaFin.HasValue || a.FechaFin >= ahora)
                    .OrderByDescending(a => a.Fijo)
                    .ThenBy(a => a.PrioridadAnuncio.Orden)
                    .ThenByDescending(a => a.FechaPublicacion)
                    .ToList()
                    .Select(MapAnuncio)
                    .ToList();

                return Ok(anuncios);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener anuncios: " + ex.GetBaseException().Message);
            }
        }

        [HttpGet]
        [Route("admin")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult ObtenerTodos()
        {
            try
            {
                var anuncios = db.Anuncios
                    .Include(a => a.PrioridadAnuncio)
                    .OrderByDescending(a => a.FechaPublicacion)
                    .ToList()
                    .Select(MapAnuncio)
                    .ToList();

                return Ok(anuncios);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener anuncios: " + ex.GetBaseException().Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear([FromBody] AnuncioRequest request)
        {
            try
            {
                var error = Validar(request);
                if (!string.IsNullOrWhiteSpace(error)) return BadRequest(error);

                var prioridad = BuscarPrioridad(request.Prioridad);
                if (prioridad == null) return BadRequest("La prioridad seleccionada no existe o esta inactiva.");

                var anuncio = new Anuncio
                {
                    Titulo = request.Titulo.Trim(),
                    Contenido = request.Contenido.Trim(),
                    idPrioridadAnuncio = prioridad.idPrioridadAnuncio,
                    PrioridadAnuncio = prioridad,
                    FechaPublicacion = DateTime.UtcNow,
                    FechaInicio = request.FechaInicio,
                    FechaFin = request.FechaFin,
                    Activo = request.Activo,
                    Fijo = request.Fijo,
                    idEmpleado = request.idEmpleado
                };

                db.Anuncios.Add(anuncio);
                db.SaveChanges();

                return Ok(MapAnuncio(anuncio));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al crear anuncio: " + ex.GetBaseException().Message);
            }
        }

        [HttpPut]
        [Route("{id:int}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Editar(int id, [FromBody] AnuncioRequest request)
        {
            try
            {
                var anuncio = db.Anuncios.Find(id);
                if (anuncio == null) return NotFound();

                var error = Validar(request);
                if (!string.IsNullOrWhiteSpace(error)) return BadRequest(error);

                var prioridad = BuscarPrioridad(request.Prioridad);
                if (prioridad == null) return BadRequest("La prioridad seleccionada no existe o esta inactiva.");

                anuncio.Titulo = request.Titulo.Trim();
                anuncio.Contenido = request.Contenido.Trim();
                anuncio.idPrioridadAnuncio = prioridad.idPrioridadAnuncio;
                anuncio.PrioridadAnuncio = prioridad;
                anuncio.FechaInicio = request.FechaInicio;
                anuncio.FechaFin = request.FechaFin;
                anuncio.Activo = request.Activo;
                anuncio.Fijo = request.Fijo;
                anuncio.idEmpleado = request.idEmpleado;

                db.SaveChanges();
                return Ok(MapAnuncio(anuncio));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al actualizar anuncio: " + ex.GetBaseException().Message);
            }
        }

        [HttpDelete]
        [Route("{id:int}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Desactivar(int id)
        {
            try
            {
                var anuncio = db.Anuncios.Find(id);
                if (anuncio == null) return NotFound();

                anuncio.Activo = false;
                db.SaveChanges();

                return Ok(MapAnuncio(anuncio));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al desactivar anuncio: " + ex.GetBaseException().Message);
            }
        }

        private static object MapAnuncio(Anuncio anuncio)
        {
            return new
            {
                anuncio.idAnuncio,
                anuncio.Titulo,
                anuncio.Contenido,
                Prioridad = anuncio.PrioridadAnuncio != null ? anuncio.PrioridadAnuncio.Nombre : "",
                anuncio.FechaPublicacion,
                anuncio.FechaInicio,
                anuncio.FechaFin,
                anuncio.Activo,
                anuncio.Fijo,
                anuncio.idEmpleado
            };
        }

        private static string Validar(AnuncioRequest request)
        {
            if (request == null) return "Los datos del anuncio son obligatorios.";
            if (string.IsNullOrWhiteSpace(request.Titulo)) return "El titulo es obligatorio.";
            if (request.Titulo.Trim().Length > 120) return "El titulo no puede superar 120 caracteres.";
            if (string.IsNullOrWhiteSpace(request.Contenido)) return "El contenido es obligatorio.";
            if (request.Contenido.Trim().Length > 1000) return "El contenido no puede superar 1000 caracteres.";
            if (!EsPrioridadValida(request.Prioridad)) return "La prioridad debe ser Alta, Media o Baja.";
            if (request.FechaInicio.HasValue && request.FechaFin.HasValue && request.FechaFin < request.FechaInicio)
                return "La fecha de fin no puede ser anterior a la fecha de inicio.";

            return "";
        }

        private PrioridadAnuncio BuscarPrioridad(string prioridad)
        {
            var nombre = NormalizarPrioridad(prioridad);
            return db.PrioridadesAnuncio.FirstOrDefault(p => p.Activo && p.Nombre == nombre);
        }

        private static bool EsPrioridadValida(string prioridad)
        {
            var valor = NormalizarPrioridad(prioridad);
            return valor == "Alta" || valor == "Media" || valor == "Baja";
        }

        private static string NormalizarPrioridad(string prioridad)
        {
            var valor = (prioridad ?? "").Trim().ToLowerInvariant();
            if (valor == "high" || valor == "alta") return "Alta";
            if (valor == "medium" || valor == "media") return "Media";
            if (valor == "low" || valor == "baja") return "Baja";
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class AnuncioRequest
    {
        public string Titulo { get; set; }
        public string Contenido { get; set; }
        public string Prioridad { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public bool Activo { get; set; }
        public bool Fijo { get; set; }
        public int? idEmpleado { get; set; }
    }
}
