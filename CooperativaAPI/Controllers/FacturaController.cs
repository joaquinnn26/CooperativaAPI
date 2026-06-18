using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.DTOs;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/factura")]
    public class FacturaController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        private string CalcularEstado(bool pagada, DateTime fechaCaducidad)
        {
            if (pagada)
                return "Pagada";

            if (fechaCaducidad.Date < DateTime.Now.Date)
                return "Vencida";

            return "Pendiente";
        }

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var facturas = (
                    from f in db.Factura
                    join s in db.Socio on f.idSocio equals s.idSocio
                    join p in db.Persona on s.idPersona equals p.idPersona
                    join e in db.Empleado on f.idEmpleado equals e.idEmpleado
                    join se in db.Servicio on f.idServicio equals se.idServicio
                    where f.Activo
                    select new
                    {
                        f.idFactura,
                        f.idSocio,
                        f.idEmpleado,
                        f.idServicio,
                        f.MontoTotal,
                        f.FechaEmision,
                        f.FechaCaducidad,
                        f.LinkPago,
                        f.Pagada,
                        f.FechaPago,
                        f.Observaciones,
                        Socio = new
                        {
                            s.idSocio,
                            p.Nombre,
                            p.Apellido,
                            p.Documento,
                            p.Email,
                            p.Telefono
                        },
                        Empleado = new
                        {
                            e.idEmpleado
                        },
                        Servicio = new
                        {
                            se.idServicio,
                            se.Nombre
                        }
                    }
                ).ToList()
                .Select(f => new
                {
                    f.idFactura,
                    f.idSocio,
                    f.idEmpleado,
                    f.idServicio,
                    f.MontoTotal,
                    f.FechaEmision,
                    f.FechaCaducidad,
                    f.LinkPago,
                    f.Pagada,
                    f.FechaPago,
                    f.Observaciones,
                    Estado = CalcularEstado(f.Pagada, f.FechaCaducidad),
                    f.Socio,
                    f.Empleado,
                    f.Servicio
                });

                return Ok(facturas);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpGet]
        [Route("socio/{idSocio}/historial/{meses}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetHistorialBySocio(int idSocio, int meses)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar facturas de otro socio.");

                if (meses <= 0)
                    meses = 6;

                var fechaDesde = DateTime.Now.AddMonths(-meses);

                var facturas = (
                    from f in db.Factura
                    join s in db.Socio on f.idSocio equals s.idSocio
                    join p in db.Persona on s.idPersona equals p.idPersona
                    join e in db.Empleado on f.idEmpleado equals e.idEmpleado
                    join se in db.Servicio on f.idServicio equals se.idServicio
                    where f.Activo
                          && f.idSocio == idSocio
                          && f.FechaEmision >= fechaDesde
                    orderby f.FechaEmision descending
                    select new
                    {
                        f.idFactura,
                        f.idSocio,
                        f.idEmpleado,
                        f.idServicio,
                        f.MontoTotal,
                        f.FechaEmision,
                        f.FechaCaducidad,
                        f.LinkPago,
                        f.Pagada,
                        f.FechaPago,
                        f.Observaciones,
                        Socio = new
                        {
                            s.idSocio,
                            p.Nombre,
                            p.Apellido,
                            p.Documento
                        },
                        Servicio = new
                        {
                            se.idServicio,
                            se.Nombre
                        }
                    }
                ).ToList()
                .Select(f => new
                {
                    f.idFactura,
                    f.idSocio,
                    f.idEmpleado,
                    f.idServicio,
                    f.MontoTotal,
                    f.FechaEmision,
                    f.FechaCaducidad,
                    f.LinkPago,
                    f.Pagada,
                    f.FechaPago,
                    f.Observaciones,
                    Estado = CalcularEstado(f.Pagada, f.FechaCaducidad),
                    f.Socio,
                    f.Servicio
                });

                return Ok(facturas);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpGet]
        [Route("socio/{idSocio}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar facturas de otro socio.");

                var facturas = (
                    from f in db.Factura
                    join s in db.Socio on f.idSocio equals s.idSocio
                    join p in db.Persona on s.idPersona equals p.idPersona
                    join e in db.Empleado on f.idEmpleado equals e.idEmpleado
                    join se in db.Servicio on f.idServicio equals se.idServicio
                    where f.Activo && f.idSocio == idSocio
                    select new
                    {
                        f.idFactura,
                        f.idSocio,
                        f.idEmpleado,
                        f.idServicio,
                        f.MontoTotal,
                        f.FechaEmision,
                        f.FechaCaducidad,
                        f.LinkPago,
                        f.Pagada,
                        f.FechaPago,
                        f.Observaciones,
                        Socio = new
                        {
                            s.idSocio,
                            p.Nombre,
                            p.Apellido,
                            p.Documento,
                            p.Email,
                            p.Telefono
                        },
                        Empleado = new
                        {
                            e.idEmpleado
                        },
                        Servicio = new
                        {
                            se.idServicio,
                            se.Nombre
                        }
                    }
                ).ToList()
                .Select(f => new
                {
                    f.idFactura,
                    f.idSocio,
                    f.idEmpleado,
                    f.idServicio,
                    f.MontoTotal,
                    f.FechaEmision,
                    f.FechaCaducidad,
                    f.LinkPago,
                    f.Pagada,
                    f.FechaPago,
                    f.Observaciones,
                    Estado = CalcularEstado(f.Pagada, f.FechaCaducidad),
                    f.Socio,
                    f.Empleado,
                    f.Servicio
                });

                return Ok(facturas);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("impagas")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult GetImpagas()
        {
            try
            {
                var facturas = (
                    from f in db.Factura
                    join s in db.Socio on f.idSocio equals s.idSocio
                    join p in db.Persona on s.idPersona equals p.idPersona
                    join e in db.Empleado on f.idEmpleado equals e.idEmpleado
                    join se in db.Servicio on f.idServicio equals se.idServicio
                    where f.Activo && !f.Pagada
                    select new
                    {
                        f.idFactura,
                        f.idSocio,
                        f.idEmpleado,
                        f.idServicio,
                        f.MontoTotal,
                        f.FechaEmision,
                        f.FechaCaducidad,
                        f.LinkPago,
                        f.Pagada,
                        f.FechaPago,
                        f.Observaciones,
                        Socio = new
                        {
                            s.idSocio,
                            p.Nombre,
                            p.Apellido,
                            p.Documento,
                            p.Email,
                            p.Telefono
                        },
                        Empleado = new
                        {
                            e.idEmpleado
                        },
                        Servicio = new
                        {
                            se.idServicio,
                            se.Nombre
                        }
                    }
                ).ToList()
                .Select(f => new
                {
                    f.idFactura,
                    f.idSocio,
                    f.idEmpleado,
                    f.idServicio,
                    f.MontoTotal,
                    f.FechaEmision,
                    f.FechaCaducidad,
                    f.LinkPago,
                    f.Pagada,
                    f.FechaPago,
                    f.Observaciones,
                    Estado = CalcularEstado(f.Pagada, f.FechaCaducidad),
                    f.Socio,
                    f.Empleado,
                    f.Servicio
                });

                return Ok(facturas);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear([FromBody] FacturaCreateRequest request)
        {
            if (request == null)
                return BadRequest("Datos invalidos.");

            try
            {
                var socio = db.Socio.Find(request.idSocio);
                if (socio == null)
                    return BadRequest("Socio no encontrado.");

                var empleado = db.Empleado.Find(request.idEmpleado);
                if (empleado == null)
                    return BadRequest("Empleado no encontrado.");

                var servicio = db.Servicio.Find(request.idServicio);
                if (servicio == null)
                    return BadRequest("Servicio no encontrado.");

                var fechaEmision = (request.FechaEmision ?? DateTime.Now).Date;
                var validacion = ValidarDatosFactura(
                    request.idSocio,
                    request.idServicio,
                    request.MontoTotal,
                    fechaEmision,
                    request.FechaCaducidad,
                    request.LinkPago);
                if (validacion != null) return BadRequest(validacion);

                var factura = new Factura
                {
                    idSocio = request.idSocio,
                    idEmpleado = request.idEmpleado,
                    idServicio = request.idServicio,
                    MontoTotal = request.MontoTotal,
                    FechaEmision = fechaEmision,
                    FechaCaducidad = request.FechaCaducidad,
                    LinkPago = request.LinkPago,
                    Pagada = false,
                    FechaPago = null,
                    Observaciones = request.Observaciones,
                    Activo = true
                };

                db.Factura.Add(factura);
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Factura creada correctamente.",
                    idFactura = factura.idFactura
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Editar(int id, [FromBody] FacturaUpdateRequest request)
        {
            if (request == null)
                return BadRequest("Datos invalidos.");

            try
            {
                var factura = db.Factura.Find(id);
                if (factura == null || !factura.Activo)
                    return NotFound();

                var servicio = db.Servicio.Find(request.idServicio);
                if (servicio == null)
                    return BadRequest("Servicio no encontrado.");

                var validacion = ValidarDatosFactura(
                    factura.idSocio,
                    request.idServicio,
                    request.MontoTotal,
                    factura.FechaEmision,
                    request.FechaCaducidad,
                    request.LinkPago);
                if (validacion != null) return BadRequest(validacion);

                factura.idServicio = request.idServicio;
                factura.MontoTotal = request.MontoTotal;
                factura.FechaCaducidad = request.FechaCaducidad;
                factura.LinkPago = request.LinkPago;
                factura.Observaciones = request.Observaciones;

                db.SaveChanges();

                return Ok("Factura actualizada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("pagar/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult MarcarPago(int id, [FromBody] FacturaPagoRequest request)
        {
            if (request == null)
                return BadRequest("Datos invalidos.");

            try
            {
                var factura = db.Factura.Find(id);
                if (factura == null || !factura.Activo)
                    return NotFound();

                if (!request.Pagada)
                    return Content(HttpStatusCode.Conflict, "Una factura pagada no puede volver a estado pendiente.");

                if (factura.Pagada)
                    return Ok("La factura ya estaba pagada.");

                var fechaPago = request.FechaPago ?? DateTime.Now;
                if (fechaPago > DateTime.Now.AddMinutes(5))
                    return BadRequest("La fecha de pago no puede estar en el futuro.");

                factura.Pagada = true;
                factura.FechaPago = fechaPago;

                db.SaveChanges();

                return Ok("Estado de pago actualizado.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete]
        [Route("{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var factura = db.Factura.Find(id);
                if (factura == null)
                    return NotFound();

                if (factura.Pagada)
                    return Content(HttpStatusCode.Conflict, "No se puede dar de baja una factura pagada.");

                factura.Activo = false;
                db.SaveChanges();

                return Ok("Factura eliminada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private string ValidarDatosFactura(
            int idSocio,
            int idServicio,
            decimal monto,
            DateTime fechaEmision,
            DateTime fechaCaducidad,
            string linkPago)
        {
            if (monto <= 0)
                return "El monto debe ser mayor a cero.";

            if (fechaCaducidad.Date < fechaEmision.Date)
                return "La fecha de vencimiento no puede ser anterior a la fecha de emision.";

            if (!string.IsNullOrWhiteSpace(linkPago))
            {
                Uri uri;
                if (!Uri.TryCreate(linkPago.Trim(), UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return "El link de pago debe ser una URL http o https valida.";
            }

            var servicioAsignado = db.ServicioSocio.Any(ss =>
                ss.idSocio == idSocio &&
                ss.idServicio == idServicio &&
                ss.Activo == true);

            return servicioAsignado ? null : "El servicio no esta activo para el socio seleccionado.";
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
