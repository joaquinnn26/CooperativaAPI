using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/avisos")]
    [JwtAuthorize(Roles = "Socio,Empleado,Administrador")]
    public class AvisosController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();

        // Only these origins have an access policy. New producers must use these
        // canonical names or add an explicit policy before their notices are visible.
        private const string TareaPermitida = @"(@gerencia=1
            OR t.Area IN (SELECT value FROM STRING_SPLIT(@encargado,','))
            OR (t.idEmpleadoAsignado=@empleado AND (
                (t.Categoria='Ruta' AND t.Area IN (SELECT value FROM STRING_SPLIT(@lecturista,',')))
                OR (t.Categoria<>'Ruta' AND t.Area='ADMINISTRACION' AND @atencion=1)
                OR (t.Categoria<>'Ruta' AND t.Area<>'ADMINISTRACION'
                    AND t.Area IN (SELECT value FROM STRING_SPLIT(@operario,','))))))";

        private static readonly string AccesoSql = @"a.idUsuarioSistema=@usuario AND a.Ambito=@ambito AND (
            (a.TipoOrigen='Turno' AND EXISTS (
                SELECT 1 FROM dbo.Turnos o WHERE o.IdTurno=TRY_CONVERT(int,a.idOrigen)
                AND ((@ambito='Personal' AND o.IdSocio=@socio)
                    OR (@ambito='Laboral' AND @atencion=1))))
            OR (a.TipoOrigen='TareaLaboral' AND @ambito='Laboral' AND EXISTS (
                SELECT 1 FROM dbo.TareaLaboral t WHERE t.idTarea=TRY_CONVERT(int,a.idOrigen)
                AND " + TareaPermitida + @"))
            OR (a.TipoOrigen='SolicitudServicio' AND EXISTS (
                SELECT 1 FROM dbo.SolicitudServicio o WHERE o.idSolicitudServicio=TRY_CONVERT(int,a.idOrigen)
                AND ((@ambito='Personal' AND o.idSocio=@socio)
                    OR (@ambito='Laboral' AND (@administrador=1 OR EXISTS (
                        SELECT 1 FROM dbo.TareaLaboral t
                        WHERE t.OrigenId=o.idSolicitudServicio AND t.Categoria IN ('Solicitud','Instalacion')
                        AND " + TareaPermitida + @"))))))
            OR (a.TipoOrigen='Factura' AND EXISTS (
                SELECT 1 FROM dbo.Factura o WHERE o.idFactura=TRY_CONVERT(int,a.idOrigen) AND o.Activo=1
                AND ((@ambito='Personal' AND o.idSocio=@socio)
                    OR (@ambito='Laboral' AND (@facturacion=1 OR (@internet=1 AND EXISTS (
                        SELECT 1 FROM dbo.AreaServicio s WHERE s.idServicio=o.idServicio AND s.Area='INTERNET')))))))
            OR (a.TipoOrigen='DocumentoPersona' AND EXISTS (
                SELECT 1 FROM dbo.DocumentoPersona o WHERE o.idDocumentoPersona=TRY_CONVERT(int,a.idOrigen)
                AND (o.idPersona=@persona OR (@ambito='Laboral' AND @administrador=1))))
            OR (a.TipoOrigen='AltaSocio' AND EXISTS (
                SELECT 1 FROM dbo.AltaSocio o WHERE o.idSocio=TRY_CONVERT(int,a.idOrigen)
                AND ((@ambito='Personal' AND o.idSocio=@socio) OR (@ambito='Laboral' AND @administrador=1))))
            OR (a.TipoOrigen='Reclamo' AND EXISTS (
                SELECT 1 FROM dbo.Reclamo o WHERE o.idReclamo=TRY_CONVERT(int,a.idOrigen) AND o.Activo=1
                AND ((@ambito='Personal' AND o.idSocio=@socio)
                    OR (@ambito='Laboral' AND (@administrador=1 OR @atencion=1)))))
        )";

        private const string Columnas = @"a.idAviso,a.Ambito,a.TipoAviso,a.Titulo,a.Mensaje,a.Prioridad,
            a.TipoOrigen,a.idOrigen,a.FechaCreacionUtc,a.FechaLecturaUtc,a.FechaArchivoUtc,a.RequiereAccion";

        [HttpGet]
        [Route("")]
        public IHttpActionResult Obtener(int pagina = 1, int tamanoPagina = 20,
            bool incluirArchivados = false, bool? leido = null)
        {
            if (!ModelState.IsValid || pagina < 1 || pagina > 100000 || tamanoPagina < 1 || tamanoPagina > 100)
                return BadRequest("La pagina debe estar entre 1 y 100000 y el tamano de pagina entre 1 y 100.");

            return Ejecutar(acceso =>
            {
                var filtro = AccesoSql
                    + (incluirArchivados ? "" : " AND a.FechaArchivoUtc IS NULL")
                    + (!leido.HasValue ? "" : leido.Value ? " AND a.FechaLecturaUtc IS NOT NULL" : " AND a.FechaLecturaUtc IS NULL");
                var total = Consultar<long>("SELECT COUNT_BIG(*) FROM dbo.Aviso a WHERE " + filtro,
                    Parametros(acceso)).Single();
                var items = Consultar<AvisoRespuesta>("SELECT " + Columnas + " FROM dbo.Aviso a WHERE " + filtro
                    + " ORDER BY a.FechaCreacionUtc DESC,a.idAviso DESC OFFSET @offset ROWS FETCH NEXT @limite ROWS ONLY",
                    Parametros(acceso, new SqlParameter("@offset", (pagina - 1) * tamanoPagina),
                        new SqlParameter("@limite", tamanoPagina)));
                items.ForEach(FechasUtc);
                return Ok(new { items, total, pagina, tamanoPagina, incluirArchivados });
            });
        }

        [HttpGet]
        [Route("resumen")]
        public IHttpActionResult Resumen()
        {
            return Ejecutar(acceso =>
            {
                var noLeidos = Consultar<long>("SELECT COUNT_BIG(*) FROM dbo.Aviso a WHERE " + AccesoSql
                    + " AND a.FechaLecturaUtc IS NULL AND a.FechaArchivoUtc IS NULL", Parametros(acceso)).Single();
                return Ok(new { noLeidos });
            });
        }

        [HttpPut]
        [Route("{id:long}/leido")]
        public IHttpActionResult Leer(long id)
        {
            return Marcar(id, false);
        }

        [HttpPut]
        [Route("{id:long}/archivado")]
        public IHttpActionResult Archivar(long id)
        {
            return Marcar(id, true);
        }

        private IHttpActionResult Marcar(long id, bool archivar)
        {
            if (id <= 0) return BadRequest("El identificador del aviso no es valido.");
            return Ejecutar(acceso =>
            {
                // Lock the authorized row until commit. Repeated requests keep the first timestamp.
                var aviso = Consultar<AvisoRespuesta>("SELECT " + Columnas
                    + " FROM dbo.Aviso a WITH (UPDLOCK,HOLDLOCK) WHERE " + AccesoSql + " AND a.idAviso=@id",
                    Parametros(acceso, new SqlParameter("@id", id))).SingleOrDefault();
                if (aviso == null) return NotFound();

                var columna = archivar ? "FechaArchivoUtc" : "FechaLecturaUtc";
                EjecutarSql("UPDATE a SET " + columna + "=COALESCE(a." + columna + ",SYSUTCDATETIME())"
                    + " FROM dbo.Aviso a WHERE " + AccesoSql + " AND a.idAviso=@id",
                    Parametros(acceso, new SqlParameter("@id", id)));
                aviso = Consultar<AvisoRespuesta>("SELECT " + Columnas + " FROM dbo.Aviso a WHERE "
                    + AccesoSql + " AND a.idAviso=@id", Parametros(acceso, new SqlParameter("@id", id))).Single();
                FechasUtc(aviso);
                return Ok(aviso);
            });
        }

        private IHttpActionResult Ejecutar(Func<Acceso, IHttpActionResult> accion)
        {
            try
            {
                return EnTransaccion(() =>
                {
                    var acceso = ObtenerAcceso();
                    if (acceso == null) return Unauthorized();
                    if (!acceso.Habilitado)
                        return Content(HttpStatusCode.Forbidden, "La cuenta no tiene acceso a Avisos.");
                    return accion(acceso);
                });
            }
            catch (Exception)
            {
                // Do not expose SQL, identifiers, or notice content in an error response.
                return Content(HttpStatusCode.InternalServerError,
                    "No se pudo completar la operacion de avisos. Intenta nuevamente.");
            }
        }

        protected virtual IHttpActionResult EnTransaccion(Func<IHttpActionResult> accion)
        {
            // Permission checks, count/page, and read/update share one consistent transaction.
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var resultado = accion();
                tx.Commit();
                return resultado;
            }
        }

        protected virtual Acceso ObtenerAcceso()
        {
            var principal = User as ClaimsPrincipal;
            if (principal == null || principal.Identity == null || !principal.Identity.IsAuthenticated) return null;
            var usuarioId = LaboralAccess.Usuario(principal);
            if (usuarioId <= 0) return null;
            var usuario = db.UsuarioSistema.Include(u => u.Rol).Include(u => u.Socio2).Include(u => u.Empleado)
                .SingleOrDefault(u => u.idUsuarioSistema == usuarioId);
            if (usuario == null || usuario.Rol == null || !principal.IsInRole(usuario.Rol.NombreRol)) return null;

            var acceso = new Acceso { Usuario = usuarioId };
            if (usuario.Rol.NombreRol == "Socio")
            {
                acceso.Ambito = "Personal";
                acceso.Socio = usuario.idSocio;
                acceso.Persona = usuario.Socio2 == null ? (int?)null : usuario.Socio2.idPersona;
                acceso.Habilitado = usuario.Socio2 != null && usuario.Socio2.Activo == true
                    && !AltaSocioAcceso.Pendiente(db, usuario);
                return acceso;
            }
            if (usuario.Rol.NombreRol != "Empleado" && usuario.Rol.NombreRol != "Administrador") return acceso;
            acceso.Ambito = "Laboral";
            acceso.Empleado = LaboralAccess.Empleado(db, principal);
            acceso.Habilitado = acceso.Empleado.HasValue;
            if (!acceso.Habilitado) return acceso;
            acceso.Persona = usuario.Empleado.idPersona;
            acceso.Administrador = usuario.Rol.NombreRol == "Administrador";
            acceso.Atencion = LaboralAccess.Atencion(db, principal);
            acceso.Gerencia = LaboralAccess.Gerencia(db, principal);
            acceso.Facturacion = LaboralAccess.Tiene(db, acceso.Empleado, "ADMINISTRACION", "FACTURACION")
                || LaboralAccess.Encargado(db, acceso.Empleado, "ADMINISTRACION");
            acceso.Internet = LaboralAccess.Tiene(db, acceso.Empleado, "INTERNET", "FACTURACION");
            var areas = db.Database.SqlQuery<string>("SELECT Codigo FROM dbo.AreaLaboral").ToList();
            acceso.Encargado = string.Join(",", areas.Where(a => LaboralAccess.Encargado(db, acceso.Empleado, a)));
            acceso.Lecturista = string.Join(",", areas.Where(a => LaboralAccess.Tiene(db, acceso.Empleado, a, "LECTURISTA")));
            acceso.Operario = string.Join(",", areas.Where(a => LaboralAccess.Tiene(db, acceso.Empleado, a, "OPERARIO")));
            return acceso;
        }

        protected virtual List<T> Consultar<T>(string sql, params SqlParameter[] parametros)
        {
            return db.Database.SqlQuery<T>(sql, parametros.Cast<object>().ToArray()).ToList();
        }

        protected virtual int EjecutarSql(string sql, params SqlParameter[] parametros)
        {
            return db.Database.ExecuteSqlCommand(sql, parametros.Cast<object>().ToArray());
        }

        private static SqlParameter[] Parametros(Acceso acceso, params SqlParameter[] extras)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@usuario", acceso.Usuario),
                new SqlParameter("@ambito", SqlDbType.NVarChar, 10) { Value = acceso.Ambito },
                new SqlParameter("@socio", SqlDbType.Int) { Value = (object)acceso.Socio ?? DBNull.Value },
                new SqlParameter("@empleado", SqlDbType.Int) { Value = (object)acceso.Empleado ?? DBNull.Value },
                new SqlParameter("@persona", SqlDbType.Int) { Value = (object)acceso.Persona ?? DBNull.Value },
                new SqlParameter("@administrador", acceso.Administrador),
                new SqlParameter("@atencion", acceso.Atencion),
                new SqlParameter("@gerencia", acceso.Gerencia),
                new SqlParameter("@facturacion", acceso.Facturacion),
                new SqlParameter("@internet", acceso.Internet),
                new SqlParameter("@encargado", SqlDbType.NVarChar, -1) { Value = acceso.Encargado },
                new SqlParameter("@lecturista", SqlDbType.NVarChar, -1) { Value = acceso.Lecturista },
                new SqlParameter("@operario", SqlDbType.NVarChar, -1) { Value = acceso.Operario }
            };
            parametros.AddRange(extras);
            return parametros.ToArray();
        }

        private static void FechasUtc(AvisoRespuesta aviso)
        {
            aviso.FechaCreacionUtc = DateTime.SpecifyKind(aviso.FechaCreacionUtc, DateTimeKind.Utc);
            if (aviso.FechaLecturaUtc.HasValue)
                aviso.FechaLecturaUtc = DateTime.SpecifyKind(aviso.FechaLecturaUtc.Value, DateTimeKind.Utc);
            if (aviso.FechaArchivoUtc.HasValue)
                aviso.FechaArchivoUtc = DateTime.SpecifyKind(aviso.FechaArchivoUtc.Value, DateTimeKind.Utc);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        protected class Acceso
        {
            public int Usuario;
            public string Ambito;
            public int? Socio;
            public int? Empleado;
            public int? Persona;
            public bool Habilitado;
            public bool Administrador;
            public bool Atencion;
            public bool Gerencia;
            public bool Facturacion;
            public bool Internet;
            public string Encargado = "";
            public string Lecturista = "";
            public string Operario = "";
        }

        public class AvisoRespuesta
        {
            public long idAviso { get; set; }
            public string Ambito { get; set; }
            public string TipoAviso { get; set; }
            public string Titulo { get; set; }
            public string Mensaje { get; set; }
            public byte Prioridad { get; set; }
            public string TipoOrigen { get; set; }
            public string idOrigen { get; set; }
            public DateTime FechaCreacionUtc { get; set; }
            public DateTime? FechaLecturaUtc { get; set; }
            public DateTime? FechaArchivoUtc { get; set; }
            public bool RequiereAccion { get; set; }
        }
    }
}