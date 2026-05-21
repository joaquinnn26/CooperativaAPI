using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.DTOs;

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
        public IHttpActionResult GetHistorialBySocio(int idSocio, int meses)
        {
            try
            {
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
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
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

                var factura = new Factura
                {
                    idSocio = request.idSocio,
                    idEmpleado = request.idEmpleado,
                    idServicio = request.idServicio,
                    MontoTotal = request.MontoTotal,
                    FechaEmision = request.FechaEmision ?? DateTime.Now,
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
        public IHttpActionResult MarcarPago(int id, [FromBody] FacturaPagoRequest request)
        {
            if (request == null)
                return BadRequest("Datos invalidos.");

            try
            {
                var factura = db.Factura.Find(id);
                if (factura == null || !factura.Activo)
                    return NotFound();

                factura.Pagada = request.Pagada;
                factura.FechaPago = request.Pagada
                    ? (request.FechaPago ?? DateTime.Now)
                    : (DateTime?)null;

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
        public IHttpActionResult Eliminar(int id)
        {
            try
            {
                var factura = db.Factura.Find(id);
                if (factura == null)
                    return NotFound();

                factura.Activo = false;
                db.SaveChanges();

                return Ok("Factura eliminada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}