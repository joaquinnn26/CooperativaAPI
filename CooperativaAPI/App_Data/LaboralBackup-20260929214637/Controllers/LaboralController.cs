using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers {
 [RoutePrefix("api/laboral"),JwtAuthorize(Roles="Administrador,Empleado,Socio")]
 public partial class LaboralController:ApiController {
  private readonly CooperativaEntities db=new CooperativaEntities();
  private ClaimsPrincipal Principal {get{return (ClaimsPrincipal)User;}}
  private int? Empleado {get{return LaboralAccess.Empleado(db,Principal);}}
  private bool Admin {get{return User.IsInRole("Administrador");}}
  private static SqlParameter P(string k,object v){return LaboralAccess.P(k,v);}
  private static string Texto(JObject data,string key,int max=1000,bool required=false){var v=OperacionesMedidor.Texto((string)data?[key]);if(v.Length>max || (required && v.Length==0))throw new ArgumentException("Completá "+key+" (hasta "+max+" caracteres).");return v;}
  private void Exigir(bool condition){if(!condition)throw new UnauthorizedAccessException("No tenés permiso para esta operación o área.");}
  private bool Jefe(string area){return LaboralAccess.Encargado(db,Empleado,area);}
  private bool Funcion(string area,string funcion){return LaboralAccess.Tiene(db,Empleado,area,funcion);}
  private List<JObject> Rows(string sql,params SqlParameter[] parameters) {
   var connection=(SqlConnection)db.Database.Connection;if(connection.State!=ConnectionState.Open)connection.Open();
   using(var cmd=connection.CreateCommand()){
    if(db.Database.CurrentTransaction!=null)cmd.Transaction=(SqlTransaction)db.Database.CurrentTransaction.UnderlyingTransaction;
    cmd.CommandText=sql;cmd.Parameters.AddRange(parameters);var list=new List<JObject>();
    using(var reader=cmd.ExecuteReader()){while(reader.Read()){var row=new JObject();for(int i=0;i<reader.FieldCount;i++){var value=reader.GetValue(i);if(value is DateTime)value=DateTime.SpecifyKind((DateTime)value,DateTimeKind.Utc);row[reader.GetName(i)]=value==DBNull.Value?JValue.CreateNull():JToken.FromObject(value);}list.Add(row);}}
    return list;
   }
  }
  private JObject Row(string sql,params SqlParameter[] ps){var result=Rows(sql,ps).SingleOrDefault();if(result==null)throw new ArgumentException("El registro no existe.");return result;}
  private IHttpActionResult Read(Func<object> action){try{return Ok(action());}catch(UnauthorizedAccessException){return StatusCode(HttpStatusCode.Forbidden);}catch(ArgumentException ex){return BadRequest(ex.Message);}}
  private IHttpActionResult Write(Func<object> action){
   if(!ModelState.IsValid)return BadRequest("Datos inválidos.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)){
    try{var result=action();db.SaveChanges();tx.Commit();return Ok(result);}
    catch(UnauthorizedAccessException){return StatusCode(HttpStatusCode.Forbidden);}
    catch(DBConcurrencyException ex){return Content(HttpStatusCode.Conflict,ex.Message);}
    catch(ArgumentException ex){return BadRequest(ex.Message);}
    catch(SqlException ex) when(new[]{2601,2627,1205,547,51001}.Contains(ex.Number)){return Content(HttpStatusCode.Conflict,"La operación entra en conflicto con los datos actuales. Actualizá la vista.");}
   }
  }
  private void Version(JObject row,JObject data){if((string)row["Version"]!=(string)data?["Version"])throw new DBConcurrencyException("El registro cambió. Actualizá antes de guardar.");}
  private void Audit(string area,int? task,string action,string before,string after,string reason,object data=null){LaboralAccess.Auditar(db,Principal,area,task,action,before,after,reason,data);}

  [HttpGet,Route("perfil")]
  public IHttpActionResult Perfil(){return Read(()=>{Exigir(!User.IsInRole("Socio"));return new {idEmpleado=Empleado,areas=LaboralAccess.Perfil(db,Principal),configurar=Admin};});}

  [HttpGet,Route("configuracion")]
  public IHttpActionResult Configuracion(){return Read(()=>{Exigir(Admin);return new {
   areas=Rows("SELECT * FROM dbo.AreaLaboral ORDER BY Nombre"),
   funciones=Rows("SELECT a.Area,f.* FROM dbo.FuncionArea a JOIN dbo.FuncionLaboral f ON f.Codigo=a.Funcion"),
   empleados=Rows("SELECT e.idEmpleado,e.idPersona,e.Activo,c.NombreCargo Cargo,CONCAT(p.Nombre,' ',p.Apellido) Nombre FROM dbo.Empleado e JOIN dbo.Persona p ON p.idPersona=e.idPersona JOIN dbo.Cargo c ON c.idCargo=e.idCargo ORDER BY p.Apellido,p.Nombre"),
   miembros=Rows("SELECT * FROM dbo.EmpleadoArea ORDER BY idEmpleadoArea DESC"),
   permisos=Rows("SELECT * FROM dbo.EmpleadoFuncion ORDER BY idEmpleadoFuncion DESC"),
   encargados=Rows("SELECT * FROM dbo.EncargadoArea ORDER BY idEncargadoArea DESC"),
   servicios=Rows("SELECT s.idServicio,s.Nombre,a.Area FROM dbo.Servicio s LEFT JOIN dbo.AreaServicio a ON a.idServicio=s.idServicio"),
   motivos=Rows("SELECT * FROM dbo.MotivoLaboral ORDER BY Area,Accion,Nombre"),
   tiposDocumento=Rows("SELECT * FROM dbo.TipoDocumentoSocio ORDER BY Nombre"),
   cuentas=Rows("SELECT u.idUsuarioSistema,u.idEmpleado,u.Usuario FROM dbo.UsuarioSistema u JOIN dbo.Rol r ON r.idRol=u.idRol WHERE r.NombreRol='Empleado'")
  };});}

  [HttpPost,Route("configuracion")]
  public IHttpActionResult Configurar(JObject data){return Write(()=>{
   Exigir(Admin);var accion=Texto(data,"Accion",40,true);var area=Texto(data,"Area",30);var motivo=Texto(data,"Motivo",500,true);
   if(accion=="Cuenta"){
    int id=(int?)data["idEmpleado"]??0;Exigir(db.Empleado.Any(e=>e.idEmpleado==id && e.Activo==true));
    string password=(string)data["Password"];
    if(string.IsNullOrWhiteSpace(password) || password.Length<8 || password.Length>100 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c=>!char.IsLetterOrDigit(c)))throw new ArgumentException("Usá una contraseña de 8 a 100 caracteres con mayúscula, minúscula, número y símbolo.");
    int rol=db.Rol.Single(r=>r.NombreRol=="Empleado").idRol;
    if(db.UsuarioSistema.Any(u=>u.idEmpleado==id && u.idRol==rol))throw new ArgumentException("El empleado ya tiene cuenta laboral.");
    db.UsuarioSistema.Add(new UsuarioSistema{Usuario="laboral-"+id,idEmpleado=id,idSocio=null,idRol=rol,Contrasenia=CooperativaAPI.Helpers.PasswordHelpers.Hashear(password)});
    data.Remove("Password");
   } else if(accion=="DocumentoTipo"){
    var nombre=Texto(data,"Nombre",100,true);int? id=(int?)data["idTipoDocumento"];
    if(id.HasValue)db.Database.ExecuteSqlCommand("UPDATE dbo.TipoDocumentoSocio SET Activo=@activo WHERE idTipoDocumento=@id",P("@activo",(bool?)data["Activo"]??false),P("@id",id));
    else db.Database.ExecuteSqlCommand("INSERT dbo.TipoDocumentoSocio(Nombre) VALUES(@nombre)",P("@nombre",nombre));
   } else {
    var areaRow=Row("SELECT * FROM dbo.AreaLaboral WHERE Codigo=@a",P("@a",area));
    if(accion=="Miembro"){
     int id=(int?)data["idEmpleado"]??0;Exigir(db.Empleado.Any(e=>e.idEmpleado==id && e.Activo==true));
     if((bool?)data["Activo"]==false){
      var actual=Row("SELECT * FROM dbo.EmpleadoArea WHERE idEmpleado=@e AND Area=@a AND HastaUtc IS NULL",P("@e",id),P("@a",area));Version(actual,data);
      db.Database.ExecuteSqlCommand("UPDATE dbo.EmpleadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleadoArea=@id; UPDATE dbo.EncargadoArea SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleado=@e AND Area=@a AND HastaUtc IS NULL",P("@id",(int)actual["idEmpleadoArea"]),P("@e",id),P("@a",area));
     } else db.Database.ExecuteSqlCommand("INSERT dbo.EmpleadoArea(idEmpleado,Area,DesdeUtc) VALUES(@e,@a,SYSUTCDATETIME())",P("@e",id),P("@a",area));
    } else if(accion=="Funcion"){
     int id=(int?)data["idEmpleado"]??0;var funcion=Texto(data,"Funcion",30,true);
     Exigir(LaboralAccess.Miembro(db,id,area));
     if(!Rows("SELECT 1 permitido FROM dbo.FuncionArea WHERE Area=@a AND Funcion=@f",P("@a",area),P("@f",funcion)).Any())throw new ArgumentException("La función no corresponde a esa área.");
     var member=Row("SELECT * FROM dbo.EmpleadoArea WHERE idEmpleado=@e AND Area=@a AND HastaUtc IS NULL",P("@e",id),P("@a",area));
     if((bool?)data["Activo"]==false){var actual=Row("SELECT * FROM dbo.EmpleadoFuncion WHERE idEmpleadoArea=@id AND Funcion=@f AND HastaUtc IS NULL",P("@id",(int)member["idEmpleadoArea"]),P("@f",funcion));Version(actual,data);db.Database.ExecuteSqlCommand("UPDATE dbo.EmpleadoFuncion SET HastaUtc=SYSUTCDATETIME() WHERE idEmpleadoFuncion=@id",P("@id",(int)actual["idEmpleadoFuncion"]));}
     else db.Database.ExecuteSqlCommand("INSERT dbo.EmpleadoFuncion(idEmpleadoArea,Funcion,DesdeUtc) VALUES(@id,@f,SYSUTCDATETIME())",P("@id",(int)member["idEmpleadoArea"]),P("@f",funcion));
    } else if(accion=="Encargado"){
     Version(areaRow,data);int? id=(int?)data["idEmpleado"];if(id.HasValue)Exigir(LaboralAccess.Miembro(db,id,area));
     db.Database.ExecuteSqlCommand("UPDATE dbo.EncargadoArea SET HastaUtc=SYSUTCDATETIME() WHERE Area=@a AND HastaUtc IS NULL; UPDATE dbo.AreaLaboral SET Nombre=Nombre WHERE Codigo=@a",P("@a",area));
     if(id.HasValue)db.Database.ExecuteSqlCommand("INSERT dbo.EncargadoArea(Area,idEmpleado,DesdeUtc,Motivo) VALUES(@a,@e,SYSUTCDATETIME(),@m)",P("@a",area),P("@e",id),P("@m",motivo));
    } else if(accion=="Servicio"){
     Exigir(new[]{"AGUA","ENERGIA","INTERNET"}.Contains(area));int id=(int?)data["idServicio"]??0;
     if(Rows("SELECT 1 existe FROM dbo.AreaServicio WHERE idServicio=@id",P("@id",id)).Any())throw new ArgumentException("El servicio ya tiene área. Un cambio de sector requiere revisar sus tareas y rutas existentes.");
     db.Database.ExecuteSqlCommand("INSERT dbo.AreaServicio(idServicio,Area) VALUES(@id,@a)",P("@id",id),P("@a",area));
    } else if(accion=="Motivo"){
     string decision=Texto(data,"Decision",30,true);if(!new[]{"Rechazar","Devolver","CorregirLectura"}.Contains(decision))throw new ArgumentException("Decisión inválida.");
     int? id=(int?)data["idMotivo"];
     if(id.HasValue){var actual=Row("SELECT * FROM dbo.MotivoLaboral WHERE idMotivo=@id AND Area=@a",P("@id",id),P("@a",area));Version(actual,data);db.Database.ExecuteSqlCommand("UPDATE dbo.MotivoLaboral SET Activo=@activo WHERE idMotivo=@id",P("@activo",(bool?)data["Activo"]??false),P("@id",id));}
     else db.Database.ExecuteSqlCommand("INSERT dbo.MotivoLaboral(Area,Accion,Nombre) VALUES(@a,@accion,@nombre)",P("@a",area),P("@accion",decision),P("@nombre",Texto(data,"Nombre",150,true)));
    } else throw new ArgumentException("Acción de configuración inválida.");
   }
   Audit(string.IsNullOrEmpty(area)?null:area,null,"Configurar"+accion,null,null,motivo,data);
   return new{mensaje="Configuración guardada con historial."};
  });}

  [HttpGet,Route("lecturistas")]
  public IHttpActionResult Lecturistas(string area){return Read(()=>{
   Exigir(Jefe(area) && new[]{"AGUA","ENERGIA"}.Contains(area));
   return Rows(@"SELECT e.idEmpleado,CONCAT(p.Nombre,' ',p.Apellido) Nombre FROM dbo.Empleado e JOIN dbo.Persona p ON p.idPersona=e.idPersona WHERE e.Activo=1").Where(e=>LaboralAccess.Tiene(db,(int)e["idEmpleado"],area,"LECTURISTA")).ToList();
  });}
  [HttpGet,Route("equipo")]
  public IHttpActionResult Equipo(string area){return Read(()=>{
   Exigir(Jefe(area) || LaboralAccess.Gerencia(db,Principal));
    return Rows(@"SELECT e.idEmpleado,CONCAT(p.Nombre,' ',p.Apellido) Nombre,c.NombreCargo Cargo,a.DesdeUtc,
      STUFF((SELECT ', '+f.Funcion FROM dbo.EmpleadoFuncion f WHERE f.idEmpleadoArea=a.idEmpleadoArea AND f.HastaUtc IS NULL AND f.DesdeUtc<=SYSUTCDATETIME() FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,'') Funciones,
    (SELECT COUNT(*) FROM dbo.TareaLaboral t WHERE t.Area=a.Area AND t.idEmpleadoAsignado=e.idEmpleado AND t.Estado NOT IN ('Cerrada','Rechazada')) Pendientes
    FROM dbo.EmpleadoArea a JOIN dbo.Empleado e ON e.idEmpleado=a.idEmpleado JOIN dbo.Persona p ON p.idPersona=e.idPersona JOIN dbo.Cargo c ON c.idCargo=e.idCargo
    WHERE a.Area=@a AND a.HastaUtc IS NULL AND e.Activo=1",P("@a",area));
  });}

  [HttpGet,Route("motivos")]
  public IHttpActionResult Motivos(string area){return Read(()=>{Exigir(LaboralAccess.Miembro(db,Empleado,area));return Rows("SELECT * FROM dbo.MotivoLaboral WHERE Area=@a AND Activo=1 ORDER BY Accion,Nombre",P("@a",area));});}

  [HttpGet,Route("actividad")]
  public IHttpActionResult Actividad(string area=null,int pagina=1){return Read(()=>{
   if(pagina<1)throw new ArgumentException("Página inválida.");Exigir(!User.IsInRole("Socio"));
   bool all=LaboralAccess.Gerencia(db,Principal) || Admin;
   return Rows(@"SELECT h.idAuditoria,h.idTarea,h.Area,h.Accion,h.EstadoAnterior,h.EstadoNuevo,h.Motivo,h.FechaUtc,u.Usuario Responsable
    FROM dbo.AuditoriaLaboral h LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=h.idUsuarioSistema
    WHERE (@area IS NULL OR h.Area=@area) AND (@all=1 OR h.idEmpleado=@e OR EXISTS(SELECT 1 FROM dbo.EncargadoArea c JOIN dbo.EmpleadoArea a ON a.idEmpleado=c.idEmpleado AND a.Area=c.Area AND a.HastaUtc IS NULL WHERE c.Area=h.Area AND c.idEmpleado=@e AND c.HastaUtc IS NULL))
    ORDER BY h.idAuditoria DESC OFFSET @offset ROWS FETCH NEXT 50 ROWS ONLY",P("@area",area),P("@all",all),P("@e",Empleado),P("@offset",(pagina-1)*50));
  });}
  protected override void Dispose(bool disposing){if(disposing)db.Dispose();base.Dispose(disposing);}
 }
}
