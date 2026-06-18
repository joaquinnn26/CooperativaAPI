using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/admin/dashboard")]
    [JwtAuthorize(Roles = "Administrador")]
    public class AdminDashboardController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();

        [HttpGet]
        [Route("metricas")]
        public IHttpActionResult ObtenerMetricas()
        {
            try
            {
                var sociosTotal = db.Socio.Count();
                var sociosActivos = db.Socio.Count(s => s.Activo == true);
                var sociosInactivos = sociosTotal - sociosActivos;

                var servicios = db.Servicio.Count();
                var serviciosAsignados = db.ServicioSocio.Count();

                var solicitudes = db.SolicitudServicio.Count();
                var solicitudesPendientes = db.SolicitudServicio.Count(s => s.FechaResolucion == null);

                var facturasActivas = db.Factura.Where(f => f.Activo);
                var facturasTotal = facturasActivas.Count();
                var facturasPendientes = facturasActivas.Count(f => !f.Pagada);
                var facturasPagadas = facturasActivas.Count(f => f.Pagada);
                var facturasMontoPendiente = facturasActivas
                    .Where(f => !f.Pagada)
                    .Select(f => (decimal?)f.MontoTotal)
                    .Sum() ?? 0;

                var reclamosTotal = db.Reclamo.Count(r => r.Activo);
                var reclamosActivos = db.Reclamo.Count(r => r.Activo && r.idEstadoReclamo != 3);

                var turnosTotal = db.Turnos.Count();
                var turnosPendientes = db.Turnos.Count(t =>
                    t.IdEstadoTurno == 1 ||
                    t.IdEstadoTurno == 3
                );
                var turnosConfirmados = db.Turnos.Count(t =>
                    t.IdEstadoTurno == 2 ||
                    t.IdEstadoTurno == 6
                );

                return Ok(new
                {
                    sociosTotal,
                    sociosActivos,
                    sociosInactivos,

                    servicios,
                    serviciosAsignados,

                    solicitudes,
                    solicitudesPendientes,

                    facturasTotal,
                    facturasPendientes,
                    facturasPagadas,
                    facturasMontoPendiente,

                    reclamosTotal,
                    reclamosActivos,

                    turnosTotal,
                    turnosPendientes,
                    turnosConfirmados,

                    generadoEn = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener metricas del dashboard: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
