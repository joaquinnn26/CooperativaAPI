using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/medicion")]
    public class MedicionController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var mediciones = (
                    from m in db.Medicion
                    join e in db.Empleado on m.idEmpleado equals e.idEmpleado
                    join p in db.Persona on e.idPersona equals p.idPersona
                    join tl in db.TipoLectura on m.idTipoLectura equals tl.idTipoLectura
                    join med in db.Medidor on m.idMedidor equals med.idMedidor
                    join ss in db.ServicioSocio on m.idServicioSocio equals ss.idServicioSocio
                    join serv in db.Servicio on ss.idServicio equals serv.idServicio
                    select new
                    {
                        m.idMedicion,
                        m.idMedidor,
                        idSocio = ss.idSocio,
                        idEmpleado = m.idEmpleado,
                        Empleado = p.Nombre + " " + p.Apellido,
                        Medidor = med.Numero,
                        Servicio = serv.Nombre,
                        TipoLectura = tl.Nombre,
                        m.FechaLectura,
                        m.FechaCarga,
                        ValorLectura = m.ValorLectura,
                        ConsumoCalculado = m.ConsumoCalculado,
                        m.Periodo,
                        m.FotoURL,
                        m.Observaciones
                    }
                ).ToList();

                return Ok(mediciones);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar mediciones de otro socio.");

                var mediciones = (
                    from m in db.Medicion
                    join med in db.Medidor on m.idMedidor equals med.idMedidor
                    join ss in db.ServicioSocio on m.idServicioSocio equals ss.idServicioSocio
                    join serv in db.Servicio on ss.idServicio equals serv.idServicio
                    join tl in db.TipoLectura on m.idTipoLectura equals tl.idTipoLectura
                    join e in db.Empleado on m.idEmpleado equals e.idEmpleado
                    join p in db.Persona on e.idPersona equals p.idPersona
                    where ss.idSocio == idSocio
                    select new
                    {
                        m.idMedicion,
                        m.idMedidor,
                        idSocio = ss.idSocio,
                        idEmpleado = m.idEmpleado,
                        Empleado = p.Nombre + " " + p.Apellido,
                        Medidor = med.Numero,
                        Servicio = serv.Nombre,
                        TipoLectura = tl.Nombre,
                        m.FechaLectura,
                        m.FechaCarga,
                        ValorLectura = m.ValorLectura,
                        ConsumoCalculado = m.ConsumoCalculado,
                        m.Periodo,
                        m.FotoURL,
                        m.Observaciones
                    }
                ).ToList();

                return Ok(mediciones);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener mediciones del socio: " + ex.Message);
            }
        }

        [HttpGet]
        [Route("medidor/{idMedidor:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult GetByMedidor(int idMedidor)
        {
            try
            {
                var mediciones = (
                    from m in db.Medicion
                    join tl in db.TipoLectura on m.idTipoLectura equals tl.idTipoLectura
                    join e in db.Empleado on m.idEmpleado equals e.idEmpleado
                    join p in db.Persona on e.idPersona equals p.idPersona
                    where m.idMedidor == idMedidor
                    select new
                    {
                        m.idMedicion,
                        m.idMedidor,
                        idEmpleado = m.idEmpleado,
                        Empleado = p.Nombre + " " + p.Apellido,
                        Medidor = m.Medidor.Numero,
                        TipoLectura = tl.Nombre,
                        m.FechaLectura,
                        m.FechaCarga,
                        ValorLectura = m.ValorLectura,
                        ConsumoCalculado = m.ConsumoCalculado,
                        m.Periodo,
                        m.FotoURL,
                        m.Observaciones
                    }
                ).ToList();

                return Ok(mediciones);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener mediciones del medidor: " + ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult Crear([FromBody] Medicion medicion)
        {
            return BadRequest("Registrá la lectura desde la hoja de ruta con la foto obligatoria del medidor.");
        }
        [HttpPut]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Editar(int id, [FromBody] Medicion medicionEditada)
        {
            try
            {
                var medicion = db.Medicion.Find(id);
                if (medicion == null)
                    return NotFound();

                bool existeTipoLectura = db.TipoLectura.Any(t => t.idTipoLectura == medicionEditada.idTipoLectura);
                if (!existeTipoLectura)
                    return BadRequest("El tipo de lectura no existe.");

                if (medicionEditada.ValorLectura < 0)
                    return BadRequest("La lectura no puede ser negativa.");

                var medicionAnterior = db.Medicion
                    .Where(m => m.idMedidor == medicion.idMedidor && m.idMedicion != medicion.idMedicion && m.FechaLectura <= medicion.FechaLectura)
                    .OrderByDescending(m => m.FechaLectura)
                    .ThenByDescending(m => m.idMedicion)
                    .FirstOrDefault();

                decimal lecturaAnterior = medicionAnterior != null ? medicionAnterior.ValorLectura : 0;

                if (medicionEditada.ValorLectura < lecturaAnterior)
                    return BadRequest("La lectura actual no puede ser menor a la lectura anterior.");

                var medidor = db.Medidor.FirstOrDefault(m => m.idMedidor == medicion.idMedidor);
                if (medidor == null)
                    return BadRequest("El medidor asociado no existe.");

                // Se conserva la conexión histórica de esta lectura.
                medicion.idTipoLectura = medicionEditada.idTipoLectura;
                medicion.ValorLectura = medicionEditada.ValorLectura;
                medicion.ConsumoCalculado = medicionEditada.ValorLectura - lecturaAnterior;
                medicion.Observaciones = medicionEditada.Observaciones;
                medicion.FotoURL = medicionEditada.FotoURL;

                if (medicionEditada.FechaLectura != default(DateTime))
                    medicion.FechaLectura = medicionEditada.FechaLectura;

                if (!string.IsNullOrWhiteSpace(medicionEditada.Periodo))
                    medicion.Periodo = medicionEditada.Periodo;

                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Medición actualizada.",
                    lecturaAnterior = lecturaAnterior,
                    lecturaActual = medicion.ValorLectura,
                    consumoCalculado = medicion.ConsumoCalculado
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpDelete]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var medicion = db.Medicion.Find(id);
                if (medicion == null)
                    return NotFound();

                db.Medicion.Remove(medicion);
                db.SaveChanges();

                return Ok("Medición eliminada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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
