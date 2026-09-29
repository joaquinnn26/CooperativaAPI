using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json;

namespace CooperativaAPI.Controllers
{
    public class OperacionMedidor
    {
        public string Accion { get; set; }
        public string Numero { get; set; }
        public string Marca { get; set; }
        public string Modelo { get; set; }
        public int idTipoMedidor { get; set; }
        public int idMotivo { get; set; }
        public int? idServicioSocio { get; set; }
        public decimal? Lectura { get; set; }
        public string EstadoFisico { get; set; }
        public DateTimeOffset? Fecha { get; set; }
        public string Detalle { get; set; }
        public string ReferenciaTrabajo { get; set; }
        public string Version { get; set; }
    }
    public class SeleccionMedidor : OperacionMedidor
    {
        public int? idMedidor { get; set; }
        public OperacionMedidor NuevoMedidor { get; set; }
        public string VersionAnterior { get; set; }
        public decimal? LecturaFinal { get; set; }
        public string EstadoRetirado { get; set; }
    }
    public class FichaMedidorDto
    {
        public int idMedidor { get; set; }
        public string Numero { get; set; }
        public int idTipoMedidor { get; set; }
        public string Tipo { get; set; }
        public string Socio { get; set; }
        public string Servicio { get; set; }
        public string Direccion { get; set; }
        public bool Usado { get; set; }
        public string Marca { get; set; }
        public string Modelo { get; set; }
        public string EstadoFisico { get; set; }
        public bool IdentidadVerificada { get; set; }
        public string NumeroAnterior { get; set; }
        public int? idServicioSocio { get; set; }
        public int? idSocio { get; set; }
        public int? idAsignacion { get; set; }
        public bool PendienteConciliacion { get; set; }
        public byte[] Version { get; set; }
    }
    public class MotivoMedidorDto
    {
        public int idMotivo { get; set; }
        public string Nombre { get; set; }
        public string Accion { get; set; }
        public bool Activo { get; set; }
        public byte[] Version { get; set; }
    }
    public class EventoMedidorDto
    {
        public long idEvento { get; set; }
        public Guid? OperacionId { get; set; }
        public string MotivoAccion { get; set; }
        public int? OtroMedidorId { get; set; }
        public string OtroMedidorNumero { get; set; }
        public DateTime FechaUtc { get; set; }
        public DateTime FechaRegistroUtc { get; set; }
        public string Accion { get; set; }
        public string Motivo { get; set; }
        public string Detalle { get; set; }
        public string Responsable { get; set; }
        public string ReferenciaTrabajo { get; set; }
        public int? idAsignacion { get; set; }
        public string Anterior { get; set; }
        public string Nuevo { get; set; }
    }
    public class AsignacionMedidorDto
    {
        public string Socio { get; set; }
        public string Servicio { get; set; }
        public string Direccion { get; set; }
        public int idAsignacion { get; set; }
        public int idServicioSocio { get; set; }
        public int idSocio { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public decimal? LecturaInicial { get; set; }
        public decimal? LecturaFinal { get; set; }
        public string Origen { get; set; }
        public bool Cerrada { get; set; }
        public bool PendienteConciliacion { get; set; }
    }
    // Compartido con el cierre de una instalación: una transacción para solicitud y medidor.
    public static class OperacionesMedidor
    {
        public static SqlParameter P(string key, object value) {
            // datetime2(3) evita redondeos distintos entre el alta y la instalación
            // del mismo equipo dentro de una única operación.
            if(value is DateTime) {
                var date=(DateTime)value;
                return new SqlParameter(key,SqlDbType.DateTime2) {Scale=3,Value=new DateTime(date.Ticks-date.Ticks%TimeSpan.TicksPerMillisecond,date.Kind)};
            }
            return new SqlParameter(key,value ?? DBNull.Value);
        }
        public const string SelectFicha=@"SELECT m.idMedidor,m.Numero,m.idTipoMedidor,t.Nombre Tipo,f.Marca,f.Modelo,
            (SELECT CONCAT(p.Nombre,' ',p.Apellido) FROM dbo.Socio so JOIN dbo.Persona p ON p.idPersona=so.idPersona WHERE so.idSocio=a.idSocio) Socio,
            (SELECT se.Nombre FROM dbo.ServicioSocio ss JOIN dbo.Servicio se ON se.idServicio=ss.idServicio WHERE ss.idServicioSocio=a.idServicioSocio) Servicio,
            (SELECT TOP 1 CONCAT(u.Direccion,' · ',u.Localidad,' · Ruta ',u.Ruta,' / ',u.Folio) FROM dbo.UbicacionServicio u WHERE u.idServicioSocio=a.idServicioSocio ORDER BY u.idServicioSocio) Direccion,
            CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.AsignacionMedidor h WHERE h.idMedidor=m.idMedidor) THEN 1 ELSE 0 END AS bit) Usado,
            f.EstadoFisico,f.IdentidadVerificada,f.NumeroAnterior,f.Version,a.idServicioSocio,a.idSocio,a.idAsignacion,ISNULL(a.PendienteConciliacion,0) PendienteConciliacion
            FROM dbo.Medidor m JOIN dbo.FichaMedidor f ON f.idMedidor=m.idMedidor
            JOIN dbo.TipoMedidor t ON t.idTipoMedidor=m.idTipoMedidor
            LEFT JOIN dbo.AsignacionMedidor a ON a.idMedidor=m.idMedidor AND a.Cerrada=0";
        public static FichaMedidorDto Ficha(CooperativaEntities db,int id) {
            return db.Database.SqlQuery<FichaMedidorDto>(SelectFicha+" WHERE m.idMedidor=@id",P("@id",id)).SingleOrDefault();
        }
        public static string Texto(string text) { return System.Text.RegularExpressions.Regex.Replace((text??"").Trim(),@"\s+"," "); }
        public static string Normalizar(string text) {
            return new string(Texto(text).Normalize(System.Text.NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        }
        public static int Usuario(ClaimsPrincipal user) { return int.Parse(user.FindFirst("idUsuarioSistema").Value); }
        public static bool PuedeOperar(CooperativaEntities db,ClaimsPrincipal user) {
            if(user.IsInRole("Administrador")) return true;
            int id;
            return int.TryParse(user.FindFirst("idEmpleado")?.Value,out id) && db.Empleado.Any(e=>e.idEmpleado==id && e.Activo==true && e.Cargo.NombreCargo=="Tecnico");
        }
        public static FichaMedidorDto Ejecutar(CooperativaEntities db,ClaimsPrincipal user,int? id,OperacionMedidor op,string motivoAccion=null,Guid? operacionId=null) {
            if(op==null) throw new ArgumentException("Indicá los datos de la operación.");
            var accion=op.Accion;
            if(!new[]{"Alta","Verificar","Editar","Instalar","Retirar","Falla","Reparacion","Revision","BajaDefinitiva"}.Contains(accion)) throw new ArgumentException("Acción inválida.");
            if(((accion=="Alta" && motivoAccion==null) || accion=="Editar" || accion=="BajaDefinitiva") && !user.IsInRole("Administrador")) throw new UnauthorizedAccessException();
            if(Texto(op.Detalle).Length>1000 || Texto(op.ReferenciaTrabajo).Length>100) throw new ArgumentException("Detalle o referencia demasiado largos.");
            var motivo=db.Database.SqlQuery<MotivoMedidorDto>("SELECT * FROM dbo.MotivoMedidor WHERE idMotivo=@id AND Activo=1 AND Accion=@accion",P("@id",op.idMotivo),P("@accion",motivoAccion??accion)).SingleOrDefault();
            if(motivo==null) throw new ArgumentException("Seleccioná un motivo activo para esta acción.");
            var fecha=op.Fecha?.UtcDateTime ?? DateTime.UtcNow;
            if(fecha>DateTime.UtcNow.AddMinutes(1)) throw new ArgumentException("La fecha no puede ser futura.");
            var antes=id.HasValue?Ficha(db,id.Value):null;
            if(id.HasValue && antes==null) throw new ArgumentException("El medidor no existe.");
            if(antes!=null && Convert.ToBase64String(antes.Version)!=op.Version) throw new DBConcurrencyException("El medidor cambió. Actualizá su ficha.");
            if(antes!=null && antes.EstadoFisico=="BajaDefinitiva") throw new ArgumentException("El medidor está dado de baja definitivamente.");
            if(id.HasValue && db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EventoMedidor WHERE idMedidor=@id AND FechaUtc>@fecha AND Accion<>'Migracion'",P("@id",id.Value),P("@fecha",fecha)).Any()) throw new ArgumentException("La fecha es anterior al último movimiento. No se reescribe la historia.");
            int medidorId=id??0;
            int? asignacion=antes?.idAsignacion;
            if(accion=="Alta" || accion=="Verificar" || accion=="Editar") {
                if((accion=="Alta") != !id.HasValue) throw new ArgumentException("Acción incompatible con el registro.");
                if(accion=="Verificar" && antes.IdentidadVerificada) throw new ArgumentException("La identidad ya fue verificada.");
                if(accion=="Editar" && !antes.IdentidadVerificada) throw new ArgumentException("Confirmá primero los datos físicos del medidor.");
                var numero=Texto(op.Numero);
                if(numero.Length==0 || numero.Length>50 || Texto(op.Marca).Length>100 || Texto(op.Modelo).Length>100) throw new ArgumentException("Indicá número real (hasta 50 caracteres); marca y modelo admiten hasta 100.");
                var tipo=db.TipoMedidor.Find(op.idTipoMedidor);
                if(tipo==null || !new[]{"agua","electrico"}.Contains(Normalizar(tipo.Nombre))) throw new ArgumentException("Seleccioná medidor de agua o eléctrico.");
                if(antes?.idAsignacion!=null && antes.idTipoMedidor!=op.idTipoMedidor) throw new ArgumentException("Retirá el medidor antes de cambiar su tipo.");
                if(db.Database.SqlQuery<int>("SELECT 1 FROM dbo.Medidor WITH(UPDLOCK,HOLDLOCK) WHERE Numero=@numero AND idMedidor<>@id",P("@numero",numero),P("@id",medidorId)).Any()) throw new ArgumentException("Ese número ya pertenece a un medidor.");
                if(accion=="Alta") {
                    medidorId=db.Database.SqlQuery<int>("INSERT dbo.Medidor(Numero,idTipoMedidor,idServicioSocio,Activo) OUTPUT INSERTED.idMedidor VALUES(@numero,@tipo,NULL,0)",P("@numero",numero),P("@tipo",op.idTipoMedidor)).Single();
                    db.Database.ExecuteSqlCommand("INSERT dbo.FichaMedidor(idMedidor,IdentidadVerificada,EstadoFisico,Marca,Modelo) VALUES(@id,1,'Operativo',@marca,@modelo)",P("@id",medidorId),P("@marca",Texto(op.Marca)),P("@modelo",Texto(op.Modelo)));
                } else {
                    db.Database.ExecuteSqlCommand("UPDATE dbo.Medidor SET Numero=@numero,idTipoMedidor=@tipo WHERE idMedidor=@id",P("@numero",numero),P("@tipo",op.idTipoMedidor),P("@id",medidorId));
                    db.Database.ExecuteSqlCommand("UPDATE dbo.FichaMedidor SET IdentidadVerificada=1,Marca=@marca,Modelo=@modelo WHERE idMedidor=@id",P("@id",medidorId),P("@marca",Texto(op.Marca)),P("@modelo",Texto(op.Modelo)));
                }
                if(accion=="Verificar" && op.EstadoFisico!=null) {
                    if(!new[]{"Operativo","PendienteRevision","Averiado"}.Contains(op.EstadoFisico)) throw new ArgumentException("Seleccioná la condición física verificada.");
                    db.Database.ExecuteSqlCommand("UPDATE dbo.FichaMedidor SET EstadoFisico=@estado WHERE idMedidor=@id",P("@estado",op.EstadoFisico),P("@id",medidorId));
                }
            } else if(accion=="Instalar") {
                if(antes==null || !antes.IdentidadVerificada || antes.EstadoFisico!="Operativo" || antes.idAsignacion.HasValue) throw new ArgumentException("El medidor debe estar verificado, operativo y sin instalación activa.");
                var conexion=db.ServicioSocio.Find(op.idServicioSocio);
                if(conexion==null || conexion.Activo!=true || !db.Socio.Any(s=>s.idSocio==conexion.idSocio && s.Activo==true)) throw new ArgumentException("Seleccioná una conexión y un socio activos.");
                var servicio=Normalizar(db.Servicio.Find(conexion.idServicio)?.Nombre); var tipo=Normalizar(antes.Tipo);
                if(!((servicio.Contains("agua") && tipo=="agua") || ((servicio.Contains("luz") || servicio.Contains("electric")) && tipo=="electrico"))) throw new ArgumentException("El tipo de medidor no corresponde al servicio.");
                if(db.Database.SqlQuery<int>("SELECT 1 FROM dbo.AsignacionMedidor WHERE idServicioSocio=@id AND Cerrada=0",P("@id",op.idServicioSocio)).Any()) throw new ArgumentException("La conexión ya tiene un medidor instalado. Retiralo primero.");
                if(!op.Lectura.HasValue || op.Lectura<0) throw new ArgumentException("Indicá la lectura inicial real.");
                var ultima=db.Database.SqlQuery<decimal?>("SELECT MAX(LecturaFinal) FROM dbo.AsignacionMedidor WHERE idMedidor=@id",P("@id",medidorId)).Single();
                if(ultima.HasValue && op.Lectura<ultima) throw new ArgumentException("La lectura inicial no puede ser menor a la del último retiro.");
                asignacion=db.Database.SqlQuery<int>(@"INSERT dbo.AsignacionMedidor(idMedidor,idServicioSocio,idSocio,FechaInicio,LecturaInicial,Origen)
                    VALUES(@id,@conexion,@socio,@fecha,@lectura,'Registrada'); SELECT CAST(SCOPE_IDENTITY() AS int)",P("@id",medidorId),P("@conexion",op.idServicioSocio),P("@socio",conexion.idSocio),P("@fecha",fecha),P("@lectura",op.Lectura)).Single();
                db.Database.ExecuteSqlCommand("UPDATE dbo.Medidor SET idServicioSocio=@conexion,Activo=1,FechaInstalacion=@fecha WHERE idMedidor=@id",P("@conexion",op.idServicioSocio),P("@fecha",fecha),P("@id",medidorId));
            } else if(accion=="Retirar") {
                if(antes?.idAsignacion==null) throw new ArgumentException("El medidor no tiene instalación activa.");
                if(db.Database.SqlQuery<int>(@"SELECT 1 FROM dbo.RutaMedicionDetalle d JOIN dbo.RutaMedicion r ON r.idRutaMedicion=d.idRutaMedicion WHERE d.idMedidor=@id AND r.Estado NOT IN ('Finalizada','Cancelada')",P("@id",medidorId)).Any()) throw new ArgumentException("Cerrá o cancelá las hojas de ruta abiertas antes de retirar el medidor.");
                var minimo=db.Database.SqlQuery<decimal>(@"SELECT ISNULL(MAX(v),0) FROM (
                    SELECT ValorLectura v FROM dbo.Medicion WHERE idMedidor=@id UNION ALL SELECT LecturaInicial FROM dbo.AsignacionMedidor WHERE idMedidor=@id) x",P("@id",medidorId)).Single();
                if(!op.Lectura.HasValue || op.Lectura<minimo) throw new ArgumentException("Indicá la lectura final, no menor a la última registrada.");
                if(!new[]{"Operativo","PendienteRevision","Averiado"}.Contains(op.EstadoFisico)) throw new ArgumentException("Indicá la condición del medidor retirado.");
                db.Database.ExecuteSqlCommand("UPDATE dbo.AsignacionMedidor SET Cerrada=1,FechaFin=@fecha,LecturaFinal=@lectura WHERE idAsignacion=@id",P("@fecha",fecha),P("@lectura",op.Lectura),P("@id",asignacion));
                db.Database.ExecuteSqlCommand("UPDATE dbo.Medidor SET idServicioSocio=NULL,Activo=0 WHERE idMedidor=@id",P("@id",medidorId));
                db.Database.ExecuteSqlCommand("UPDATE dbo.FichaMedidor SET EstadoFisico=@estado WHERE idMedidor=@id",P("@estado",op.EstadoFisico),P("@id",medidorId));
            } else {
                if(antes==null) throw new ArgumentException("Seleccioná un medidor.");
                string estado;
                if(accion=="Falla") estado="Averiado";
                else if(accion=="Reparacion") {
                    if(antes.idAsignacion.HasValue || !new[]{"Averiado","PendienteRevision"}.Contains(antes.EstadoFisico)) throw new ArgumentException("Para reparar, retiralo y registrá su falla o revisión pendiente.");
                    estado="EnReparacion";
                } else if(accion=="Revision") {
                    if(!new[]{"Operativo","Averiado","PendienteRevision"}.Contains(op.EstadoFisico)) throw new ArgumentException("Seleccioná el resultado de revisión.");
                    if(op.EstadoFisico=="Operativo" && !antes.IdentidadVerificada) throw new ArgumentException("Verificá primero el número físico del medidor.");
                    estado=op.EstadoFisico;
                } else {
                    if(antes.idAsignacion.HasValue) throw new ArgumentException("Retirá el medidor antes de su baja definitiva.");
                    estado="BajaDefinitiva";
                }
                db.Database.ExecuteSqlCommand("UPDATE dbo.FichaMedidor SET EstadoFisico=@estado WHERE idMedidor=@id",P("@estado",estado),P("@id",medidorId));
            }
            if((accion=="Verificar" || accion=="Revision") && antes?.idAsignacion!=null)
                db.Database.ExecuteSqlCommand(@"UPDATE a SET PendienteConciliacion=0 FROM dbo.AsignacionMedidor a
                    WHERE a.idAsignacion=@id AND NOT EXISTS(SELECT 1 FROM dbo.AsignacionMedidor b WHERE b.idServicioSocio=a.idServicioSocio AND b.idAsignacion<>a.idAsignacion AND b.Cerrada=0)",P("@id",antes.idAsignacion));
            // Incrementa la versión también cuando solo cambia la asignación.
            db.Database.ExecuteSqlCommand("UPDATE dbo.FichaMedidor SET EstadoFisico=EstadoFisico WHERE idMedidor=@id",P("@id",medidorId));
            var nuevo=Ficha(db,medidorId);
            db.Database.ExecuteSqlCommand(@"INSERT dbo.EventoMedidor(idMedidor,FechaUtc,Accion,idUsuarioSistema,idMotivo,Motivo,Detalle,idAsignacion,ReferenciaTrabajo,Anterior,Nuevo,OperacionId)
                VALUES(@id,@fecha,@accion,@usuario,@motivoId,@motivo,@detalle,@asignacion,@trabajo,@anterior,@nuevo,@operacion)",
                P("@id",medidorId),P("@fecha",fecha),P("@accion",accion),P("@usuario",Usuario(user)),P("@motivoId",op.idMotivo),P("@motivo",motivo.Nombre),
                P("@detalle",Texto(op.Detalle)),P("@asignacion",asignacion),P("@trabajo",Texto(op.ReferenciaTrabajo)),P("@anterior",antes==null?null:JsonConvert.SerializeObject(antes)),P("@nuevo",JsonConvert.SerializeObject(nuevo)),P("@operacion",operacionId));
            return nuevo;
        }
        // Solo se llama dentro de una transacción de instalación o reemplazo autorizada.
        public static FichaMedidorDto Colocar(CooperativaEntities db,ClaimsPrincipal user,SeleccionMedidor op,int conexion,string motivoAccion,Guid operacionId) {
            if(op==null || (op.idMedidor.GetValueOrDefault()>0)==(op.NuevoMedidor!=null)) throw new ArgumentException("Elegí un medidor disponible o registrá uno nuevo.");
            if(op.NuevoMedidor!=null) {
                var alta=op.NuevoMedidor;
                alta.Accion="Alta"; alta.idMotivo=op.idMotivo; alta.Fecha=op.Fecha;
                alta.Detalle=op.Detalle; alta.ReferenciaTrabajo=op.ReferenciaTrabajo;
                var nuevo=Ejecutar(db,user,null,alta,motivoAccion,operacionId);
                op.idMedidor=nuevo.idMedidor; op.Version=Convert.ToBase64String(nuevo.Version);
            }
            op.Accion="Instalar"; op.idServicioSocio=conexion;
            return Ejecutar(db,user,op.idMedidor,op,motivoAccion,operacionId);
        }
    }

    [RoutePrefix("api/inventario-medidores")]
    [JwtAuthorize(Roles="Administrador,Empleado")]
    public class InventarioMedidoresController:ApiController
    {
        private readonly CooperativaEntities db=new CooperativaEntities();
        private ClaimsPrincipal Principal { get { return (ClaimsPrincipal)User; } }
        [HttpGet,Route("")]
        public IHttpActionResult Listar(int pagina=1,string q="",bool disponibles=false,string estado="Todos",int? tipo=null,bool exacto=false) {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            if(pagina<1 || pagina>100000 || (q??"").Length>100 || !new[]{"Todos","Disponibles","Instalados","Revision","Baja"}.Contains(estado)) return BadRequest("Filtros inválidos.");
            var where=" WHERE (@q='' OR (@exacto=1 AND m.Numero=@q) OR (@exacto=0 AND CHARINDEX(@q,CONCAT(m.Numero,' ',m.Marca,' ',m.Modelo,' ',m.Socio,' ',m.Servicio,' ',m.Direccion))>0)) AND (@tipo IS NULL OR m.idTipoMedidor=@tipo)";
            if(disponibles || estado=="Disponibles") where+=" AND m.IdentidadVerificada=1 AND m.EstadoFisico='Operativo' AND m.idAsignacion IS NULL";
            else if(estado=="Instalados") where+=" AND m.idAsignacion IS NOT NULL";
            else if(estado=="Revision") where+=" AND m.EstadoFisico<>'BajaDefinitiva' AND (m.IdentidadVerificada=0 OR m.EstadoFisico<>'Operativo' OR m.PendienteConciliacion=1)";
            else if(estado=="Baja") where+=" AND m.EstadoFisico='BajaDefinitiva'";
            var from=" FROM ("+OperacionesMedidor.SelectFicha+") m"+where;
            Func<SqlParameter[]> parameters=()=>new[]{OperacionesMedidor.P("@q",OperacionesMedidor.Texto(q)),OperacionesMedidor.P("@tipo",tipo),OperacionesMedidor.P("@exacto",exacto),OperacionesMedidor.P("@offset",(pagina-1)*25)};
            var items=db.Database.SqlQuery<FichaMedidorDto>("SELECT m.*"+from+" ORDER BY m.idMedidor DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",parameters()).ToList();
            var total=db.Database.SqlQuery<int>("SELECT COUNT(*)"+from,parameters()).Single();
            return Ok(new {items,total});
        }
        [HttpGet,Route("conexiones")]
        public IHttpActionResult Conexiones(int pagina=1,string q="") {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            if(pagina<1 || pagina>100000 || (q??"").Length>100) return BadRequest("Filtros inválidos.");
            var from=@" FROM dbo.ServicioSocio s JOIN dbo.Socio so ON so.idSocio=s.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona
                JOIN dbo.Servicio se ON se.idServicio=s.idServicio LEFT JOIN dbo.UbicacionServicio u ON u.idServicioSocio=s.idServicioSocio
                WHERE s.Activo=1 AND so.Activo=1 AND (@q='' OR CHARINDEX(@q,CONCAT(s.idServicioSocio,' ',so.NumeroSocio,' ',p.Nombre,' ',p.Apellido,' ',se.Nombre,' ',u.Direccion,' ',u.Ruta,' ',u.Folio))>0)";
            var items=db.Database.SqlQuery<TerritorioController.Conexion>(@"SELECT s.idServicioSocio,s.idSocio,so.NumeroSocio,CONCAT(p.Nombre,' ',p.Apellido) Socio,se.Nombre Servicio,CAST(1 AS bit) Activo,u.Direccion,u.Localidad,u.Ambito,u.Ruta,u.Folio,u.idBarrio,CAST(NULL AS int) idZona,CAST(NULL AS nvarchar(100)) Barrio,CAST(NULL AS nvarchar(100)) Zona,u.Version"+from+" ORDER BY s.idServicioSocio OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",OperacionesMedidor.P("@q",q??""),OperacionesMedidor.P("@offset",(pagina-1)*25)).ToList();
            return Ok(new {items,total=db.Database.SqlQuery<int>("SELECT COUNT(*)"+from,OperacionesMedidor.P("@q",q??"")).Single()});
        }
        [HttpGet,Route("{id:int}")]
        public IHttpActionResult Detalle(int id) {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            var ficha=OperacionesMedidor.Ficha(db,id); if(ficha==null) return NotFound();
            var eventos=db.Database.SqlQuery<EventoMedidorDto>(@"SELECT e.*,u.Usuario Responsable,mot.Accion MotivoAccion,
                (SELECT TOP 1 otro.idMedidor FROM dbo.EventoMedidor otro WHERE otro.OperacionId=e.OperacionId AND otro.idMedidor<>e.idMedidor ORDER BY otro.idEvento) OtroMedidorId,
                (SELECT TOP 1 m.Numero FROM dbo.EventoMedidor otro JOIN dbo.Medidor m ON m.idMedidor=otro.idMedidor WHERE otro.OperacionId=e.OperacionId AND otro.idMedidor<>e.idMedidor ORDER BY otro.idEvento) OtroMedidorNumero
                FROM dbo.EventoMedidor e LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=e.idUsuarioSistema
                LEFT JOIN dbo.MotivoMedidor mot ON mot.idMotivo=e.idMotivo WHERE e.idMedidor=@id ORDER BY e.FechaUtc DESC,e.idEvento DESC",OperacionesMedidor.P("@id",id)).ToList();
            foreach(var e in eventos) { e.FechaUtc=DateTime.SpecifyKind(e.FechaUtc,DateTimeKind.Utc); e.FechaRegistroUtc=DateTime.SpecifyKind(e.FechaRegistroUtc,DateTimeKind.Utc); }
            var asignaciones=db.Database.SqlQuery<AsignacionMedidorDto>(@"SELECT a.*,CONCAT(p.Nombre,' ',p.Apellido) Socio,se.Nombre Servicio,
                (SELECT TOP 1 u.Direccion FROM dbo.UbicacionServicio u WHERE u.idServicioSocio=a.idServicioSocio ORDER BY u.idServicioSocio) Direccion
                FROM dbo.AsignacionMedidor a JOIN dbo.Socio so ON so.idSocio=a.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona
                JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=a.idServicioSocio JOIN dbo.Servicio se ON se.idServicio=ss.idServicio
                WHERE a.idMedidor=@id ORDER BY a.idAsignacion DESC",OperacionesMedidor.P("@id",id)).ToList();
            foreach(var a in asignaciones) {
                if(a.FechaInicio.HasValue) a.FechaInicio=DateTime.SpecifyKind(a.FechaInicio.Value,DateTimeKind.Utc);
                if(a.FechaFin.HasValue) a.FechaFin=DateTime.SpecifyKind(a.FechaFin.Value,DateTimeKind.Utc);
            }
            return Ok(new {ficha,eventos,asignaciones});
        }
        [HttpGet,Route("catalogos")]
        public IHttpActionResult Catalogos() {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            return Ok(new {tipos=db.TipoMedidor.Select(t=>new {t.idTipoMedidor,t.Nombre}).ToList(),motivos=db.Database.SqlQuery<MotivoMedidorDto>("SELECT * FROM dbo.MotivoMedidor ORDER BY Accion,Nombre").ToList()});
        }
        [HttpPost,Route("")]
        public IHttpActionResult Crear(OperacionMedidor op) { if(op!=null) op.Accion="Alta"; return Guardar(null,op); }
        [HttpPost,Route("{id:int}/eventos")]
        public IHttpActionResult Evento(int id,OperacionMedidor op) { return Guardar(id,op); }
        [HttpPost,Route("{id:int}/reemplazar")]
        public IHttpActionResult Reemplazar(int id,SeleccionMedidor op) {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            if(op==null || !ModelState.IsValid) return BadRequest("Completá los datos del reemplazo.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                try {
                    var anterior=OperacionesMedidor.Ficha(db,id);
                    if(anterior?.idAsignacion==null) return BadRequest("El medidor anterior ya no está instalado. Actualizá la lista.");
                    if(op.idMedidor==id) return BadRequest("El reemplazo debe ser otro medidor.");
                    if(anterior.PendienteConciliacion) return BadRequest("Primero confirmá qué medidor está instalado y resolvé las asignaciones anteriores de esta conexión.");
                    var operacion=Guid.NewGuid();
                    op.Fecha=DateTimeOffset.UtcNow;
                    var retiro=new OperacionMedidor {Accion="Retirar",Version=op.VersionAnterior,Lectura=op.LecturaFinal,EstadoFisico=op.EstadoRetirado,idMotivo=op.idMotivo,Fecha=op.Fecha,Detalle=op.Detalle,ReferenciaTrabajo=op.ReferenciaTrabajo};
                    var retirado=OperacionesMedidor.Ejecutar(db,Principal,id,retiro,"Reemplazar",operacion);
                    var instalado=OperacionesMedidor.Colocar(db,Principal,op,anterior.idServicioSocio.Value,"Reemplazar",operacion);
                    tx.Commit(); return Ok(new {retirado,instalado,operacion});
                } catch(ArgumentException ex) { return BadRequest(ex.Message); }
                catch(DBConcurrencyException ex) { return Content(HttpStatusCode.Conflict,ex.Message); }
                catch(UnauthorizedAccessException) { return StatusCode(HttpStatusCode.Forbidden); }
                catch(SqlException ex) when(ex.Number==2601 || ex.Number==2627 || ex.Number==1205 || ex.Number==547 || ex.Number==51001) { return Content(HttpStatusCode.Conflict,"Los datos cambiaron. Actualizá los medidores antes de volver a intentar."); }
            }
        }
        private IHttpActionResult Guardar(int? id,OperacionMedidor op) {
            if(!OperacionesMedidor.PuedeOperar(db,Principal)) return StatusCode(HttpStatusCode.Forbidden);
            if(!ModelState.IsValid) return BadRequest("Datos inválidos.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                try { var ficha=OperacionesMedidor.Ejecutar(db,Principal,id,op); tx.Commit(); return Ok(ficha); }
                catch(ArgumentException ex) { return BadRequest(ex.Message); }
                catch(DBConcurrencyException ex) { return Content(HttpStatusCode.Conflict,ex.Message); }
                catch(UnauthorizedAccessException) { return StatusCode(HttpStatusCode.Forbidden); }
                catch(SqlException ex) when(ex.Number==2601 || ex.Number==2627 || ex.Number==1205 || ex.Number==547 || ex.Number==51001) { return Content(HttpStatusCode.Conflict,"La operación entra en conflicto con los datos actuales. Actualizá y revisá fechas y asignaciones."); }
            }
        }
        [HttpPost,Route("motivos")]
        [JwtAuthorize(Roles="Administrador")]
        public IHttpActionResult Motivo(MotivoMedidorDto item) {
            if(item==null || !ModelState.IsValid || OperacionesMedidor.Texto(item.Nombre).Length==0 || OperacionesMedidor.Texto(item.Nombre).Length>100 || !new[]{"Alta","Verificar","Editar","Instalar","Reemplazar","Retirar","Falla","Reparacion","Revision","BajaDefinitiva"}.Contains(item.Accion)) return BadRequest("Indicá nombre y acción válidos.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                try {
                    int id;
                    if(item.idMotivo==0) id=db.Database.SqlQuery<int>("INSERT dbo.MotivoMedidor(Nombre,Accion,Activo) OUTPUT INSERTED.idMotivo VALUES(@nombre,@accion,1)",OperacionesMedidor.P("@nombre",OperacionesMedidor.Texto(item.Nombre)),OperacionesMedidor.P("@accion",item.Accion)).Single();
                    else {
                        var actual=db.Database.SqlQuery<MotivoMedidorDto>("SELECT * FROM dbo.MotivoMedidor WHERE idMotivo=@id",OperacionesMedidor.P("@id",item.idMotivo)).SingleOrDefault();
                        if(actual==null) return NotFound();
                        if(item.Version==null || !actual.Version.SequenceEqual(item.Version)) return Content(HttpStatusCode.Conflict,"El motivo cambió. Actualizá.");
                        if(actual.Nombre!=item.Nombre || actual.Accion!=item.Accion) return BadRequest("Los motivos utilizados se conservan. Desactivalo y creá otro si cambia su significado.");
                        id=item.idMotivo;
                        db.Database.ExecuteSqlCommand("UPDATE dbo.MotivoMedidor SET Activo=@activo WHERE idMotivo=@id",OperacionesMedidor.P("@activo",item.Activo),OperacionesMedidor.P("@id",id));
                    }
                    db.Database.ExecuteSqlCommand(@"INSERT dbo.HistorialTerritorial(idUsuarioSistema,Usuario,Entidad,idEntidad,Accion,Motivo,Nuevo)
                        SELECT idUsuarioSistema,Usuario,'MotivoMedidor',@id,'Configurar','Configuración de catálogo',@nuevo FROM dbo.UsuarioSistema WHERE idUsuarioSistema=@usuario",OperacionesMedidor.P("@id",id),OperacionesMedidor.P("@nuevo",JsonConvert.SerializeObject(item)),OperacionesMedidor.P("@usuario",OperacionesMedidor.Usuario(Principal)));
                    tx.Commit(); return Ok(new {idMotivo=id});
                } catch(SqlException ex) when(ex.Number==2601 || ex.Number==2627 || ex.Number==1205) { return Content(HttpStatusCode.Conflict,"Motivo duplicado o modificado simultáneamente."); }
            }
        }
        protected override void Dispose(bool disposing) { if(disposing) db.Dispose(); base.Dispose(disposing); }
    }
}
