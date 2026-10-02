using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web.Http;
using System.Data.Entity;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;
using CooperativaAPI.Helpers;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/socio")]
    public class SocioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetAll()
        {
            try
            {
                IQueryable<Socio> query = db.Socio
                    .Include("Persona")
                    .Include("UsuarioSistema2");

                if (User.IsInRole("Socio"))
                {
                    var idSocioActual = ObtenerClaimEntero("idSocio");
                    if (!idSocioActual.HasValue) return Unauthorized();
                    query = query.Where(s => s.idSocio == idSocioActual.Value);
                }

                var sociosBD = query.ToList();

                var socios = sociosBD.Select(s => new
                {
                    s.idSocio,
                    s.FechaAlta,
                    s.Activo,
                    Persona = new
                    {
                        s.Persona.idPersona,
                        s.Persona.Nombre,
                        s.Persona.Apellido,
                        s.Persona.Documento,
                        s.Persona.Telefono,
                        s.Persona.Email,
                        s.Persona.Calle,
                        s.Persona.Numero
                    },
                    Usuario = s.UsuarioSistemaUnico != null ? s.UsuarioSistemaUnico.Usuario : null,
                    Servicios = db.ServicioSocio
                        .Where(ss => ss.idSocio == s.idSocio)
                        .Select(ss => new {
                            ss.idServicio,
                            Nombre = ss.Servicio.Nombre
                        })
                        .ToList()
                }).ToList();

                return Ok(socios);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al listar socios: " + ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [AllowAnonymous]
        public IHttpActionResult Crear([FromBody] Persona persona)
        {
            if (persona == null)
                return ValidationError("Los datos del socio son obligatorios.");

            var error = ValidarPersona(persona);
            if (error != null) return ValidationError(error);
            if (!persona.EsTitular.HasValue) return ValidationError("Indicá si sos titular del domicilio.");

            using (var trans = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    NormalizarPersona(persona);

                    bool personaExiste = db.Persona.Any(p => p.Documento == persona.Documento);
                    if (personaExiste)
                        return Content(HttpStatusCode.Conflict, "Ya existe una persona registrada con este documento.");

                    if (db.Persona.Any(p => p.Email == persona.Email))
                        return Content(HttpStatusCode.Conflict, "Ya existe una cuenta con ese correo.");

                    db.Persona.Add(persona);
                    db.SaveChanges();

                    var socio = new Socio
                    {
                        idPersona = persona.idPersona,
                        FechaAlta = DateTime.Now,
                        Activo = false
                    };
                    db.Socio.Add(socio);
                    db.SaveChanges();

                    var rolSocio = db.Rol.FirstOrDefault(r => r.NombreRol == "Socio");
                    if (rolSocio == null)
                        return BadRequest("No existe un rol 'Socio'.");

                    string passwordTemporal = Guid.NewGuid().ToString().Substring(0, 8);
                    string passwordHasheada = PasswordHelpers.Hashear(passwordTemporal);

                    var usuario = new UsuarioSistema
                    {
                        Usuario = persona.Documento,
                        Contrasenia = passwordHasheada,
                        idRol = rolSocio.idRol,
                        idSocio = socio.idSocio
                    };

                    db.UsuarioSistema.Add(usuario);
                    db.SaveChanges();

                    db.Database.ExecuteSqlCommand("INSERT dbo.AltaSocio(idSocio,EsTitular,Estado) VALUES(@socio,@titular,'PendienteDocumentacion')",
                        new System.Data.SqlClient.SqlParameter("@socio", socio.idSocio),
                        new System.Data.SqlClient.SqlParameter("@titular", persona.EsTitular.Value));

                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Socio y usuario creados correctamente.",
                        usuario = usuario.Usuario,
                        idSocio = socio.idSocio,
                        passwordTemporal = passwordTemporal
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest("Error al crear socio: " + ex.Message);
                }
            }
        }

        [HttpPut]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult Editar(int id, [FromBody] Persona personaEditada)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != id)
                    return Content(HttpStatusCode.Forbidden, "No puedes editar otro socio.");

                if (personaEditada == null)
                    return ValidationError("Los datos del socio son obligatorios.");

                var error = ValidarPersona(personaEditada);
                if (error != null) return ValidationError(error);

                var socio = db.Socio.Find(id);
                if (socio == null) return NotFound();

                var persona = db.Persona.Find(socio.idPersona);
                if (persona == null) return NotFound();

                NormalizarPersona(personaEditada);

                bool documentoDuplicado = db.Persona.Any(p => p.Documento == personaEditada.Documento && p.idPersona != persona.idPersona);
                if (documentoDuplicado)
                    return Content(HttpStatusCode.Conflict, "Ya existe una persona registrada con este documento.");

                var documentoAnterior = persona.Documento;

                persona.Nombre = personaEditada.Nombre;
                persona.Apellido = personaEditada.Apellido;
                persona.Documento = personaEditada.Documento;
                persona.Telefono = personaEditada.Telefono;
                persona.Email = personaEditada.Email;
                persona.Calle = personaEditada.Calle;
                persona.Numero = personaEditada.Numero;

                var usuario = db.UsuarioSistema.FirstOrDefault(u => u.idSocio == socio.idSocio);
                if (usuario != null && usuario.Usuario == documentoAnterior)
                    usuario.Usuario = personaEditada.Documento;

                db.SaveChanges();
                return Ok("Socio actualizado correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al editar socio: " + ex.Message);
            }
        }

        [HttpDelete]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var socio = db.Socio.Find(id);
                if (socio == null) return NotFound();

                if (socio.Activo == false)
                    return Ok("Socio ya estaba dado de baja.");

                socio.Activo = false;
                db.SaveChanges();

                return Ok("Socio dado de baja correctamente. No se eliminaron historial ni relaciones.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al dar de baja socio: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("estado/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult CambiarEstadoSocio(int id)
        {
            try
            {
                var socio = db.Socio.Find(id);
                if (socio == null) return NotFound();

                socio.Activo = !socio.Activo;
                db.SaveChanges();

                string estado = socio.Activo == true ? "activado" : "dado de baja";
                return Ok($"Socio {estado} correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cambiar estado de socio: " + ex.Message);
            }
        }

        private static IHttpActionResult ValidationError(string message)
        {
            return new System.Web.Http.Results.ResponseMessageResult(
                new System.Net.Http.HttpResponseMessage((HttpStatusCode)422)
                {
                    Content = new System.Net.Http.StringContent(message)
                });
        }

        private static void NormalizarPersona(Persona persona)
        {
            persona.Nombre = Limpiar(persona.Nombre);
            persona.Apellido = Limpiar(persona.Apellido);
            persona.Documento = Limpiar(persona.Documento);
            persona.Telefono = Limpiar(persona.Telefono);
            persona.Email = Limpiar(persona.Email).ToLowerInvariant();
            persona.Calle = Limpiar(persona.Calle);
            persona.Numero = Limpiar(persona.Numero);
        }

        private static string ValidarPersona(Persona persona)
        {
            if (!NombreValido(persona.Nombre)) return "El nombre debe tener entre 2 y 25 letras y solo puede contener letras y espacios.";
            if (!NombreValido(persona.Apellido)) return "El apellido debe tener entre 2 y 25 letras y solo puede contener letras y espacios.";
            if (!Regex.IsMatch(Limpiar(persona.Documento), @"^\d{7,9}$")) return "El documento debe contener entre 7 y 9 numeros.";
            if (Limpiar(persona.Telefono).Length < 7 || Limpiar(persona.Telefono).Length > 20 || !Regex.IsMatch(Limpiar(persona.Telefono), @"^[0-9+() \-]+$")) return "El telefono debe tener entre 7 y 20 caracteres validos.";
            if (Limpiar(persona.Email).Length < 6 || Limpiar(persona.Email).Length > 120 || !Regex.IsMatch(Limpiar(persona.Email), @"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$", RegexOptions.IgnoreCase)) return "El correo electronico no es valido.";
            if (Limpiar(persona.Calle).Length < 2 || Limpiar(persona.Calle).Length > 100) return "La calle debe tener entre 2 y 100 caracteres.";
            if (Limpiar(persona.Numero).Length < 1 || Limpiar(persona.Numero).Length > 7 || !Regex.IsMatch(Limpiar(persona.Numero), @"^\d{1,6}[A-Za-z]?$")) return "El numero de domicilio no es valido.";
            return null;
        }

        private static bool NombreValido(string value)
        {
            var text = Limpiar(value);
            return text.Length >= 2 && text.Length <= 25 && Regex.IsMatch(text, @"^[\p{L}]+(?: [\p{L}]+)*$");
        }

        private static string Limpiar(string value)
        {
            return Regex.Replace(value ?? "", @"\s+", " ").Trim();
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
