using System;
using System.Data;
using System.Linq;
using System.Web.Http;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers {
 public partial class LaboralController {
  private JObject Task(int id){return Row("SELECT * FROM dbo.TareaLaboral WHERE idTarea=@id",P("@id",id));}
  private bool PuedeTrabajar(JObject task,int? empleado=null){
   var e=empleado??Empleado;string area=(string)task["Area"];
   return LaboralAccess.Tiene(db,e,area,(string)task["Categoria"]=="Ruta"?"LECTURISTA":area=="ADMINISTRACION"?"ATENCION":"OPERARIO");
  }
  private bool PuedeVer(JObject task){return LaboralAccess.Gerencia(db,Principal) || Jefe((string)task["Area"]) || ((int?)task["idEmpleadoAsignado"]==Empleado && Empleado.HasValue && PuedeTrabajar(task));}
  private const string TaskContext=@"SELECT t.*,CONCAT(p.Nombre,' ',p.Apellido) Socio,a.Nombre AreaNombre,CONCAT(ep.Nombre,' ',ep.Apellido) Asignado,
   se.Nombre Servicio,u.Direccion,u.Ruta,u.Folio FROM dbo.TareaLaboral t JOIN dbo.AreaLaboral a ON a.Codigo=t.Area
   LEFT JOIN dbo.Socio s ON s.idSocio=t.idSocio LEFT JOIN dbo.Persona p ON p.idPersona=s.idPersona
   LEFT JOIN dbo.Empleado e ON e.idEmpleado=t.idEmpleadoAsignado LEFT JOIN dbo.Persona ep ON ep.idPersona=e.idPersona
   LEFT JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=t.idServicioSocio LEFT JOIN dbo.Servicio se ON se.idServicio=ss.idServicio
   LEFT JOIN dbo.UbicacionServicio u ON u.idServicioSocio=ss.idServicioSocio";

  [HttpGet,Route("tareas")]
  public IHttpActionResult Tareas(string area=null,string estado=null,int pagina=1){return Read(()=>{
   Exigir(Empleado.HasValue);if(pagina<1 || pagina>100000)throw new ArgumentException("Página inválida.");
   var scope=@" WHERE (@a IS NULL OR t.Area=@a) AND (@estado IS NULL OR t.Estado=@estado) AND (@gerencia=1 OR
    EXISTS(SELECT 1 FROM dbo.EmpleadoArea ea WHERE ea.idEmpleado=@e AND ea.Area=t.Area AND ea.HastaUtc IS NULL AND ea.DesdeUtc<=SYSUTCDATETIME() AND
     ((t.idEmpleadoAsignado=@e AND EXISTS(SELECT 1 FROM dbo.EmpleadoFuncion f WHERE f.idEmpleadoArea=ea.idEmpleadoArea AND f.HastaUtc IS NULL AND f.DesdeUtc<=SYSUTCDATETIME() AND f.Funcion=CASE WHEN t.Categoria='Ruta' THEN 'LECTURISTA' WHEN t.Area='ADMINISTRACION' THEN 'ATENCION' ELSE 'OPERARIO' END)) OR EXISTS(SELECT 1 FROM dbo.EncargadoArea c WHERE c.idEmpleado=@e AND c.Area=t.Area AND c.HastaUtc IS NULL))))";
   Func<System.Data.SqlClient.SqlParameter[]> args=()=>new[]{P("@a",area),P("@estado",estado),P("@gerencia",LaboralAccess.Gerencia(db,Principal)),P("@e",Empleado),P("@offset",(pagina-1)*25)};
   return new{items=Rows(TaskContext+scope+" ORDER BY t.FechaActualizacionUtc DESC,t.idTarea DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",args()),total=db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.TareaLaboral t"+scope,args()).Single()};
  });}
  [HttpGet,Route("tareas/{id:int}")]
  public IHttpActionResult DetalleTarea(int id){return Read(()=>{
   var task=Task(id);Exigir(PuedeVer(task));
   var acciones=new System.Collections.Generic.List<string>();string estado=(string)task["Estado"];
   if(Jefe((string)task["Area"])){
    if(new[]{"PendienteAsignacion","Asignada","EnCurso","Observada"}.Contains(estado))acciones.Add("Asignar");
    if(estado=="EnRevision")acciones.AddRange((string)task["Categoria"]=="Instalacion"?new[]{"Aprobar","Devolver"}:new[]{"Aprobar","Rechazar","Devolver"});
    if(estado=="Aprobada")acciones.AddRange(new[]{"Cerrar","Devolver"});
   }
   if((int?)task["idEmpleadoAsignado"]==Empleado && PuedeTrabajar(task)){
    if(new[]{"Asignada","Observada"}.Contains(estado))acciones.Add("Iniciar");
    if(new[]{"EnCurso","Observada"}.Contains(estado))acciones.AddRange(new[]{"Avance","EnviarRevision"});
    if((string)task["Categoria"]=="Instalacion" && estado=="EnCurso")acciones.Add("NoSePudoInstalar");
   }
   object origen=null;
   if(new[]{"Solicitud","Instalacion"}.Contains((string)task["Categoria"])){
    origen=Rows(@"SELECT s.idSolicitudServicio,s.idServicio,se.Nombre Servicio,s.Observaciones,es.NombreEstado Estado FROM dbo.SolicitudServicio s JOIN dbo.Servicio se ON se.idServicio=s.idServicio JOIN dbo.EstadoSolicitudServicio es ON es.idEstadoSolicitud=s.idEstadoSolicitud WHERE s.idSolicitudServicio=@id",P("@id",(int?)task["OrigenId"]));
   }
   if((string)task["Categoria"]=="Reclamo")origen=Rows("SELECT Descripcion Observaciones FROM dbo.Reclamo WHERE idReclamo=@id",P("@id",(int?)task["OrigenId"]));
   return new{tarea=Rows(TaskContext+" WHERE t.idTarea=@id",P("@id",id)).Single(),acciones,origen,
    documentosSolicitud=new[]{"Solicitud","Instalacion"}.Contains((string)task["Categoria"])?Rows("SELECT idDocumentoSolicitud,NombreArchivo,TipoArchivo,FechaSubida FROM dbo.DocumentoSolicitud WHERE idSolicitudServicio=@id",P("@id",(int?)task["OrigenId"])):null,
    documentosLegajo=new[]{"Solicitud","Instalacion"}.Contains((string)task["Categoria"])?Rows(@"SELECT d.idDocumentoPersona,t.Codigo,t.Nombre Requisito,d.NombreArchivo,d.Estado,d.FechaCargaUtc,
     link.FechaUtc FechaCompartido FROM dbo.SolicitudDocumentoLegajo link JOIN dbo.DocumentoPersona d ON d.idDocumentoPersona=link.idDocumentoPersona
     JOIN dbo.TipoDocumentoPersona t ON t.idTipoDocumentoPersona=d.idTipoDocumentoPersona WHERE link.idSolicitudServicio=@id
     ORDER BY CASE t.Codigo WHEN 'DniFrente' THEN 1 WHEN 'DniDorso' THEN 2 ELSE 3 END",P("@id",(int?)task["OrigenId"])):null,
    historial=Rows("SELECT h.*,u.Usuario Responsable FROM dbo.AuditoriaLaboral h LEFT JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=h.idUsuarioSistema WHERE h.idTarea=@id ORDER BY h.idAuditoria DESC",P("@id",id)),
    evidencias=Rows("SELECT idEvidencia,Nombre,Tipo,FechaUtc FROM dbo.EvidenciaLaboral WHERE idTarea=@id ORDER BY idEvidencia",P("@id",id))};
  });}

  private int CrearTarea(string area,string categoria,int? origen,int? socio,int? conexion,string titulo){
   return db.Database.SqlQuery<int>(@"INSERT dbo.TareaLaboral(Area,Categoria,OrigenId,idSocio,idServicioSocio,Titulo,Estado)
    OUTPUT INSERTED.idTarea VALUES(@a,@c,@o,@s,@ss,@t,'PendienteAsignacion')",P("@a",area),P("@c",categoria),P("@o",origen),P("@s",socio),P("@ss",conexion),P("@t",titulo)).Single();
  }
  [HttpPost,Route("tareas")]
  public IHttpActionResult NuevaTarea(JObject data){return Write(()=>{
   string area=Texto(data,"Area",30,true),categoria=Texto(data,"Categoria",30,true),titulo=Texto(data,"Titulo",200,true);
   Exigir(Jefe(area) || (LaboralAccess.Atencion(db,Principal) && (categoria=="Reclamo" || (area=="ADMINISTRACION" && categoria=="Solicitud"))));
   if(!new[]{"ADMINISTRACION","AGUA","ENERGIA","INTERNET"}.Contains(area))throw new ArgumentException("Esta área no tiene un flujo operativo definido.");
   int? origen=(int?)data["OrigenId"],socio=(int?)data["idSocio"],conexion=(int?)data["idServicioSocio"];
   if(categoria=="Solicitud" || categoria=="Instalacion"){
    var s=db.SolicitudServicio.Find(origen);if(s==null)throw new ArgumentException("La solicitud no existe.");
    var estado=OperacionesMedidor.Normalizar(s.EstadoSolicitudServicio.NombreEstado);
    if(categoria=="Solicitud" && (area!="ADMINISTRACION" || !new[]{"pendiente","en revision"}.Contains(estado)))throw new ArgumentException("La solicitud no está en revisión administrativa.");
    if(categoria=="Instalacion" && (estado!="pendiente instalacion" || LaboralAccess.AreaServicio(db,s.idServicio)!=area))throw new ArgumentException("La instalación no corresponde a esa área o etapa.");
    socio=s.idSocio;conexion=null;
   } else if(categoria=="Reclamo"){
    var r=db.Reclamo.Find(origen);if(r==null || !r.Activo || r.idEstadoReclamo==3)throw new ArgumentException("El reclamo no está disponible.");socio=r.idSocio;
   } else if(categoria!="Trabajo" || origen.HasValue)throw new ArgumentException("Tipo de tarea inválido.");
   if(conexion.HasValue){var c=db.ServicioSocio.Find(conexion);if(c==null || c.Activo!=true || (socio.HasValue && c.idSocio!=socio) || LaboralAccess.AreaServicio(db,c.idServicio)!=area)throw new ArgumentException("La conexión no corresponde al socio y área.");socio=c.idSocio;}
   if(socio.HasValue && !db.Socio.Any(s=>s.idSocio==socio && s.Activo==true))throw new ArgumentException("El socio no está activo.");
   int id=CrearTarea(area,categoria,origen,socio,conexion,titulo);Audit(area,id,"CrearTarea",null,"PendienteAsignacion",Texto(data,"Motivo",500),data);
   return Task(id);
  });}

  [HttpPost,Route("tareas/{id:int}/accion")]
  public IHttpActionResult AccionTarea(int id,JObject data){return Write(()=>{
   var t=Task(id);Version(t,data);Exigir(PuedeVer(t));string area=(string)t["Area"],antes=(string)t["Estado"],accion=Texto(data,"Accion",30,true),nuevo=antes,motivo=Texto(data,"Motivo",1000);
   int? asignado=(int?)t["idEmpleadoAsignado"],idMotivoLaboral=null;
   if(accion=="Asignar"){
    Exigir(Jefe(area));if(!new[]{"PendienteAsignacion","Asignada","EnCurso","Observada"}.Contains(antes))throw new ArgumentException("No se puede reasignar durante una revisión o después de decidir.");
    int e=(int?)data["idEmpleado"]??0;if(!PuedeTrabajar(t,e))throw new ArgumentException("El empleado no tiene la función vigente para esta tarea en el área.");
    if(asignado.HasValue && string.IsNullOrEmpty(motivo))throw new ArgumentException("Indicá el motivo de la reasignación.");
    db.Database.ExecuteSqlCommand("UPDATE dbo.TareaLaboral SET idEmpleadoAsignado=@e WHERE idTarea=@id",P("@e",e),P("@id",id));nuevo="Asignada";
   } else if(accion=="NoSePudoInstalar"){
    Exigir((string)t["Categoria"]=="Instalacion" && asignado==Empleado && PuedeTrabajar(t) && antes=="EnCurso");
    motivo=Texto(data,"Motivo",1000,true);var solicitud=db.SolicitudServicio.Find((int?)t["OrigenId"]);
    if(solicitud==null || OperacionesMedidor.Normalizar(solicitud.EstadoSolicitudServicio.NombreEstado)!="pendiente instalacion")throw new DBConcurrencyException("La solicitud ya no esta pendiente de instalacion.");
    db.Database.ExecuteSqlCommand("UPDATE dbo.TareaLaboral SET idEmpleadoAsignado=NULL,Resultado=@r,DatosResultado=NULL WHERE idTarea=@id",P("@r",motivo),P("@id",id));
    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud{idSolicitudServicio=solicitud.idSolicitudServicio,idEstadoSolicitud=solicitud.idEstadoSolicitud,idEmpleado=Empleado,FechaCambio=DateTime.Now,Observaciones="No se pudo realizar la visita; solicitud pendiente de reprogramacion. Motivo: "+motivo});
    EventoOperativoStore.Registrar(db,Principal,new EventoOperativoNuevo{Contexto="INSTALACION_NO_REALIZADA",CodigoEvento="InstalacionNoRealizada",Entidad="SolicitudServicio",idEntidad=solicitud.idSolicitudServicio.ToString(),Area=area,idSocio=solicitud.idSocio,idServicio=solicitud.idServicio,EstadoAnterior="EnCurso",EstadoNuevo="PendienteAsignacion",Resultado="PendienteReprogramacion",Detalle=motivo});
    nuevo="PendienteAsignacion";
   } else if(new[]{"Iniciar","Avance","EnviarRevision"}.Contains(accion)){
    Exigir(asignado==Empleado && PuedeTrabajar(t));
    if(accion=="Iniciar"){if(!new[]{"Asignada","Observada"}.Contains(antes))throw new ArgumentException("La tarea no está lista para iniciar.");nuevo="EnCurso";}
    else {
     if(!new[]{"EnCurso","Observada"}.Contains(antes))throw new ArgumentException("Iniciá la tarea antes de registrar el resultado.");
     var resultado=Texto(data,"Resultado",2000,true);string datos=data["DatosResultado"]?.Type==JTokenType.Object?data["DatosResultado"].ToString(Newtonsoft.Json.Formatting.None):null;
     if(datos!=null && datos.Length>10000)throw new ArgumentException("El resultado técnico es demasiado extenso.");
     if((string)t["Categoria"]=="Instalacion" && accion=="EnviarRevision" && area!="INTERNET" && datos==null)throw new ArgumentException("Completá el medidor instalado y su lectura inicial.");
     db.Database.ExecuteSqlCommand("UPDATE dbo.TareaLaboral SET Resultado=@r,DatosResultado=@d WHERE idTarea=@id",P("@r",resultado),P("@d",datos),P("@id",id));
     if(accion=="EnviarRevision")nuevo="EnRevision";
    }
   } else if(new[]{"Aprobar","Rechazar","Devolver","Cerrar"}.Contains(accion)){
    Exigir(Jefe(area));if((accion=="Cerrar" && antes!="Aprobada") || (accion!="Cerrar" && antes!="EnRevision" && !(accion=="Devolver" && antes=="Aprobada")))throw new ArgumentException("La tarea no está en el estado requerido para esta decisión.");
    if(accion=="Rechazar" && (string)t["Categoria"]=="Instalacion")throw new ArgumentException("Una instalacion se devuelve para corregir o reprogramar; no se cancela la solicitud desde esta tarea.");
    if(accion=="Rechazar" || accion=="Devolver"){
     var m=Row("SELECT * FROM dbo.MotivoLaboral WHERE idMotivo=@id AND Area=@a AND Accion=@accion AND Activo=1",P("@id",(int?)data["idMotivo"]),P("@a",area),P("@accion",accion));idMotivoLaboral=(int)m["idMotivo"];motivo=(string)m["Nombre"];
    }
    nuevo=accion=="Aprobar"?"Aprobada":accion=="Cerrar"?"Cerrada":accion=="Devolver"?"Observada":"Rechazada";
    if(accion=="Cerrar" || accion=="Rechazar")AplicarDecision(t,accion,motivo);
   } else throw new ArgumentException("Acción inválida.");
   db.Database.ExecuteSqlCommand("UPDATE dbo.TareaLaboral SET Estado=@estado,FechaActualizacionUtc=SYSUTCDATETIME(),FechaCierreUtc=CASE WHEN @estado IN ('Cerrada','Rechazada') THEN SYSUTCDATETIME() ELSE NULL END WHERE idTarea=@id",P("@estado",nuevo),P("@id",id));
   Audit(area,id,accion,antes,nuevo,motivo,data,idMotivoLaboral);return Task(id);
  });}

  private void PrepararEtapa(string area,string categoria,int origen,int socio,string titulo){
   var existente=Rows("SELECT * FROM dbo.TareaLaboral WHERE Area=@a AND Categoria=@c AND OrigenId=@o",P("@a",area),P("@c",categoria),P("@o",origen)).SingleOrDefault();
   if(existente==null){int next=CrearTarea(area,categoria,origen,socio,null,titulo);Audit(area,next,"DerivarEtapa",null,"PendienteAsignacion","Etapa anterior completada");}
   else {int id=(int)existente["idTarea"];string antes=(string)existente["Estado"];if(!new[]{"Cerrada","Rechazada"}.Contains(antes))throw new ArgumentException("La etapa de destino ya tiene una tarea abierta.");
    db.Database.ExecuteSqlCommand("UPDATE dbo.TareaLaboral SET Estado='PendienteAsignacion',idEmpleadoAsignado=NULL,Resultado=NULL,DatosResultado=NULL,FechaCierreUtc=NULL,FechaActualizacionUtc=SYSUTCDATETIME() WHERE idTarea=@id",P("@id",id));Audit(area,id,"ReabrirEtapa",antes,"PendienteAsignacion","Devuelta a esta etapa");}
  }
  private void AplicarDecision(JObject t,string accion,string motivo){
   string categoria=(string)t["Categoria"],area=(string)t["Area"];int? origen=(int?)t["OrigenId"];bool cerrar=accion=="Cerrar";
   if(categoria=="Solicitud" || categoria=="Instalacion"){
    var s=db.SolicitudServicio.Find(origen);if(s==null)throw new ArgumentException("No existe la solicitud de origen.");
    string actual=OperacionesMedidor.Normalizar(s.EstadoSolicitudServicio.NombreEstado);
    if((categoria=="Instalacion" && actual!="pendiente instalacion") || (categoria=="Solicitud" && !new[]{"en revision","pendiente"}.Contains(actual)))throw new DBConcurrencyException("La solicitud cambió de etapa. Actualizá su tarea.");
    string sector=LaboralAccess.AreaServicio(db,s.idServicio),nuevo;
    if(categoria=="Solicitud"){
     Exigir(area=="ADMINISTRACION");nuevo=cerrar?(sector==null?"Aprobada":"Pendiente instalacion"):"Rechazada";
     if(cerrar && sector!=null)PrepararEtapa(sector,"Instalacion",s.idSolicitudServicio,s.idSocio,"Instalación de "+s.Servicio.Nombre);
     else if(cerrar)CrearConexionSolicitud(s);
    } else {
     Exigir(area==sector);
     if(cerrar){
      var conexion=CrearConexionSolicitud(s);
      if(area=="AGUA" || area=="ENERGIA"){
       var resultado=(string)t["DatosResultado"];if(string.IsNullOrEmpty(resultado))throw new ArgumentException("Faltan los datos del medidor instalado.");
       var op=JObject.Parse(resultado).ToObject<SeleccionMedidor>();op.Fecha=DateTimeOffset.UtcNow;
       OperacionesMedidor.Colocar(db,Principal,op,conexion.idServicioSocio,"Instalar",Guid.NewGuid());
      }
      nuevo="Aprobada";
     } else {nuevo="Pendiente instalacion";}
    }
    var estado=db.EstadoSolicitudServicio.FirstOrDefault(e=>e.NombreEstado==nuevo);
    if(estado==null){estado=new EstadoSolicitudServicio{NombreEstado=nuevo};db.EstadoSolicitudServicio.Add(estado);db.SaveChanges();}
    s.idEstadoSolicitud=estado.idEstadoSolicitud;s.idEmpleadoRevisor=Empleado;s.MotivoRechazo=cerrar?null:motivo;s.FechaResolucion=new[]{"Aprobada","Rechazada"}.Contains(nuevo)?DateTime.Now:(DateTime?)null;
    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud{idSolicitudServicio=s.idSolicitudServicio,idEstadoSolicitud=estado.idEstadoSolicitud,idEmpleado=Empleado,FechaCambio=DateTime.Now,Observaciones="Decisión de "+area+": "+accion+". "+motivo});
   } else if(categoria=="Reclamo" && cerrar){
    var r=db.Reclamo.Find(origen);if(r==null || !r.Activo)throw new ArgumentException("El reclamo ya no está activo.");
    if(Rows("SELECT 1 pendiente FROM dbo.TareaLaboral WHERE Categoria='Reclamo' AND OrigenId=@o AND idTarea<>@id AND Estado NOT IN ('Cerrada','Rechazada')",P("@o",origen),P("@id",(int)t["idTarea"])).Any())throw new ArgumentException("El reclamo aún tiene trabajos abiertos en otra área.");
    r.idEstadoReclamo=3;
   } else if(categoria=="Documentacion"){
    Exigir(area=="ADMINISTRACION");db.Database.ExecuteSqlCommand("UPDATE dbo.DocumentoPersona SET Estado=@estado,MotivoRechazo=@motivo,FechaRevisionUtc=SYSUTCDATETIME(),idUsuarioRevisor=@usuario WHERE idDocumentoPersona=@id",P("@estado",cerrar?"Aprobado":"Rechazado"),P("@motivo",cerrar?null:motivo),P("@usuario",LaboralAccess.Usuario(Principal)),P("@id",origen));
   } else if(categoria=="Trabajo" && cerrar && !string.IsNullOrEmpty((string)t["DatosResultado"])){
    var datos=JObject.Parse((string)t["DatosResultado"]);string tipo=(string)datos["TipoTrabajo"];
    if(tipo=="Reemplazo" || tipo=="Retiro"){
     int id=(int?)datos["idAnterior"]??0;var ficha=OperacionesMedidor.Ficha(db,id);
     if(ficha==null || ficha.idServicioSocio!=(int?)t["idServicioSocio"])throw new ArgumentException("El medidor no corresponde a la conexión de la tarea.");
     Exigir(area==LaboralAccess.AreaMedidor(db,id));
     if(tipo=="Retiro"){var op=datos.ToObject<OperacionMedidor>();op.Accion="Retirar";op.Fecha=DateTimeOffset.UtcNow;OperacionesMedidor.Ejecutar(db,Principal,id,op);}
     else {
      var op=datos.ToObject<SeleccionMedidor>();if(op.idMedidor==id || ficha.PendienteConciliacion)throw new ArgumentException("Revisá el equipo de reemplazo y la asignación actual.");
      op.Fecha=DateTimeOffset.UtcNow;Guid operacion=Guid.NewGuid();
      OperacionesMedidor.Ejecutar(db,Principal,id,new OperacionMedidor{Accion="Retirar",Version=op.VersionAnterior,Lectura=op.LecturaFinal,EstadoFisico=op.EstadoRetirado,idMotivo=op.idMotivo,Fecha=op.Fecha,Detalle=op.Detalle},"Reemplazar",operacion);
      OperacionesMedidor.Colocar(db,Principal,op,ficha.idServicioSocio.Value,"Reemplazar",operacion);
     }
    } else throw new ArgumentException("El resultado contiene un tipo de trabajo no reconocido.");
   }
  }
  private ServicioSocio CrearConexionSolicitud(SolicitudServicio s){
   if(Rows("SELECT 1 existe FROM dbo.SolicitudConexion WHERE idSolicitudServicio=@id",P("@id",s.idSolicitudServicio)).Any())throw new DBConcurrencyException("La solicitud ya tiene una conexión instalada.");
   var conexion=new ServicioSocio{idSocio=s.idSocio,idServicio=s.idServicio,Activo=true,FechaAlta=DateTime.Now};db.ServicioSocio.Add(conexion);db.SaveChanges();
   db.Database.ExecuteSqlCommand("INSERT dbo.SolicitudConexion(idSolicitudServicio,idServicioSocio) VALUES(@s,@c)",P("@s",s.idSolicitudServicio),P("@c",conexion.idServicioSocio));return conexion;
  }

  [HttpGet,Route("bandeja")]
  public IHttpActionResult Bandeja(string area){return Read(()=>{
   Exigir(Jefe(area) || (area=="ADMINISTRACION" && LaboralAccess.Atencion(db,Principal)));
   return new{solicitudes=Rows(@"SELECT s.idSolicitudServicio OrigenId,s.idSocio,CONCAT(p.Nombre,' ',p.Apellido) Socio,se.Nombre Servicio,es.NombreEstado Estado,s.Observaciones,
    CASE WHEN @area='ADMINISTRACION' THEN 'Solicitud' ELSE 'Instalacion' END Categoria
    FROM dbo.SolicitudServicio s JOIN dbo.Socio so ON so.idSocio=s.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona JOIN dbo.Servicio se ON se.idServicio=s.idServicio JOIN dbo.EstadoSolicitudServicio es ON es.idEstadoSolicitud=s.idEstadoSolicitud LEFT JOIN dbo.AreaServicio a ON a.idServicio=s.idServicio
    WHERE ((@area='ADMINISTRACION' AND es.NombreEstado IN ('En Revision','Pendiente')) OR (a.Area=@area AND es.NombreEstado='Pendiente instalacion'))
    AND NOT EXISTS(SELECT 1 FROM dbo.TareaLaboral t WHERE t.OrigenId=s.idSolicitudServicio AND t.Area=@area AND t.Categoria IN ('Solicitud','Instalacion')) ORDER BY s.FechaSolicitud",P("@area",area)),
    reclamos=area=="ADMINISTRACION"?Rows(@"SELECT r.idReclamo OrigenId,r.idSocio,CONCAT(p.Nombre,' ',p.Apellido) Socio,r.Descripcion,r.Fecha FROM dbo.Reclamo r JOIN dbo.Socio so ON so.idSocio=r.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona WHERE r.Activo=1 AND r.idEstadoReclamo<>3 AND NOT EXISTS(SELECT 1 FROM dbo.TareaLaboral t WHERE t.Categoria='Reclamo' AND t.OrigenId=r.idReclamo) ORDER BY r.Fecha"):null,
    seguimiento=area=="ADMINISTRACION"?Rows(@"SELECT TOP 200 s.idSolicitudServicio,s.idSocio,CONCAT(p.Nombre,' ',p.Apellido) Socio,se.Nombre Servicio,es.NombreEstado Estado,s.FechaSolicitud,s.Observaciones,ars.Area AreaResponsable,
      tarea.Estado EstadoTarea,CONCAT(pe.Nombre,' ',pe.Apellido) Operario,actividad.FechaUtc FechaUltimaActividad,actividad.Detalle UltimaActividad
      FROM dbo.SolicitudServicio s JOIN dbo.Socio so ON so.idSocio=s.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona JOIN dbo.Servicio se ON se.idServicio=s.idServicio JOIN dbo.EstadoSolicitudServicio es ON es.idEstadoSolicitud=s.idEstadoSolicitud
      LEFT JOIN dbo.AreaServicio ars ON ars.idServicio=s.idServicio
      OUTER APPLY(SELECT TOP 1 t.* FROM dbo.TareaLaboral t WHERE t.Categoria='Instalacion' AND t.OrigenId=s.idSolicitudServicio ORDER BY t.FechaActualizacionUtc DESC,t.idTarea DESC)tarea
      LEFT JOIN dbo.Empleado emp ON emp.idEmpleado=tarea.idEmpleadoAsignado LEFT JOIN dbo.Persona pe ON pe.idPersona=emp.idPersona
      OUTER APPLY(SELECT TOP 1 x.FechaUtc,x.Detalle FROM(
       SELECT h.FechaCambio FechaUtc,h.Observaciones Detalle FROM dbo.HistorialEstadoSolicitud h WHERE h.idSolicitudServicio=s.idSolicitudServicio
       UNION ALL SELECT au.FechaUtc,CASE WHEN au.Accion='NoSePudoInstalar' THEN N'Visita sin instalar: '+COALESCE(au.Motivo,N'') WHEN au.Accion='Asignar' THEN N'Tarea asignada para coordinar la instalacion.' WHEN au.Accion='Iniciar' THEN N'Trabajo de instalacion iniciado.' WHEN au.Accion='EnviarRevision' THEN N'Instalacion enviada a revision del encargado.' WHEN au.Accion='Aprobar' THEN N'Instalacion aprobada por el encargado.' WHEN au.Accion='Cerrar' THEN N'Instalacion confirmada y solicitud cerrada.' ELSE au.Accion END
       FROM dbo.AuditoriaLaboral au JOIN dbo.TareaLaboral tt ON tt.idTarea=au.idTarea WHERE tt.Categoria='Instalacion' AND tt.OrigenId=s.idSolicitudServicio AND au.Accion IN('NoSePudoInstalar','Asignar','Iniciar','EnviarRevision','Aprobar','Cerrar')
      )x ORDER BY x.FechaUtc DESC)actividad ORDER BY s.FechaSolicitud DESC"):null};
  });}

  [HttpGet,Route("seguimiento/reclamo/{id:int}")]
  public IHttpActionResult SeguimientoReclamo(int id){return Read(()=>{
   var reclamo=db.Reclamo.Find(id);if(reclamo==null)throw new ArgumentException("Reclamo inexistente.");
   Exigir(User.IsInRole("Socio") && Principal.FindFirst("idSocio")?.Value==reclamo.idSocio.ToString());
   return Rows(@"SELECT t.Titulo,a.Nombre Area,t.Estado,t.FechaCreacionUtc,t.FechaActualizacionUtc,t.FechaCierreUtc,CASE WHEN t.Estado='Cerrada' THEN t.Resultado ELSE NULL END Resultado FROM dbo.TareaLaboral t JOIN dbo.AreaLaboral a ON a.Codigo=t.Area WHERE t.Categoria='Reclamo' AND t.OrigenId=@id ORDER BY t.FechaCreacionUtc",P("@id",id));
  });}
 }
}
