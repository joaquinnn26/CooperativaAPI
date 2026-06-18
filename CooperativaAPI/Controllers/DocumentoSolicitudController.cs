using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/documentosolicitud")]
    public class DocumentoSolicitudController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("solicitud/{idSolicitud}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySolicitud(int idSolicitud)
        {
            try
            {
                var solicitud = db.SolicitudServicio.Find(idSolicitud);
                if (solicitud == null) return NotFound();
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != solicitud.idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar documentos de otra solicitud.");

                var documentos = db.DocumentoSolicitud
                    .Where(d => d.idSolicitudServicio == idSolicitud)
                    .Select(d => new
                    {
                        d.idDocumentoSolicitud,
                        d.idSolicitudServicio,
                        d.idRequisitoServicio,
                        d.NombreArchivo,
                        d.RutaArchivo,
                        d.TipoArchivo,
                        d.FechaSubida
                    })
                    .ToList();

                return Ok(documentos);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("upload")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult Upload()
        {
            try
            {
                var request = HttpContext.Current.Request;

                var archivo = request.Files["archivo"];
                var rawSolicitud = request.Form["idSolicitudServicio"];
                var rawRequisito = request.Form["idRequisitoServicio"];

                if (archivo == null)
                    return BadRequest("Debe enviar un archivo.");

                if (!int.TryParse(rawSolicitud, out int idSolicitudServicio))
                    return BadRequest("idSolicitudServicio inválido.");

                if (!int.TryParse(rawRequisito, out int idRequisitoServicio))
                    return BadRequest("idRequisitoServicio inválido.");

                var solicitud = db.SolicitudServicio.Find(idSolicitudServicio);
                if (solicitud == null)
                    return BadRequest("La solicitud no existe.");
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != solicitud.idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes subir documentos a una solicitud ajena.");
                if (solicitud.FechaResolucion.HasValue)
                    return Content(HttpStatusCode.Conflict, "No se pueden modificar documentos de una solicitud cerrada.");

                bool requisitoValido = db.RequisitoServicio.Any(r =>
                    r.idRequisitoServicio == idRequisitoServicio &&
                    r.idServicio == solicitud.idServicio &&
                    r.Activo == true);
                if (!requisitoValido)
                    return BadRequest("El requisito no existe, esta inactivo o no pertenece al servicio solicitado.");

                var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(archivo.FileName)?.ToLower();

                if (string.IsNullOrEmpty(extension) || !extensionesPermitidas.Contains(extension))
                    return BadRequest("Tipo de archivo no permitido.");
                if (archivo.ContentLength <= 0)
                    return BadRequest("El archivo esta vacio.");
                if (archivo.ContentLength > 5 * 1024 * 1024)
                    return BadRequest("El archivo no puede superar 5 MB.");

                var carpeta = HttpContext.Current.Server.MapPath("~/Uploads/DocumentosSolicitud");
                if (!Directory.Exists(carpeta))
                    Directory.CreateDirectory(carpeta);

                var nombreGenerado = $"{Guid.NewGuid()}{extension}";
                var rutaFisica = Path.Combine(carpeta, nombreGenerado);
                archivo.SaveAs(rutaFisica);

                var anterior = db.DocumentoSolicitud.FirstOrDefault(d =>
                    d.idSolicitudServicio == idSolicitudServicio &&
                    d.idRequisitoServicio == idRequisitoServicio);

                if (anterior != null)
                {
                    var rutaAnterior = HttpContext.Current.Server.MapPath(anterior.RutaArchivo);
                    if (!string.IsNullOrWhiteSpace(rutaAnterior) && File.Exists(rutaAnterior))
                        File.Delete(rutaAnterior);
                    db.DocumentoSolicitud.Remove(anterior);
                }

                var documento = new DocumentoSolicitud
                {
                    idSolicitudServicio = idSolicitudServicio,
                    idRequisitoServicio = idRequisitoServicio,
                    NombreArchivo = archivo.FileName,
                    RutaArchivo = "/Uploads/DocumentosSolicitud/" + nombreGenerado,
                    TipoArchivo = archivo.ContentType,
                    FechaSubida = DateTime.Now
                };

                db.DocumentoSolicitud.Add(documento);
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Documento subido correctamente.",
                    documento.idDocumentoSolicitud,
                    documento.idSolicitudServicio,
                    documento.idRequisitoServicio,
                    documento.NombreArchivo,
                    documento.RutaArchivo,
                    documento.TipoArchivo,
                    documento.FechaSubida
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al subir documento: " + ex.Message);
            }
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
