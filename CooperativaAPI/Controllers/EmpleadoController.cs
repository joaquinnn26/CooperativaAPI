using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/empleado")]
    public class EmpleadoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var empleados = (
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
                    }
                ).ToList();

                return Ok(empleados);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
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
        [Route("estado/{id}")]
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
    }
}