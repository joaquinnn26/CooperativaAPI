using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Claims;
using Newtonsoft.Json;

namespace CooperativaAPI.Controllers {
 public class AreaPerfil {
  public string Area {get;set;} public string Nombre {get;set;} public bool Encargado {get;set;}
  public List<string> Funciones {get;set;} public int? idEncargado {get;set;} public string EncargadoNombre {get;set;}
 }
 public static class LaboralAccess {
  public static SqlParameter P(string key,object value){return OperacionesMedidor.P(key,value);}
  public static int Usuario(ClaimsPrincipal user){int id;return int.TryParse(user?.FindFirst("idUsuarioSistema")?.Value,out id)?id:0;}
  public static int? Empleado(CooperativaEntities db,ClaimsPrincipal user) {
   int uid=Usuario(user);
   return db.UsuarioSistema.Where(u=>u.idUsuarioSistema==uid && u.Rol.NombreRol!="Socio" && u.Empleado.Activo==true).Select(u=>u.idEmpleado).SingleOrDefault();
  }
  public static bool Miembro(CooperativaEntities db,int? empleado,string area) {
   return empleado.HasValue && db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EmpleadoArea a JOIN dbo.Empleado e ON e.idEmpleado=a.idEmpleado WHERE a.idEmpleado=@e AND a.Area=@a AND e.Activo=1 AND a.DesdeUtc<=SYSUTCDATETIME() AND (a.HastaUtc IS NULL OR a.HastaUtc>SYSUTCDATETIME())",P("@e",empleado),P("@a",area)).Any();
  }
  public static bool Tiene(CooperativaEntities db,int? empleado,string area,string funcion) {
   return Miembro(db,empleado,area) && db.Database.SqlQuery<int>(@"SELECT 1 FROM dbo.EmpleadoFuncion f JOIN dbo.EmpleadoArea a ON a.idEmpleadoArea=f.idEmpleadoArea
    WHERE a.idEmpleado=@e AND a.Area=@a AND a.HastaUtc IS NULL AND f.Funcion=@f AND f.DesdeUtc<=SYSUTCDATETIME() AND (f.HastaUtc IS NULL OR f.HastaUtc>SYSUTCDATETIME())",P("@e",empleado),P("@a",area),P("@f",funcion)).Any();
  }
  public static bool Encargado(CooperativaEntities db,int? empleado,string area) {
   return Miembro(db,empleado,area) && db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EncargadoArea WHERE idEmpleado=@e AND Area=@a AND DesdeUtc<=SYSUTCDATETIME() AND (HastaUtc IS NULL OR HastaUtc>SYSUTCDATETIME())",P("@e",empleado),P("@a",area)).Any();
  }
  public static bool Gerencia(CooperativaEntities db,ClaimsPrincipal user){return Tiene(db,Empleado(db,user),"GERENCIA","CONSULTA_GENERAL");}
  public static bool Atencion(CooperativaEntities db,ClaimsPrincipal user){var e=Empleado(db,user);return Tiene(db,e,"ADMINISTRACION","ATENCION") || Encargado(db,e,"ADMINISTRACION");}
  public static string AreaServicio(CooperativaEntities db,int servicio){return db.Database.SqlQuery<string>("SELECT Area FROM dbo.AreaServicio WHERE idServicio=@id",P("@id",servicio)).SingleOrDefault();}
  public static string AreaMedidor(CooperativaEntities db,int medidor){return db.Database.SqlQuery<string>(@"SELECT CASE WHEN t.Nombre='Agua' THEN 'AGUA' WHEN t.Nombre IN ('Electrico','Eléctrico') THEN 'ENERGIA' END FROM dbo.Medidor m JOIN dbo.TipoMedidor t ON t.idTipoMedidor=m.idTipoMedidor WHERE m.idMedidor=@id",P("@id",medidor)).SingleOrDefault();}
  public static List<AreaPerfil> Perfil(CooperativaEntities db,ClaimsPrincipal user) {
   var empleado=Empleado(db,user);if(!empleado.HasValue)return new List<AreaPerfil>();
   var areas=db.Database.SqlQuery<AreaPerfil>(@"SELECT a.Area,r.Nombre,CAST(0 AS bit) Encargado,c.idEmpleado idEncargado,CONCAT(p.Nombre,' ',p.Apellido) EncargadoNombre
    FROM dbo.EmpleadoArea a JOIN dbo.AreaLaboral r ON r.Codigo=a.Area
    LEFT JOIN dbo.EncargadoArea c ON c.Area=a.Area AND c.HastaUtc IS NULL
    LEFT JOIN dbo.Empleado e ON e.idEmpleado=c.idEmpleado AND e.Activo=1 LEFT JOIN dbo.Persona p ON p.idPersona=e.idPersona
    WHERE a.idEmpleado=@id AND a.DesdeUtc<=SYSUTCDATETIME() AND a.HastaUtc IS NULL",P("@id",empleado)).ToList();
   foreach(var a in areas){a.Encargado=Encargado(db,empleado,a.Area);a.Funciones=db.Database.SqlQuery<string>(@"SELECT f.Funcion FROM dbo.EmpleadoFuncion f JOIN dbo.EmpleadoArea ea ON ea.idEmpleadoArea=f.idEmpleadoArea
     WHERE ea.idEmpleado=@id AND ea.Area=@area AND ea.HastaUtc IS NULL AND f.HastaUtc IS NULL AND f.DesdeUtc<=SYSUTCDATETIME()",P("@id",empleado),P("@area",a.Area)).ToList();}
   return areas;
  }
  public static void Auditar(CooperativaEntities db,ClaimsPrincipal user,string area,int? tarea,string accion,string anterior,string nuevo,string motivo,object datos=null) {
   db.Database.ExecuteSqlCommand(@"INSERT dbo.AuditoriaLaboral(idTarea,Area,idUsuarioSistema,idEmpleado,Accion,EstadoAnterior,EstadoNuevo,Motivo,Datos)
    VALUES(@t,@a,@u,@e,@accion,@antes,@despues,@motivo,@datos)",P("@t",tarea),P("@a",area),P("@u",Usuario(user)),P("@e",Empleado(db,user)),P("@accion",accion),P("@antes",anterior),P("@despues",nuevo),P("@motivo",motivo),P("@datos",datos==null?null:JsonConvert.SerializeObject(datos)));
  }
  public static bool LecturistaRuta(CooperativaEntities db,ClaimsPrincipal user,int ruta,bool encargado=false) {
   var e=Empleado(db,user);
   var datos=db.RutaMedicion.Find(ruta);if(datos==null)return false;
   var areas=db.Database.SqlQuery<string>(@"SELECT DISTINCT COALESCE(s.Area,'SIN_AREA') FROM dbo.RutaMedicionDetalle d LEFT JOIN dbo.ContextoRutaMedidor c ON c.idRutaMedicionDetalle=d.idRutaMedicionDetalle LEFT JOIN dbo.Servicio se ON se.Nombre=c.ServicioNombre LEFT JOIN dbo.AreaServicio s ON s.idServicio=se.idServicio WHERE d.idRutaMedicion=@id",P("@id",ruta)).ToList();
   return areas.Any() && areas.All(a=> encargado ? Encargado(db,e,a) : datos.idEmpleado==e && Tiene(db,e,a,"LECTURISTA"));
  }
  // Los endpoints anteriores no deben permitir saltarse la revisión por área.
  public static bool LegacyGate(CooperativaEntities db,ClaimsPrincipal user,string controller,string action,string method) {
   if(controller=="Factura" && method!="GET")return false;
   if(controller=="Medicion" && method!="GET")return false;
   if(controller=="Medicion" && user.IsInRole("Empleado"))return false;
   if(controller=="Medidor" && user.IsInRole("Empleado"))return false;
   if(controller=="SolicitudServicio" && new[]{"Aprobar","Rechazar","MarcarInstalada","RechazarInstalacion"}.Contains(action))return false;
   if(controller=="SolicitudServicio" && action=="GetAll" && user.IsInRole("Empleado"))return Atencion(db,user);
   if(controller=="Reclamo" && new[]{"Editar","Eliminar"}.Contains(action))return false;
   if(controller=="Reclamo" && action=="GetReclamosPorSocio" && user.IsInRole("Empleado"))return Atencion(db,user);
   if(controller=="InventarioMedidores" && action=="Reemplazar")return false;
   if(controller=="Medidor" && method!="GET")return false;
   if(controller=="RutasMedicion" && new[]{"Crear","Cancelar","ObtenerTodas"}.Contains(action))return Perfil(db,user).Any(a=>a.Encargado && (a.Area=="AGUA" || a.Area=="ENERGIA"));
   if(controller=="Turnos" && new[]{"ActualizarTurnoAdmin","EliminarTurnoAdmin"}.Contains(action))return Encargado(db,Empleado(db,user),"ADMINISTRACION");
   if(controller=="Turnos" && action=="CancelarTurno" && !user.IsInRole("Socio"))return Encargado(db,Empleado(db,user),"ADMINISTRACION");
   if(controller=="Turnos" && new[]{"ObtenerTurnosAdmin","ObtenerTurnosPorSocio","ObtenerDisponibilidad"}.Contains(action) && !user.IsInRole("Socio"))return Atencion(db,user);
   return true;
  }
 }
}
