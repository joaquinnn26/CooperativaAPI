using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;
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
        [JwtAuthorize(Roles = "Administrador,Empleado")]
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
                        m.idServicioSocio,

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

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar medidores de otro socio.");

                var medidores = (
                    from m in db.Medidor
                    join ss in db.ServicioSocio on m.idServicioSocio equals ss.idServicioSocio
                    join s in db.Socio on ss.idSocio equals s.idSocio
                    join p in db.Persona on s.idPersona equals p.idPersona
                    join serv in db.Servicio on ss.idServicio equals serv.idServicio
                    join tm in db.TipoMedidor on m.idTipoMedidor equals tm.idTipoMedidor
                    where ss.idSocio == idSocio && ss.Activo == true && m.Activo == true
                    select new
                    {
                        m.idMedidor,
                        ss.idSocio,
                        m.idServicioSocio,
                        ss.idServicio,
                        m.idTipoMedidor,
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
            catch (Exception)
            {
                return InternalServerError();
            }
        }

        // =========================================
        // OBTENER UNO
        // =========================================
        [HttpGet]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
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
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear([FromBody] Medidor medidor)
        { return Content(HttpStatusCode.Conflict,"Usá Inventario de medidores para registrar la operación con motivo e historial."); }

        // =========================================
        // EDITAR
        // =========================================
        [HttpPut]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Editar(int id, [FromBody] Medidor medidorEditado)
        { return Content(HttpStatusCode.Conflict,"Usá Inventario de medidores para registrar la operación con motivo e historial."); }

        // =========================================
        // BAJA LOGICA
        // =========================================
        [HttpPut]
        [Route("estado/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult CambiarEstado(int id)
        { return Content(HttpStatusCode.Conflict,"Usá Inventario de medidores para registrar la operación con motivo e historial."); }

        // =========================================
        // ELIMINAR
        // =========================================
        [HttpDelete]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Eliminar(int id)
        { return Content(HttpStatusCode.Conflict,"Usá Inventario de medidores para registrar la operación con motivo e historial."); }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
