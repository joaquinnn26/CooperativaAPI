using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers {
 public partial class LaboralController {
  private byte[] Archivo(JObject data,out string nombre,out string tipo){
   nombre=Path.GetFileName(Texto(data,"Nombre",150,true));tipo=Texto(data,"Tipo",50,true);
   byte[] bytes;try{bytes=Convert.FromBase64String((string)data["Base64"]??"");}catch(FormatException){throw new ArgumentException("Archivo inválido.");}
   if(bytes.Length==0 || bytes.Length>2*1024*1024)throw new ArgumentException("El archivo debe pesar hasta 2 MB.");
   bool valido=(tipo=="application/pdf" && bytes.Take(5).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("%PDF-"))) || (tipo=="image/png" && bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) || (tipo=="image/jpeg" && bytes.Length>3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255);
   if(!valido)throw new ArgumentException("Solo se admiten archivos PDF, PNG o JPEG válidos.");return bytes;
  }
  private IHttpActionResult Descargar(byte[] bytes,string nombre,string tipo){var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(tipo);response.Content.Headers.ContentDisposition=new ContentDispositionHeaderValue("attachment"){FileNameStar=nombre};response.Headers.Add("X-Content-Type-Options","nosniff");response.Headers.CacheControl=new CacheControlHeaderValue{NoStore=true};return ResponseMessage(response);}

  [HttpPost,Route("tareas/{id:int}/evidencias")]
  public IHttpActionResult Evidencia(int id,JObject data){return Write(()=>{
   var t=Task(id);Exigir((int?)t["idEmpleadoAsignado"]==Empleado && PuedeTrabajar(t));Version(t,data);
   if(!new[]{"EnCurso","Observada"}.Contains((string)t["Estado"]))throw new ArgumentException("Solo se pueden adjuntar evidencias mientras se realiza o corrige la tarea.");
   string nombre,tipo;var bytes=Archivo(data,out nombre,out tipo);
   db.Database.ExecuteSqlCommand("INSERT dbo.EvidenciaLaboral(idTarea,idUsuarioSistema,Nombre,Tipo,Contenido) VALUES(@t,@u,@n,@tipo,@b); UPDATE dbo.TareaLaboral SET FechaActualizacionUtc=SYSUTCDATETIME() WHERE idTarea=@t",P("@t",id),P("@u",LaboralAccess.Usuario(Principal)),P("@n",nombre),P("@tipo",tipo),P("@b",bytes));
   Audit((string)t["Area"],id,"AdjuntarEvidencia",(string)t["Estado"],(string)t["Estado"],nombre,new{nombre,tipo,tamano=bytes.Length});return Task(id);
  });}
  [HttpGet,Route("evidencias/{id:int}")]
  public IHttpActionResult DescargarEvidencia(int id){
   var file=Rows("SELECT * FROM dbo.EvidenciaLaboral WHERE idEvidencia=@id",P("@id",id)).SingleOrDefault();if(file==null)return NotFound();if(!PuedeVer(Task((int)file["idTarea"])))return StatusCode(HttpStatusCode.Forbidden);return Descargar((byte[])file["Contenido"],(string)file["Nombre"],(string)file["Tipo"]);
  }
  // Ruta de compatibilidad: lee el archivo compartido en DocumentoPersona.
  [HttpGet,Route("documentos/{id:int}")]
  public IHttpActionResult DescargarDocumento(int id){
   var file=Rows("SELECT * FROM dbo.DocumentoPersona WHERE idDocumentoPersona=@id",P("@id",id)).SingleOrDefault();if(file==null)return NotFound();
   int usuario=LaboralAccess.Usuario(Principal),persona=(int)file["idPersona"];
   bool personal=Rows(@"SELECT 1 Permitido FROM dbo.UsuarioSistema u
    LEFT JOIN dbo.Socio s ON s.idSocio=u.idSocio LEFT JOIN dbo.Empleado e ON e.idEmpleado=u.idEmpleado
    WHERE u.idUsuarioSistema=@u AND COALESCE(s.idPersona,e.idPersona)=@p",P("@u",usuario),P("@p",persona)).Any();
   var task=Rows("SELECT * FROM dbo.TareaLaboral WHERE Categoria='Documentacion' AND OrigenId=@id",P("@id",id)).SingleOrDefault();
   if(!personal && (!User.IsInRole("Administrador") && (task==null || !PuedeVer(task))))return StatusCode(HttpStatusCode.Forbidden);
   return Descargar((byte[])file["Contenido"],(string)file["NombreArchivo"],(string)file["TipoArchivo"]);
  }
  [HttpGet,Route("documentos-solicitud/{id:int}")]
  public IHttpActionResult DescargarDocumentoSolicitud(int id){
   var file=db.DocumentoSolicitud.Find(id);if(file==null)return NotFound();
   var tasks=Rows("SELECT * FROM dbo.TareaLaboral WHERE Categoria IN ('Solicitud','Instalacion') AND OrigenId=@id",P("@id",file.idSolicitudServicio));
   if(!tasks.Any(PuedeVer))return StatusCode(HttpStatusCode.Forbidden);
   string root=System.Web.Hosting.HostingEnvironment.MapPath("~/"),relative=(file.RutaArchivo??"").TrimStart('~','/','\\');
   if(root==null)return NotFound();string path=Path.GetFullPath(Path.Combine(root,relative));
   if(!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))return NotFound();
   return Descargar(File.ReadAllBytes(path),file.NombreArchivo,"application/octet-stream");
  }
 }
}
