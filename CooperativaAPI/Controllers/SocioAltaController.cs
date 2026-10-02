using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers
{
    public static class AltaSocioAcceso
    {
        public static bool Pendiente(CooperativaEntities db, UsuarioSistema usuario)
        {
            if (usuario == null || usuario.Rol == null || usuario.Rol.NombreRol != "Socio" || !usuario.idSocio.HasValue) return false;
            return db.Database.SqlQuery<int>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.AltaSocio WHERE idSocio=@p0 AND Estado<>'Aprobada') THEN 1 ELSE 0 END", usuario.idSocio.Value).Single() == 1;
        }
    }

    [RoutePrefix("api/alta-socio"), JwtAuthorize(Roles="Administrador,Socio,Empleado")]
    public class SocioAltaController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        private ClaimsPrincipal Principal { get { return (ClaimsPrincipal)User; } }
        private int? UsuarioId { get { return Claim("idUsuarioSistema"); } }
        private int? SocioId { get { return Claim("idSocio"); } }
        private int? Claim(string name) { int value; return int.TryParse(Principal.FindFirst(name)?.Value, out value) ? value : (int?)null; }
        private static SqlParameter P(string name, object value) { return new SqlParameter(name, value ?? DBNull.Value); }
        private List<JObject> Rows(string sql, params SqlParameter[] parameters)
        {
            var connection = (SqlConnection)db.Database.Connection;
            if (connection.State != ConnectionState.Open) connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql; command.Parameters.AddRange(parameters);
                var result = new List<JObject>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var row = new JObject();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var value = reader.GetValue(i);
                            row[reader.GetName(i)] = value == DBNull.Value ? JValue.CreateNull() : JToken.FromObject(value);
                        }
                        result.Add(row);
                    }
                return result;
            }
        }
        private JObject One(string sql, params SqlParameter[] parameters) { return Rows(sql, parameters).SingleOrDefault(); }
        private int PersonForUser()
        {
            var row = One(@"SELECT COALESCE(s.idPersona,e.idPersona) idPersona
                FROM dbo.UsuarioSistema u LEFT JOIN dbo.Socio s ON s.idSocio=u.idSocio
                LEFT JOIN dbo.Empleado e ON e.idEmpleado=u.idEmpleado WHERE u.idUsuarioSistema=@u", P("@u", UsuarioId));
            if (row == null || row["idPersona"].Type == JTokenType.Null) throw new UnauthorizedAccessException();
            return (int)row["idPersona"];
        }
        private int TargetPerson(int userId)
        {
            var row = One(@"SELECT s.idPersona FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol
                JOIN dbo.Socio s ON s.idSocio=u.idSocio WHERE u.idUsuarioSistema=@u AND r.NombreRol='Socio'", P("@u", userId));
            if (row == null) throw new ArgumentException("No se encontró la cuenta de socio.");
            return (int)row["idPersona"];
        }
        private JObject Alta(int socio) { return One(@"SELECT a.*,m.Codigo MotivoCodigo,m.Nombre Motivo FROM dbo.AltaSocio a LEFT JOIN dbo.MotivoRechazoAltaSocio m ON m.idMotivo=a.idMotivoRechazo WHERE a.idSocio=@s", P("@s", socio)); }
        private object Tramite(int socio)
        {
            var a = Alta(socio);
            return new { idSocio = socio, EsTitular = a["EsTitular"], Estado = a["Estado"], Motivo = a["Motivo"], MotivoCodigo = a["MotivoCodigo"], Version = a["Version"], FechaEnvioUtc = a["FechaEnvioUtc"], FechaDecisionUtc = a["FechaDecisionUtc"] };
        }
        private object Documentos(int person)
        {
            return Rows(@"SELECT d.idDocumentoPersona idDocumento,d.NombreArchivo Nombre,t.Codigo Tipo,t.Nombre Requisito,d.Estado,d.FechaCargaUtc FechaUtc,d.MotivoRechazo
                FROM dbo.DocumentoPersona d JOIN dbo.TipoDocumentoPersona t ON t.idTipoDocumentoPersona=d.idTipoDocumentoPersona
                WHERE d.idPersona=@p ORDER BY d.FechaCargaUtc DESC,d.idDocumentoPersona DESC", P("@p", person));
        }
        private byte[] Archivo(JObject data, out string name, out string mime)
        {
            name = Path.GetFileName((string)data["Nombre"] ?? ""); mime = ((string)data["Mime"] ?? "").Trim().ToLowerInvariant();
            byte[] bytes; try { bytes = Convert.FromBase64String((string)data["Base64"] ?? ""); } catch { throw new ArgumentException("El archivo no tiene un formato válido."); }
            if (name.Length == 0 || name.Length > 150 || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) throw new ArgumentException("Elegí un archivo válido de hasta 2 MB.");
            bool valid = mime == "application/pdf" && bytes.Take(5).SequenceEqual(Encoding.ASCII.GetBytes("%PDF-")) || mime == "image/png" && bytes.Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) || mime == "image/jpeg" && bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            if (!valid) throw new ArgumentException("Solo se admiten PDF, PNG y JPEG válidos."); return bytes;
        }
        private IHttpActionResult Error(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return StatusCode(HttpStatusCode.Forbidden);
            if (ex is ArgumentException) return BadRequest(ex.Message);
            return InternalServerError();
        }
        private IHttpActionResult Download(byte[] bytes, string name, string mime)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = name };
            response.Headers.Add("X-Content-Type-Options", "nosniff"); response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return ResponseMessage(response);
        }

        [HttpGet, Route("mi-tramite"), JwtAuthorize(Roles="Socio")]
        public IHttpActionResult MiTramite()
        {
            try { if (!SocioId.HasValue) throw new UnauthorizedAccessException(); return Ok(new { tramite = Tramite(SocioId.Value), documentos = Documentos(PersonForUser()) }); }
            catch (Exception ex) { return Error(ex); }
        }
        [HttpPost, Route("documentos"), JwtAuthorize(Roles="Socio")]
        public IHttpActionResult SubirDocumentoAlta(JObject data)
        {
            using (var tx = db.Database.BeginTransaction()) try
            {
                if (!SocioId.HasValue) throw new UnauthorizedAccessException();
                int person = PersonForUser(); var alta = Alta(SocioId.Value);
                if (alta == null || !new[] { "PendienteDocumentacion", "Rechazada" }.Contains((string)alta["Estado"])) throw new ArgumentException("La documentación no se puede modificar en este estado.");
                if ((string)alta["Version"] != (string)data["Version"]) return Content(HttpStatusCode.Conflict, "El trámite cambió. Actualizá la página.");
                string code = (string)data["Tipo"];
                if (!new[] { "Domicilio", "DniFrente", "DniDorso" }.Contains(code)) throw new ArgumentException("Seleccioná uno de los documentos requeridos.");
                var type = One("SELECT * FROM dbo.TipoDocumentoPersona WHERE Codigo=@c AND Activo=1", P("@c", code)); if (type == null) throw new ArgumentException("El tipo de documento no está disponible.");
                string name, mime; var bytes = Archivo(data, out name, out mime);
                db.Database.ExecuteSqlCommand(@"INSERT dbo.DocumentoPersona(idPersona,idTipoDocumentoPersona,NombreArchivo,TipoArchivo,Contenido,Estado,idUsuarioCarga)
                    VALUES(@p,@t,@n,@m,@b,'Pendiente',@u)", P("@p", person), P("@t", (int)type["idTipoDocumentoPersona"]), P("@n", name), P("@m", mime), P("@b", bytes), P("@u", UsuarioId));
                db.Database.ExecuteSqlCommand("UPDATE dbo.AltaSocio SET Estado='PendienteDocumentacion',idMotivoRechazo=NULL,FechaEnvioUtc=NULL WHERE idSocio=@s", P("@s", SocioId.Value));
                tx.Commit(); return Ok(new { Message = "Documento guardado en el legajo compartido de la persona.", idPersona = person });
            }
            catch (Exception ex) { tx.Rollback(); return Error(ex); }
        }
        [HttpPost, Route("enviar"), JwtAuthorize(Roles="Socio")]
        public IHttpActionResult Enviar(JObject data)
        {
            try
            {
                if (!SocioId.HasValue) throw new UnauthorizedAccessException(); var alta = Alta(SocioId.Value);
                if ((string)alta["Version"] != (string)data["Version"]) return Content(HttpStatusCode.Conflict, "El trámite cambió. Actualizá la página.");
                int person = PersonForUser();
                int total = db.Database.SqlQuery<int>(@"SELECT COUNT(DISTINCT t.Codigo) FROM dbo.TipoDocumentoPersona t JOIN dbo.DocumentoPersona d ON d.idTipoDocumentoPersona=t.idTipoDocumentoPersona
                    WHERE d.idPersona=@p AND t.Codigo IN ('Domicilio','DniFrente','DniDorso') AND d.Estado<>'Rechazado' AND d.idDocumentoPersona=(SELECT MAX(x.idDocumentoPersona) FROM dbo.DocumentoPersona x WHERE x.idPersona=@p AND x.idTipoDocumentoPersona=t.idTipoDocumentoPersona)", P("@p", person)).Single();
                if (total != 3) throw new ArgumentException("Cargá la constancia de domicilio y ambos lados del DNI antes de enviar.");
                db.Database.ExecuteSqlCommand("UPDATE dbo.AltaSocio SET Estado='EnRevision',FechaEnvioUtc=SYSUTCDATETIME(),FechaDecisionUtc=NULL,idMotivoRechazo=NULL WHERE idSocio=@s", P("@s", SocioId.Value));
                return Ok(new { Message = "Documentación enviada a Administración para su revisión." });
            }
            catch (Exception ex) { return Error(ex); }
        }
        [HttpGet, Route("") , JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Altas()
        {
            return Ok(Rows(@"SELECT u.idUsuarioSistema,s.idSocio,a.EsTitular,a.Estado,m.Nombre Motivo,m.Codigo MotivoCodigo,a.FechaEnvioUtc,a.FechaDecisionUtc,p.Nombre,p.Apellido,p.Documento,p.Telefono,p.Email,p.Calle,p.Numero,a.Version
                FROM dbo.AltaSocio a JOIN dbo.Socio s ON s.idSocio=a.idSocio JOIN dbo.Persona p ON p.idPersona=s.idPersona
                LEFT JOIN dbo.MotivoRechazoAltaSocio m ON m.idMotivo=a.idMotivoRechazo LEFT JOIN dbo.UsuarioSistema u ON u.idSocio=s.idSocio ORDER BY CASE a.Estado WHEN 'EnRevision' THEN 0 WHEN 'Rechazada' THEN 1 ELSE 2 END,p.Apellido,p.Nombre"));
        }
        [HttpGet, Route("motivos"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Motivos() { return Ok(Rows("SELECT idMotivo,Codigo,Nombre FROM dbo.MotivoRechazoAltaSocio WHERE Activo=1 ORDER BY Nombre")); }
        [HttpGet, Route("{userId:int}"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Detalle(int userId)
        {
            try { int person = TargetPerson(userId); int socio = (int)One("SELECT idSocio FROM dbo.UsuarioSistema WHERE idUsuarioSistema=@u", P("@u", userId))["idSocio"]; return Ok(new { tramite = Tramite(socio), documentos = Documentos(person) }); }
            catch (Exception ex) { return Error(ex); }
        }
        [HttpPost, Route("{userId:int}/decision"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Decidir(int userId, JObject data)
        {
            using (var tx = db.Database.BeginTransaction()) try
            {
                var account = One("SELECT idSocio FROM dbo.UsuarioSistema WHERE idUsuarioSistema=@u", P("@u", userId)); if (account == null || account["idSocio"].Type == JTokenType.Null) throw new ArgumentException("La cuenta no corresponde a un socio.");
                int socio = (int)account["idSocio"]; var alta = Alta(socio);
                if ((string)alta["Version"] != (string)data["Version"]) return Content(HttpStatusCode.Conflict, "El trámite cambió. Actualizá la vista.");
                bool aprobar = (bool?)data["Aprobar"] ?? false; int? reason = null;
                if (aprobar)
                {
                    int person = TargetPerson(userId); int count = db.Database.SqlQuery<int>(@"SELECT COUNT(DISTINCT t.Codigo) FROM dbo.TipoDocumentoPersona t JOIN dbo.DocumentoPersona d ON d.idTipoDocumentoPersona=t.idTipoDocumentoPersona WHERE d.idPersona=@p AND t.Codigo IN ('Domicilio','DniFrente','DniDorso') AND d.Estado<>'Rechazado' AND d.idDocumentoPersona=(SELECT MAX(x.idDocumentoPersona) FROM dbo.DocumentoPersona x WHERE x.idPersona=@p AND x.idTipoDocumentoPersona=t.idTipoDocumentoPersona)", P("@p", person)).Single();
                    if (count != 3) throw new ArgumentException("Faltan documentos requeridos para aprobar el alta.");
                    db.Database.ExecuteSqlCommand("UPDATE dbo.AltaSocio SET Estado='Aprobada',idMotivoRechazo=NULL,FechaDecisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@u WHERE idSocio=@s", P("@u", UsuarioId), P("@s", socio));
                    db.Database.ExecuteSqlCommand(@"UPDATE d SET Estado='Aprobado',FechaRevisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@u FROM dbo.DocumentoPersona d JOIN dbo.TipoDocumentoPersona t ON t.idTipoDocumentoPersona=d.idTipoDocumentoPersona WHERE d.idPersona=(SELECT idPersona FROM dbo.Socio WHERE idSocio=@s) AND t.Codigo IN ('Domicilio','DniFrente','DniDorso') AND d.idDocumentoPersona=(SELECT MAX(x.idDocumentoPersona) FROM dbo.DocumentoPersona x WHERE x.idPersona=d.idPersona AND x.idTipoDocumentoPersona=d.idTipoDocumentoPersona)", P("@u", UsuarioId), P("@s", socio));
                }
                else
                {
                    string code = (string)data["Motivo"]; var selected = One("SELECT idMotivo FROM dbo.MotivoRechazoAltaSocio WHERE Codigo=@c AND Activo=1", P("@c", code)); if (selected == null) throw new ArgumentException("Seleccioná un motivo de rechazo predefinido."); reason = (int)selected["idMotivo"];
                    db.Database.ExecuteSqlCommand("UPDATE dbo.AltaSocio SET Estado='Rechazada',idMotivoRechazo=@m,FechaDecisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@u WHERE idSocio=@s", P("@m", reason), P("@u", UsuarioId), P("@s", socio));
                    db.Database.ExecuteSqlCommand(@"UPDATE d SET Estado='Rechazado',MotivoRechazo=(SELECT Nombre FROM dbo.MotivoRechazoAltaSocio WHERE idMotivo=@m),FechaRevisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@u FROM dbo.DocumentoPersona d WHERE d.idPersona=(SELECT idPersona FROM dbo.Socio WHERE idSocio=@s) AND d.idDocumentoPersona=(SELECT MAX(x.idDocumentoPersona) FROM dbo.DocumentoPersona x WHERE x.idPersona=d.idPersona AND x.idTipoDocumentoPersona=d.idTipoDocumentoPersona)", P("@m", reason), P("@u", UsuarioId), P("@s", socio));
                }
                db.Database.ExecuteSqlCommand(@"INSERT dbo.AltaSocioRevision(idSocio,Estado,idMotivo,idUsuarioRevisor)
                    VALUES(@s,@estado,@motivo,@usuario)", P("@s", socio), P("@estado", aprobar ? "Aprobada" : "Rechazada"), P("@motivo", reason), P("@usuario", UsuarioId));
                tx.Commit(); return Ok(new { Message = aprobar ? "Alta aprobada." : "Documentación rechazada; el motivo aparece en la cuenta del socio." });
            }
            catch (Exception ex) { tx.Rollback(); return Error(ex); }
        }
        [HttpGet, Route("{userId:int}/historial"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Historial(int userId)
        {
            var cuenta = One("SELECT idSocio FROM dbo.UsuarioSistema WHERE idUsuarioSistema=@u", P("@u", userId));
            if (cuenta == null || cuenta["idSocio"].Type == JTokenType.Null) return NotFound();
            return Ok(Rows(@"SELECT r.idRevision,r.Estado,m.Codigo MotivoCodigo,m.Nombre Motivo,
                u.Usuario Revisor,r.FechaUtc FROM dbo.AltaSocioRevision r
                LEFT JOIN dbo.MotivoRechazoAltaSocio m ON m.idMotivo=r.idMotivo
                JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=r.idUsuarioRevisor
                WHERE r.idSocio=@s ORDER BY r.FechaUtc DESC,r.idRevision DESC", P("@s", (int)cuenta["idSocio"])));
        }

        [HttpGet, Route("documentos/{id:int}")]
        public IHttpActionResult DescargarAlta(int id)
        {
            var file = One(@"SELECT d.*,t.Codigo FROM dbo.DocumentoPersona d JOIN dbo.TipoDocumentoPersona t ON t.idTipoDocumentoPersona=d.idTipoDocumentoPersona WHERE d.idDocumentoPersona=@id", P("@id", id));
            if (file == null) return NotFound();
            if (!User.IsInRole("Administrador"))
            {
                try { if ((int)file["idPersona"] != PersonForUser()) return StatusCode(HttpStatusCode.Forbidden); }
                catch { return StatusCode(HttpStatusCode.Forbidden); }
            }
            return Download((byte[])file["Contenido"], (string)file["NombreArchivo"], (string)file["TipoArchivo"]);
        }

        [HttpGet, Route("legajos"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Legajos()
        {
            return Ok(Rows(@"SELECT p.idPersona,p.Nombre,p.Apellido,p.Documento,p.Email,s.idSocio,e.idEmpleado,COUNT(d.idDocumentoPersona) CantidadDocumentos,
                SUM(CASE WHEN d.Estado='Pendiente' THEN 1 ELSE 0 END) Pendientes
                FROM dbo.Persona p LEFT JOIN dbo.Socio s ON s.idPersona=p.idPersona LEFT JOIN dbo.Empleado e ON e.idPersona=p.idPersona
                LEFT JOIN dbo.DocumentoPersona d ON d.idPersona=p.idPersona WHERE s.idSocio IS NOT NULL OR e.idEmpleado IS NOT NULL
                GROUP BY p.idPersona,p.Nombre,p.Apellido,p.Documento,p.Email,s.idSocio,e.idEmpleado ORDER BY p.Apellido,p.Nombre"));
        }
        [HttpGet, Route("legajos/{person:int}"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult LegajoPersona(int person)
        {
            return Ok(new { persona = One("SELECT p.*,s.idSocio,e.idEmpleado FROM dbo.Persona p LEFT JOIN dbo.Socio s ON s.idPersona=p.idPersona LEFT JOIN dbo.Empleado e ON e.idPersona=p.idPersona WHERE p.idPersona=@p", P("@p", person)), documentos = Documentos(person) });
        }
        [HttpGet, Route("legajo/tipos")]
        public IHttpActionResult TiposLegajo() { return Ok(Rows("SELECT Codigo,Nombre,Grupo FROM dbo.TipoDocumentoPersona WHERE Activo=1 ORDER BY Grupo,Nombre")); }
        [HttpGet, Route("legajo/motivos"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult MotivosLegajo() { return Ok(Rows("SELECT Codigo,Nombre FROM dbo.MotivoDocumentoPersona WHERE Activo=1 ORDER BY Nombre")); }
        [HttpGet, Route("legajo/mis-documentos")]
        public IHttpActionResult MisDocumentos()
        {
            try { return Ok(new { persona = One("SELECT idPersona FROM dbo.Persona WHERE idPersona=@p", P("@p", PersonForUser())), documentos = Documentos(PersonForUser()) }); }
            catch (Exception ex) { return Error(ex); }
        }
        [HttpPost, Route("legajo/documentos")]
        public IHttpActionResult SubirLegajo(JObject data)
        {
            try
            {
                int person = PersonForUser(); string code = (string)data["Tipo"];
                var type = One("SELECT * FROM dbo.TipoDocumentoPersona WHERE Codigo=@c AND Activo=1", P("@c", code)); if (type == null) throw new ArgumentException("Elegí un tipo del catálogo.");
                string name, mime; var bytes = Archivo(data, out name, out mime);
                db.Database.ExecuteSqlCommand(@"INSERT dbo.DocumentoPersona(idPersona,idTipoDocumentoPersona,NombreArchivo,TipoArchivo,Contenido,Estado,idUsuarioCarga)
                    VALUES(@p,@t,@n,@m,@b,'Pendiente',@u)", P("@p", person), P("@t", (int)type["idTipoDocumentoPersona"]), P("@n", name), P("@m", mime), P("@b", bytes), P("@u", UsuarioId));
                return Ok(new { Message = "Documento agregado al legajo compartido." });
            }
            catch (Exception ex) { return Error(ex); }
        }
        [HttpPost, Route("legajos/documentos/{id:int}/decision"), JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult RevisarDocumento(int id, JObject data)
        {
            try
            {
                var document = One("SELECT * FROM dbo.DocumentoPersona WHERE idDocumentoPersona=@id", P("@id", id)); if (document == null) throw new ArgumentException("El documento no existe.");
                string status = (bool?)data["Aprobar"] == true ? "Aprobado" : "Rechazado"; string motive = null;
                if (status == "Rechazado")
                {
                    string code = (string)data["Motivo"]; var reason = One("SELECT Nombre FROM dbo.MotivoDocumentoPersona WHERE Codigo=@c AND Activo=1", P("@c", code));
                    if (reason == null) throw new ArgumentException("Seleccioná un motivo predefinido."); motive = (string)reason["Nombre"];
                }
                db.Database.ExecuteSqlCommand("UPDATE dbo.DocumentoPersona SET Estado=@e,MotivoRechazo=@m,FechaRevisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@u WHERE idDocumentoPersona=@id", P("@e", status), P("@m", motive), P("@u", UsuarioId), P("@id", id));
                return Ok(new { Message = status == "Aprobado" ? "Documento aprobado." : "Documento rechazado." });
            }
            catch (Exception ex) { return Error(ex); }
        }
    }
}
