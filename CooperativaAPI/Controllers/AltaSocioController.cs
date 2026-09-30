using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    public class AltaSocioEstado
    {
        public int idUsuarioSistema { get; set; }
        public bool EsTitular { get; set; }
        public string Estado { get; set; }
        public string Motivo { get; set; }
        public string Observacion { get; set; }
        public int Version { get; set; }
        public DateTime FechaRegistro { get; set; }
    }
    public static class AltaSocioAcceso
    {
        public static AltaSocioEstado Obtener(CooperativaEntities db, int id)
        {
            return db.Database.SqlQuery<AltaSocioEstado>(
                "SELECT idUsuarioSistema,EsTitular,Estado,Motivo,Observacion,Version,FechaRegistro FROM dbo.AltaSocio WHERE idUsuarioSistema=@id",
                new SqlParameter("@id", id)).SingleOrDefault();
        }
        public static bool Pendiente(CooperativaEntities db, UsuarioSistema usuario)
        {
            if (usuario.Rol.NombreRol != "Socio") return false;
            var alta = Obtener(db, usuario.idUsuarioSistema);
            return alta != null && alta.Estado != "Aprobada";
        }
    }
    public class RegistroSocioRequest
    {
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Documento { get; set; }
        public string Email { get; set; }
        public string Telefono { get; set; }
        public string Calle { get; set; }
        public string Numero { get; set; }
        public bool EsTitular { get; set; }
    }
    public class AltaSocioArchivo
    {
        public string Tipo { get; set; }
        public string Nombre { get; set; }
        public string Mime { get; set; }
        public string Base64 { get; set; }
        public int Version { get; set; }
    }
    public class AltaSocioDecision
    {
        public bool? Aprobar { get; set; }
        public string Motivo { get; set; }
        public string Observacion { get; set; }
        public int Version { get; set; }
    }
    public class AltaSocioVersion { public int Version { get; set; } }
    public class AltaSocioDocumentoVista
    {
        public int idDocumento { get; set; }
        public string Tipo { get; set; }
        public string Nombre { get; set; }
        public DateTime Fecha { get; set; }
    }
    public class AltaSocioArchivoDescarga
    {
        public int idUsuarioSistema { get; set; }
        public string Nombre { get; set; }
        public string Mime { get; set; }
        public byte[] Contenido { get; set; }
    }
    public class AltaSocioLista : AltaSocioEstado
    {
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Email { get; set; }
        public string Documento { get; set; }
        public string Telefono { get; set; }
        public string Calle { get; set; }
        public string Numero { get; set; }
    }

    [RoutePrefix("api/alta-socio")]
    [JwtAuthorize(Roles = "Socio,Administrador")]
    public class AltaSocioController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        private int UsuarioId { get { return int.Parse(((ClaimsIdentity)User.Identity).FindFirst("idUsuarioSistema").Value); } }
        private bool PuedeVer(int id) { return User.IsInRole("Administrador") || UsuarioId == id; }
        private IHttpActionResult Conflicto() { return Content(HttpStatusCode.Conflict, new { Message = "El trámite cambió. Actualizá la pantalla antes de continuar." }); }

        [HttpGet, Route("mi-tramite")]
        public IHttpActionResult MiTramite() { return Detalle(UsuarioId); }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult Detalle(int id)
        {
            if (!PuedeVer(id)) return StatusCode(HttpStatusCode.Forbidden);
            var tramite = AltaSocioAcceso.Obtener(db, id);
            var documentos = db.Database.SqlQuery<AltaSocioDocumentoVista>(
                "SELECT idDocumento,Tipo,Nombre,Fecha FROM dbo.AltaSocioDocumento WHERE idUsuarioSistema=@id AND Vigente=1 ORDER BY Tipo",
                new SqlParameter("@id", id)).ToList();
            return Ok(new { tramite, documentos });
        }

        [HttpGet, Route("")]
        public IHttpActionResult Listar()
        {
            if (!User.IsInRole("Administrador")) return StatusCode(HttpStatusCode.Forbidden);
            return Ok(db.Database.SqlQuery<AltaSocioLista>(@"SELECT a.*, p.Nombre,p.Apellido,p.Email,p.Documento,p.Telefono,p.Calle,p.Numero
                FROM dbo.AltaSocio a JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=a.idUsuarioSistema
                JOIN dbo.Socio s ON s.idSocio=u.idSocio JOIN dbo.Persona p ON p.idPersona=s.idPersona
                ORDER BY CASE WHEN a.Estado='EnRevision' THEN 0 ELSE 1 END,a.FechaRegistro DESC").ToList());
        }

        [HttpPost, Route("documentos")]
        public IHttpActionResult Subir(AltaSocioArchivo archivo)
        {
            if (!User.IsInRole("Socio")) return StatusCode(HttpStatusCode.Forbidden);
            if (archivo == null || !new[] { "Domicilio", "DniFrente", "DniDorso" }.Contains(archivo.Tipo)) return BadRequest("Seleccioná un requisito válido.");
            if (string.IsNullOrWhiteSpace(archivo.Nombre) || archivo.Nombre.Length > 180 || archivo.Nombre.Any(char.IsControl)) return BadRequest("Nombre de archivo inválido.");
            if (string.IsNullOrEmpty(archivo.Base64) || archivo.Base64.Length > 2796204) return BadRequest("El archivo debe tener hasta 2 MB.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(archivo.Base64); } catch (FormatException) { return BadRequest("Archivo inválido."); }
            if (bytes.Length == 0 || bytes.Length > 2097152) return BadRequest("El archivo debe tener hasta 2 MB.");
            var pdf = bytes.Take(5).SequenceEqual(new byte[] { 37,80,68,70,45 });
            var png = bytes.Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 });
            var jpg = bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            if (!(archivo.Mime == "application/pdf" && pdf || archivo.Mime == "image/png" && png || archivo.Mime == "image/jpeg" && jpg)) return BadRequest("Se admite PDF, PNG o JPEG. El contenido debe coincidir con el formato.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var changed = db.Database.ExecuteSqlCommand(@"UPDATE dbo.AltaSocio SET Version=Version+1
                    WHERE idUsuarioSistema=@id AND Version=@version AND Estado IN ('PendienteDocumentacion','Rechazada')",
                    new SqlParameter("@id", UsuarioId), new SqlParameter("@version", archivo.Version));
                if (changed != 1) return Conflicto();
                db.Database.ExecuteSqlCommand("UPDATE dbo.AltaSocioDocumento SET Vigente=0 WHERE idUsuarioSistema=@id AND Tipo=@tipo AND Vigente=1",
                    new SqlParameter("@id", UsuarioId), new SqlParameter("@tipo", archivo.Tipo));
                db.Database.ExecuteSqlCommand(@"INSERT dbo.AltaSocioDocumento(idUsuarioSistema,Tipo,Nombre,Mime,Contenido) VALUES(@id,@tipo,@nombre,@mime,@contenido)",
                    new SqlParameter("@id", UsuarioId), new SqlParameter("@tipo", archivo.Tipo), new SqlParameter("@nombre", archivo.Nombre),
                    new SqlParameter("@mime", archivo.Mime), new SqlParameter("@contenido", SqlDbType.VarBinary, -1) { Value = bytes });
                tx.Commit();
            }
            return Ok(new { Message = "Documento guardado. Enviá el trámite cuando estén los tres documentos." });
        }

        [HttpPost, Route("enviar")]
        public IHttpActionResult Enviar(AltaSocioVersion request)
        {
            if (!User.IsInRole("Socio")) return StatusCode(HttpStatusCode.Forbidden);
            if (request == null) return BadRequest("Datos inválidos.");
            var changed = db.Database.ExecuteSqlCommand(@"UPDATE dbo.AltaSocio SET Estado='EnRevision',Version=Version+1
                WHERE idUsuarioSistema=@id AND Version=@version AND Estado IN ('PendienteDocumentacion','Rechazada')
                AND (SELECT COUNT(*) FROM dbo.AltaSocioDocumento WHERE idUsuarioSistema=@id AND Vigente=1)=3",
                new SqlParameter("@id", UsuarioId), new SqlParameter("@version", request.Version));
            if (changed != 1) return Content(HttpStatusCode.Conflict, new { Message = "Cargá los tres documentos y actualizá el trámite antes de enviarlo." });
            return Ok(new { Message = "Documentación enviada a Administración." });
        }

        [HttpPost, Route("{id:int}/decision")]
        public IHttpActionResult Decidir(int id, AltaSocioDecision request)
        {
            if (!User.IsInRole("Administrador")) return StatusCode(HttpStatusCode.Forbidden);
            if (request == null || !request.Aprobar.HasValue) return BadRequest("Indicá la decisión.");
            var motivo = request.Aprobar.Value ? null : (request.Motivo ?? "").Trim();
            var observacion = request.Aprobar.Value ? null : (request.Observacion ?? "").Trim();
            if (!request.Aprobar.Value)
            {
                var motivos = new[] { "Documento ilegible", "Documento vencido", "DNI frente incorrecto", "DNI dorso incorrecto", "Constancia de domicilio incorrecta", "Datos no coinciden con la documentación", "Falta documentación", "Documentación incompleta", "Otro" };
                if (!motivos.Contains(motivo)) return BadRequest("Seleccioná un motivo del rechazo válido.");
                if (motivo == "Otro" && string.IsNullOrWhiteSpace(observacion)) return BadRequest("Explicá el motivo en la observación adicional.");
                if (observacion.Length > 1000) return BadRequest("La observación adicional admite hasta 1000 caracteres.");
            }
            if (string.IsNullOrWhiteSpace(observacion)) observacion = null;
            var estado = request.Aprobar.Value ? "Aprobada" : "Rechazada";
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var changed = db.Database.ExecuteSqlCommand(@"UPDATE dbo.AltaSocio SET Estado=@estado,Motivo=@motivo,Observacion=@observacion,RevisadoPor=@admin,FechaRevision=SYSUTCDATETIME(),Version=Version+1
                    WHERE idUsuarioSistema=@id AND Version=@version AND Estado='EnRevision'
                    AND (SELECT COUNT(*) FROM dbo.AltaSocioDocumento WHERE idUsuarioSistema=@id AND Vigente=1)=3",
                    new SqlParameter("@estado", estado), new SqlParameter("@motivo", (object)motivo ?? DBNull.Value), new SqlParameter("@observacion", (object)observacion ?? DBNull.Value), new SqlParameter("@admin", UsuarioId),
                    new SqlParameter("@id", id), new SqlParameter("@version", request.Version));
                if (changed != 1) return Conflicto();
                db.Database.ExecuteSqlCommand(@"INSERT dbo.AltaSocioRevision(idUsuarioSistema,Estado,Motivo,Observacion,RevisadoPor) VALUES(@id,@estado,@motivo,@observacion,@admin)",
                    new SqlParameter("@id", id), new SqlParameter("@estado", estado), new SqlParameter("@motivo", (object)motivo ?? DBNull.Value), new SqlParameter("@observacion", (object)observacion ?? DBNull.Value), new SqlParameter("@admin", UsuarioId));
                if (request.Aprobar.Value)
                    db.Database.ExecuteSqlCommand(@"UPDATE s SET Activo=1,FechaAlta=GETDATE() FROM dbo.Socio s JOIN dbo.UsuarioSistema u ON u.idSocio=s.idSocio WHERE u.idUsuarioSistema=@id", new SqlParameter("@id", id));
                tx.Commit();
            }
            return Ok(new { Message = request.Aprobar.Value ? "Alta aprobada. Se habilitó el panel de socio." : "Documentación rechazada. El usuario verá el motivo en su trámite." });
        }

        [HttpGet, Route("documentos/{id:int}")]
        public IHttpActionResult Descargar(int id)
        {
            var documento = db.Database.SqlQuery<AltaSocioArchivoDescarga>("SELECT idUsuarioSistema,Nombre,Mime,Contenido FROM dbo.AltaSocioDocumento WHERE idDocumento=@id", new SqlParameter("@id", id)).SingleOrDefault();
            if (documento == null) return NotFound();
            if (!PuedeVer(documento.idUsuarioSistema)) return StatusCode(HttpStatusCode.Forbidden);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(documento.Contenido) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(documento.Mime);
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = documento.Nombre };
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return ResponseMessage(response);
        }
        protected override void Dispose(bool disposing) { if (disposing) db.Dispose(); base.Dispose(disposing); }
    }
}
