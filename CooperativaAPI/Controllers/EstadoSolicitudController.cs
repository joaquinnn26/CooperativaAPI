using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/estadosolicitud")]
    public class EstadoSolicitudServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var estados = db.EstadoSolicitudServicio
                    .Select(e => new
                    {
                        e.idEstadoSolicitud,
                        e.NombreEstado
                    })
                    .ToList();

                return Ok(estados);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
