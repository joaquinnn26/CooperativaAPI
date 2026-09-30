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
					int? idSocio = (int?)data.idSocio;
                    int? idPersona = (int?)data.idPersona;
					int idCargo = data.idCargo;
					DateTime fechaInicio = data.FechaDeInicio;

					var socio = idSocio.HasValue?db.Socio.Find(idSocio.Value):null;
                    var persona=db.Persona.Find(idPersona??socio?.idPersona);

					if (persona == null)
						return BadRequest("Persona no encontrada.");

					bool yaEsEmpleado = db.Empleado.Any(e => e.idPersona == persona.idPersona);

					if (yaEsEmpleado)
						return BadRequest("La persona ya es empleado.");

					var empleado = new Empleado
					{
						idPersona = persona.idPersona,
						idCargo = idCargo,
						FechaDelInicio = fechaInicio,
						Activo = true
					};

					db.Empleado.Add(empleado);
					db.SaveChanges();

					var rolEmpleado = db.Rol.FirstOrDefault(r => r.NombreRol == "Empleado");

					if (rolEmpleado == null)
						return BadRequest("No existe el rol Empleado.");

                    string password=(string)data.PasswordLaboral;
                    if(string.IsNullOrWhiteSpace(password) || password.Length<8 || password.Length>100 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c=>!char.IsLetterOrDigit(c))) return BadRequest("La contraseña laboral debe tener al menos 8 caracteres, mayúscula, minúscula, número y símbolo.");
                    db.UsuarioSistema.Add(new UsuarioSistema {Usuario="laboral-"+empleado.idEmpleado,Contrasenia=CooperativaAPI.Helpers.PasswordHelpers.Hashear(password),idRol=rolEmpleado.idRol,idSocio=null,idEmpleado=empleado.idEmpleado});
                    LaboralAccess.Auditar(db,(ClaimsPrincipal)User,null,null,"AltaEmpleado",null,"Activo","Alta laboral con cuenta independiente",new{empleado.idEmpleado,empleado.idPersona,empleado.idCargo});

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
            using(var trans=db.Database.BeginTransaction()) {
			try
			{
				var empleado = db.Empleado.Find(id);

				if (empleado == null)
					return NotFound();

				int idCargo = data.idCargo;
				bool cargoExiste = db.Cargo.Any(c => c.idCargo == idCargo);

				if (!cargoExiste)
					return BadRequest("El cargo seleccionado no existe.");

				LaboralAccess.Auditar(db,(ClaimsPrincipal)User,null,null,"CambiarCargo",empleado.idCargo.ToString(),idCargo.ToString(),"Cambio de cargo",new{empleado.idEmpleado});
                empleado.idCargo = idCargo;
				db.SaveChanges();
                trans.Commit();

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

                    LaboralAccess.Auditar(db,(ClaimsPrincipal)User,null,null,"BajaEmpleado","Activo","Inactivo","Baja del acceso laboral",new{empleado.idEmpleado});
                    db.Database.ExecuteSqlCommand("UPDATE dbo.EncargadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleado=@id AND HastaUtc IS NULL; UPDATE dbo.EmpleadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleado=@id AND HastaUtc IS NULL",LaboralAccess.P("@id",id));

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
            using(var trans=db.Database.BeginTransaction()) {
			try
			{
				var empleado = db.Empleado.Find(id);

				if (empleado == null)
					return NotFound();

				empleado.Activo = !empleado.Activo;
                if(empleado.Activo!=true) db.Database.ExecuteSqlCommand("UPDATE dbo.EncargadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleado=@id AND HastaUtc IS NULL; UPDATE dbo.EmpleadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleado=@id AND HastaUtc IS NULL",LaboralAccess.P("@id",id));
                LaboralAccess.Auditar(db,(ClaimsPrincipal)User,null,null,"EstadoEmpleado",null,empleado.Activo==true?"Activo":"Inactivo","Cambio de acceso laboral",new{empleado.idEmpleado});
				db.SaveChanges();
                trans.Commit();

				return Ok("Estado actualizado.");
			}
			catch (Exception ex)
			{
				return BadRequest(ex.Message);
			}
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
