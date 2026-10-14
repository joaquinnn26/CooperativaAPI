using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers {
 [RoutePrefix("api/facturacion/intercambio/internet"),JwtAuthorize(Roles="Administrador,Empleado")]
 public class InternetFacturacionController:ApiController {
  private readonly CooperativaEntities db=new CooperativaEntities();
  private ClaimsPrincipal Principal {get{return (ClaimsPrincipal)User;}}
  private int? Empleado {get{return LaboralAccess.Empleado(db,Principal);}}
  private static SqlParameter P(string name,object value){return LaboralAccess.P(name,value);}

  private void ExigirFacturacion(){
   if(!LaboralAccess.Tiene(db,Empleado,"INTERNET","FACTURACION"))throw new UnauthorizedAccessException("Necesitás la función de facturación de Internet.");
  }
  private IHttpActionResult Read(Func<object> action){
   try{ExigirFacturacion();return Ok(action());}
   catch(UnauthorizedAccessException){return StatusCode(HttpStatusCode.Forbidden);}
   catch(ArgumentException ex){return BadRequest(ex.Message);}
   catch(SqlException ex) when(new[]{2601,2627,547}.Contains(ex.Number)){return Content(HttpStatusCode.Conflict,"El lote entra en conflicto con datos ya registrados.");}
  }
  private IHttpActionResult Write(Func<object> action){
   try{
    ExigirFacturacion();
    using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)){
     try{var result=action();tx.Commit();return Ok(result);}
     catch{tx.Rollback();throw;}
    }
   }
   catch(UnauthorizedAccessException){return StatusCode(HttpStatusCode.Forbidden);}
   catch(ArgumentException ex){return BadRequest(ex.Message);}
   catch(DBConcurrencyException ex){return Content(HttpStatusCode.Conflict,ex.Message);}
   catch(SqlException ex) when(new[]{2601,2627,547,1205}.Contains(ex.Number)){return Content(HttpStatusCode.Conflict,"El período o la factura ya se procesó. Actualizá la pantalla y revisá el lote.");}
  }
  private static DateTime InicioPeriodo(string value){
   DateTime date;
   if(!DateTime.TryParseExact(value,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))throw new ArgumentException("Ingresá el período con formato AAAA-MM.");
   return new DateTime(date.Year,date.Month,1);
  }
  private static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  private static string Csv(params string[] values){
   return string.Join(",",values.Select(v=>"\""+((v??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," "))+"\""));
  }
  public sealed class ContractRow {
   public int idServicioSocio {get;set;} public int idSocio {get;set;} public int idServicio {get;set;}
   public string CodigoSocioExterno {get;set;} public DateTime FechaAlta {get;set;} public DateTime? FechaBaja {get;set;} public decimal? TarifaAplicada {get;set;}
  }
  public sealed class LotRow {
   public int idLote {get;set;} public string Periodo {get;set;} public string Estado {get;set;}
   public string NombreArchivoSalida {get;set;} public int CantidadFilas {get;set;} public DateTime FechaCreacionUtc {get;set;}
   public DateTime? FechaValidacionUtc {get;set;} public DateTime? FechaAplicacionUtc {get;set;} public string Errores {get;set;}
  }
  public sealed class DetailRow {
   public int idServicioSocio {get;set;} public string CodigoSocioExterno {get;set;} public int idSocio {get;set;} public int idServicio {get;set;}
   public DateTime VigenteDesde {get;set;} public DateTime VigenteHasta {get;set;} public decimal ImporteExportado {get;set;}
  }
  public sealed class ResponseRow {
   public int idLote {get;set;} public int idServicioSocio {get;set;} public string CodigoSocioExterno {get;set;}
   public string Resultado {get;set;} public string ReferenciaFacturaExterna {get;set;} public string NumeroFactura {get;set;}
   public DateTime? FechaEmision {get;set;} public string Periodo {get;set;} public decimal? ImporteTotal {get;set;}
   public DateTime? FechaVencimiento {get;set;} public string LinkPago {get;set;} public string MotivoRechazo {get;set;}
  }

  private LotRow Lote(string periodo){
   return db.Database.SqlQuery<LotRow>(@"SELECT TOP 1 idLote,Periodo,Estado,NombreArchivoSalida,CantidadFilas,FechaCreacionUtc,FechaValidacionUtc,FechaAplicacionUtc,Errores
    FROM dbo.LoteFacturacionInternet WHERE Periodo=@periodo AND Estado<>'Anulado' ORDER BY idLote DESC",P("@periodo",periodo)).SingleOrDefault();
  }
  [HttpGet,Route("lotes")]
  public IHttpActionResult ObtenerLote(string periodo){return Read(()=>{
   InicioPeriodo(periodo);
   var lote=Lote(periodo);if(lote==null)return null;
   var counts=db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.LoteFacturacionInternetDetalle WHERE idLote=@id AND Resultado='FACTURADA'",P("@id",lote.idLote)).Single();
   var rejects=db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.LoteFacturacionInternetDetalle WHERE idLote=@id AND Resultado='RECHAZADA'",P("@id",lote.idLote)).Single();
   return new{idLote=lote.idLote,lote.Periodo,lote.Estado,lote.NombreArchivoSalida,lote.CantidadFilas,lote.FechaCreacionUtc,lote.FechaValidacionUtc,lote.FechaAplicacionUtc,CantidadFacturadas=counts,CantidadRechazadas=rejects,Errores=DeserializeErrors(lote.Errores)};
  });}

  [HttpPost,Route("lotes")]
  public IHttpActionResult CrearLote(JObject data){return Write(()=>{
   string periodo=(string)data?["Periodo"];DateTime inicio=InicioPeriodo(periodo);DateTime siguiente=inicio.AddMonths(1),fin=siguiente.AddDays(-1);
   var existente=Lote(periodo);
   if(existente!=null)return new{idLote=existente.idLote,existente.Periodo,existente.Estado,existente.NombreArchivoSalida,existente.CantidadFilas,Reutilizado=true};
   var contracts=db.Database.SqlQuery<ContractRow>(@"SELECT ss.idServicioSocio,ss.idSocio,ss.idServicio,CONVERT(nvarchar(80),s.NumeroSocio) CodigoSocioExterno,
      ss.FechaAlta,ss.FechaBaja,ss.TarifaAplicada
     FROM dbo.ServicioSocio ss WITH(UPDLOCK,HOLDLOCK)
     JOIN dbo.Socio s ON s.idSocio=ss.idSocio
     JOIN dbo.AreaServicio a ON a.idServicio=ss.idServicio
     WHERE a.Area='INTERNET' AND s.Activo=1 AND ss.FechaAlta<@siguiente
      AND (ss.FechaBaja IS NULL OR ss.FechaBaja>=@inicio)
      AND (ss.Activo=1 OR (ss.FechaBaja>=@inicio AND ss.FechaBaja<@siguiente))
     ORDER BY ss.idServicioSocio",P("@inicio",inicio),P("@siguiente",siguiente)).ToList();
   if(!contracts.Any())throw new ArgumentException("No hay servicios de Internet facturables en ese período.");
   var invalid=contracts.Where(x=>string.IsNullOrWhiteSpace(x.CodigoSocioExterno) || !x.TarifaAplicada.HasValue || x.TarifaAplicada.Value<0).ToList();
   if(invalid.Any()){
    string references=string.Join(", ",invalid.Take(10).Select(x=>"contrato #"+x.idServicioSocio+" / socio #"+(x.CodigoSocioExterno??"sin código")));
    throw new ArgumentException("Hay "+invalid.Count+" servicios sin número de socio o tarifa válida. Revisá: "+references+(invalid.Count>10?", entre otros.":"."));
   }
   var rows=new List<DetailRow>();var csv=new StringBuilder();
   csv.AppendLine(Csv("version_formato","id_lote","periodo","id_socio_externo","id_servicio_cooperativa","codigo_servicio","codigo_plan","operacion","vigente_desde","vigente_hasta","cantidad","importe_acordado","concepto_ajuste"));
   string nombre="internet_facturacion_"+periodo+"_pendiente.csv";
   int idLote=db.Database.SqlQuery<int>(@"INSERT dbo.LoteFacturacionInternet(Periodo,Estado,NombreArchivoSalida,HashSalida,ContenidoSalida,CantidadFilas,idUsuarioSistema)
      VALUES(@periodo,'Exportado',@nombre,REPLICATE('0',64),N'',@cantidad,@usuario); SELECT CAST(SCOPE_IDENTITY() AS int)",
      P("@periodo",periodo),P("@nombre",nombre),P("@cantidad",contracts.Count),P("@usuario",LaboralAccess.Usuario(Principal))).Single();
   nombre="internet_facturacion_"+periodo+"_lote-"+idLote+".csv";
   foreach(var c in contracts){
    var desde=c.FechaAlta.Date>inicio?c.FechaAlta.Date:inicio;var hasta=c.FechaBaja.HasValue && c.FechaBaja.Value.Date<fin?c.FechaBaja.Value.Date:fin;
    var amount=c.TarifaAplicada.Value;
    rows.Add(new DetailRow{idServicioSocio=c.idServicioSocio,idSocio=c.idSocio,idServicio=c.idServicio,CodigoSocioExterno=c.CodigoSocioExterno,VigenteDesde=desde,VigenteHasta=hasta,ImporteExportado=amount});
    csv.AppendLine(Csv("1",idLote.ToString(CultureInfo.InvariantCulture),periodo,c.CodigoSocioExterno,c.idServicioSocio.ToString(CultureInfo.InvariantCulture),"INTERNET","","MENSUAL",desde.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),hasta.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"1",amount.ToString("0.00",CultureInfo.InvariantCulture),""));
   }
   string contenido=csv.ToString();
   foreach(var row in rows)db.Database.ExecuteSqlCommand(@"INSERT dbo.LoteFacturacionInternetDetalle(idLote,idServicioSocio,idSocio,idServicio,CodigoSocioExterno,VigenteDesde,VigenteHasta,ImporteExportado)
    VALUES(@lote,@conexion,@socio,@servicio,@codigo,@desde,@hasta,@importe)",P("@lote",idLote),P("@conexion",row.idServicioSocio),P("@socio",row.idSocio),P("@servicio",row.idServicio),P("@codigo",row.CodigoSocioExterno),P("@desde",row.VigenteDesde),P("@hasta",row.VigenteHasta),P("@importe",row.ImporteExportado));
   db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternet SET NombreArchivoSalida=@nombre,ContenidoSalida=@contenido,HashSalida=@hash WHERE idLote=@id",P("@nombre",nombre),P("@contenido",contenido),P("@hash",Hash(contenido)),P("@id",idLote));
   db.Database.ExecuteSqlCommand("INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoNuevo,Motivo,Datos) VALUES('INTERNET',@u,@e,'ExportarFacturacionInternet','Exportado','Exportación de padrón facturable',@datos)",P("@u",LaboralAccess.Usuario(Principal)),P("@e",Empleado),P("@datos",JsonConvert.SerializeObject(new{idLote,periodo,cantidad=contracts.Count})));
   return new{idLote,Periodo=periodo,Estado="Exportado",NombreArchivoSalida=nombre,CantidadFilas=contracts.Count,Reutilizado=false};
  });}

  [HttpGet,Route("lotes/{id:int}/archivo")]
  public IHttpActionResult DescargarArchivo(int id){
   try{
    ExigirFacturacion();
    var file=db.Database.SqlQuery<FileRow>("SELECT NombreArchivoSalida,ContenidoSalida FROM dbo.LoteFacturacionInternet WHERE idLote=@id AND Estado<>'Anulado'",P("@id",id)).SingleOrDefault();
    if(file==null)return NotFound();
    var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new UTF8Encoding(true).GetBytes(file.ContenidoSalida))};
    response.Content.Headers.ContentType=new MediaTypeHeaderValue("text/csv");response.Content.Headers.ContentType.CharSet="utf-8";
    response.Content.Headers.ContentDisposition=new ContentDispositionHeaderValue("attachment"){FileName=file.NombreArchivoSalida};
    return ResponseMessage(response);
   }catch(UnauthorizedAccessException){return StatusCode(HttpStatusCode.Forbidden);}
  }
  public sealed class FileRow {public string NombreArchivoSalida {get;set;} public string ContenidoSalida {get;set;}}

  [HttpPost,Route("importaciones/validar")]
  public IHttpActionResult ValidarRespuesta(JObject data){return Write(()=>{
   string filename=(string)data?["NombreArchivo"]??"respuesta.csv",content=(string)data?["Contenido"];
   if(string.IsNullOrWhiteSpace(content) || content.Length>5*1024*1024)throw new ArgumentException("Seleccioná un CSV de respuesta de hasta 5 MB.");
   var parsed=ParseCsv(content);if(parsed.Count<2)throw new ArgumentException("El CSV no contiene filas de respuesta.");
   string[] expected={"version_formato","id_lote","id_socio_externo","id_servicio_cooperativa","resultado","referencia_factura_externa","numero_factura","fecha_emision","periodo","importe_total","fecha_vencimiento","link_pago","motivo_rechazo"};
   var header=parsed[0].Select(x=>x.Trim().Trim('\uFEFF').ToLowerInvariant()).ToList();
   if(header.Count!=expected.Length || !header.SequenceEqual(expected))throw new ArgumentException("Los encabezados no coinciden con la plantilla de respuesta de Internet.");
   var records=new List<Dictionary<string,string>>();
   for(int i=1;i<parsed.Count;i++){
    if(parsed[i].All(string.IsNullOrWhiteSpace))continue;
    if(parsed[i].Count!=expected.Length)throw new ArgumentException("La fila "+(i+1)+" tiene una cantidad de columnas incorrecta.");
    records.Add(expected.Select((key,index)=>new{key,value=parsed[i][index].Trim()}).ToDictionary(x=>x.key,x=>x.value));
   }
   if(!records.Any())throw new ArgumentException("El CSV no contiene filas de respuesta.");
   int loteId; if(!int.TryParse(records[0]["id_lote"],NumberStyles.None,CultureInfo.InvariantCulture,out loteId))throw new ArgumentException("El identificador del lote no es válido.");
   var lote=db.Database.SqlQuery<LotRow>("SELECT idLote,Periodo,Estado,NombreArchivoSalida,CantidadFilas,FechaCreacionUtc,FechaValidacionUtc,FechaAplicacionUtc,Errores FROM dbo.LoteFacturacionInternet WITH(UPDLOCK,HOLDLOCK) WHERE idLote=@id",P("@id",loteId)).SingleOrDefault();
   if(lote==null)throw new ArgumentException("El lote no existe.");
   if(lote.Estado!="Exportado")throw new ArgumentException("Solo se pueden validar respuestas para lotes en estado Exportado.");
   var expectedDetails=db.Database.SqlQuery<DetailRow>("SELECT idServicioSocio,idSocio,idServicio,CodigoSocioExterno,VigenteDesde,VigenteHasta,ImporteExportado FROM dbo.LoteFacturacionInternetDetalle WHERE idLote=@id",P("@id",loteId)).ToDictionary(x=>x.idServicioSocio);
   var errors=new List<string>();var responses=new List<ResponseRow>();var seen=new HashSet<int>();
   foreach(var row in records){
    int rowLot,connection;
    if(row["version_formato"]!="1")errors.Add("Fila: versión de formato no soportada.");
    if(!int.TryParse(row["id_lote"],NumberStyles.None,CultureInfo.InvariantCulture,out rowLot) || rowLot!=loteId)errors.Add("Fila: el identificador de lote no coincide.");
    if(row["periodo"]!=lote.Periodo)errors.Add("Fila: el período no coincide con el lote.");
    if(!int.TryParse(row["id_servicio_cooperativa"],NumberStyles.None,CultureInfo.InvariantCulture,out connection) || !expectedDetails.ContainsKey(connection)){errors.Add("Fila: servicio de cooperativa inexistente en el lote.");continue;}
    if(!seen.Add(connection)){errors.Add("Fila: contrato repetido "+connection+".");continue;}
    var detail=expectedDetails[connection];
    if(row["id_socio_externo"]!=detail.CodigoSocioExterno)errors.Add("Fila: el código de socio no coincide para el servicio "+connection+".");
    string result=row["resultado"].ToUpperInvariant();var response=new ResponseRow{idLote=loteId,idServicioSocio=connection,CodigoSocioExterno=row["id_socio_externo"],Resultado=result,ReferenciaFacturaExterna=row["referencia_factura_externa"],NumeroFactura=row["numero_factura"],Periodo=row["periodo"],LinkPago=row["link_pago"],MotivoRechazo=row["motivo_rechazo"]};
    if(result=="RECHAZADA"){
     if(string.IsNullOrWhiteSpace(response.MotivoRechazo))errors.Add("Fila: falta el motivo de rechazo para el servicio "+connection+".");
    } else if(result=="FACTURADA"){
     response.FechaEmision=ReadDate(row["fecha_emision"]);response.FechaVencimiento=ReadDate(row["fecha_vencimiento"]);decimal amount;
     if(string.IsNullOrWhiteSpace(response.ReferenciaFacturaExterna) || response.ReferenciaFacturaExterna.Length>200)errors.Add("Fila: referencia externa inválida para el servicio "+connection+".");
     if(string.IsNullOrWhiteSpace(response.NumeroFactura) || response.NumeroFactura.Length>100)errors.Add("Fila: número de factura inválido para el servicio "+connection+".");
     if(!decimal.TryParse(row["importe_total"],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out amount) || amount<=0)errors.Add("Fila: importe inválido para el servicio "+connection+".");else response.ImporteTotal=amount;
     if(!response.FechaEmision.HasValue || !response.FechaVencimiento.HasValue || response.FechaVencimiento<response.FechaEmision)errors.Add("Fila: fechas de factura inválidas para el servicio "+connection+".");
     Uri uri;if(!Uri.TryCreate(response.LinkPago,UriKind.Absolute,out uri) || uri.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || response.LinkPago.Length>500)errors.Add("Fila: el enlace de pago debe ser una URL HTTPS válida para el servicio "+connection+".");
     if(!string.IsNullOrWhiteSpace(response.MotivoRechazo))errors.Add("Fila: una factura emitida no debe incluir motivo de rechazo.");
    } else errors.Add("Fila: resultado debe ser FACTURADA o RECHAZADA para el servicio "+connection+".");
    responses.Add(response);
   }
   foreach(var missing in expectedDetails.Keys.Except(seen))errors.Add("Falta la respuesta para el servicio "+missing+".");
   if(errors.Count>0){
    db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternet SET NombreArchivoRespuesta=@nombre,ContenidoRespuesta=@contenido,HashRespuesta=@hash,Errores=@errores WHERE idLote=@id",P("@nombre",filename),P("@contenido",content),P("@hash",Hash(content)),P("@errores",JsonConvert.SerializeObject(errors)),P("@id",loteId));
    return new{idLote=loteId,Estado="ConErrores",CantidadFilas=records.Count,CantidadValidas=0,CantidadAplicadas=0,Errores=errors};
   }
   foreach(var response in responses){
    db.Database.ExecuteSqlCommand(@"UPDATE dbo.LoteFacturacionInternetDetalle SET Resultado=@resultado,ReferenciaFacturaExterna=@referencia,NumeroFactura=@numero,
     FechaEmision=@emision,ImporteFactura=@importe,FechaVencimiento=@vencimiento,LinkPago=@link,MotivoRechazo=@motivo WHERE idLote=@lote AND idServicioSocio=@conexion",
     P("@resultado",response.Resultado),P("@referencia",response.Resultado=="FACTURADA"?response.ReferenciaFacturaExterna:null),P("@numero",response.Resultado=="FACTURADA"?response.NumeroFactura:null),
     P("@emision",response.FechaEmision),P("@importe",response.ImporteTotal),P("@vencimiento",response.FechaVencimiento),P("@link",response.Resultado=="FACTURADA"?response.LinkPago:null),P("@motivo",response.Resultado=="RECHAZADA"?response.MotivoRechazo:null),P("@lote",loteId),P("@conexion",response.idServicioSocio));
   }
   db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternet SET Estado='Validado',NombreArchivoRespuesta=@nombre,ContenidoRespuesta=@contenido,HashRespuesta=@hash,Errores=NULL,FechaValidacionUtc=SYSUTCDATETIME() WHERE idLote=@id",P("@nombre",filename),P("@contenido",content),P("@hash",Hash(content)),P("@id",loteId));
   db.Database.ExecuteSqlCommand("INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoAnterior,EstadoNuevo,Motivo,Datos) VALUES('INTERNET',@u,@e,'ValidarFacturasInternet','Exportado','Validado','Respuesta externa validada',@datos)",P("@u",LaboralAccess.Usuario(Principal)),P("@e",Empleado),P("@datos",JsonConvert.SerializeObject(new{idLote=loteId,cantidad=responses.Count})));
   return new{idLote=loteId,Periodo=lote.Periodo,Estado="Validado",CantidadFilas=records.Count,CantidadValidas=responses.Count,CantidadAplicadas=0,CantidadFacturadas=responses.Count(x=>x.Resultado=="FACTURADA"),CantidadRechazadas=responses.Count(x=>x.Resultado=="RECHAZADA"),Errores=new string[0]};
  });}
  private static DateTime? ReadDate(string value){DateTime parsed;return DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed)?(DateTime?)parsed.Date:null;}
  private static List<string> DeserializeErrors(string value){try{return string.IsNullOrWhiteSpace(value)?new List<string>():JsonConvert.DeserializeObject<List<string>>(value)??new List<string>();}catch{return new List<string>();}}

  [HttpPost,Route("lotes/{id:int}/aplicar")]
  public IHttpActionResult Aplicar(int id){return Write(()=>{
   var lot=db.Database.SqlQuery<LotRow>("SELECT idLote,Periodo,Estado,NombreArchivoSalida,CantidadFilas,FechaCreacionUtc,FechaValidacionUtc,FechaAplicacionUtc,Errores FROM dbo.LoteFacturacionInternet WITH(UPDLOCK,HOLDLOCK) WHERE idLote=@id",P("@id",id)).SingleOrDefault();
   if(lot==null)throw new ArgumentException("El lote no existe.");if(lot.Estado!="Validado")throw new ArgumentException("Solo se pueden aplicar lotes validados.");
   var details=db.Database.SqlQuery<ApplyRow>(@"SELECT d.idServicioSocio,d.idSocio,d.idServicio,d.Resultado,d.ReferenciaFacturaExterna,d.NumeroFactura,d.FechaEmision,d.ImporteFactura,d.FechaVencimiento,d.LinkPago,d.MotivoRechazo
    FROM dbo.LoteFacturacionInternetDetalle d WHERE d.idLote=@id ORDER BY d.idServicioSocio",P("@id",id)).ToList();
   if(details.Count!=lot.CantidadFilas || details.Any(x=>string.IsNullOrEmpty(x.Resultado)))throw new ArgumentException("El detalle del lote está incompleto; volvé a validar el archivo.");
   int created=0,rejected=0;int employee=Empleado.Value;
   foreach(var d in details){
    if(d.Resultado=="RECHAZADA"){rejected++;continue;}
    int invoice=db.Database.SqlQuery<int>(@"INSERT dbo.Factura(idSocio,idEmpleado,idServicio,MontoTotal,FechaEmision,FechaCaducidad,LinkPago,Pagada,FechaPago,Observaciones,Activo)
     OUTPUT INSERTED.idFactura VALUES(@socio,@empleado,@servicio,@importe,@emision,@vencimiento,@link,0,NULL,@observaciones,1)",
     P("@socio",d.idSocio),P("@empleado",employee),P("@servicio",d.idServicio),P("@importe",d.ImporteFactura),P("@emision",d.FechaEmision),P("@vencimiento",d.FechaVencimiento),P("@link",d.LinkPago),P("@observaciones","Internet · período "+lot.Periodo+" · Factura externa "+d.NumeroFactura)).Single();
    db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternetDetalle SET idFactura=@factura WHERE idLote=@lote AND idServicioSocio=@conexion",P("@factura",invoice),P("@lote",id),P("@conexion",d.idServicioSocio));created++;
   }
   db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternet SET Estado='Aplicado',FechaAplicacionUtc=SYSUTCDATETIME() WHERE idLote=@id",P("@id",id));
   db.Database.ExecuteSqlCommand("INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoAnterior,EstadoNuevo,Motivo,Datos) VALUES('INTERNET',@u,@e,'AplicarFacturasInternet','Validado','Aplicado','Importación de facturas del período',@datos)",P("@u",LaboralAccess.Usuario(Principal)),P("@e",employee),P("@datos",JsonConvert.SerializeObject(new{idLote=id,creadas=created,rechazadas=rejected})));
   return new{idLote=id,Estado="Aplicado",CantidadAplicadas=created,CantidadRechazadas=rejected};
  });}
  public sealed class ApplyRow {
   public int idServicioSocio {get;set;} public int idSocio {get;set;} public int idServicio {get;set;} public string Resultado {get;set;}
   public string ReferenciaFacturaExterna {get;set;} public string NumeroFactura {get;set;} public DateTime? FechaEmision {get;set;}
   public decimal? ImporteFactura {get;set;} public DateTime? FechaVencimiento {get;set;} public string LinkPago {get;set;} public string MotivoRechazo {get;set;}
  }

  [HttpPost,Route("lotes/{id:int}/anular")]
  public IHttpActionResult Anular(int id,JObject data){return Write(()=>{
   string motivo=((string)data?["Motivo"]??"").Trim();if(motivo.Length<3 || motivo.Length>500)throw new ArgumentException("Indicá un motivo de anulación de 3 a 500 caracteres.");
   var lot=db.Database.SqlQuery<LotRow>("SELECT idLote,Periodo,Estado,NombreArchivoSalida,CantidadFilas,FechaCreacionUtc,FechaValidacionUtc,FechaAplicacionUtc,Errores FROM dbo.LoteFacturacionInternet WITH(UPDLOCK,HOLDLOCK) WHERE idLote=@id",P("@id",id)).SingleOrDefault();
   if(lot==null)throw new ArgumentException("El lote no existe.");if(lot.Estado!="Exportado")throw new ArgumentException("Solo se puede anular un lote exportado que todavía no tenga una respuesta validada.");
   db.Database.ExecuteSqlCommand("UPDATE dbo.LoteFacturacionInternet SET Estado='Anulado',MotivoAnulacion=@motivo WHERE idLote=@id",P("@motivo",motivo),P("@id",id));
   db.Database.ExecuteSqlCommand("INSERT dbo.AuditoriaLaboral(Area,idUsuarioSistema,idEmpleado,Accion,EstadoAnterior,EstadoNuevo,Motivo,Datos) VALUES('INTERNET',@u,@e,'AnularLoteFacturacionInternet','Exportado','Anulado',@motivo,@datos)",P("@u",LaboralAccess.Usuario(Principal)),P("@e",Empleado),P("@motivo",motivo),P("@datos",JsonConvert.SerializeObject(new{idLote=id,Periodo=lot.Periodo})));
   return new{idLote=id,Estado="Anulado"};
  });}

  private static List<List<string>> ParseCsv(string content){
   var rows=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quoted=false;
   for(int i=0;i<content.Length;i++){
    char c=content[i];
    if(quoted){if(c=='"' && i+1<content.Length && content[i+1]=='"'){field.Append('"');i++;}else if(c=='"')quoted=false;else field.Append(c);}
    else if(c=='"'){if(field.Length!=0)throw new ArgumentException("El CSV tiene comillas mal formadas.");quoted=true;}
    else if(c==','){row.Add(field.ToString());field.Clear();}
    else if(c=='\r' || c=='\n'){
     if(c=='\r' && i+1<content.Length && content[i+1]=='\n')i++;
     row.Add(field.ToString());field.Clear();
     if(row.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(row);
     row=new List<string>();
    } else field.Append(c);
   }
   if(quoted)throw new ArgumentException("El CSV termina dentro de un campo entre comillas.");
   if(field.Length>0 || row.Count>0){row.Add(field.ToString());if(row.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(row);}
   return rows;
  }
 }
}
