using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/adhesiones"), JwtAuthorize(Roles="Socio,Administrador,Empleado")]
    public class AdhesionesController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        protected override void Dispose(bool disposing) { if(disposing) db.Dispose(); base.Dispose(disposing); }
        private ClaimsPrincipal Principal { get { return (ClaimsPrincipal)User; } }
        private int UserId { get { return int.Parse(Principal.FindFirst("idUsuarioSistema").Value); } }
        private int SocioId { get { if (!User.IsInRole("Socio")) throw new UnauthorizedAccessException(); return int.Parse(Principal.FindFirst("idSocio").Value); } }
        private static SqlParameter P(string key, object value) { return AdhesionesBeneficios.P(key,value); }
        private System.Collections.Generic.List<JObject> Rows(string sql, params SqlParameter[] args) { return AdhesionesBeneficios.Rows(db,sql,args); }
        private void Gestion()
        {
            int? empleado = LaboralAccess.Empleado(db, Principal);
            if (!(User.IsInRole("Administrador") || LaboralAccess.Encargado(db,empleado,"ADMINISTRACION") || LaboralAccess.Tiene(db,empleado,"ADMINISTRACION","ATENCION") || LaboralAccess.Encargado(db,empleado,"BENEFICIOS") || LaboralAccess.Tiene(db,empleado,"BENEFICIOS","GESTION_BENEFICIO"))) throw new UnauthorizedAccessException();
        }
        private bool Ready() { return db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('dbo.AdhesionBeneficio','U') IS NULL THEN 0 ELSE 1 END").Single()==1; }
        private IHttpActionResult Read(Func<object> action)
        {
            if (!Ready()) return Content(HttpStatusCode.ServiceUnavailable,"Falta aplicar la migración 20261016_AdhesionesBeneficios.sql en la API.");
            try { return Ok(action()); } catch(UnauthorizedAccessException) { return StatusCode(HttpStatusCode.Forbidden); } catch(ArgumentException ex) { return BadRequest(ex.Message); }
        }
        private IHttpActionResult Write(Func<object> action)
        {
            if (!Ready()) return Content(HttpStatusCode.ServiceUnavailable,"Falta aplicar la migración 20261016_AdhesionesBeneficios.sql en la API.");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try { var value=action(); db.SaveChanges(); tx.Commit(); return Ok(value); }
                catch(UnauthorizedAccessException) { return StatusCode(HttpStatusCode.Forbidden); }
                catch(ArgumentException ex) { return BadRequest(ex.Message); }
                catch(DBConcurrencyException ex) { return Content(HttpStatusCode.Conflict,ex.Message); }
                catch(SqlException ex) when(new[]{2601,2627,1205,547}.Contains(ex.Number)) { return Content(HttpStatusCode.Conflict,"Los datos cambiaron o existe una adhesión vigente. Actualizá la vista."); }
            }
        }
        private JObject Adhesion(int id, bool gestor=false)
        {
            var row=Rows("SELECT * FROM dbo.AdhesionBeneficio WHERE idAdhesion=@id",P("@id",id)).SingleOrDefault();
            if(row==null) throw new ArgumentException("La adhesión no existe.");
            if(gestor) Gestion(); else if((int)row["idSocio"]!=SocioId) throw new UnauthorizedAccessException();
            return row;
        }
        private void Revision(JObject row,JObject data) { if((int?)data?["Revision"]!=(int)row["Revision"]) throw new DBConcurrencyException("El trámite cambió. Actualizá antes de guardar."); }
        private object Listar(int? socio)
        {
            var items=Rows(@"SELECT a.*,s.NumeroSocio,CONCAT(p.Nombre,' ',p.Apellido) Socio FROM dbo.AdhesionBeneficio a JOIN dbo.Socio s ON s.idSocio=a.idSocio JOIN dbo.Persona p ON p.idPersona=s.idPersona WHERE (@s IS NULL OR a.idSocio=@s) ORDER BY a.idAdhesion DESC",P("@s",socio));
            foreach(var a in items)
            {
                int id=(int)a["idAdhesion"];
                a["Familiares"]=JArray.FromObject(Rows("SELECT idBeneficiario,Nombre,Documento,Vinculo,MismoDomicilio,ConservaAdhesion,NombreArchivo FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@id ORDER BY idBeneficiario",P("@id",id)));
                a["Historial"]=JArray.FromObject(Rows("SELECT Estado,Motivo,FechaUtc FROM dbo.AdhesionHistorial WHERE idAdhesion=@id ORDER BY idHistorial",P("@id",id)));
                a["CoberturaVigente"]=db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Socio s WHERE s.idSocio=@s AND s.Activo=1 AND EXISTS(SELECT 1 FROM dbo.Medidor m JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=m.idServicioSocio WHERE ss.idSocio=s.idSocio AND m.Activo=1 AND ss.Activo=1)",P("@s",(int)a["idSocio"])).Single()>0;
                a["Prestaciones"]=JArray.FromObject(Rows("SELECT * FROM dbo.PrestacionBeneficio WHERE idAdhesion=@id ORDER BY idPrestacion DESC",P("@id",id)));
                a["ConexionDisponible"]=Rows(@"SELECT ss.idServicioSocio FROM dbo.ServicioSocio ss WHERE ss.idSocio=@s AND ss.Activo=1 AND (ss.idServicioSocio=@c OR EXISTS(SELECT 1 FROM dbo.SolicitudConexion sc WHERE sc.idSolicitudServicio=@sol AND sc.idServicioSocio=ss.idServicioSocio))",P("@s",(int)a["idSocio"]),P("@c",(int?)a["idConexionEnergia"]),P("@sol",(int?)a["idSolicitudEnergia"])).Count==1;
            }
            return items;
        }
        [HttpGet,Route("catalogo")]
        public IHttpActionResult Catalogo() { return Read(()=>new {
            servicios=db.Servicio.ToList().Where(s=>new[]{"SEPELIO","ORTOPEDIA"}.Contains(s.Nombre.Trim().ToUpperInvariant())).Select(s=>new {Tipo=s.Nombre.Trim().ToUpperInvariant(),s.TarifaBase}),
            conexiones=db.ServicioSocio.Where(s=>s.idSocio==SocioId && s.Activo==true).ToList().Where(s=>AdhesionesBeneficios.Energia(s.Servicio.Nombre)).Select(s=>new {s.idServicioSocio,Servicio=s.Servicio.Nombre})
        }); }
        [HttpGet,Route("mias")]
        public IHttpActionResult Mias() { return Read(()=>Listar(SocioId)); }
        [HttpGet,Route("gestion")]
        public IHttpActionResult Todas() { return Read(()=>{ Gestion(); return Listar(null); }); }
        [HttpPost,Route("")]
        public IHttpActionResult Crear(JObject data) { return Write(()=>new {ids=AdhesionesBeneficios.Crear(db,SocioId,UserId,null,(int?)data?["idConexionEnergia"],data)}); }
        [HttpPost,Route("{id:int}/corregir")]
        public IHttpActionResult Corregir(int id,JObject data) { return Write(()=>{
            var row=Adhesion(id); Revision(row,data);
            if((string)row["Estado"]!="Observada") throw new ArgumentException("Solo se corrigen adhesiones observadas.");
            if((string)row["Tipo"]=="SEPELIO") {
                db.Database.ExecuteSqlCommand("DELETE FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@id",P("@id",id));
                AdhesionesBeneficios.GuardarFamilia(db,id,SocioId,data["Familiares"] as JArray);
            }
            var direccion=AdhesionesBeneficios.Text(data,"Direccion",200);
            db.Database.ExecuteSqlCommand("UPDATE dbo.AdhesionBeneficio SET Estado='EnRevision',Motivo=NULL,Direccion=@d,Revision=Revision+1 WHERE idAdhesion=@id",P("@id",id),P("@d",direccion));
            AdhesionesBeneficios.History(db,id,"EnRevision",UserId,"Documentación corregida por el socio."); return new {Estado="EnRevision"};
        }); }
        [HttpPost,Route("{id:int}/accion")]
        public IHttpActionResult Accion(int id,JObject data) { return Write(()=>{
            bool personal=User.IsInRole("Socio"); var row=Adhesion(id,!personal); Revision(row,data);
            string accion=AdhesionesBeneficios.Text(data,"Accion",30),actual=(string)row["Estado"],estado;
            string motivo=AdhesionesBeneficios.Text(data,"Motivo",500, false); int? conexion=(int?)row["idConexionEnergia"]; decimal? importe=(decimal?)row["ImporteMensual"];
            if(accion=="Baja") {
                if(!new[]{"EnRevision","Observada","Activa"}.Contains(actual)) throw new ArgumentException("La adhesión ya está cerrada.");
                if(motivo.Length==0) throw new ArgumentException("Indicá el motivo de la baja."); estado="Baja";
            } else {
                if(personal) throw new UnauthorizedAccessException();
                if(actual!="EnRevision") throw new ArgumentException("La adhesión debe estar en revisión.");
                if(accion=="Activar") {
                    var opciones=Rows(@"SELECT ss.idServicioSocio FROM dbo.ServicioSocio ss JOIN dbo.Servicio se ON se.idServicio=ss.idServicio WHERE ss.idSocio=@s AND ss.Activo=1 AND (ss.idServicioSocio=@c OR EXISTS(SELECT 1 FROM dbo.SolicitudConexion sc WHERE sc.idSolicitudServicio=@sol AND sc.idServicioSocio=ss.idServicioSocio))",P("@s",(int)row["idSocio"]),P("@c",conexion),P("@sol",(int?)row["idSolicitudEnergia"]));
                    int titular=(int)row["idSocio"];
                    if(!db.Socio.Any(s=>s.idSocio==titular && s.Activo==true) || !db.Medidor.Any(m=>m.Activo && m.ServicioSocio.idSocio==titular && m.ServicioSocio.Activo==true)) throw new ArgumentException("La cobertura requiere socio activo con un medidor registrado a su nombre.");
                    if(opciones.Count!=1) throw new ArgumentException("Primero debe quedar activa la conexión eléctrica asociada.");
                    conexion=(int)opciones[0]["idServicioSocio"];
                    importe=db.Servicio.Find((int)row["idServicio"]).TarifaBase;
                    if(importe<=0 || importe!=decimal.Round(importe.Value,2)) throw new ArgumentException("Configurá una tarifa mensual positiva con dos decimales.");
                    estado="Activa";
                } else if(accion=="Observar" || accion=="Rechazar") {
                    if(motivo.Length==0) throw new ArgumentException("Indicá el motivo para el socio."); estado=accion=="Observar"?"Observada":"Rechazada";
                } else throw new ArgumentException("Acción inválida.");
            }
            db.Database.ExecuteSqlCommand(@"UPDATE dbo.AdhesionBeneficio SET Estado=@e,Motivo=@m,Revision=Revision+1,idUsuarioDecision=@u,idConexionEnergia=@c,ImporteMensual=@i,Vigente=@v,
                FechaActivacionUtc=CASE WHEN @e='Activa' THEN SYSUTCDATETIME() ELSE FechaActivacionUtc END,
                FechaBajaUtc=CASE WHEN @e='Baja' THEN SYSUTCDATETIME() ELSE FechaBajaUtc END WHERE idAdhesion=@id",P("@e",estado),P("@m",motivo),P("@u",UserId),P("@c",conexion),P("@i",importe),P("@v",!new[]{"Rechazada","Baja"}.Contains(estado)),P("@id",id));
            AdhesionesBeneficios.History(db,id,estado,UserId,motivo); return new {Estado=estado};
        }); }
        [HttpGet,Route("beneficiarios/{id:int}/dni")]
        public IHttpActionResult Dni(int id)
        {
            if(!Ready()) return Content(HttpStatusCode.ServiceUnavailable,"Falta instalar el módulo de adhesiones.");
            var row=Rows("SELECT * FROM dbo.AdhesionBeneficiario WHERE idBeneficiario=@id",P("@id",id)).SingleOrDefault();
            if(row==null) return NotFound();
            try { Adhesion((int)row["idAdhesion"],!User.IsInRole("Socio")); } catch(UnauthorizedAccessException) { return StatusCode(HttpStatusCode.Forbidden); }
            var response=new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent((byte[])row["Contenido"].ToObject<byte[]>()) };
            response.Content.Headers.ContentType=new MediaTypeHeaderValue((string)row["Mime"]);
            response.Content.Headers.ContentDisposition=new ContentDispositionHeaderValue("attachment") {FileName="dni-"+id+PathSafe((string)row["NombreArchivo"])};
            response.Headers.CacheControl=new CacheControlHeaderValue {NoStore=true}; return ResponseMessage(response);
        }
        private string PathSafe(string name) { return System.IO.Path.GetExtension(name); }
        [HttpPost,Route("{id:int}/prestaciones")]
        public IHttpActionResult Prestacion(int id,JObject data) { return Write(()=>{
            var row=Adhesion(id); if((string)row["Estado"]!="Activa") throw new ArgumentException("Necesitás una adhesión activa para pedir una prestación.");
            int titular=(int)row["idSocio"];
            if(!db.Socio.Any(s=>s.idSocio==titular && s.Activo==true) || !db.Medidor.Any(m=>m.Activo && m.ServicioSocio.idSocio==titular && m.ServicioSocio.Activo==true)) throw new ArgumentException("La cobertura requiere socio activo con medidor a su nombre.");
            int? beneficiario=(int?)data?["idBeneficiario"];
            if((string)row["Tipo"]=="SEPELIO" && !Rows("SELECT idBeneficiario FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@id AND idBeneficiario=@b",P("@id",id),P("@b",beneficiario)).Any()) throw new ArgumentException("Elegí una persona incluida en la cobertura.");
            if((string)row["Tipo"]=="ORTOPEDIA") beneficiario=null;
            int prestacion=db.Database.SqlQuery<int>("INSERT dbo.PrestacionBeneficio(idAdhesion,idBeneficiario,Detalle) VALUES(@id,@b,@d); SELECT CAST(SCOPE_IDENTITY() AS int)",P("@id",id),P("@b",beneficiario),P("@d",AdhesionesBeneficios.Text(data,"Detalle",1500))).Single();
            db.Database.ExecuteSqlCommand("INSERT dbo.PrestacionHistorial(idPrestacion,Estado,idUsuarioSistema) VALUES(@id,'Recibida',@u)",P("@id",prestacion),P("@u",UserId));
            return new {idPrestacion=prestacion};
        }); }
        [HttpPost,Route("prestaciones/{id:int}/accion")]
        public IHttpActionResult ResolverPrestacion(int id,JObject data) { return Write(()=>{
            Gestion(); var row=Rows("SELECT * FROM dbo.PrestacionBeneficio WHERE idPrestacion=@id",P("@id",id)).SingleOrDefault();
            if(row==null) throw new ArgumentException("La prestación no existe."); Revision(row,data);
            string accion=AdhesionesBeneficios.Text(data,"Accion",30),actual=(string)row["Estado"];
            string estado=accion=="Derivar"?"Derivada":accion=="Finalizar"?"Finalizada":accion=="Cancelar"?"Cancelada":"";
            if(!(estado=="Derivada" && actual=="Recibida" || estado=="Finalizada" && actual=="Derivada" || estado=="Cancelada" && new[]{"Recibida","Derivada"}.Contains(actual))) throw new ArgumentException("Transición no permitida.");
            string nota=AdhesionesBeneficios.Text(data,"Resultado",500),referencia=AdhesionesBeneficios.Text(data,"ReferenciaPrestador",200,estado=="Derivada" || estado=="Finalizada");
            db.Database.ExecuteSqlCommand("UPDATE dbo.PrestacionBeneficio SET Estado=@e,Resultado=@r,ReferenciaPrestador=@ref,Revision=Revision+1 WHERE idPrestacion=@id",P("@e",estado),P("@r",nota),P("@ref",referencia),P("@id",id));
            db.Database.ExecuteSqlCommand("INSERT dbo.PrestacionHistorial(idPrestacion,Estado,Motivo,idUsuarioSistema) VALUES(@id,@e,@m,@u)",P("@id",id),P("@e",estado),P("@m",nota),P("@u",UserId)); return new {Estado=estado};
        }); }
    }
}
