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
                (Contexto,CodigoEvento,Entidad,idEntidad,FechaEventoUtc,idUsuarioSistema,ActorSistema,idEmpleado,idEmpleadoActor,Area,idSocio,idServicio,idServicioSocio,idMedidor,idRutaMedicion,Folio,
                 EstadoAnterior,EstadoNuevo,idMotivoEvento,MotivoRegistrado,MotivoDesconocido,OperacionId,Resultado,Detalle,Datos,ClaveOrigen)
                OUTPUT INSERTED.idEventoOperativo
                VALUES(@contexto,@evento,@entidad,@entidadId,SYSUTCDATETIME(),@usuario,@sistema,@empleado,@empleadoActor,@area,@socio,@servicio,@conexion,@medidor,@ruta,@folio,
                 @anterior,@nuevo,@motivo,@motivoNombre,@desconocido,@operacion,@resultado,@detalle,@datos,@clave)",
                P("@contexto", item.Contexto.Trim()), P("@evento", item.CodigoEvento.Trim()), P("@entidad", item.Entidad.Trim()), P("@entidadId", item.idEntidad.Trim()),
                P("@usuario", userId), P("@sistema", item.ActorSistema), P("@empleado", item.idEmpleado ?? responsibleEmployee), P("@empleadoActor", responsibleEmployee), P("@area", item.Area),
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
            if (!PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            if (!PuedeAdministrar()) return StatusCode(HttpStatusCode.Forbidden);
            if (f == null) f = new EventoOperativoFiltro();
            var validation = ValidarFiltro(f); if (validation != null) return BadRequest(validation);
            if (!PuedeConsultar(f.Area)) return StatusCode(HttpStatusCode.Forbidden);
            var scope = Scope(f, out var parameters);
            var denominatorScope = scope.Replace(" AND (@motivo IS NULL OR e.idMotivoEvento=@motivo)", "");
            var sql = @"WITH A AS (SELECT * FROM dbo.EventoOperativo e WHERE " + denominatorScope + @"),
                E AS (SELECT * FROM dbo.EventoOperativo e WHERE " + scope + @"),
                T AS (SELECT Contexto,CodigoEvento,COUNT_BIG(*) Total FROM A GROUP BY Contexto,CodigoEvento)
                SELECT x.Contexto,COALESCE(x.Area,'GENERAL') Area,x.CodigoEvento,COALESCE(x.MotivoRegistrado,m.Nombre,x.Resultado,x.EstadoNuevo,x.EstadoAnterior,'Sin clasificar') Motivo,COUNT_BIG(*) CantidadEventos,
                  COUNT(DISTINCT CONCAT(Entidad,':',idEntidad)) RegistrosDistintos,
                  COUNT_BIG(DISTINCT COALESCE(CONVERT(nvarchar(36),OperacionId),CONCAT('EV:',idEventoOperativo))) CantidadOperaciones,
                  SUM(CONVERT(bigint,CASE WHEN x.MotivoDesconocido=1 THEN 1 ELSE 0 END)) MotivosDesconocidos,
                  MAX(T.Total) DenominadorEventos,
                  CAST(COUNT_BIG(*)*100.0/NULLIF(MAX(T.Total),0) AS decimal(6,2)) PorcentajeDelEvento
                FROM E x JOIN T ON T.Contexto=x.Contexto AND T.CodigoEvento=x.CodigoEvento
                LEFT JOIN dbo.MotivoEventoOperativo m ON m.idMotivoEvento=x.idMotivoEvento
                GROUP BY x.Contexto,COALESCE(x.Area,'GENERAL'),x.CodigoEvento,COALESCE(x.MotivoRegistrado,m.Nombre,x.Resultado,x.EstadoNuevo,x.EstadoAnterior,'Sin clasificar')
                ORDER BY x.Contexto,x.CodigoEvento,CantidadEventos DESC";
            var data = db.Database.SqlQuery<EventoOperativoEstadistica>(sql, parameters.ToArray()).ToList();
            return Ok(new { datos = data, desdeUtc = f.DesdeUtc, hastaUtc = f.HastaUtc, sinDatos = data.Count == 0, denominador = "Eventos del mismo contexto y tipo dentro del período y filtros seleccionados." });
        }

        [HttpGet, Route("dashboard")]
        public IHttpActionResult Dashboard(string area = null)
        {
            bool gerente = PuedeAdministrar();
            var profile = LaboralAccess.Perfil(db, Principal);
            var areasPermitidas = profile.Where(a => a.Encargado || a.Funciones.Contains("ESTADISTICAS")).Select(a => a.Area).Distinct().ToList();
            if (!gerente && areasPermitidas.Count == 0) return StatusCode(HttpStatusCode.Forbidden);
            if (!gerente && !string.IsNullOrWhiteSpace(area)) return StatusCode(HttpStatusCode.Forbidden);
            if (!string.IsNullOrWhiteSpace(area) && !db.Database.SqlQuery<int>("SELECT 1 FROM dbo.AreaLaboral WHERE Codigo=@a", P("@a", area)).Any()) return BadRequest("El área indicada no existe.");

            var tz = TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var today = localNow.Date; var tomorrow = today.AddDays(1);
            var month = new DateTime(localNow.Year, localNow.Month, 1); var trendStartLocal = month.AddMonths(-11);
            Func<DateTime, DateTime> utc = d => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), tz);
            var filter = new EventoOperativoFiltro { Area = gerente ? (string.IsNullOrWhiteSpace(area) ? null : area) : null };
            Func<string, bool> incluyeArea = target => gerente ? (filter.Area == null || filter.Area == target) : areasPermitidas.Contains(target);
            var scope = Scope(filter, out var parameters);
            if (!gerente) { parameters.Add(P("@dashboardAreas", string.Join(",", areasPermitidas))); scope += " AND e.Area IN (SELECT value FROM STRING_SPLIT(@dashboardAreas,','))"; }
            Func<DateTime, DateTime, SqlParameter[]> range = (from, to) => parameters.Select(x => new SqlParameter(x.ParameterName, x.Value)).Concat(new[] { P("@from", from), P("@to", to) }).ToArray();
            Func<IEnumerable<SqlParameter>, SqlParameter[]> cloneParams = source => source.Select(x => new SqlParameter(x.ParameterName, x.Value)).ToArray();

            var daily = db.Database.SqlQuery<DashboardEvent>(@"SELECT e.Area,e.Contexto,e.CodigoEvento,COUNT_BIG(*) Cantidad FROM dbo.EventoOperativo e WHERE " + scope + " AND e.FechaEventoUtc>=@from AND e.FechaEventoUtc<@to GROUP BY e.Area,e.Contexto,e.CodigoEvento", range(utc(today), utc(tomorrow))).ToList();
            var motiveScope = scope.Replace("e.", "z.");
            var motiveSql = @"SELECT e.Area,e.Contexto,e.CodigoEvento,e.idMotivoEvento,CAST(NULL AS int) idMotivoDocumento,CASE WHEN e.MotivoDesconocido=1 THEN N'Motivo histórico sin equivalencia confirmada' ELSE COALESCE(e.MotivoRegistrado,m.Nombre,N'Sin motivo registrado') END Motivo,e.MotivoDesconocido,COUNT_BIG(*) Cantidad,
                (SELECT COUNT_BIG(*) FROM dbo.EventoOperativo z WHERE z.Area=e.Area AND z.Contexto=e.Contexto AND z.CodigoEvento=e.CodigoEvento AND z.FechaEventoUtc>=@from AND z.FechaEventoUtc<@to AND " + motiveScope + @") Denominador
                FROM dbo.EventoOperativo e LEFT JOIN dbo.MotivoEventoOperativo m ON m.idMotivoEvento=e.idMotivoEvento WHERE " + scope + " AND e.Entidad<>'TareaLaboral' AND e.FechaEventoUtc>=@from AND e.FechaEventoUtc<@to AND (e.idMotivoEvento IS NOT NULL OR e.MotivoRegistrado IS NOT NULL OR e.MotivoDesconocido=1) GROUP BY e.Area,e.Contexto,e.CodigoEvento,e.idMotivoEvento,e.MotivoDesconocido,e.MotivoRegistrado,m.Nombre";
            var motives = db.Database.SqlQuery<DashboardMotive>(motiveSql, range(utc(month), utc(tomorrow))).ToList();
            var laborMotiveAreaScope = gerente ? "(@area IS NULL OR a.Area=@area)" : "a.Area IN (SELECT value FROM STRING_SPLIT(@team,','))";
            var laborMotiveParameters = new[] { P("@area", gerente ? (object)filter.Area : null), P("@team", string.Join(",", areasPermitidas)), P("@from", utc(month)), P("@to", utc(tomorrow)) };
            motives.AddRange(db.Database.SqlQuery<DashboardMotive>(@"SELECT a.Area,CONCAT('LABORAL_',a.Area,'_',UPPER(a.Accion)) Contexto,a.Accion CodigoEvento,CAST(NULL AS int) idMotivoEvento,CAST(NULL AS int) idMotivoDocumento,m.idMotivo idMotivoLaboral,m.Nombre Motivo,CONVERT(bit,0) MotivoDesconocido,COUNT_BIG(*) Cantidad,
                (SELECT COUNT_BIG(*) FROM dbo.AuditoriaLaboral z WHERE z.Area=a.Area AND z.Accion=a.Accion AND z.FechaUtc>=@from AND z.FechaUtc<@to) Denominador
                FROM dbo.AuditoriaLaboral a JOIN dbo.MotivoLaboral m ON m.idMotivo=a.idMotivoLaboral AND m.Area=a.Area AND m.Accion=a.Accion
                WHERE a.FechaUtc>=@from AND a.FechaUtc<@to AND " + laborMotiveAreaScope + " GROUP BY a.Area,a.Accion,m.idMotivo,m.Nombre", laborMotiveParameters).ToList());
            if (incluyeArea("ADMINISTRACION"))
                motives.AddRange(db.Database.SqlQuery<DashboardMotive>(@"SELECT 'ADMINISTRACION' Area,'DOCUMENTO_PERSONA_RECHAZO' Contexto,'Rechazado' CodigoEvento,CAST(NULL AS int) idMotivoEvento,h.idMotivoDocumento,CAST(NULL AS int) idMotivoLaboral,m.Nombre Motivo,CONVERT(bit,0) MotivoDesconocido,COUNT_BIG(*) Cantidad,(SELECT COUNT_BIG(*) FROM dbo.HistorialDocumentoPersona d WHERE d.FechaUtc>=@from AND d.FechaUtc<@to AND d.EstadoNuevo='Rechazado' AND d.idMotivoDocumento IS NOT NULL) Denominador FROM dbo.HistorialDocumentoPersona h JOIN dbo.MotivoDocumentoPersona m ON m.idMotivo=h.idMotivoDocumento WHERE h.FechaUtc>=@from AND h.FechaUtc<@to AND h.EstadoNuevo='Rechazado' AND h.idMotivoDocumento IS NOT NULL GROUP BY h.idMotivoDocumento,m.Nombre", range(utc(month), utc(tomorrow))));
            if (incluyeArea("INTERNET"))
                motives.AddRange(db.Database.SqlQuery<DashboardMotive>(@"SELECT 'INTERNET' Area,'INTERNET_FACTURACION' Contexto,'RECHAZADA' CodigoEvento,CAST(NULL AS int) idMotivoEvento,CAST(NULL AS int) idMotivoDocumento,CAST(NULL AS int) idMotivoLaboral,d.MotivoRechazo Motivo,CONVERT(bit,0) MotivoDesconocido,COUNT_BIG(*) Cantidad,(SELECT COUNT_BIG(*) FROM dbo.LoteFacturacionInternet l2 JOIN dbo.LoteFacturacionInternetDetalle d2 ON d2.idLote=l2.idLote WHERE l2.FechaCreacionUtc>=@from AND l2.FechaCreacionUtc<@to AND l2.Estado<>'Anulado' AND d2.Resultado='RECHAZADA') Denominador FROM dbo.LoteFacturacionInternet l JOIN dbo.LoteFacturacionInternetDetalle d ON d.idLote=l.idLote WHERE l.FechaCreacionUtc>=@from AND l.FechaCreacionUtc<@to AND l.Estado<>'Anulado' AND d.Resultado='RECHAZADA' AND d.MotivoRechazo IS NOT NULL GROUP BY d.MotivoRechazo", range(utc(month), utc(tomorrow))));
            var trends = db.Database.SqlQuery<DashboardTrend>(@"SELECT DATEADD(MONTH,DATEDIFF(MONTH,0,e.FechaEventoUtc),0) Periodo,e.Area,e.Contexto,COUNT_BIG(*) Cantidad FROM dbo.EventoOperativo e WHERE " + scope + " AND e.FechaEventoUtc>=@from AND e.FechaEventoUtc<@to GROUP BY DATEADD(MONTH,DATEDIFF(MONTH,0,e.FechaEventoUtc),0),e.Area,e.Contexto", range(utc(trendStartLocal), utc(tomorrow))).ToList();
            var coverage = db.Database.SqlQuery<DateTime?>("SELECT MIN(e.FechaEventoUtc) FROM dbo.EventoOperativo e WHERE " + scope, cloneParams(parameters)).SingleOrDefault();
            var compareStart = month.AddMonths(-1); var compareDay = Math.Min(localNow.Day, DateTime.DaysInMonth(compareStart.Year, compareStart.Month)); var compareEnd = compareStart.AddDays(compareDay);
            var monthComparison = db.Database.SqlQuery<DashboardPeriodCount>(@"SELECT 'Mes actual' Periodo,COUNT_BIG(*) Cantidad FROM dbo.EventoOperativo e WHERE " + scope + " AND e.FechaEventoUtc>=@from AND e.FechaEventoUtc<@to UNION ALL SELECT 'Mismo tramo mes anterior',COUNT_BIG(*) FROM dbo.EventoOperativo e WHERE " + scope + " AND e.FechaEventoUtc>=@compareFrom AND e.FechaEventoUtc<@compareTo", cloneParams(parameters).Concat(new[] { P("@from",utc(month)),P("@to",utc(tomorrow)),P("@compareFrom",utc(compareStart)),P("@compareTo",utc(compareEnd)) }).ToArray()).ToList();

            var weekly = db.Database.SqlQuery<DashboardFlowWeek>(@"WITH X AS(SELECT e.*,ROW_NUMBER() OVER(PARTITION BY e.Entidad,e.idEntidad ORDER BY e.FechaEventoUtc,e.idEventoOperativo) rn FROM dbo.EventoOperativo e WHERE " + scope + @" AND e.FechaEventoUtc>=@from AND e.FechaEventoUtc<@to AND (e.Entidad='TareaLaboral' OR e.Entidad='SolicitudServicio')), F AS(
                SELECT DATEADD(WEEK,DATEDIFF(WEEK,0,FechaEventoUtc),0) Semana,Area,'Tareas' Proceso,'Recibidos' Tipo FROM X WHERE Entidad='TareaLaboral' AND CodigoEvento='CrearTarea'
                UNION ALL SELECT DATEADD(WEEK,DATEDIFF(WEEK,0,FechaEventoUtc),0),Area,'Solicitudes','Recibidos' FROM X WHERE Entidad='SolicitudServicio' AND CodigoEvento='SolicitudCreada'
                UNION ALL SELECT DATEADD(WEEK,DATEDIFF(WEEK,0,FechaEventoUtc),0),Area,CASE WHEN Entidad='TareaLaboral' THEN 'Tareas' ELSE 'Solicitudes' END,'Resueltos' FROM X WHERE EstadoNuevo IN ('Cerrada','Rechazada','Resuelta','Finalizada','Cancelada') AND (EstadoAnterior IS NULL OR EstadoAnterior NOT IN ('Cerrada','Rechazada','Resuelta','Finalizada','Cancelada')))
                SELECT Semana,Area,Proceso,Tipo,COUNT_BIG(*) Cantidad FROM F GROUP BY Semana,Area,Proceso,Tipo ORDER BY Semana,Area,Proceso", range(utc(trendStartLocal),utc(tomorrow))).ToList();
            var ageing = db.Database.SqlQuery<DashboardAgeing>(@"SELECT t.Area,t.Categoria Proceso,CASE WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<2 THEN '0–1 días' WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<8 THEN '2–7 días' WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<31 THEN '8–30 días' ELSE 'Más de 30 días' END Rango,COUNT_BIG(*) Cantidad FROM dbo.TareaLaboral t WHERE t.Estado NOT IN ('Cerrada','Rechazada','Cancelada','Anulada') AND (@area IS NULL OR t.Area=@area) AND (@manager=1 OR (t.Area IN (SELECT value FROM STRING_SPLIT(@team,',')) AND (t.idEmpleadoAsignado=@employee OR @isLeader=1))) GROUP BY t.Area,t.Categoria,CASE WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<2 THEN '0–1 días' WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<8 THEN '2–7 días' WHEN DATEDIFF(DAY,t.FechaCreacionUtc,SYSUTCDATETIME())<31 THEN '8–30 días' ELSE 'Más de 30 días' END", P("@area",gerente?(object)filter.Area:null),P("@manager",gerente),P("@team",string.Join(",",areasPermitidas)),P("@employee",LaboralAccess.Empleado(db,Principal)),P("@isLeader",profile.Any(x=>x.Encargado))).ToList();
            var repeatMeters = new List<DashboardRepeat>();
            var incidentMotives = new List<DashboardIncidentMotive>();
            var motiveResolution = new List<DashboardMotiveResolution>();
            bool incidenceSchema = db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('dbo.IncidenciaMedidor','U') IS NULL THEN 0 ELSE 1 END").Single() == 1;
            if (incidenceSchema && (incluyeArea("AGUA") || incluyeArea("ENERGIA")))
            {
                var incidentAreaScope = gerente ? "(@area IS NULL OR x.Area=@area)" : "x.Area IN (SELECT value FROM STRING_SPLIT(@team,','))";
                var incidentParameters = new[] { P("@area", gerente ? (object)filter.Area : null), P("@team", string.Join(",", areasPermitidas)), P("@from", utc(localNow.AddMonths(-12).Date)), P("@to", utc(tomorrow)) };
                const string areaExpression = "CASE WHEN tm.Nombre='Agua' THEN 'AGUA' ELSE 'ENERGIA' END";
                repeatMeters = db.Database.SqlQuery<DashboardRepeat>(@"SELECT " + areaExpression + @" Area,COUNT_BIG(*) Cantidad FROM dbo.IncidenciaMedidor i JOIN dbo.Medidor md ON md.idMedidor=i.idMedidor JOIN dbo.TipoMedidor tm ON tm.idTipoMedidor=md.idTipoMedidor CROSS APPLY(SELECT " + areaExpression + @" Area) x WHERE i.FechaAperturaUtc>=@from AND i.FechaAperturaUtc<@to AND EXISTS(SELECT 1 FROM dbo.IncidenciaMedidor p WHERE p.idMedidor=i.idMedidor AND p.idIncidencia<>i.idIncidencia AND p.FechaAperturaUtc<i.FechaAperturaUtc AND p.FechaAperturaUtc>=DATEADD(MONTH,-12,i.FechaAperturaUtc)) AND " + incidentAreaScope + " GROUP BY " + areaExpression, cloneParams(incidentParameters)).ToList();
                incidentMotives = db.Database.SqlQuery<DashboardIncidentMotive>(@"SELECT x.Area,COALESCE(m.Nombre,N'Sin motivo asociado') Motivo,COUNT_BIG(*) Cantidad FROM dbo.IncidenciaMedidor i JOIN dbo.Medidor md ON md.idMedidor=i.idMedidor JOIN dbo.TipoMedidor tm ON tm.idTipoMedidor=md.idTipoMedidor CROSS APPLY(SELECT " + areaExpression + @" Area) x LEFT JOIN dbo.MotivoMedidor m ON m.idMotivo=i.idMotivo WHERE i.idEventoResolucion IS NULL AND " + incidentAreaScope + " GROUP BY x.Area,m.Nombre ORDER BY Cantidad DESC", cloneParams(incidentParameters)).ToList();
                motiveResolution = db.Database.SqlQuery<DashboardMotiveResolution>(@"SELECT x.Area,COALESCE(m.Nombre,N'Sin motivo asociado') Motivo,COUNT_BIG(*) Casos,AVG(CONVERT(float,DATEDIFF(MINUTE,i.FechaAperturaUtc,i.FechaResolucionUtc)))/60.0 HorasPromedio,MIN(DATEDIFF(MINUTE,i.FechaAperturaUtc,i.FechaResolucionUtc))/60.0 HorasMinimas,MAX(DATEDIFF(MINUTE,i.FechaAperturaUtc,i.FechaResolucionUtc))/60.0 HorasMaximas FROM dbo.IncidenciaMedidor i JOIN dbo.Medidor md ON md.idMedidor=i.idMedidor JOIN dbo.TipoMedidor tm ON tm.idTipoMedidor=md.idTipoMedidor CROSS APPLY(SELECT " + areaExpression + @" Area) x LEFT JOIN dbo.MotivoMedidor m ON m.idMotivo=i.idMotivo WHERE i.FechaResolucionUtc>=@from AND i.FechaResolucionUtc<@to AND " + incidentAreaScope + " GROUP BY x.Area,m.Nombre ORDER BY Casos DESC", cloneParams(incidentParameters)).ToList();
            }
            var states = new List<DashboardState>();
            if (incluyeArea("ADMINISTRACION"))
            {
                states.AddRange(db.Database.SqlQuery<DashboardState>(@"SELECT 'Altas' Proceso,'ADMINISTRACION' Area,Estado,COUNT_BIG(*) Cantidad FROM dbo.AltaSocio WHERE (@area IS NULL OR @area='ADMINISTRACION') GROUP BY Estado UNION ALL SELECT 'Documentación','ADMINISTRACION',Estado,COUNT_BIG(*) FROM dbo.DocumentoPersona WHERE (@area IS NULL OR @area='ADMINISTRACION') GROUP BY Estado", P("@area", gerente ? (object)filter.Area : null)).ToList());
            }
            var team = gerente ? new List<string>() : areasPermitidas;
            var pState = new[] { P("@area", gerente ? (object)filter.Area : null), P("@manager", gerente), P("@employee", LaboralAccess.Empleado(db, Principal)), P("@team", string.Join(",", team)), P("@isLeader", profile.Any(x => x.Encargado)) };
            states.AddRange(db.Database.SqlQuery<DashboardState>(@"SELECT 'Tareas' Proceso,t.Area,CONVERT(nvarchar(60),t.Estado) Estado,COUNT_BIG(*) Cantidad FROM dbo.TareaLaboral t WHERE (@manager=1 AND (@area IS NULL OR t.Area=@area)) OR (@manager=0 AND t.Area IN (SELECT value FROM STRING_SPLIT(@team,',')) AND (t.idEmpleadoAsignado=@employee OR @isLeader=1)) GROUP BY t.Area,t.Estado", cloneParams(pState)).ToList());
            states.AddRange(db.Database.SqlQuery<DashboardState>(@"SELECT 'Solicitudes' Proceso,a.Area,es.NombreEstado Estado,COUNT_BIG(*) Cantidad FROM dbo.SolicitudServicio s JOIN dbo.EstadoSolicitudServicio es ON es.idEstadoSolicitud=s.idEstadoSolicitud LEFT JOIN dbo.AreaServicio a ON a.idServicio=s.idServicio WHERE (@area IS NULL OR a.Area=@area) AND (@manager=1 OR a.Area IN (SELECT value FROM STRING_SPLIT(@team,','))) GROUP BY a.Area,es.NombreEstado", cloneParams(pState)).ToList());
            if (incluyeArea("AGUA") || incluyeArea("ENERGIA"))
                states.AddRange(db.Database.SqlQuery<DashboardState>(@"SELECT 'Mediciones' Proceso,CASE WHEN tm.Nombre='Agua' THEN 'AGUA' ELSE 'ENERGIA' END Area,CONVERT(nvarchar(60),d.Estado) Estado,COUNT_BIG(*) Cantidad FROM dbo.RutaMedicionDetalle d JOIN dbo.RutaMedicion r ON r.idRutaMedicion=d.idRutaMedicion JOIN dbo.Medidor m ON m.idMedidor=d.idMedidor JOIN dbo.TipoMedidor tm ON tm.idTipoMedidor=m.idTipoMedidor WHERE r.Fecha>=@month AND (@area IS NULL OR @area=CASE WHEN tm.Nombre='Agua' THEN 'AGUA' ELSE 'ENERGIA' END) AND (@manager=1 OR CASE WHEN tm.Nombre='Agua' THEN 'AGUA' ELSE 'ENERGIA' END IN (SELECT value FROM STRING_SPLIT(@team,','))) GROUP BY tm.Nombre,d.Estado", cloneParams(pState).Concat(new[] { P("@month", month) }).ToArray()).ToList());
            if (incluyeArea("INTERNET"))
                states.AddRange(db.Database.SqlQuery<DashboardState>(@"SELECT 'Facturación Internet' Proceso,'INTERNET' Area,COALESCE(d.Resultado,'Sin respuesta') Estado,COUNT_BIG(*) Cantidad FROM dbo.LoteFacturacionInternet l JOIN dbo.LoteFacturacionInternetDetalle d ON d.idLote=l.idLote WHERE l.Periodo=@periodo AND l.Estado<>'Anulado' AND (@area IS NULL OR @area='INTERNET') GROUP BY d.Resultado", P("@periodo", localNow.ToString("yyyy-MM")), P("@area",gerente?(object)filter.Area:null)));

            var quality = new List<DashboardQuality>();
            quality.AddRange(db.Database.SqlQuery<DashboardQuality>(@"WITH R AS(SELECT Area,Accion Resultado,COUNT_BIG(*) Cantidad FROM dbo.AuditoriaLaboral WHERE FechaUtc>=@from AND FechaUtc<@to AND Accion IN ('Aprobar','Rechazar','Devolver') AND ((@manager=1 AND (@area IS NULL OR Area=@area)) OR (@manager=0 AND Area IN (SELECT value FROM STRING_SPLIT(@team,',')))) GROUP BY Area,Accion),D AS(SELECT Area,SUM(Cantidad) Denominador FROM R GROUP BY Area) SELECT 'Tareas' Proceso,R.Area,'LABORAL' Contexto,R.Resultado,R.Cantidad,D.Denominador,CAST(R.Cantidad*100.0/NULLIF(D.Denominador,0) AS decimal(6,2)) Porcentaje FROM R JOIN D ON D.Area=R.Area", cloneParams(parameters).Concat(new[] { P("@from",utc(month)),P("@to",utc(tomorrow)),P("@manager",gerente),P("@team",string.Join(",",areasPermitidas)) }).ToArray()).ToList());
            if (incluyeArea("ADMINISTRACION"))
                quality.AddRange(db.Database.SqlQuery<DashboardQuality>(@"WITH R AS(SELECT EstadoNuevo Resultado,COUNT_BIG(*) Cantidad FROM dbo.HistorialDocumentoPersona WHERE FechaUtc>=@from AND FechaUtc<@to AND (@area IS NULL OR @area='ADMINISTRACION') GROUP BY EstadoNuevo),D AS(SELECT SUM(Cantidad) Denominador FROM R) SELECT 'Documentación' Proceso,'ADMINISTRACION' Area,'DOCUMENTO_PERSONA_REVISION' Contexto,R.Resultado,R.Cantidad,D.Denominador,CAST(R.Cantidad*100.0/NULLIF(D.Denominador,0) AS decimal(6,2)) Porcentaje FROM R CROSS JOIN D", range(utc(month),utc(tomorrow))));
            if (incluyeArea("AGUA") || incluyeArea("ENERGIA"))
                quality.AddRange(db.Database.SqlQuery<DashboardQuality>(@"SELECT 'Correcciones de lectura' Proceso,c.Area,'LECTURA_CORRECCION' Contexto,'Corregida' Resultado,COUNT_BIG(*) Cantidad,(SELECT COUNT_BIG(*) FROM dbo.Medicion m JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=m.idServicioSocio JOIN dbo.AreaServicio a ON a.idServicio=ss.idServicio WHERE a.Area=c.Area AND m.FechaLectura>=@from AND m.FechaLectura<@to) Denominador,CAST(COUNT_BIG(*)*100.0/NULLIF((SELECT COUNT_BIG(*) FROM dbo.Medicion m JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=m.idServicioSocio JOIN dbo.AreaServicio a ON a.idServicio=ss.idServicio WHERE a.Area=c.Area AND m.FechaLectura>=@from AND m.FechaLectura<@to),0) AS decimal(6,2)) Porcentaje FROM dbo.CorreccionLecturaLaboral c WHERE c.FechaUtc>=@from AND c.FechaUtc<@to AND ((@manager=1 AND (@area IS NULL OR c.Area=@area)) OR (@manager=0 AND c.Area IN (SELECT value FROM STRING_SPLIT(@team,',')))) GROUP BY c.Area", cloneParams(parameters).Concat(new[] { P("@from",utc(month)),P("@to",utc(tomorrow)),P("@manager",gerente),P("@team",string.Join(",",areasPermitidas)) }).ToArray()));
            var unavailable = new List<string>();
            if (incluyeArea("ADMINISTRACION")) unavailable.Add("Documentos históricos previos a la nueva migración: no se pueden reconstruir revisiones ni clasificar sus textos libres por motivo sin suponer equivalencias.");
            if (incluyeArea("AGUA") || incluyeArea("ENERGIA")) unavailable.Add("Lecturas no realizadas: falta el historial de visitas esperadas para calcular cumplimiento; una corrección se cuenta como evento separado.");
            if (gerente) unavailable.Add("Facturación de otros servicios: el sistema externo no devuelve aquí archivos de facturas ni un historial de intercambios comparable al lote de Internet.");
            if (!incidenceSchema && (incluyeArea("AGUA") || incluyeArea("ENERGIA"))) unavailable.Add("Incidencias de medidor: ejecutar 20261014_IncidenciasMedidor.sql para habilitar recurrencia, motivos vigentes y tiempos de resolución; no se enlazan eventos históricos por suposición.");
            else if (incluyeArea("AGUA") || incluyeArea("ENERGIA")) unavailable.Add("Los tiempos de resolución por motivo describen incidencias cerradas durante el mes; no incluyen incidencias aún abiertas.");
            unavailable.Add("La recepción semanal de solicitudes se cuenta desde que la API registra SolicitudCreada; los registros anteriores a ese evento no tienen fecha de recepción verificable en la bitácora.");
            return Ok(new { area = filter.Area, esGerencia = gerente, areasPermitidas, actualizadoUtc = DateTime.UtcNow, periodos = new { hoyDesdeLocal = today, hoyHastaLocalExclusivo = tomorrow, mesDesdeLocal = month, mesHastaLocalExclusivo = tomorrow, comparacionDesdeLocal = compareStart, comparacionHastaLocalExclusivo = compareEnd, tendenciaDesdeLocal = trendStartLocal, mesActualIncompleto = localNow.Day < DateTime.DaysInMonth(localNow.Year, localNow.Month), periodoOperativo = localNow.ToString("yyyy-MM") }, coberturaEventosDesdeUtc = coverage, actividadHoy = daily, motivosMes = motives, motivosDocumentacionMes = motives.Where(x => x.Contexto == "DOCUMENTO_PERSONA_RECHAZO").ToList(), tendencias = trends, semanasFlujo = weekly, pendientesAntiguedad = ageing, medidoresRecurrentes = repeatMeters, motivosIncidenciasAbiertas = incidentMotives, tiemposResolucionPorMotivo = motiveResolution, comparacionMensualEventos = monthComparison, estadosProcesos = states, estadosTareas = new List<DashboardState>(), indicadoresCalidad = quality, pendientesDeDatos = unavailable });
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
            var rows = db.Database.SqlQuery<EventoOperativoRegistro>(@"SELECT e.idEventoOperativo,e.Contexto,e.CodigoEvento,e.Entidad,e.idEntidad,e.FechaEventoUtc,e.idUsuarioSistema,
                u.Usuario,e.idEmpleado,e.idEmpleadoActor,e.ActorSistema,CONCAT(pa.Nombre,' ',pa.Apellido) EmpleadoActor,CONCAT(pt.Nombre,' ',pt.Apellido) EmpleadoRelacionado,e.Area,e.idSocio,e.idServicio,e.idServicioSocio,e.idMedidor,e.idRutaMedicion,e.Folio,e.EstadoAnterior,e.EstadoNuevo,
                e.idMotivoEvento,COALESCE(e.MotivoRegistrado,m.Nombre) Motivo,e.MotivoDesconocido,e.OperacionId,e.Resultado,e.Detalle,e.Datos
                FROM dbo.EventoOperativo e LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=e.idUsuarioSistema
                LEFT JOIN dbo.Empleado ea ON ea.idEmpleado=e.idEmpleadoActor LEFT JOIN dbo.Persona pa ON pa.idPersona=ea.idPersona
                LEFT JOIN dbo.Empleado et ON et.idEmpleado=e.idEmpleado LEFT JOIN dbo.Persona pt ON pt.idPersona=et.idPersona WHERE " + scope +
                " ORDER BY e.FechaEventoUtc DESC,e.idEventoOperativo DESC OFFSET @offset ROWS FETCH NEXT @tamano ROWS ONLY", parameters.ToArray()).ToList();
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
            var where = "(@desde IS NULL OR e.FechaEventoUtc>=@desde) AND (@hasta IS NULL OR e.FechaEventoUtc<@hasta) AND (@contexto IS NULL OR e.Contexto=@contexto) AND (@evento IS NULL OR e.CodigoEvento=@evento) AND (@area IS NULL OR e.Area=@area) AND (@empleado IS NULL OR e.idEmpleado=@empleado OR e.idEmpleadoActor=@empleado) AND (@servicio IS NULL OR e.idServicio=@servicio) AND (@conexion IS NULL OR e.idServicioSocio=@conexion) AND (@medidor IS NULL OR e.idMedidor=@medidor) AND (@ruta IS NULL OR e.idRutaMedicion=@ruta) AND (@motivo IS NULL OR e.idMotivoEvento=@motivo) AND (@folio IS NULL OR e.Folio=@folio)";
            if (!Principal.IsInRole("Administrador") && !LaboralAccess.Gerencia(db, Principal))
            {
                var employee = LaboralAccess.Empleado(db, Principal);
                if (!employee.HasValue) return where + " AND 1=0";
                var areas = LaboralAccess.Perfil(db, Principal).Where(a => a.Encargado || a.Funciones.Contains("ESTADISTICAS")).Select(a => a.Area).ToList();
                p.Add(P("@actorEmpleado", employee.Value));
                if (areas.Count == 0) where += " AND (e.Area IS NULL AND (e.idEmpleado=@actorEmpleado OR e.idEmpleadoActor=@actorEmpleado))";
                else { where += " AND (e.Area IN (SELECT value FROM STRING_SPLIT(@areas,',')) OR (e.Area IS NULL AND (e.idEmpleado=@actorEmpleado OR e.idEmpleadoActor=@actorEmpleado)))"; p.Add(P("@areas", string.Join(",", areas))); }
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
        public string Area { get; set; }
        public string CodigoEvento { get; set; }
        public string Motivo { get; set; }
        public long CantidadEventos { get; set; }
        public int RegistrosDistintos { get; set; }
        public long CantidadOperaciones { get; set; }
        public long MotivosDesconocidos { get; set; }
        public long DenominadorEventos { get; set; }
        public decimal PorcentajeDelEvento { get; set; }
    }

    public sealed class DashboardEvent { public string Area { get; set; } public string Contexto { get; set; } public string CodigoEvento { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardMotive { public string Area { get; set; } public string Contexto { get; set; } public string CodigoEvento { get; set; } public int? idMotivoEvento { get; set; } public int? idMotivoDocumento { get; set; } public int? idMotivoLaboral { get; set; } public string Motivo { get; set; } public bool MotivoDesconocido { get; set; } public long Cantidad { get; set; } public long Denominador { get; set; } }
    public sealed class DashboardTrend { public DateTime Periodo { get; set; } public string Area { get; set; } public string Contexto { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardFlowWeek { public DateTime Semana { get; set; } public string Area { get; set; } public string Proceso { get; set; } public string Tipo { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardAgeing { public string Area { get; set; } public string Proceso { get; set; } public string Rango { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardRepeat { public string Area { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardIncidentMotive { public string Area { get; set; } public string Motivo { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardMotiveResolution { public string Area { get; set; } public string Motivo { get; set; } public long Casos { get; set; } public double HorasPromedio { get; set; } public double HorasMinimas { get; set; } public double HorasMaximas { get; set; } }
    public sealed class DashboardPeriodCount { public string Periodo { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardState { public string Proceso { get; set; } public string Area { get; set; } public string Estado { get; set; } public long Cantidad { get; set; } }
    public sealed class DashboardQuality { public string Proceso { get; set; } public string Area { get; set; } public string Contexto { get; set; } public string Resultado { get; set; } public long Cantidad { get; set; } public long? Denominador { get; set; } public decimal? Porcentaje { get; set; } }

    public sealed class EventoOperativoRegistro
    {
        public long idEventoOperativo { get; set; }
        public string Contexto { get; set; }
        public string CodigoEvento { get; set; }
        public string Entidad { get; set; }
        public string idEntidad { get; set; }
        public DateTime FechaEventoUtc { get; set; }
        public int? idUsuarioSistema { get; set; }
        public string Usuario { get; set; }
        public int? idEmpleado { get; set; }
        public int? idEmpleadoActor { get; set; }
        public bool ActorSistema { get; set; }
        public string EmpleadoActor { get; set; }
        public string EmpleadoRelacionado { get; set; }
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
