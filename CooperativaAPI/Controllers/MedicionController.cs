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

        // LISTAR
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var mediciones = (
                    from m in db.Medicion
                    join e in db.Empleado
                        on m.idEmpleado equals e.idEmpleado
                    join p in db.Persona
                        on e.idPersona equals p.idPersona
                    join tl in db.TipoLectura
                        on m.idTipoLectura equals tl.idTipoLectura
                    join med in db.Medidor
                        on m.idMedidor equals med.idMedidor
                    select new
                    {
                        m.idMedicion,

                        Empleado = p.Nombre + " " + p.Apellido,

                        Medidor = med.Numero,

                        TipoLectura = tl.Nombre,

                        m.FechaLectura,
                        m.FechaCarga,
                        m.ValorLectura,
                        m.ConsumoCalculado,
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

        // CREAR
        [HttpPost]
        [Route("")]
        public IHttpActionResult Crear([FromBody] Medicion medicion)
        {
            try
            {
                if (medicion == null)
                    return BadRequest("Datos inválidos.");

                medicion.FechaCarga = DateTime.Now;

                db.Medicion.Add(medicion);

                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Medición registrada correctamente."
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al registrar medición: " + ex.Message);
            }
        }

        // EDITAR
        [HttpPut]
        [Route("{id}")]
        public IHttpActionResult Editar(int id, [FromBody] Medicion medicionEditada)
        {
            try
            {
                var medicion = db.Medicion.Find(id);

                if (medicion == null)
                    return NotFound();

                medicion.idTipoLectura = medicionEditada.idTipoLectura;
                medicion.ValorLectura = medicionEditada.ValorLectura;
                medicion.ConsumoCalculado = medicionEditada.ConsumoCalculado;
                medicion.Observaciones = medicionEditada.Observaciones;
                medicion.FotoURL = medicionEditada.FotoURL;

                db.SaveChanges();

                return Ok("Medición actualizada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ELIMINAR
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