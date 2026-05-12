using System;
using System.Linq;
using System.Web.Http;
using System.Data.Entity;
using CooperativaAPI.DTOs;
using CooperativaAPI.Helpers;
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
                // 1. Buscar usuario
                var usuario = (from u in db.UsuarioSistema
                               join s in db.Socio on u.idSocio equals s.idSocio
                               join p in db.Persona on s.idPersona equals p.idPersona
                               where p.Email == request.Email
                               select u)
               .FirstOrDefault();


                if (usuario == null)
                    return Unauthorized();

                // 2. Verificar contraseña (SHA256)
                bool passwordValida = PasswordHelpers.Verificar(
                    request.Password,
                    usuario.Contrasenia
                );

                if (!passwordValida)
                    return Unauthorized();

                // 3. Respuesta
                string token = JwtHelper.GenerarToken(usuario);

                return Ok(new
                {
                    mensaje = "Login exitoso",
                    token = token,
                    usuario = usuario.Usuario,
                    rol = usuario.Rol.NombreRol
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error en login: " + ex.Message);
            }
        }
    }
}
