using System;
using System.Linq;
using System.Web.Http;
using System.Data.Entity;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

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
                return BadRequest("Datos inválidos.");

            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
           
                    bool personaExiste = db.Persona.Any(p => p.Documento == persona.Documento);
                    if (personaExiste)
                        return BadRequest("Ya existe una persona registrada con este documento.");

                    db.Persona.Add(persona);
                    db.SaveChanges();

                    var socio = new Socio
                    {
                        idPersona = persona.idPersona,
                        FechaAlta = DateTime.Now,
                        Activo = true
                    };
                    db.Socio.Add(socio);
                    db.SaveChanges();

                    var rolSocio = db.Rol.FirstOrDefault(r => r.NombreRol == "Socio");
                    if (rolSocio == null)
                        return BadRequest("No existe un rol 'Socio'.");

                
                    string passwordTemporal = Guid.NewGuid().ToString().Substring(0, 8);
                    string passwordHasheada = HashPassword(passwordTemporal);

                    var usuario = new UsuarioSistema
                    {
                        Usuario = persona.Documento,
                        Contrasenia = passwordHasheada,
                        idRol = rolSocio.idRol,
                        idSocio = socio.idSocio
                    };

                    db.UsuarioSistema.Add(usuario);
                    db.SaveChanges();

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
                    return Content(System.Net.HttpStatusCode.Forbidden, "No puedes editar otro socio.");

                if (personaEditada == null)
                    return BadRequest("Los datos del socio son obligatorios.");

                var socio = db.Socio.Find(id);
                if (socio == null) return NotFound();

                var persona = db.Persona.Find(socio.idPersona);
                if (persona == null) return NotFound();

                persona.Nombre = personaEditada.Nombre;
                persona.Apellido = personaEditada.Apellido;
                persona.Documento = personaEditada.Documento;
                persona.Telefono = personaEditada.Telefono;
                persona.Email = personaEditada.Email;
                persona.Calle = personaEditada.Calle;
                persona.Numero = personaEditada.Numero;

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
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var socio = db.Socio.Find(id);
                    if (socio == null) return NotFound();

                    var serviciosSocio = db.ServicioSocio.Where(ss => ss.idSocio == socio.idSocio).ToList();
                    if (serviciosSocio.Any())
                    {
                        db.ServicioSocio.RemoveRange(serviciosSocio);
                        db.SaveChanges();
                    }

                    var reclamos = db.Reclamo.Where(r => r.idSocio == socio.idSocio).ToList();
                    if (reclamos.Any())
                    {
                        db.Reclamo.RemoveRange(reclamos);
                        db.SaveChanges();
                    }

                  
                    var usuario = db.UsuarioSistema.FirstOrDefault(u => u.idSocio == socio.idSocio);
                    if (usuario != null)
                    {
                        db.UsuarioSistema.Remove(usuario);
                        db.SaveChanges();
                    }

                    db.Socio.Remove(socio);
                    db.SaveChanges();

                    trans.Commit();
                    return Ok("✅ Socio, usuario y relaciones eliminados correctamente.");
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest("❌ Error al eliminar socio: " + ex.ToString());
                }
            }
        }

    
        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
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

                string estado = (bool)socio.Activo ? "activado" : "dado de baja";
                return Ok($"Socio {estado} correctamente.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cambiar estado de socio: " + ex.Message);
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
