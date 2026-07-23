using System;
using System.Linq;
using System.Web.Http;
using System.Security.Claims;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/empleado")]
    public class EmpleadoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var query =
                    from e in db.Empleado
                    join p in db.Persona on e.idPersona equals p.idPersona
                    join c in db.Cargo on e.idCargo equals c.idCargo
                    select new
                    {
                        e.idEmpleado,
                        e.idPersona,
                        e.idCargo,
                        e.FechaDelInicio,
                        e.Activo,
                        Persona = new
                        {
                            p.idPersona,
                            p.Nombre,
                            p.Apellido,
                            p.Documento,
                            p.Telefono,
                            p.Email,
                            p.Calle,
                            p.Numero
                        },
                        Cargo = new
                        {
                            c.idCargo,
                            Nombre = c.NombreCargo
                        }
                    };

                if (User.IsInRole("Empleado"))
                {
                    var idEmpleadoActual = ObtenerClaimEntero("idEmpleado");
                    if (!idEmpleadoActual.HasValue) return Unauthorized();
                    query = query.Where(e => e.idEmpleado == idEmpleadoActual.Value);
                }

                var empleados = query.ToList();

                return Ok(empleados);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear(dynamic data)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    int idSocio = data.idSocio;
                    int idCargo = data.idCargo;
                    DateTime fechaInicio = data.FechaDeInicio;

                    var socio = db.Socio.Find(idSocio);

                    if (socio == null)
                        return BadRequest("Socio no encontrado.");

                    bool yaEsEmpleado = db.Empleado.Any(e => e.idPersona == socio.idPersona);

                    if (yaEsEmpleado)
                        return BadRequest("La persona ya es empleado.");

                    var empleado = new Empleado
                    {
                        idPersona = socio.idPersona,
                        idCargo = idCargo,
                        FechaDelInicio = fechaInicio,
                        Activo = true
                    };

                    db.Empleado.Add(empleado);
                    db.SaveChanges();

                    var rolEmpleado = db.Rol.FirstOrDefault(r => r.NombreRol == "Empleado");

                    if (rolEmpleado == null)
                        return BadRequest("No existe el rol Empleado.");

                    var usuario = db.UsuarioSistema.FirstOrDefault(u => u.idSocio == socio.idSocio);

                    if (usuario == null)
                        return BadRequest("El socio no tiene usuario.");

                    usuario.idEmpleado = empleado.idEmpleado;
                    usuario.idRol = rolEmpleado.idRol;

                    db.SaveChanges();
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Empleado creado correctamente.",
                        idEmpleado = empleado.idEmpleado
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest("Error: " + ex.Message);
                }
            }
        }
        [HttpPut]
        [Route("{id:int}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Actualizar(int id, dynamic data)
        {
            try
            {
                var empleado = db.Empleado.Find(id);

                if (empleado == null)
                    return NotFound();

                int idCargo = data.idCargo;
                bool cargoExiste = db.Cargo.Any(c => c.idCargo == idCargo);

                if (!cargoExiste)
                    return BadRequest("El cargo seleccionado no existe.");

                empleado.idCargo = idCargo;
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Empleado actualizado correctamente.",
                    empleado.idEmpleado,
                    empleado.idCargo,
                    empleado.FechaDelInicio,
                    empleado.Activo
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpDelete]
        [Route("{id:int}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Eliminar(int id)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var empleado = db.Empleado.Find(id);

                    if (empleado == null)
                        return NotFound();

                    empleado.Activo = false;

                    var usuario = db.UsuarioSistema.FirstOrDefault(u => u.idEmpleado == empleado.idEmpleado);
                    if (usuario != null)
                    {
                        usuario.idEmpleado = null;

                        var rolSocio = db.Rol.FirstOrDefault(r => r.NombreRol == "Socio");
                        if (rolSocio != null)
                            usuario.idRol = rolSocio.idRol;
                    }

                    db.SaveChanges();
                    trans.Commit();

                    return Ok("Empleado dado de baja correctamente.");
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest("Error al eliminar empleado: " + ex.Message);
                }
            }
        }



        [HttpPut]
        [Route("estado/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult CambiarEstado(int id)
        {
            try
            {
                var empleado = db.Empleado.Find(id);

                if (empleado == null)
                    return NotFound();

                empleado.Activo = !empleado.Activo;
                db.SaveChanges();

                return Ok("Estado actualizado.");
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
