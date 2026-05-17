using System;
using System.Linq;
using System.Web.Http;
using System.Net;


namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/tipotarifa")]
    public class TipoTarifaController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            var tipos = db.TipoTarifa
                .Select(t => new
                {
                    t.idTipoTarifa,
                    t.NombreTipo
                })
                .ToList();

            return Ok(tipos);
        }
    }
}
