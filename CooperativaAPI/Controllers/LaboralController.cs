using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
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
     else throw new ArgumentException("Los motivos se eligen del catálogo estadístico predefinido; no se permiten categorías de texto libre.");
    } else throw new ArgumentException("Acción de configuración inválida.");
   }
   Audit(string.IsNullOrEmpty(area)?null:area,null,"Configurar"+accion,null,null,motivo,data);
   return new{mensaje="Configuración guardada con historial."};
  });}

  [HttpPost,Route("accesos/gerencia-administracion")]
  public IHttpActionResult CrearAccesosGerenciaAdministracion(JObject data){
   if(!Admin)return StatusCode(HttpStatusCode.Forbidden);
   if(data==null)return BadRequest("Completá los datos de ambas personas.");
   using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)){
    try{
     var gerente=data["Gerente"] as JObject;var administrador=data["Administrador"] as JObject;
     int personaGerente=CrearPersonaYEmpleado(gerente,"Gerente");
     int personaAdministrador=CrearPersonaYEmpleado(administrador,"Administrador");
     if(personaGerente==personaAdministrador)throw new ArgumentException("Gerencia y Administración deben corresponder a personas distintas.");
     int empleadoGerente=db.Database.SqlQuery<int>("SELECT idEmpleado FROM dbo.Empleado WHERE idPersona=@p",P("@p",personaGerente)).Single();
     int empleadoAdministrador=db.Database.SqlQuery<int>("SELECT idEmpleado FROM dbo.Empleado WHERE idPersona=@p",P("@p",personaAdministrador)).Single();
     string passwordGerente=CrearPasswordTemporal();string passwordAdministrador=CrearPasswordTemporal();
     int rolEmpleado=db.Database.SqlQuery<int>("SELECT idRol FROM dbo.Rol WHERE NombreRol='Empleado'").Single();
     int rolAdministrador=db.Database.SqlQuery<int>("SELECT idRol FROM dbo.Rol WHERE NombreRol='Administrador'").Single();
     string emailGerente=db.Database.SqlQuery<string>("SELECT Email FROM dbo.Persona WHERE idPersona=@p",P("@p",personaGerente)).Single();
     string emailAdministrador=db.Database.SqlQuery<string>("SELECT Email FROM dbo.Persona WHERE idPersona=@p",P("@p",personaAdministrador)).Single();
     InsertarCuentaLaboral(empleadoGerente,rolEmpleado,passwordGerente);
     InsertarCuentaAdministrador(empleadoAdministrador,rolAdministrador,emailAdministrador,passwordAdministrador);
     db.Database.ExecuteSqlCommand("INSERT dbo.EmpleadoArea(idEmpleado,Area,DesdeUtc) VALUES(@e,'GERENCIA',SYSUTCDATETIME()); INSERT dbo.EmpleadoFuncion(idEmpleadoArea,Funcion,DesdeUtc) SELECT SCOPE_IDENTITY(),'CONSULTA_GENERAL',SYSUTCDATETIME()",P("@e",empleadoGerente));
     AuditarAltaAcceso(empleadoGerente,"AltaCuentaGerencia");AuditarAltaAcceso(empleadoAdministrador,"AltaCuentaAdministrador");
     tx.Commit();
     return Ok(new{mensaje="Se crearon los accesos. Guardá las contraseñas iniciales: no volverán a mostrarse.",gerente=new{usuario=emailGerente,contrasenaInicial=passwordGerente,rol="Empleado · Gerencia"},administrador=new{usuario=emailAdministrador,contrasenaInicial=passwordAdministrador,rol="Administrador del sistema"}});
    }catch(ArgumentException ex){tx.Rollback();return BadRequest(ex.Message);}catch(InvalidOperationException){tx.Rollback();return BadRequest("No se encontró un rol o cargo requerido para crear los accesos.");}catch(SqlException ex) when(new[]{2601,2627,547}.Contains(ex.Number)){tx.Rollback();return Content(HttpStatusCode.Conflict,"El documento, correo o acceso ya está registrado. No se creó ningún usuario.");}catch(Exception ex){tx.Rollback();return BadRequest("No se pudieron crear los accesos: "+ex.Message);}
   }
  }

  [HttpGet,Route("accesos/personas-disponibles")]
  public IHttpActionResult PersonasDisponiblesParaAcceso(){
   if(!Admin)return StatusCode(HttpStatusCode.Forbidden);
   try{return Ok(Rows(@"SELECT p.idPersona,p.Nombre,p.Apellido,p.Documento,p.Email FROM dbo.Persona p
    WHERE NULLIF(LTRIM(RTRIM(p.Email)),'') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Empleado e WHERE e.idPersona=p.idPersona)
    ORDER BY p.Apellido,p.Nombre"));}catch{return BadRequest("No se pudieron cargar las personas registradas.");}
  }

  private int CrearPersonaYEmpleado(JObject person,string cargoNombre){
   if(person==null)throw new ArgumentException("Faltan los datos de una de las personas.");
   int? existing=(int?)person["idPersona"];
   if(existing.HasValue){
    if(db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.Persona WHERE idPersona=@p AND NULLIF(LTRIM(RTRIM(Email)),'') IS NOT NULL",P("@p",existing.Value)).Single()!=1)throw new ArgumentException("La persona seleccionada no existe o no tiene correo registrado.");
    if(db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.Empleado WHERE idPersona=@p",P("@p",existing.Value)).Single()>0)throw new ArgumentException("La persona seleccionada ya figura como empleado.");
    int existingCargo=db.Database.SqlQuery<int>("SELECT idCargo FROM dbo.Cargo WHERE NombreCargo=@n",P("@n",cargoNombre)).Single();
    db.Database.ExecuteSqlCommand("INSERT dbo.Empleado(idPersona,FechaDelInicio,Activo,idCargo) VALUES(@p,CAST(GETDATE() AS date),1,@cargo)",P("@p",existing.Value),P("@cargo",existingCargo));
    return existing.Value;
   }
   string nombre=Texto(person,"Nombre",100,true),apellido=Texto(person,"Apellido",100,true),documento=Texto(person,"Documento",30,true),email=Texto(person,"Email",150,true).ToLowerInvariant();
   string telefono=Texto(person,"Telefono",40),calle=Texto(person,"Calle",120),numero=Texto(person,"Numero",20);
   if(!System.Text.RegularExpressions.Regex.IsMatch(email,@"^[^\s@]+@[^\s@]+\.[^\s@]+$"))throw new ArgumentException("Ingresá un correo válido para cada persona.");
   if(db.Database.SqlQuery<int>("SELECT COUNT(1) FROM dbo.Persona WHERE Documento=@d OR Email=@e",P("@d",documento),P("@e",email)).Single()>0)throw new ArgumentException("El documento o correo ya pertenece a una persona registrada. Revisá los datos antes de volver a intentar.");
   int cargo=db.Database.SqlQuery<int>("SELECT idCargo FROM dbo.Cargo WHERE NombreCargo=@n",P("@n",cargoNombre)).Single();
   var connection=(SqlConnection)db.Database.Connection;
   using(var command=connection.CreateCommand()){
    command.Transaction=(SqlTransaction)db.Database.CurrentTransaction.UnderlyingTransaction;
    command.CommandText=@"INSERT dbo.Persona(Nombre,Apellido,Documento,Telefono,Email,Calle,Numero) OUTPUT INSERTED.idPersona VALUES(@n,@a,@d,@t,@e,@c,@num)";
    command.Parameters.AddRange(new[]{P("@n",nombre),P("@a",apellido),P("@d",documento),P("@t",(object)telefono??DBNull.Value),P("@e",email),P("@c",(object)calle??DBNull.Value),P("@num",(object)numero??DBNull.Value)});
    int persona=(int)command.ExecuteScalar();
    command.Parameters.Clear();command.CommandText="INSERT dbo.Empleado(idPersona,FechaDelInicio,Activo,idCargo) OUTPUT INSERTED.idEmpleado VALUES(@p,CAST(GETDATE() AS date),1,@cargo)";command.Parameters.AddRange(new[]{P("@p",persona),P("@cargo",cargo)});
    command.ExecuteNonQuery();return persona;
   }
  }
  private void InsertarCuentaLaboral(int empleado,int rol,string password){
   db.Database.ExecuteSqlCommand("INSERT dbo.UsuarioSistema(Usuario,Contrasenia,idRol,idSocio,idEmpleado) VALUES(@u,@p,@r,NULL,@e)",P("@u","laboral-"+empleado),P("@p",CooperativaAPI.Helpers.PasswordHelpers.Hashear(password)),P("@r",rol),P("@e",empleado));
  }
  private void InsertarCuentaAdministrador(int empleado,int rol,string usuario,string password){
   db.Database.ExecuteSqlCommand("INSERT dbo.UsuarioSistema(Usuario,Contrasenia,idRol,idSocio,idEmpleado) VALUES(@u,@p,@r,NULL,@e)",P("@u",usuario),P("@p",CooperativaAPI.Helpers.PasswordHelpers.Hashear(password)),P("@r",rol),P("@e",empleado));
  }
  private void AuditarAltaAcceso(int empleado,string accion){
   db.Database.ExecuteSqlCommand("INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoNuevo,Motivo,Datos) VALUES('GERENCIA',@u,@e,@a,'Activo','Alta de acceso desde Administración',N'{\"Origen\":\"Administracion\"}')",P("@u",LaboralAccess.Usuario(Principal)),P("@e",empleado),P("@a",accion));
  }
  private static string CrearPasswordTemporal(){
   const string mayus="ABCDEFGHJKLMNPQRSTUVWXYZ",minus="abcdefghijkmnopqrstuvwxyz",numeros="23456789",simbolos="!#$%&*+-=?@";
   var chars=new List<char>{Elegir(mayus),Elegir(minus),Elegir(numeros),Elegir(simbolos)};string todos=mayus+minus+numeros+simbolos;
   while(chars.Count<16)chars.Add(Elegir(todos));
   for(int i=chars.Count-1;i>0;i--){int j=NumeroSeguro(i+1);char c=chars[i];chars[i]=chars[j];chars[j]=c;}
   return new string(chars.ToArray());
  }
  private static char Elegir(string chars){return chars[NumeroSeguro(chars.Length)];}
  private static int NumeroSeguro(int max){var b=new byte[4];using(var rng=RandomNumberGenerator.Create()){rng.GetBytes(b);}return (int)(BitConverter.ToUInt32(b,0)%(uint)max);}

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
