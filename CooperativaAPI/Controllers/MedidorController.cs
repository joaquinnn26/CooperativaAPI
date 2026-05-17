using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/medidor")]
    public class MedidorController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        // =========================================
        // GENERAR NUMERO AUTOMATICO
        // =========================================
        private string GenerarNumeroMedidor()
        {
            int ultimoId = 0;

            if (db.Medidor.Any())
            {
                ultimoId = db.Medidor.Max(m => m.idMedidor);
            }

            int siguiente = ultimoId + 1;

            return "MED-" + siguiente.ToString("D4");
        }

        // =========================================
        // LISTAR TODOS
        // =========================================
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var medidores = (
                    from m in db.Medidor

                    join ss in db.ServicioSocio
                        on m.idServicioSocio equals ss.idServicioSocio

                    join s in db.Socio
                        on ss.idSocio equals s.idSocio

                    join p in db.Persona
                        on s.idPersona equals p.idPersona

                    join serv in db.Servicio
                        on ss.idServicio equals serv.idServicio

                    join tm in db.TipoMedidor
                        on m.idTipoMedidor equals tm.idTipoMedidor

                    where m.Activo == true

                    select new
                    {
                        m.idMedidor,

                        m.Numero,

                        TipoMedidor = tm.Nombre,

                        Servicio = serv.Nombre,

                        Socio = p.Nombre + " " + p.Apellido,

                        p.Documento,

                        m.FechaInstalacion,

                        m.Activo
                    }
                ).ToList();

                return Ok(medidores);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al listar medidores: " + ex.Message);
            }
        }

        // =========================================
        // OBTENER UNO
        // =========================================
        [HttpGet]
        [Route("{id}")]
        public IHttpActionResult GetById(int id)
        {
            try
            {
                var medidor = (
                    from m in db.Medidor

                    join ss in db.ServicioSocio
                        on m.idServicioSocio equals ss.idServicioSocio

                    join s in db.Socio
                        on ss.idSocio equals s.idSocio

                    join p in db.Persona
                        on s.idPersona equals p.idPersona

                    join serv in db.Servicio
                        on ss.idServicio equals serv.idServicio

                    join tm in db.TipoMedidor
                        on m.idTipoMedidor equals tm.idTipoMedidor

                    where m.idMedidor == id

                    select new
                    {
                        m.idMedidor,

                        m.Numero,

                        m.idServicioSocio,

                        m.idTipoMedidor,

                        TipoMedidor = tm.Nombre,

                        Servicio = serv.Nombre,

                        Socio = p.Nombre + " " + p.Apellido,

                        p.Documento,

                        m.FechaInstalacion,

                        m.Activo
                    }
                ).FirstOrDefault();

                if (medidor == null)
                    return NotFound();

                return Ok(medidor);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener medidor: " + ex.Message);
            }
        }

        // =========================================
        // CREAR
        // =========================================
        [HttpPost]
        [Route("")]
        public IHttpActionResult Crear([FromBody] Medidor medidor)
        {
            try
            {
                if (medidor == null)
                    return BadRequest("Datos inválidos.");

                // VALIDAR SERVICIO SOCIO
                bool existeServicioSocio = db.ServicioSocio
                    .Any(ss => ss.idServicioSocio == medidor.idServicioSocio);

                if (!existeServicioSocio)
                    return BadRequest("El servicio del socio no existe.");

                // VALIDAR TIPO MEDIDOR
                bool existeTipo = db.TipoMedidor
                    .Any(tm => tm.idTipoMedidor == medidor.idTipoMedidor);

                if (!existeTipo)
                    return BadRequest("El tipo de medidor no existe.");

                // GENERAR NUMERO
                medidor.Numero = GenerarNumeroMedidor();

                // DATOS AUTOMATICOS
                medidor.Activo = true;

                medidor.FechaInstalacion = DateTime.Now;

                db.Medidor.Add(medidor);

                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Medidor creado correctamente.",
                    numeroGenerado = medidor.Numero
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al crear medidor: " + ex.Message);
            }
        }

        // =========================================
        // EDITAR
        // =========================================
        [HttpPut]
        [Route("{id}")]
        public IHttpActionResult Editar(int id, [FromBody] Medidor medidorEditado)
        {
            try
            {
                var medidor = db.Medidor.Find(id);

                if (medidor == null)
                    return NotFound();

                medidor.idTipoMedidor = medidorEditado.idTipoMedidor;

                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Medidor actualizado correctamente."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al editar medidor: " + ex.Message);
            }
        }

        // =========================================
        // BAJA LOGICA
        // =========================================
        [HttpPut]
        [Route("estado/{id}")]
        public IHttpActionResult CambiarEstado(int id)
        {
            try
            {
                var medidor = db.Medidor.Find(id);

                if (medidor == null)
                    return NotFound();

                medidor.Activo = !medidor.Activo;

                db.SaveChanges();

                string estado = medidor.Activo
                    ? "activado"
                    : "desactivado";

                return Ok("Medidor " + estado + " correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cambiar estado: " + ex.Message);
            }
        }

        // =========================================
        // ELIMINAR
        // =========================================
        [HttpDelete]
        [Route("{id}")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var medidor = db.Medidor.Find(id);

                if (medidor == null)
                    return NotFound();

                bool tieneMediciones = db.Medicion
                    .Any(m => m.idMedidor == id);

                if (tieneMediciones)
                    return BadRequest("No se puede eliminar porque tiene mediciones asociadas.");

                db.Medidor.Remove(medidor);

                db.SaveChanges();

                return Ok("Medidor eliminado correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al eliminar medidor: " + ex.Message);
            }
        }
    }
}