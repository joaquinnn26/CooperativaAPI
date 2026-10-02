using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json;

namespace CooperativaAPI.Controllers
{
    public sealed class EventoOperativoNuevo
    {
        public string Contexto { get; set; }
        public string CodigoEvento { get; set; }
        public string Entidad { get; set; }
        public string idEntidad { get; set; }
        public string Area { get; set; }
        public int? idSocio { get; set; }
        public int? idServicio { get; set; }
        public int? idServicioSocio { get; set; }
        public int? idEmpleado { get; set; }
        public int? idMedidor { get; set; }
        public int? idRutaMedicion { get; set; }
        public string Folio { get; set; }
        public string EstadoAnterior { get; set; }
        public string EstadoNuevo { get; set; }
        public int? idMotivoEvento { get; set; }
        public Guid? OperacionId { get; set; }
        public string ClaveIdempotencia { get; set; }
        public string MotivoNombre { get; set; }
        public bool MotivoRequerido { get; set; }
        public bool MotivoDesconocido { get; set; }
        public string Resultado { get; set; }
        public string Detalle { get; set; }
        public object Datos { get; set; }
        public bool ActorSistema { get; set; }
        public DateTime? FechaUtc { get; set; }
    }

    public static class EventoOperativoStore
    {
        private static SqlParameter P(string name, object value)
        {
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        public static long Registrar(CooperativaEntities db, ClaimsPrincipal actor, EventoOperativoNuevo item)
        {
            return Registrar(db.Database, actor, LaboralAccess.Empleado(db, actor), item);
        }

        public static long Registrar(CooperativaAPI.Data.AppDbContext db, ClaimsPrincipal actor, EventoOperativoNuevo item)
        {
            int employee;
            int? employeeId = int.TryParse(actor?.FindFirst("idEmpleado")?.Value, out employee) ? employee : (int?)null;
            return Registrar(db.Database, actor, employeeId, item);
        }

        private static long Registrar(Database database, ClaimsPrincipal actor, int? employeeId, EventoOperativoNuevo item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Contexto) || string.IsNullOrWhiteSpace(item.CodigoEvento) ||
                string.IsNullOrWhiteSpace(item.Entidad) || string.IsNullOrWhiteSpace(item.idEntidad))
                throw new ArgumentException("El evento operativo requiere contexto, evento y registro de origen.");
            if (item.Contexto.Length > 60 || item.CodigoEvento.Length > 80 || item.Entidad.Length > 60 || item.idEntidad.Length > 80)
                throw new ArgumentException("Los códigos del evento operativo superan el tamaño permitido.");
            if (!string.IsNullOrWhiteSpace(item.ClaveIdempotencia))
            {
                if (item.ClaveIdempotencia.Length > 256) throw new ArgumentException("La clave de reintento es demasiado larga.");
                var existing = database.SqlQuery<long?>("SELECT idEventoOperativo FROM dbo.EventoOperativo WHERE ClaveOrigen=@key", P("@key", "API:" + item.ClaveIdempotencia)).SingleOrDefault();
                if (existing.HasValue) return existing.Value;
            }

            int? motiveId = item.idMotivoEvento;
            string motiveName = null;
            bool motiveRequired = item.MotivoRequerido;
            if (motiveId.HasValue)
            {
                var motive = database.SqlQuery<MotivoEventoRow>(@"SELECT idMotivoEvento,Contexto,Codigo,Nombre,RequiereDetalle,ConfirmadoCooperativa
                    FROM dbo.MotivoEventoOperativo WHERE idMotivoEvento=@id AND Contexto=@contexto AND Activo=1",
                    P("@id", motiveId.Value), P("@contexto", item.Contexto)).SingleOrDefault();
                if (motive == null) throw new ArgumentException("El motivo no existe, está inactivo o pertenece a otro contexto.");
                if (motiveRequired && EsContextoConfirmado(item.Contexto) && !motive.ConfirmadoCooperativa)
                    throw new ArgumentException("SeleccionÃ¡ una opciÃ³n confirmada por la cooperativa para este proceso.");
                motiveName = motive.Nombre;
                if (motive.RequiereDetalle && string.IsNullOrWhiteSpace(item.Detalle))
                    throw new ArgumentException("El motivo seleccionado requiere un detalle complementario.");
            }
            else if (!string.IsNullOrWhiteSpace(item.MotivoNombre))
            {
                var motive = database.SqlQuery<MotivoEventoRow>(@"SELECT TOP 1 idMotivoEvento,Contexto,Codigo,Nombre,RequiereDetalle,ConfirmadoCooperativa
                    FROM dbo.MotivoEventoOperativo WHERE Contexto=@contexto AND NombreNormalizado=UPPER(LTRIM(RTRIM(@nombre))) AND Activo=1",
                    P("@contexto", item.Contexto), P("@nombre", item.MotivoNombre.Trim())).SingleOrDefault();
                if (motive != null)
                {
                    motiveId = motive.idMotivoEvento;
                    motiveName = motive.Nombre;
                    if (motiveRequired && EsContextoConfirmado(item.Contexto) && !motive.ConfirmadoCooperativa)
                        throw new ArgumentException("SeleccionÃ¡ una opciÃ³n confirmada por la cooperativa para este proceso.");
                    if (motive.RequiereDetalle && string.IsNullOrWhiteSpace(item.Detalle))
                        throw new ArgumentException("El motivo seleccionado requiere un detalle complementario.");
                }
            }
            if (motiveRequired && !motiveId.HasValue)
                throw new ArgumentException("Seleccioná un motivo válido para esta decisión.");

            var userId = item.ActorSistema ? (int?)null : (int?)LaboralAccess.Usuario(actor);
            if (!item.ActorSistema && userId.GetValueOrDefault() == 0) throw new UnauthorizedAccessException();
            var responsibleEmployee = item.ActorSistema ? null : employeeId;
            return database.SqlQuery<long>(@"INSERT dbo.EventoOperativo
                (Contexto,CodigoEvento,Entidad,idEntidad,FechaUtc,idUsuarioSistema,ActorSistema,idEmpleado,idEmpleadoActor,Area,idSocio,idServicio,idServicioSocio,idMedidor,idRutaMedicion,Folio,
                 EstadoAnterior,EstadoNuevo,idMotivoEvento,MotivoRegistrado,MotivoDesconocido,OperacionId,Resultado,Detalle,Datos,ClaveOrigen)
                OUTPUT INSERTED.idEventoOperativo
                VALUES(@contexto,@evento,@entidad,@entidadId,COALESCE(@fecha,SYSUTCDATETIME()),@usuario,@sistema,@empleado,@empleadoActor,@area,@socio,@servicio,@conexion,@medidor,@ruta,@folio,
                 @anterior,@nuevo,@motivo,@motivoNombre,@desconocido,@operacion,@resultado,@detalle,@datos,@clave)",
                P("@contexto", item.Contexto.Trim()), P("@evento", item.CodigoEvento.Trim()), P("@entidad", item.Entidad.Trim()), P("@entidadId", item.idEntidad.Trim()),
                P("@fecha", item.FechaUtc), P("@usuario", userId), P("@sistema", item.ActorSistema), P("@empleado", item.idEmpleado ?? responsibleEmployee), P("@empleadoActor", responsibleEmployee), P("@area", item.Area),
                P("@socio", item.idSocio), P("@servicio", item.idServicio), P("@conexion", item.idServicioSocio), P("@medidor", item.idMedidor),
                P("@ruta", item.idRutaMedicion), P("@folio", item.Folio), P("@anterior", item.EstadoAnterior), P("@nuevo", item.EstadoNuevo),
                P("@motivo", motiveId), P("@motivoNombre", motiveName), P("@desconocido", item.MotivoDesconocido || motiveRequired && !motiveId.HasValue), P("@operacion", item.OperacionId),
                P("@resultado", item.Resultado), P("@detalle", item.Detalle), P("@datos", item.Datos == null ? null : JsonConvert.SerializeObject(item.Datos)),
                P("@clave", string.IsNullOrWhiteSpace(item.ClaveIdempotencia) ? null : "API:" + item.ClaveIdempotencia)).Single();
        }

        private sealed class MotivoEventoRow
        {
            public int idMotivoEvento { get; set; }
            public string Contexto { get; set; }
            public string Codigo { get; set; }
            public string Nombre { get; set; }
            public bool RequiereDetalle { get; set; }
            public bool ConfirmadoCooperativa { get; set; }
        }

        private static bool EsContextoConfirmado(string contexto)
        {
            return new[] { "LECTURA_NO_REALIZADA", "MEDIDOR_REEMPLAZO", "INSTALACION_NO_REALIZADA", "SOLICITUD_SERVICIO_RECHAZO" }
                .Contains(contexto ?? "", StringComparer.OrdinalIgnoreCase);
        }
    }

    [RoutePrefix("api/eventos-operativos")]
    [JwtAuthorize(Roles = "Administrador,Empleado")]
    public sealed class EventosOperativosController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        private ClaimsPrincipal Principal { get { return (ClaimsPrincipal)User; } }

        [HttpGet, Route("motivos")]
        public IHttpActionResult Motivos(string contexto, string area = null, bool incluirPendientes = false, bool incluirInactivos = false)
        {
            if (string.IsNullOrWhiteSpace(contexto) || contexto.Length > 60) return BadRequest("Indicá un contexto válido.");
            if (!PuedeConsultar(area)) return StatusCode(HttpStatusCode.Forbidden);
            if (incluirPendientes && !PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            if (incluirInactivos && !PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            var rows = db.Database.SqlQuery<MotivoEventoOperativoDto>(@"SELECT idMotivoEvento,Contexto,Codigo,Nombre,Orden,Activo,ConfirmadoCooperativa,RequiereDetalle,Origen,Version
                FROM dbo.MotivoEventoOperativo WHERE Contexto=@contexto AND (@inactivos=1 OR Activo=1) AND (@pendientes=1 OR ConfirmadoCooperativa=1)
                ORDER BY Orden,Nombre", P("@contexto", contexto.Trim()), P("@pendientes", incluirPendientes), P("@inactivos", incluirInactivos)).ToList();
            return Ok(rows);
        }

        [HttpGet, Route("catalogos")]
        public IHttpActionResult Catalogos()
        {
            if (!PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            return Ok(db.Database.SqlQuery<EventoCatalogoContexto>(@"SELECT Contexto,COUNT(*) Total,SUM(CASE WHEN ConfirmadoCooperativa=1 THEN 1 ELSE 0 END) Confirmados,
                SUM(CASE WHEN ConfirmadoCooperativa=0 THEN 1 ELSE 0 END) PendientesValidacion
                FROM dbo.MotivoEventoOperativo GROUP BY Contexto ORDER BY Contexto").ToList());
        }

        [HttpGet, Route("contextos")]
        public IHttpActionResult Contextos([FromUri] EventoOperativoFiltro f)
        {
            if (f == null) f = new EventoOperativoFiltro();
            var validation = ValidarFiltro(f); if (validation != null) return BadRequest(validation);
            if (!PuedeConsultar(f.Area)) return StatusCode(HttpStatusCode.Forbidden);
            var scope = Scope(f, out var parameters);
            return Ok(db.Database.SqlQuery<string>("SELECT DISTINCT e.Contexto FROM dbo.EventoOperativo e WHERE " + scope + " ORDER BY e.Contexto", parameters.ToArray()).ToList());
        }

        [HttpPost, Route("motivos")]
        public IHttpActionResult CrearMotivo(MotivoEventoOperativoDto item)
        {
            if (!PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            if (item == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(item.Contexto) || string.IsNullOrWhiteSpace(item.Nombre) ||
                item.Contexto.Length > 60 || item.Nombre.Trim().Length > 150 || item.Orden < 0)
                return BadRequest("Completá contexto, motivo y orden válidos.");
            var knownEmptyContexts = new[] { "RECLAMO", "RECLAMO_DERIVACION", "RECLAMO_DEVOLUCION", "RECLAMO_RESOLUCION", "DOCUMENTO_PERSONA", "BAJA_SERVICIO", "BAJA_SOCIO", "EMPLEADO_BAJA", "HOJA_RUTA_CANCELACION", "HOJA_RUTA_REASIGNACION", "LECTURA_CORRECCION" };
            if (!knownEmptyContexts.Contains(item.Contexto.Trim().ToUpperInvariant()) && !db.Database.SqlQuery<int>("SELECT 1 FROM dbo.MotivoEventoOperativo WHERE Contexto=@contexto", P("@contexto", item.Contexto.Trim().ToUpperInvariant())).Any())
                return BadRequest("El contexto debe existir en un catÃ¡logo operativo configurado.");
            var code = "USR_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            try
            {
                var id = db.Database.SqlQuery<int>(@"INSERT dbo.MotivoEventoOperativo(Contexto,Codigo,Nombre,Orden,Activo,ConfirmadoCooperativa,RequiereDetalle,Origen,idUsuarioCreador)
                    OUTPUT INSERTED.idMotivoEvento VALUES(@contexto,@codigo,@nombre,@orden,1,0,@detalle,'Administracion',@usuario)",
                    P("@contexto", item.Contexto.Trim().ToUpperInvariant()), P("@codigo", code), P("@nombre", item.Nombre.Trim()), P("@orden", item.Orden),
                    P("@detalle", item.RequiereDetalle), P("@usuario", LaboralAccess.Usuario(Principal))).Single();
                return Ok(new { idMotivoEvento = id, Codigo = code, ConfirmadoCooperativa = false, mensaje = "Opción creada pendiente de validación de la cooperativa." });
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                return Content(HttpStatusCode.Conflict, "Ya existe una opción con el mismo nombre en este contexto.");
            }
        }

        [HttpPut, Route("motivos/{id:int}")]
        public IHttpActionResult ActualizarMotivo(int id, MotivoEventoOperativoDto item)
        {
            if (!PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            if (item == null || item.Version == null || item.Orden < 0) return BadRequest("Actualizá la lista e indicá su versión.");
            var actual = db.Database.SqlQuery<MotivoEventoOperativoDto>("SELECT * FROM dbo.MotivoEventoOperativo WHERE idMotivoEvento=@id", P("@id", id)).SingleOrDefault();
            if (actual == null) return NotFound();
            if (!actual.Version.SequenceEqual(item.Version)) return Content(HttpStatusCode.Conflict, "La opción cambió; actualizá el catálogo.");
            // El código y el texto quedan inmutables para preservar el significado histórico.
            if (!string.IsNullOrWhiteSpace(item.Codigo) && item.Codigo != actual.Codigo || !string.IsNullOrWhiteSpace(item.Nombre) && item.Nombre != actual.Nombre || item.Contexto != null && item.Contexto != actual.Contexto)
                return BadRequest("El contexto, código y nombre son históricos; desactivá esta opción y creá otra.");
            db.Database.ExecuteSqlCommand(@"UPDATE dbo.MotivoEventoOperativo SET Orden=@orden,Activo=@activo,ConfirmadoCooperativa=@confirmado,
                RequiereDetalle=@detalle WHERE idMotivoEvento=@id", P("@orden", item.Orden), P("@activo", item.Activo), P("@confirmado", item.ConfirmadoCooperativa),
                P("@detalle", item.RequiereDetalle), P("@id", id));
            return Ok(new { idMotivoEvento = id });
        }

        [HttpGet, Route("estadisticas")]
        public IHttpActionResult Estadisticas([FromUri] EventoOperativoFiltro f)
        {
            if (f == null) f = new EventoOperativoFiltro();
            var validation = ValidarFiltro(f); if (validation != null) return BadRequest(validation);
            if (!PuedeConsultar(f.Area)) return StatusCode(HttpStatusCode.Forbidden);
            var scope = Scope(f, out var parameters);
            var denominatorScope = scope.Replace(" AND (@motivo IS NULL OR e.idMotivoEvento=@motivo)", "");
            var sql = @"WITH A AS (SELECT * FROM dbo.EventoOperativo e WHERE " + denominatorScope + @"),
                E AS (SELECT * FROM dbo.EventoOperativo e WHERE " + scope + @"),
                T AS (SELECT Contexto,CodigoEvento,COUNT_BIG(*) Total FROM A GROUP BY Contexto,CodigoEvento)
                SELECT x.Contexto,x.CodigoEvento,COALESCE(x.MotivoRegistrado,m.Nombre,x.Resultado,x.EstadoNuevo,x.EstadoAnterior,'Sin clasificar') Motivo,COUNT_BIG(*) CantidadEventos,
                  COUNT(DISTINCT CONCAT(Entidad,':',idEntidad)) RegistrosDistintos,
                  COUNT(DISTINCT COALESCE(CONVERT(nvarchar(36),OperacionId),CONCAT('EV:',idEventoOperativo))) CantidadOperaciones,
                  SUM(CONVERT(bigint,CASE WHEN x.MotivoDesconocido=1 THEN 1 ELSE 0 END)) MotivosDesconocidos,
                  MAX(T.Total) DenominadorEventos,
                  CAST(COUNT_BIG(*)*100.0/NULLIF(MAX(T.Total),0) AS decimal(6,2)) PorcentajeDelEvento
                FROM E x JOIN T ON T.Contexto=x.Contexto AND T.CodigoEvento=x.CodigoEvento
                LEFT JOIN dbo.MotivoEventoOperativo m ON m.idMotivoEvento=x.idMotivoEvento
                GROUP BY x.Contexto,x.CodigoEvento,COALESCE(x.MotivoRegistrado,m.Nombre,x.Resultado,x.EstadoNuevo,x.EstadoAnterior,'Sin clasificar')
                ORDER BY x.Contexto,x.CodigoEvento,CantidadEventos DESC";
            var data = db.Database.SqlQuery<EventoOperativoEstadistica>(sql, parameters.ToArray()).ToList();
            return Ok(new { datos = data, desdeUtc = f.DesdeUtc, hastaUtc = f.HastaUtc, sinDatos = data.Count == 0, denominador = "Eventos del mismo contexto y tipo dentro del período y filtros seleccionados." });
        }

        [HttpGet, Route("registros")]
        public IHttpActionResult Registros([FromUri] EventoOperativoFiltro f, int pagina = 1, int tamano = 50, string contexto = null, string codigoEvento = null, int? idMotivoEvento = null)
        {
            if (f == null) f = new EventoOperativoFiltro();
            if (pagina < 1 || tamano < 1 || tamano > 200) return BadRequest("Paginación inválida.");
            if (contexto != null) f.Contexto = contexto;
            if (codigoEvento != null) f.CodigoEvento = codigoEvento;
            f.idMotivoEvento = idMotivoEvento;
            var validation = ValidarFiltro(f); if (validation != null) return BadRequest(validation);
            if (!PuedeConsultar(f.Area)) return StatusCode(HttpStatusCode.Forbidden);
            var scope = Scope(f, out var parameters);
            parameters.Add(P("@offset", (pagina - 1) * tamano)); parameters.Add(P("@tamano", tamano));
            var rows = db.Database.SqlQuery<EventoOperativoRegistro>(@"SELECT e.idEventoOperativo,e.Contexto,e.CodigoEvento,e.Entidad,e.idEntidad,e.FechaUtc,e.idUsuarioSistema,
                u.Usuario,e.idEmpleado,e.idEmpleadoActor,e.Area,e.idSocio,e.idServicio,e.idServicioSocio,e.idMedidor,e.idRutaMedicion,e.Folio,e.EstadoAnterior,e.EstadoNuevo,
                e.idMotivoEvento,COALESCE(e.MotivoRegistrado,m.Nombre) Motivo,e.MotivoDesconocido,e.OperacionId,e.Resultado,e.Detalle,e.Datos
                FROM dbo.EventoOperativo e LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=e.idUsuarioSistema WHERE " + scope +
                " ORDER BY e.FechaUtc DESC,e.idEventoOperativo DESC OFFSET @offset ROWS FETCH NEXT @tamano ROWS ONLY", parameters.ToArray()).ToList();
            return Ok(new { items = rows, pagina, tamano });
        }

        private string ValidarFiltro(EventoOperativoFiltro f)
        {
            if (f.DesdeUtc.HasValue && f.HastaUtc.HasValue && f.HastaUtc.Value <= f.DesdeUtc.Value) return "La fecha final debe ser posterior a la inicial.";
            if ((f.Contexto?.Length ?? 0) > 60 || (f.CodigoEvento?.Length ?? 0) > 80 || (f.Area?.Length ?? 0) > 30) return "Uno de los filtros supera el tamaño permitido.";
            if (f.DesdeUtc.HasValue && f.HastaUtc.HasValue && (f.HastaUtc.Value - f.DesdeUtc.Value).TotalDays > 3660) return "El rango máximo permitido es de diez años.";
            return null;
        }

        private string Scope(EventoOperativoFiltro f, out List<SqlParameter> p)
        {
            p = new List<SqlParameter> { P("@desde", f.DesdeUtc), P("@hasta", f.HastaUtc), P("@contexto", f.Contexto), P("@evento", f.CodigoEvento), P("@area", f.Area), P("@empleado", f.idEmpleado), P("@servicio", f.idServicio), P("@conexion", f.idServicioSocio), P("@medidor", f.idMedidor), P("@ruta", f.idRutaMedicion), P("@motivo", f.idMotivoEvento), P("@folio", f.Folio) };
            var where = "(@desde IS NULL OR e.FechaUtc>=@desde) AND (@hasta IS NULL OR e.FechaUtc<@hasta) AND (@contexto IS NULL OR e.Contexto=@contexto) AND (@evento IS NULL OR e.CodigoEvento=@evento) AND (@area IS NULL OR e.Area=@area) AND (@empleado IS NULL OR e.idEmpleado=@empleado OR e.idEmpleadoActor=@empleado) AND (@servicio IS NULL OR e.idServicio=@servicio) AND (@conexion IS NULL OR e.idServicioSocio=@conexion) AND (@medidor IS NULL OR e.idMedidor=@medidor) AND (@ruta IS NULL OR e.idRutaMedicion=@ruta) AND (@motivo IS NULL OR e.idMotivoEvento=@motivo) AND (@folio IS NULL OR e.Folio=@folio)";
            if (!Principal.IsInRole("Administrador") && !LaboralAccess.Gerencia(db, Principal))
            {
                var employee = LaboralAccess.Empleado(db, Principal);
                if (!employee.HasValue) return where + " AND 1=0";
                var areas = LaboralAccess.Perfil(db, Principal).Where(a => a.Encargado).Select(a => a.Area).ToList();
                p.Add(P("@actorEmpleado", employee.Value));
                if (areas.Count == 0) where += " AND (e.idEmpleado=@actorEmpleado OR e.idEmpleadoActor=@actorEmpleado)";
                else { where += " AND (e.Area IN (SELECT value FROM STRING_SPLIT(@areas,',')) OR e.idEmpleado=@actorEmpleado OR e.idEmpleadoActor=@actorEmpleado)"; p.Add(P("@areas", string.Join(",", areas))); }
            }
            return where;
        }

        private bool PuedeConsultar(string area)
        {
            if (Principal.IsInRole("Administrador") || LaboralAccess.Gerencia(db, Principal)) return true;
            var employee = LaboralAccess.Empleado(db, Principal);
            if (!employee.HasValue) return false;
            if (string.IsNullOrWhiteSpace(area)) return true; // Scope() still limits employees to their own events/areas.
            return LaboralAccess.Miembro(db, employee, area) && (LaboralAccess.Encargado(db, employee, area) ||
                LaboralAccess.Perfil(db, Principal).Any(a => a.Area == area && a.Funciones.Contains("ESTADISTICAS")));
        }

        private bool PuedeAdministrar()
        {
            return Principal.IsInRole("Administrador") || LaboralAccess.Gerencia(db, Principal);
        }

        private static SqlParameter P(string name, object value) { return new SqlParameter(name, value ?? DBNull.Value); }
        protected override void Dispose(bool disposing) { if (disposing) db.Dispose(); base.Dispose(disposing); }
    }

    public sealed class MotivoEventoOperativoDto
    {
        public int idMotivoEvento { get; set; }
        public string Contexto { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public bool ConfirmadoCooperativa { get; set; }
        public bool RequiereDetalle { get; set; }
        public string Origen { get; set; }
        public byte[] Version { get; set; }
    }

    public sealed class EventoOperativoFiltro
    {
        public DateTime? DesdeUtc { get; set; }
        public DateTime? HastaUtc { get; set; }
        public string Contexto { get; set; }
        public string CodigoEvento { get; set; }
        public string Area { get; set; }
        public int? idEmpleado { get; set; }
        public int? idServicio { get; set; }
        public int? idServicioSocio { get; set; }
        public int? idMedidor { get; set; }
        public int? idRutaMedicion { get; set; }
        public int? idMotivoEvento { get; set; }
        public string Folio { get; set; }
    }

    public sealed class EventoCatalogoContexto
    {
        public string Contexto { get; set; }
        public int Total { get; set; }
        public int Confirmados { get; set; }
        public int PendientesValidacion { get; set; }
    }

    public sealed class EventoOperativoEstadistica
    {
        public string Contexto { get; set; }
        public string CodigoEvento { get; set; }
        public string Motivo { get; set; }
        public long CantidadEventos { get; set; }
        public int RegistrosDistintos { get; set; }
        public long CantidadOperaciones { get; set; }
        public long MotivosDesconocidos { get; set; }
        public long DenominadorEventos { get; set; }
        public decimal PorcentajeDelEvento { get; set; }
    }

    public sealed class EventoOperativoRegistro
    {
        public long idEventoOperativo { get; set; }
        public string Contexto { get; set; }
        public string CodigoEvento { get; set; }
        public string Entidad { get; set; }
        public string idEntidad { get; set; }
        public DateTime FechaUtc { get; set; }
        public int? idUsuarioSistema { get; set; }
        public string Usuario { get; set; }
        public int? idEmpleado { get; set; }
        public int? idEmpleadoActor { get; set; }
        public string Area { get; set; }
        public int? idSocio { get; set; }
        public int? idServicio { get; set; }
        public int? idServicioSocio { get; set; }
        public int? idMedidor { get; set; }
        public int? idRutaMedicion { get; set; }
        public string Folio { get; set; }
        public string EstadoAnterior { get; set; }
        public string EstadoNuevo { get; set; }
        public int? idMotivoEvento { get; set; }
        public string Motivo { get; set; }
        public bool MotivoDesconocido { get; set; }
        public Guid? OperacionId { get; set; }
        public string Resultado { get; set; }
        public string Detalle { get; set; }
        public string Datos { get; set; }
    }
}
