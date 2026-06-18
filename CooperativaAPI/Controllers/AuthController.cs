using System;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Runtime.Caching;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Web.Http;
using System.Data.Entity;
using CooperativaAPI.DTOs;
using CooperativaAPI.Helpers;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

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

                if (!usuario.Contrasenia.StartsWith("$2", StringComparison.Ordinal))
                {
                    usuario.Contrasenia = PasswordHelpers.Hashear(request.Password);
                    db.SaveChanges();
                }

                if (usuario.idEmpleado.HasValue && usuario.Empleado != null && usuario.Empleado.Activo == false)
                    return BadRequest("El empleado se encuentra inactivo.");
                if (usuario.Socio2 != null && usuario.Socio2.Activo == false)
                    return BadRequest("El socio se encuentra inactivo.");

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
            catch (Exception)
            {
                return InternalServerError();
            }
        }

        [HttpPut]
        [Route("cambiar-contrasena")]
        [JwtAuthorize(Roles = "Administrador,Empleado,Socio")]
        public IHttpActionResult CambiarContrasena([FromBody] CambiarContrasenaRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.PasswordActual) ||
                string.IsNullOrWhiteSpace(request.PasswordNueva))
                return BadRequest("La contrasena actual y la nueva son obligatorias.");

            var error = ValidarPassword(request.PasswordNueva);
            if (error != null) return BadRequest(error);
            if (request.PasswordNueva != request.ConfirmarPassword)
                return BadRequest("La nueva contrasena y la confirmacion no coinciden.");

            var idUsuario = ObtenerClaimEntero("idUsuarioSistema");
            if (!idUsuario.HasValue) return Unauthorized();

            var usuario = db.UsuarioSistema.Find(idUsuario.Value);
            if (usuario == null) return NotFound();
            if (!PasswordHelpers.Verificar(request.PasswordActual, usuario.Contrasenia))
                return BadRequest("La contrasena actual es incorrecta.");
            if (PasswordHelpers.Verificar(request.PasswordNueva, usuario.Contrasenia))
                return BadRequest("La nueva contrasena debe ser diferente de la actual.");

            usuario.Contrasenia = PasswordHelpers.Hashear(request.PasswordNueva);
            db.SaveChanges();
            return Ok(new { mensaje = "Contrasena actualizada correctamente." });
        }

        [HttpGet]
        [Route("session")]
        [JwtAuthorize(Roles = "Administrador,Empleado,Socio")]
        public IHttpActionResult ObtenerSesion()
        {
            var idUsuario = ObtenerClaimEntero("idUsuarioSistema");
            if (!idUsuario.HasValue) return Unauthorized();

            var usuario = db.UsuarioSistema
                .Include(u => u.Rol)
                .Include(u => u.Socio2.Persona)
                .Include(u => u.Empleado.Cargo)
                .FirstOrDefault(u => u.idUsuarioSistema == idUsuario.Value);

            if (usuario == null) return Unauthorized();
            if (usuario.Socio2 != null && usuario.Socio2.Activo == false)
                return Content(System.Net.HttpStatusCode.Forbidden, "El socio se encuentra inactivo.");
            if (usuario.Empleado != null && usuario.Empleado.Activo == false)
                return Content(System.Net.HttpStatusCode.Forbidden, "El empleado se encuentra inactivo.");

            var persona = usuario.Socio2?.Persona;
            return Ok(new
            {
                usuario = usuario.Usuario,
                nombre = persona != null
                    ? (persona.Nombre + " " + persona.Apellido).Trim()
                    : usuario.Usuario,
                email = persona?.Email,
                rol = usuario.Rol?.NombreRol,
                idSocio = usuario.idSocio,
                idEmpleado = usuario.idEmpleado,
                cargo = usuario.Empleado?.Cargo?.NombreCargo
            });
        }

        [HttpPost]
        [Route("recuperar-contrasena")]
        public IHttpActionResult SolicitarRecuperacion([FromBody] RecuperarContrasenaRequest request)
        {
            var email = request?.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("El correo electronico es obligatorio.");

            var usuario = db.UsuarioSistema
                .Include(u => u.Socio2.Persona)
                .FirstOrDefault(u => u.Socio2.Persona.Email.ToLower() == email);

            if (usuario != null)
            {
                var token = CrearTokenSeguro();
                var cacheKey = ClaveRecuperacion(email, token);
                MemoryCache.Default.Set(
                    cacheKey,
                    usuario.idUsuarioSistema,
                    DateTimeOffset.UtcNow.AddMinutes(30));

                var exponerToken = string.Equals(
                    ConfigurationManager.AppSettings["ExposePasswordResetToken"],
                    "true",
                    StringComparison.OrdinalIgnoreCase);

                if (exponerToken)
                    return Ok(new { mensaje = MensajeRecuperacion(), resetToken = token });

                try
                {
                    EnviarCorreoRecuperacion(email, token);
                }
                catch (Exception)
                {
                    MemoryCache.Default.Remove(cacheKey);
                    return InternalServerError();
                }
            }

            return Ok(new { mensaje = MensajeRecuperacion() });
        }

        [HttpPost]
        [Route("restablecer-contrasena")]
        public IHttpActionResult RestablecerContrasena([FromBody] RestablecerContrasenaRequest request)
        {
            var email = request?.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request?.Token))
                return BadRequest("El correo y el token son obligatorios.");

            var error = ValidarPassword(request.PasswordNueva);
            if (error != null) return BadRequest(error);
            if (request.PasswordNueva != request.ConfirmarPassword)
                return BadRequest("La nueva contrasena y la confirmacion no coinciden.");

            var cacheKey = ClaveRecuperacion(email, request.Token.Trim());
            var cached = MemoryCache.Default.Get(cacheKey);
            if (!(cached is int))
                return BadRequest("El token de recuperacion es invalido o vencio.");

            var usuario = db.UsuarioSistema.Find((int)cached);
            if (usuario == null) return BadRequest("El token de recuperacion es invalido o vencio.");

            usuario.Contrasenia = PasswordHelpers.Hashear(request.PasswordNueva);
            db.SaveChanges();
            MemoryCache.Default.Remove(cacheKey);
            return Ok(new { mensaje = "Contrasena restablecida correctamente." });
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }

        private static string ValidarPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return "La nueva contrasena debe tener al menos 8 caracteres.";
            if (!password.Any(char.IsUpper) ||
                !password.Any(char.IsLower) ||
                !password.Any(char.IsDigit) ||
                !password.Any(c => !char.IsLetterOrDigit(c)))
                return "La nueva contrasena debe incluir mayuscula, minuscula, numero y simbolo.";
            return null;
        }

        private static string CrearTokenSeguro()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string ClaveRecuperacion(string email, string token)
        {
            return "password-reset:" + email + ":" + token;
        }

        private static string MensajeRecuperacion()
        {
            return "Si el correo existe, se genero una solicitud de recuperacion valida por 30 minutos.";
        }

        private static void EnviarCorreoRecuperacion(string email, string token)
        {
            var frontendUrl = (ConfigurationManager.AppSettings["FrontendUrl"] ?? "http://localhost:5173")
                .TrimEnd('/');
            var link = frontendUrl +
                "/recuperar-contrasena?email=" + Uri.EscapeDataString(email) +
                "&token=" + Uri.EscapeDataString(token);

            using (var message = new MailMessage())
            {
                message.To.Add(email);
                message.Subject = "Recuperacion de contrasena";
                message.Body =
                    "Se solicito restablecer tu contrasena. El enlace vence en 30 minutos:\r\n\r\n" +
                    link +
                    "\r\n\r\nSi no realizaste esta solicitud, ignora este mensaje.";

                using (var smtp = new SmtpClient())
                    smtp.Send(message);
            }
        }
    }

    public class CambiarContrasenaRequest
    {
        public string PasswordActual { get; set; }
        public string PasswordNueva { get; set; }
        public string ConfirmarPassword { get; set; }
    }

    public class RecuperarContrasenaRequest
    {
        public string Email { get; set; }
    }

    public class RestablecerContrasenaRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string PasswordNueva { get; set; }
        public string ConfirmarPassword { get; set; }
    }
}

