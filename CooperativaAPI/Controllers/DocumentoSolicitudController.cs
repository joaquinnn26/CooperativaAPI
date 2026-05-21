using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/documentosolicitud")]
    public class DocumentoSolicitudController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("solicitud/{idSolicitud}")]
        public IHttpActionResult GetBySolicitud(int idSolicitud)
        {
            try
            {
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

                bool solicitudExiste = db.SolicitudServicio.Any(s => s.idSolicitudServicio == idSolicitudServicio);
                if (!solicitudExiste)
                    return BadRequest("La solicitud no existe.");

                bool requisitoExiste = db.RequisitoServicio.Any(r => r.idRequisitoServicio == idRequisitoServicio);
                if (!requisitoExiste)
                    return BadRequest("El requisito no existe.");

                var extensionesPermitidas = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(archivo.FileName)?.ToLower();

                if (string.IsNullOrEmpty(extension) || !extensionesPermitidas.Contains(extension))
                    return BadRequest("Tipo de archivo no permitido.");

                var carpeta = HttpContext.Current.Server.MapPath("~/Uploads/DocumentosSolicitud");
                if (!Directory.Exists(carpeta))
                    Directory.CreateDirectory(carpeta);

                var nombreGenerado = $"{Guid.NewGuid()}{extension}";
                var rutaFisica = Path.Combine(carpeta, nombreGenerado);
                archivo.SaveAs(rutaFisica);

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
    }
}