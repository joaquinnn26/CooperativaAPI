using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers {
 public partial class LaboralController {
  [HttpPost,Route("recepcion")]
  public IHttpActionResult Recepcion(JObject data){return Write(()=>{
   Exigir(LaboralAccess.Atencion(db,Principal));string tipo=Texto(data,"Tipo",30,true),canal=Texto(data,"Canal",20,true),descripcion=Texto(data,"Descripcion",800,true);int socio=(int?)data["idSocio"]??0;
   if(!new[]{"Telefono","Ventanilla"}.Contains(canal) || !db.Socio.Any(s=>s.idSocio==socio && s.Activo==true))throw new ArgumentException("Seleccioná el canal y un socio activo.");
   int origen,tarea;string area;
   if(tipo=="Reclamo"){
    int reclamoTipo=(int?)data["idTipoReclamo"]??0;area=Texto(data,"Area",30,true);
    if(!db.TipoReclamo.Any(t=>t.idTipoReclamo==reclamoTipo) || !new[]{"AGUA","ENERGIA","INTERNET","ADMINISTRACION"}.Contains(area) || descripcion.Length<10)throw new ArgumentException("Revisá el tipo, área y descripción del reclamo.");
    var r=new Reclamo{idSocio=socio,idTipoReclamo=reclamoTipo,Descripcion=descripcion,Fecha=DateTime.Now,idEstadoReclamo=1,Activo=true};db.Reclamo.Add(r);db.SaveChanges();origen=r.idReclamo;
    tarea=CrearTarea(area,"Reclamo",origen,socio,null,"Atender reclamo #"+origen);
   } else if(tipo=="Solicitud"){
    area="ADMINISTRACION";int servicio=(int?)data["idServicio"]??0;if(!db.Servicio.Any(s=>s.idServicio==servicio))throw new ArgumentException("Servicio inexistente.");
    if(db.SolicitudServicio.Any(s=>s.idSocio==socio && s.idServicio==servicio && s.FechaResolucion==null))throw new ArgumentException("El socio ya tiene una solicitud pendiente para ese servicio.");
    var estado=db.EstadoSolicitudServicio.FirstOrDefault(e=>e.NombreEstado=="En Revision");if(estado==null)throw new ArgumentException("Falta configurar el estado de revisión de solicitudes.");
    var solicitud=new SolicitudServicio{idSocio=socio,idServicio=servicio,idEstadoSolicitud=estado.idEstadoSolicitud,Observaciones=descripcion,FechaSolicitud=DateTime.Now};db.SolicitudServicio.Add(solicitud);db.SaveChanges();origen=solicitud.idSolicitudServicio;tarea=CrearTarea(area,"Solicitud",origen,socio,null,"Revisar solicitud #"+origen);
   } else throw new ArgumentException("Tipo de trámite inválido.");
   Audit(area,tarea,"Recepcion"+tipo,null,"PendienteAsignacion",canal,new{OrigenId=origen,idSocio=socio,Canal=canal});return new{idTarea=tarea,OrigenId=origen};
  });}
  [HttpGet,Route("tareas/{id:int}/medidores")]
  public IHttpActionResult MedidoresTarea(int id){return Read(()=>{var t=Task(id);Exigir(PuedeVer(t));return db.Database.SqlQuery<FichaMedidorDto>(OperacionesMedidor.SelectFicha+" WHERE a.idServicioSocio=@c",P("@c",(int?)t["idServicioSocio"])).ToList();});}
  [HttpGet,Route("resumen")]
  public IHttpActionResult Resumen(){return Read(()=>{
   Exigir(Empleado.HasValue);var perfil=LaboralAccess.Perfil(db,Principal);bool gerencia=LaboralAccess.Gerencia(db,Principal);
   var areas=gerencia?Rows("SELECT Codigo Area,Nombre FROM dbo.AreaLaboral"):perfil.Select(a=>new JObject{{"Area",a.Area},{"Nombre",a.Nombre}}).ToList();
   return areas.Select(a=>new{Area=(string)a["Area"],Nombre=(string)a["Nombre"],conteos=Rows(@"SELECT Estado,COUNT(*) Cantidad FROM dbo.TareaLaboral WHERE Area=@a AND (@equipo=1 OR idEmpleadoAsignado=@e) GROUP BY Estado",P("@a",(string)a["Area"]),P("@equipo",gerencia || Jefe((string)a["Area"])),P("@e",Empleado))}).ToList();
  });}

  [HttpGet,Route("indicadores")]
  public IHttpActionResult Indicadores(){return Read(()=>{
   Exigir(LaboralAccess.Gerencia(db,Principal));
   var porArea=Rows(@"SELECT a.Codigo Area,a.Nombre,COUNT(t.idTarea) Total,
    SUM(CASE WHEN t.Estado IN ('PendienteAsignacion','EnRevision','Observada','Aprobada') THEN 1 ELSE 0 END) PorAtender,
    SUM(CASE WHEN t.Estado IN ('Asignada','EnCurso') THEN 1 ELSE 0 END) EnCurso,
    SUM(CASE WHEN t.Estado='Cerrada' THEN 1 ELSE 0 END) Cerradas,
    SUM(CASE WHEN t.Estado='Rechazada' THEN 1 ELSE 0 END) Rechazadas
    FROM dbo.AreaLaboral a LEFT JOIN dbo.TareaLaboral t ON t.Area=a.Codigo
    GROUP BY a.Codigo,a.Nombre ORDER BY a.Nombre");
   var tendencia=Rows(@"WITH Meses AS (
      SELECT DATEADD(MONTH,-5,DATEADD(MONTH,DATEDIFF(MONTH,0,GETUTCDATE()),0)) Mes
      UNION ALL SELECT DATEADD(MONTH,1,Mes) FROM Meses
       WHERE Mes<DATEADD(MONTH,DATEDIFF(MONTH,0,GETUTCDATE()),0)
    ), Creadas AS (
      SELECT DATEADD(MONTH,DATEDIFF(MONTH,0,FechaCreacionUtc),0) Mes,COUNT(*) Cantidad
      FROM dbo.TareaLaboral
      WHERE FechaCreacionUtc>=DATEADD(MONTH,-5,DATEADD(MONTH,DATEDIFF(MONTH,0,GETUTCDATE()),0))
      GROUP BY DATEADD(MONTH,DATEDIFF(MONTH,0,FechaCreacionUtc),0)
    ), Cerradas AS (
      SELECT DATEADD(MONTH,DATEDIFF(MONTH,0,FechaCierreUtc),0) Mes,COUNT(*) Cantidad
      FROM dbo.TareaLaboral
      WHERE FechaCierreUtc>=DATEADD(MONTH,-5,DATEADD(MONTH,DATEDIFF(MONTH,0,GETUTCDATE()),0))
      GROUP BY DATEADD(MONTH,DATEDIFF(MONTH,0,FechaCierreUtc),0)
    )
    SELECT CONVERT(date,m.Mes) Periodo,ISNULL(c.Cantidad,0) Creadas,ISNULL(x.Cantidad,0) Cerradas
    FROM Meses m LEFT JOIN Creadas c ON c.Mes=m.Mes LEFT JOIN Cerradas x ON x.Mes=m.Mes
    ORDER BY m.Mes OPTION(MAXRECURSION 6)");
   return new{porArea,tendencia};
  });}

  [HttpGet,Route("socios")]
  public IHttpActionResult Socios(string q="",int pagina=1){return Read(()=>{
   Exigir(LaboralAccess.Atencion(db,Principal) || LaboralAccess.Gerencia(db,Principal));if(pagina<1 || (q??"").Length>100)throw new ArgumentException("Filtros inválidos.");
   return Rows(@"SELECT s.idSocio,s.NumeroSocio,s.Activo,CONCAT(p.Nombre,' ',p.Apellido) Nombre,p.Documento,p.Telefono,p.Email
    FROM dbo.Socio s JOIN dbo.Persona p ON p.idPersona=s.idPersona WHERE @q='' OR CHARINDEX(@q,CONCAT(s.NumeroSocio,' ',p.Nombre,' ',p.Apellido,' ',p.Documento))>0 ORDER BY p.Apellido,p.Nombre OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",P("@q",q??""),P("@offset",(pagina-1)*25));
  });}
  [HttpGet,Route("conexiones")]
  public IHttpActionResult ConexionesLaborales(int? socio=null,string area=null,string q="",int pagina=1){return Read(()=>{
   Exigir(Empleado.HasValue);if(pagina<1 || (q??"").Length>100)throw new ArgumentException("Filtros inválidos.");
   bool all=LaboralAccess.Atencion(db,Principal) || LaboralAccess.Gerencia(db,Principal);bool jefe=Jefe(area);
   Exigir(all || jefe);
   return Rows(@"SELECT ss.idServicioSocio,ss.idSocio,ss.Activo,CONCAT(p.Nombre,' ',p.Apellido) Socio,se.Nombre Servicio,a.Area,u.Direccion,u.Localidad,u.Ruta,u.Folio,
    m.idMedidor,m.Numero NumeroMedidor,f.Version VersionMedidor,f.EstadoFisico
    FROM dbo.ServicioSocio ss JOIN dbo.Socio s ON s.idSocio=ss.idSocio JOIN dbo.Persona p ON p.idPersona=s.idPersona JOIN dbo.Servicio se ON se.idServicio=ss.idServicio
    LEFT JOIN dbo.AreaServicio a ON a.idServicio=ss.idServicio LEFT JOIN dbo.UbicacionServicio u ON u.idServicioSocio=ss.idServicioSocio
    LEFT JOIN dbo.AsignacionMedidor am ON am.idServicioSocio=ss.idServicioSocio AND am.Cerrada=0 LEFT JOIN dbo.Medidor m ON m.idMedidor=am.idMedidor LEFT JOIN dbo.FichaMedidor f ON f.idMedidor=m.idMedidor
    WHERE (@s IS NULL OR ss.idSocio=@s) AND (@a IS NULL OR a.Area=@a) AND (@all=1 OR a.Area=@a)
    AND (@q='' OR CHARINDEX(@q,CONCAT(p.Nombre,' ',p.Apellido,' ',u.Direccion,' ',u.Ruta,' ',u.Folio,' ',m.Numero))>0)
    ORDER BY ss.idServicioSocio,m.idMedidor OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",P("@s",socio),P("@a",area),P("@all",all),P("@q",q??""),P("@offset",(pagina-1)*25));
  });}
  [HttpGet,Route("facturas")]
  public IHttpActionResult Facturas(int pagina=1,string area=null){return Read(()=>{
   if(pagina<1 || pagina>100000)throw new ArgumentException("Página inválida.");
   bool personal=User.IsInRole("Socio");int? socio=personal?int.Parse(Principal.FindFirst("idSocio").Value):(int?)null;
   bool adminFact=Admin || Funcion("ADMINISTRACION","FACTURACION") || Jefe("ADMINISTRACION");
   bool internetFact=Funcion("INTERNET","FACTURACION");
   Exigir(personal || adminFact || internetFact);
   var where=" WHERE f.Activo=1 AND (@s IS NULL OR f.idSocio=@s) AND (@all=1 OR a.Area='INTERNET') AND (@area IS NULL OR a.Area=@area)";
   var from=" FROM dbo.Factura f JOIN dbo.Servicio se ON se.idServicio=f.idServicio LEFT JOIN dbo.AreaServicio a ON a.idServicio=f.idServicio LEFT JOIN dbo.ConfirmacionFactura c ON c.idFactura=f.idFactura";
   Func<System.Data.SqlClient.SqlParameter[]> args=()=>new[]{P("@s",socio),P("@all",personal || adminFact),P("@area",area),P("@offset",(pagina-1)*25)};
   var items=Rows(@"SELECT f.idFactura,f.idSocio,se.Nombre Servicio,f.MontoTotal,f.FechaEmision,f.FechaCaducidad,f.LinkPago,c.Estado EstadoConfirmado,c.FechaActualizacionUtc,c.Fuente,c.Referencia"+from+where+" ORDER BY f.FechaEmision DESC,f.idFactura DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",args());
   bool conceptos=db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('dbo.FacturaConcepto','U') IS NULL THEN 0 ELSE 1 END").Single()==1;
   foreach(var item in items) item["Conceptos"]=conceptos?JArray.FromObject(Rows("SELECT Tipo,Descripcion,Importe FROM dbo.FacturaConcepto WHERE idFactura=@f ORDER BY idConcepto",P("@f",(int)item["idFactura"]))):new JArray();
   return new{items,total=db.Database.SqlQuery<int>("SELECT COUNT(*)"+from+where,args()).Single(),integracionPendiente=true};
  });}

  [HttpGet,Route("lecturas")]
  public IHttpActionResult Lecturas(string area,int? conexion=null,int pagina=1){return Read(()=>{
   Exigir(Jefe(area) && new[]{"AGUA","ENERGIA"}.Contains(area));if(pagina<1)throw new ArgumentException("Página inválida.");
   return Rows(@"SELECT m.*,me.Numero NumeroMedidor,CONCAT(p.Nombre,' ',p.Apellido) Socio FROM dbo.Medicion m JOIN dbo.Medidor me ON me.idMedidor=m.idMedidor JOIN dbo.ServicioSocio s ON s.idServicioSocio=m.idServicioSocio JOIN dbo.AreaServicio a ON a.idServicio=s.idServicio JOIN dbo.Socio so ON so.idSocio=s.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona
    WHERE a.Area=@a AND (@c IS NULL OR m.idServicioSocio=@c) ORDER BY m.FechaLectura DESC,m.idMedicion DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",P("@a",area),P("@c",conexion),P("@offset",(pagina-1)*25));
  });}
  [HttpPost,Route("lecturas/{id:int}/corregir")]
  public IHttpActionResult CorregirLectura(int id,JObject data){return Write(()=>{
   var actual=db.Medicion.Find(id);if(actual==null)throw new ArgumentException("La lectura no existe.");
   var conexion=db.ServicioSocio.Find(actual.idServicioSocio);string area=LaboralAccess.AreaServicio(db,conexion.idServicio);Exigir(Jefe(area) && new[]{"AGUA","ENERGIA"}.Contains(area));
   if((decimal?)data["ValorAnterior"]!=actual.ValorLectura)throw new DBConcurrencyException("La lectura fue modificada. Actualizá la consulta.");
   var motivo=Row("SELECT * FROM dbo.MotivoLaboral WHERE idMotivo=@id AND Area=@a AND Accion='CorregirLectura' AND Activo=1",P("@id",(int?)data["idMotivo"]),P("@a",area));
   decimal valor=(decimal?)data["ValorLectura"]??-1;
   var anterior=db.Medicion.Where(m=>m.idMedidor==actual.idMedidor && (m.FechaLectura<actual.FechaLectura || (m.FechaLectura==actual.FechaLectura && m.idMedicion<id))).OrderByDescending(m=>m.FechaLectura).ThenByDescending(m=>m.idMedicion).FirstOrDefault();
   var siguiente=db.Medicion.Where(m=>m.idMedidor==actual.idMedidor && (m.FechaLectura>actual.FechaLectura || (m.FechaLectura==actual.FechaLectura && m.idMedicion>id))).OrderBy(m=>m.FechaLectura).ThenBy(m=>m.idMedicion).FirstOrDefault();
   // Medicion.FechaLectura is SQL date (calendar day); installations are datetime2.
   // Compare date boundaries inclusively so an installation made later that same day remains valid.
   var asignaciones=Rows("SELECT * FROM dbo.AsignacionMedidor WHERE idMedidor=@m AND idServicioSocio=@s AND (FechaInicio IS NULL OR FechaInicio<DATEADD(day,1,CONVERT(date,@fecha))) AND (FechaFin IS NULL OR FechaFin>=CONVERT(date,@fecha)) ORDER BY idAsignacion DESC",P("@m",actual.idMedidor),P("@s",actual.idServicioSocio),P("@fecha",actual.FechaLectura));
   if(asignaciones.Count!=1)throw new ArgumentException("Revisá primero el historial de instalación: no se puede determinar de forma única la asignación de esta lectura.");
   decimal inicial=asignaciones.Any()?(decimal?)asignaciones[0]["LecturaInicial"]??0:0;decimal? final=asignaciones.Any()?(decimal?)asignaciones[0]["LecturaFinal"]:null;
   decimal minimo=Math.Max(anterior?.ValorLectura??0,inicial);
   if(valor<minimo || (siguiente!=null && valor>siguiente.ValorLectura) || (final.HasValue && valor>final.Value))throw new ArgumentException("La lectura corregida debe respetar las lecturas anterior, siguiente y de retiro.");
   var antes=new{actual.idMedicion,actual.ValorLectura,actual.ConsumoCalculado,Siguiente=siguiente==null?null:new{ siguiente.idMedicion,siguiente.ConsumoCalculado}};
   actual.ValorLectura=valor;actual.ConsumoCalculado=valor-minimo;
   // El consumo de la siguiente lectura solo cambia si pertenece a la misma instalación.
   if(siguiente!=null && siguiente.idServicioSocio==actual.idServicioSocio && asignaciones.Any() && (asignaciones[0]["FechaFin"].Type==JTokenType.Null || siguiente.FechaLectura<=(DateTime)asignaciones[0]["FechaFin"]))siguiente.ConsumoCalculado=siguiente.ValorLectura-valor;
   var despues=new{actual.idMedicion,actual.ValorLectura,actual.ConsumoCalculado,Siguiente=siguiente==null?null:new{siguiente.idMedicion,siguiente.ConsumoCalculado}};
   db.Database.ExecuteSqlCommand("INSERT dbo.CorreccionLecturaLaboral(idMedicion,Area,idUsuarioSistema,idMotivo,Anterior,Nuevo) VALUES(@id,@a,@u,@motivo,@antes,@nuevo)",P("@id",id),P("@a",area),P("@u",LaboralAccess.Usuario(Principal)),P("@motivo",(int)motivo["idMotivo"]),P("@antes",JsonConvert.SerializeObject(antes)),P("@nuevo",JsonConvert.SerializeObject(despues)));
   EventoOperativoStore.Registrar(db,Principal,new EventoOperativoNuevo {
    Contexto="LECTURA_CORRECCION",CodigoEvento="CorregirLectura",Entidad="Medicion",idEntidad=id.ToString(),Area=area,
    idSocio=conexion.idSocio,idServicio=conexion.idServicio,idServicioSocio=conexion.idServicioSocio,idMedidor=actual.idMedidor,
    EstadoAnterior="Registrada",EstadoNuevo="Corregida",MotivoNombre=(string)motivo["Nombre"],MotivoRequerido=true,
    Datos=new{antes,despues}
   });
   Audit(area,null,"CorregirLectura",null,null,(string)motivo["Nombre"],new{antes,despues});return despues;
  });}
  [HttpGet,Route("lecturas/{id:int}/historial")]
  public IHttpActionResult HistorialLectura(int id){return Read(()=>{
   var m=db.Medicion.Find(id);if(m==null)throw new ArgumentException("Lectura inexistente.");string area=LaboralAccess.AreaServicio(db,db.ServicioSocio.Find(m.idServicioSocio).idServicio);Exigir(Jefe(area) || LaboralAccess.Gerencia(db,Principal));
   return Rows("SELECT c.*,u.Usuario Responsable,m.Nombre Motivo FROM dbo.CorreccionLecturaLaboral c JOIN dbo.UsuarioSistema u ON u.idUsuarioSistema=c.idUsuarioSistema JOIN dbo.MotivoLaboral m ON m.idMotivo=c.idMotivo WHERE c.idMedicion=@id ORDER BY c.idCorreccion DESC",P("@id",id));
  });}
 }
}
