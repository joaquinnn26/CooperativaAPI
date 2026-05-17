using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/cargo")]
    public class CargoController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            var cargos = db.Cargo
                .Select(c => new
                {
                    c.idCargo,
                    c.NombreCargo
                })
                .ToList();

            return Ok(cargos);
        }
    }
}