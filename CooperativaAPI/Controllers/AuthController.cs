using System;
using System.Linq;
using System.Web.Http;
using System.Data.Entity;
using CooperativaAPI.DTOs;
using CooperativaAPI.Helpers;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null ||
                string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Datos inválidos.");
            }

            try
            {
                var usuario = db.UsuarioSistema
                    .Include(u => u.Rol)
                    .Include(u => u.Socio2.Persona)
                    .Include(u => u.Empleado.Cargo)
                    .FirstOrDefault(u => u.Socio2.Persona.Email == request.Email);

                if (usuario == null)
                    return Unauthorized();

                bool passwordValida = PasswordHelpers.Verificar(
                    request.Password,
                    usuario.Contrasenia
                );

                if (!passwordValida)
                    return Unauthorized();

                if (usuario.idEmpleado.HasValue && usuario.Empleado != null)
                    return BadRequest("El empleado se encuentra inactivo.");

                string token = JwtHelper.GenerarToken(usuario);

                var persona = usuario.Socio2?.Persona;
                var cargo = usuario.Empleado?.Cargo?.NombreCargo;

                return Ok(new
                {
                    mensaje = "Login exitoso",
                    token = token,
                    usuario = usuario.Usuario,
                    nombre = persona != null ? (persona.Nombre + " " + persona.Apellido).Trim() : usuario.Usuario,
                    email = persona?.Email,
                    rol = usuario.Rol?.NombreRol,
                    idSocio = usuario.idSocio,
                    idEmpleado = usuario.idEmpleado,
                    cargo = cargo
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error en login: " + ex.Message);
            }
        }
    }
}

