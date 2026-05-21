using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/medicion")]
    public class MedicionController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
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
                    join ss in db.ServicioSocio on med.idServicioSocio equals ss.idServicioSocio
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
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                var mediciones = (
                    from m in db.Medicion
                    join med in db.Medidor on m.idMedidor equals med.idMedidor
                    join ss in db.ServicioSocio on med.idServicioSocio equals ss.idServicioSocio
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
        public IHttpActionResult Crear([FromBody] Medicion medicion)
        {
            try
            {
                if (medicion == null)
                    return BadRequest("Datos inválidos.");

                var medidor = db.Medidor.FirstOrDefault(m => m.idMedidor == medicion.idMedidor && m.Activo == true);
                if (medidor == null)
                    return BadRequest("El medidor no existe o está inactivo.");

                bool existeEmpleado = db.Empleado.Any(e => e.idEmpleado == medicion.idEmpleado && e.Activo == true);
                if (!existeEmpleado)
                    return BadRequest("El empleado no existe o está inactivo.");

                bool existeTipoLectura = db.TipoLectura.Any(t => t.idTipoLectura == medicion.idTipoLectura);
                if (!existeTipoLectura)
                    return BadRequest("El tipo de lectura no existe.");

                if (medicion.ValorLectura < 0)
                    return BadRequest("La lectura no puede ser negativa.");

                var ultimaMedicion = db.Medicion
                    .Where(m => m.idMedidor == medicion.idMedidor)
                    .OrderByDescending(m => m.FechaLectura)
                    .ThenByDescending(m => m.idMedicion)
                    .FirstOrDefault();

                decimal lecturaAnterior = ultimaMedicion != null ? ultimaMedicion.ValorLectura : 0;

                if (medicion.ValorLectura < lecturaAnterior)
                    return BadRequest("La lectura actual no puede ser menor a la lectura anterior.");

                medicion.idServicioSocio = medidor.idServicioSocio;
                medicion.ConsumoCalculado = medicion.ValorLectura - lecturaAnterior;
                medicion.FechaCarga = DateTime.Now;

                if (medicion.FechaLectura == default(DateTime))
                    medicion.FechaLectura = DateTime.Now;

                if (string.IsNullOrWhiteSpace(medicion.Periodo))
                    medicion.Periodo = medicion.FechaLectura.ToString("yyyy-MM");

                db.Medicion.Add(medicion);
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Medición registrada correctamente.",
                    idMedicion = medicion.idMedicion,
                    lecturaAnterior = lecturaAnterior,
                    lecturaActual = medicion.ValorLectura,
                    consumoCalculado = medicion.ConsumoCalculado
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al registrar medición: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{id}")]
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

                medicion.idServicioSocio = medidor.idServicioSocio;
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
    }
}