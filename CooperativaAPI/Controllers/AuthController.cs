using System;
using System.Linq;
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
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Datos inválidos.");
            }

            try
            {
                var identificador = request.Email.Trim();
                var buscarPorEmail = identificador.Contains("@");
                var emailNormalizado = identificador.ToLowerInvariant();
                var candidatos = db.UsuarioSistema.Include(u=>u.Rol).Include(u=>u.Socio2.Persona).Include(u=>u.Empleado.Persona).Include(u=>u.Empleado.Cargo)
                    .Where(u => (buscarPorEmail && (u.Socio2.Persona.Email.ToLower()==emailNormalizado || u.Empleado.Persona.Email.ToLower()==emailNormalizado || (u.Rol.NombreRol=="Administrador" && u.Usuario.ToLower()==emailNormalizado)))
                        || (!buscarPorEmail && (u.Socio2.Persona.Documento==identificador || u.Empleado.Persona.Documento==identificador || (u.Rol.NombreRol=="Administrador" && u.Usuario==identificador))));
                if (request.TipoAcceso == "Socio") candidatos = candidatos.Where(u => u.Rol.NombreRol == "Socio");
                else if (request.TipoAcceso == "Empleado") candidatos = candidatos.Where(u => u.Rol.NombreRol == "Empleado" || u.Rol.NombreRol == "Administrador");
                else if (request.TipoAcceso == "Administrador") candidatos = candidatos.Where(u => u.Rol.NombreRol == "Administrador");
                var cuentas=candidatos.ToList();
                if(cuentas.Count>1)return BadRequest("Elegí acceso personal, laboral o administración del sistema.");
                var usuario=cuentas.SingleOrDefault();

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

                if (usuario.Rol.NombreRol == "Empleado" && usuario.idEmpleado.HasValue && usuario.Empleado != null && usuario.Empleado.Activo != true)
                    return BadRequest("El empleado se encuentra inactivo.");
                if (usuario.Rol.NombreRol == "Socio" && (usuario.Socio2 == null || usuario.Socio2.Activo != true) && !AltaSocioAcceso.Pendiente(db, usuario))
                    return BadRequest("El socio se encuentra inactivo.");

                string token = JwtHelper.GenerarToken(usuario);

                var persona = usuario.Rol.NombreRol=="Socio" ? usuario.Socio2?.Persona : usuario.Empleado?.Persona ?? usuario.Socio2?.Persona;
                var cargo = usuario.Empleado?.Cargo?.NombreCargo;

                return Ok(new
                {
                    mensaje = "Login exitoso",
                    token = token,
                    usuario = usuario.Usuario,
                    nombre = persona != null ? (persona.Nombre + " " + persona.Apellido).Trim() : usuario.Usuario,
                    email = persona?.Email,
                    rol = usuario.Rol?.NombreRol,
                    idSocio = usuario.Rol.NombreRol=="Socio" ? usuario.idSocio : null,
                    idEmpleado = usuario.Rol.NombreRol=="Socio" ? null : usuario.idEmpleado,
                    cargo = cargo,
                    altaSocio = usuario.Rol.NombreRol == "Socio" ? ObtenerEstadoAlta(usuario.idSocio) : null,
                    areas = LaboralAccess.Perfil(db,(ClaimsPrincipal)new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("idUsuarioSistema",usuario.idUsuarioSistema.ToString())})))
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
                    .Include(u => u.Empleado.Persona)
                .FirstOrDefault(u => u.idUsuarioSistema == idUsuario.Value);

            if (usuario == null) return Unauthorized();
            if (usuario.Rol.NombreRol == "Socio" && (usuario.Socio2 == null || usuario.Socio2.Activo != true) && !AltaSocioAcceso.Pendiente(db, usuario))
                return Content(System.Net.HttpStatusCode.Forbidden, "El socio se encuentra inactivo.");
            if (usuario.Rol.NombreRol != "Socio" && usuario.Empleado != null && usuario.Empleado.Activo != true)
                return Content(System.Net.HttpStatusCode.Forbidden, "El empleado se encuentra inactivo.");

            var persona = usuario.Rol.NombreRol=="Socio" ? usuario.Socio2?.Persona : usuario.Empleado?.Persona ?? usuario.Socio2?.Persona;
            return Ok(new
            {
                usuario = usuario.Usuario,
                nombre = persona != null
                    ? (persona.Nombre + " " + persona.Apellido).Trim()
                    : usuario.Usuario,
                email = persona?.Email,
                rol = usuario.Rol?.NombreRol,
                idSocio = usuario.Rol.NombreRol=="Socio" ? usuario.idSocio : null,
                idEmpleado = usuario.Rol.NombreRol=="Socio" ? null : usuario.idEmpleado,
                cargo = usuario.Empleado?.Cargo?.NombreCargo,
                altaSocio = usuario.Rol.NombreRol == "Socio" ? ObtenerEstadoAlta(usuario.idSocio) : null,
                areas = LaboralAccess.Perfil(db,(ClaimsPrincipal)User)
            });
        }

        [HttpPost]
        [Route("recuperar-contrasena")]
        public IHttpActionResult SolicitarRecuperacion([FromBody] RecuperarContrasenaRequest request)
        {
            var email = request?.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("El correo electronico es obligatorio.");

            if (request.TipoAcceso != "Socio" && request.TipoAcceso != "Empleado")
                return BadRequest("El tipo de acceso debe ser Socio o Empleado.");

            RecoverySmtpSettings smtpSettings;
            if (!RecoverySmtpSettings.TryLoad(out smtpSettings))
            {
                System.Diagnostics.Trace.TraceError("COSPRAL_RECOVERY_SMTP_UNAVAILABLE");
                return Content(System.Net.HttpStatusCode.ServiceUnavailable, new { Message = "La recuperacion por correo no esta disponible temporalmente. Intenta mas tarde o contacta a la cooperativa." });
            }

            string cacheKey = null;
            try
            {
                var usuario = db.UsuarioSistema
                    .Include(u => u.Socio2.Persona)
                    .Where(u => (u.Socio2.Persona.Email.ToLower()==email || u.Empleado.Persona.Email.ToLower()==email || (u.Rol.NombreRol=="Administrador" && u.Usuario.ToLower()==email))
                        && ((request.TipoAcceso == "Socio" && u.Rol.NombreRol == "Socio")
                            || (request.TipoAcceso == "Empleado" && (u.Rol.NombreRol == "Empleado" || u.Rol.NombreRol == "Administrador")))).SingleOrDefault();
                if (usuario != null)
                {
                    var token = CrearTokenSeguro();
                    cacheKey = ClaveRecuperacion(email, token);
                    MemoryCache.Default.Set(cacheKey, usuario.idUsuarioSistema, DateTimeOffset.UtcNow.AddMinutes(30));
                    new RecoveryMailService(smtpSettings).SendRecovery(email, token);
                }
            }
            catch (Exception)
            {
                if (cacheKey != null) MemoryCache.Default.Remove(cacheKey);
                // No revelar la existencia de cuentas ni registrar datos del correo.
                System.Diagnostics.Trace.TraceError("COSPRAL_RECOVERY_REQUEST_FAILED");
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

        private object ObtenerEstadoAlta(int? idSocio)
        {
            if (!idSocio.HasValue) return null;
            return db.Database.SqlQuery<EstadoAltaSesion>(@"SELECT a.Estado,m.Nombre Motivo,m.Codigo MotivoCodigo,a.Version
                FROM dbo.AltaSocio a LEFT JOIN dbo.MotivoRechazoAltaSocio m ON m.idMotivo=a.idMotivoRechazo WHERE a.idSocio=@p0", idSocio.Value).FirstOrDefault();
        }

        private sealed class EstadoAltaSesion
        {
            public string Estado { get; set; }
            public string Motivo { get; set; }
            public string MotivoCodigo { get; set; }
            public byte[] Version { get; set; }
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
            return "Si el correo corresponde a una cuenta, recibiras instrucciones para recuperar el acceso. El enlace vence en 30 minutos.";
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
        public string TipoAcceso { get; set; } = "Socio";
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

