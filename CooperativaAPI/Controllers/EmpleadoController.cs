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

        // GET TODOS
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var empleados = (
                    from e in db.Empleado
                    join p in db.Persona
                        on e.idPersona equals p.idPersona
                    join c in db.Cargo
                        on e.idCargo equals c.idCargo
                    select new
                    {
                        e.idEmpleado,
                        Nombre = p.Nombre,
                        Apellido = p.Apellido,
                        p.Documento,
                        Cargo = c.NombreCargo,
                        e.FechaDelInicio,
                        e.Activo
                    }
                ).ToList();

                return Ok(empleados);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // CREAR EMPLEADO DESDE SOCIO
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

                    // BUSCAR SOCIO
                    var socio = db.Socio.Find(idSocio);

                    if (socio == null)
                        return BadRequest("Socio no encontrado.");

                    // VERIFICAR SI YA ES EMPLEADO
                    bool yaEsEmpleado = db.Empleado
                        .Any(e => e.idPersona == socio.idPersona);

                    if (yaEsEmpleado)
                        return BadRequest("La persona ya es empleado.");

                    // CREAR EMPLEADO
                    var empleado = new Empleado
                    {
                        idPersona = socio.idPersona,
                        idCargo = idCargo,
                        FechaDelInicio = fechaInicio,
                        Activo = true
                    };

                    db.Empleado.Add(empleado);

                    db.SaveChanges();

                    // BUSCAR ROL EMPLEADO
                    var rolEmpleado = db.Rol
                        .FirstOrDefault(r => r.NombreRol == "Empleado");

                    if (rolEmpleado == null)
                        return BadRequest("No existe el rol Empleado.");

                    // BUSCAR USUARIO
                    var usuario = db.UsuarioSistema
                        .FirstOrDefault(u => u.idSocio == socio.idSocio);

                    if (usuario == null)
                        return BadRequest("El socio no tiene usuario.");

                    // ACTUALIZAR USUARIO
                    usuario.idEmpleado = empleado.idEmpleado;
                    usuario.idRol = rolEmpleado.idRol;

                    db.SaveChanges();

                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Empleado creado correctamente."
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();

                    return BadRequest("Error: " + ex.Message);
                }
            }
        }

        // BAJA LOGICA
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