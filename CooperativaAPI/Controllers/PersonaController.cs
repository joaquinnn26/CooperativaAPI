using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/persona")]
    public class PersonaController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            var personas = db.Persona
                .Select(p => new
                {
                    p.idPersona,
                    NombreCompleto = p.Nombre + " " + p.Apellido,
                    p.Documento
                })
                .ToList();

            return Ok(personas);
        }
    }
}