using System;
using System.Linq;
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
    }
}