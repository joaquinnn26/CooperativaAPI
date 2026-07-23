using System;
using System.Linq;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;
using System.Security.Claims;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/solicitudservicio")]
    public class SolicitudServicioController : ApiController
    {
        private CooperativaEntities db = new CooperativaEntities();

        private string GenerarNumeroMedidor()
        {
            int ultimoId = 0;

            if (db.Medidor.Any())
            {
                ultimoId = db.Medidor.Max(m => m.idMedidor);
            }

            int siguiente = ultimoId + 1;
            return "MED-" + siguiente.ToString("D4");
        }

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult GetAll()
        {
            try
            {
                var solicitudes = db.SolicitudServicio
                    .Select(ss => new
                    {
                        ss.idSolicitudServicio,
                        ss.idSocio,
                        ss.idServicio,
                        Socio = ss.Socio.Persona.Nombre + " " + ss.Socio.Persona.Apellido,
                        Documento = ss.Socio.Persona.Documento,
                        Servicio = ss.Servicio.Nombre,
                        Estado = ss.EstadoSolicitudServicio.NombreEstado,
                        ss.FechaSolicitud,
                        ss.Observaciones,
                        ss.MotivoRechazo,
                        Documentos = ss.DocumentoSolicitud.Select(d => new
                        {
                            d.idDocumentoSolicitud,
                            d.idRequisitoServicio,
                            d.NombreArchivo,
                            d.RutaArchivo,
                            d.TipoArchivo,
                            d.FechaSubida
                        }).ToList()
                    })
                    .ToList();

                return Ok(solicitudes);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("socio/{idSocio:int}")]
        [JwtAuthorize(Roles = "Administrador,Socio")]
        public IHttpActionResult GetBySocio(int idSocio)
        {
            try
            {
                if (User.IsInRole("Socio") && ObtenerClaimEntero("idSocio") != idSocio)
                    return Content(System.Net.HttpStatusCode.Forbidden, "No puedes consultar solicitudes de otro socio.");

                var solicitudes = db.SolicitudServicio
                    .Where(ss => ss.idSocio == idSocio)
                    .Select(ss => new
                    {
                        ss.idSolicitudServicio,
                        ss.idSocio,
                        ss.idServicio,
                        Socio = ss.Socio.Persona.Nombre + " " + ss.Socio.Persona.Apellido,
                        Documento = ss.Socio.Persona.Documento,
                        Servicio = ss.Servicio.Nombre,
                        Estado = ss.EstadoSolicitudServicio.NombreEstado,
                        ss.FechaSolicitud,
                        ss.Observaciones,
                        ss.MotivoRechazo,
                        Documentos = ss.DocumentoSolicitud.Select(d => new
                        {
                            d.idDocumentoSolicitud,
                            d.idRequisitoServicio,
                            d.NombreArchivo,
                            d.RutaArchivo,
                            d.TipoArchivo,
                            d.FechaSubida
                        }).ToList()
                    })
                    .ToList();

                return Ok(solicitudes);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Socio")]
        public IHttpActionResult Crear([FromBody] SolicitudServicio solicitud)
        {
            try
            {
                if (solicitud == null)
                    return BadRequest("Datos inválidos.");

                var idSocioActual = ObtenerClaimEntero("idSocio");
                if (!idSocioActual.HasValue) return Unauthorized();
                solicitud.idSocio = idSocioActual.Value;

                if (!db.Socio.Any(s => s.idSocio == solicitud.idSocio && s.Activo == true))
                    return BadRequest("El socio no existe o esta inactivo.");

                if (!db.Servicio.Any(s => s.idServicio == solicitud.idServicio))
                    return BadRequest("El servicio no existe.");

                bool servicioActivo = db.ServicioSocio.Any(ss =>
                    ss.idSocio == solicitud.idSocio &&
                    ss.idServicio == solicitud.idServicio &&
                    ss.Activo == true);
                if (servicioActivo)
                    return BadRequest("Ya tenes ese servicio activo.");

                var solicitudActiva = db.SolicitudServicio.Any(s =>
                    s.idSocio == solicitud.idSocio &&
                    s.idServicio == solicitud.idServicio &&
                    s.FechaResolucion == null);
                if (solicitudActiva)
                    return BadRequest("Ya existe una solicitud activa para este servicio.");

                solicitud.FechaSolicitud = DateTime.Now;

                var estadoEnRevision = ObtenerOCrearEstadoSolicitud("En Revision");
                solicitud.idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud;
                solicitud.MotivoRechazo = null;

                db.SolicitudServicio.Add(solicitud);
                db.SaveChanges();

                return Ok(new
                {
                    mensaje = "Solicitud creada correctamente.",
                    solicitud.idSolicitudServicio
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("aprobar/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Aprobar(int id)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    var solicitud = db.SolicitudServicio.Find(id);
                    if (solicitud == null)
                        return NotFound();

                    var estadoActual = NormalizarTexto(solicitud.EstadoSolicitudServicio?.NombreEstado);
                    if (estadoActual != "en revision" && estadoActual != "pendiente")
                        return BadRequest("Solo se pueden aprobar solicitudes pendientes o en revision.");

                    var servicio = db.Servicio.Find(solicitud.idServicio);
                    if (servicio == null)
                        return BadRequest("El servicio no existe.");

                    bool requiereInstalacion = ServicioRequiereInstalacion(servicio.Nombre);
                    var estado = requiereInstalacion
                        ? ObtenerOCrearEstadoSolicitud("Pendiente instalacion")
                        : ObtenerOCrearEstadoSolicitud("Aprobada");

                    solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                    solicitud.MotivoRechazo = null;

                    bool medidorCreado = false;
                    string numeroMedidor = null;
                    int? idServicioSocio = null;

                    if (requiereInstalacion)
                    {
                        solicitud.FechaResolucion = null;
                    }
                    else
                    {
                        solicitud.FechaResolucion = DateTime.Now;
                        var servicioSocio = ObtenerOCrearServicioSocio(solicitud.idSocio, solicitud.idServicio);
                        idServicioSocio = servicioSocio.idServicioSocio;
                        CrearMedidorSiCorresponde(servicioSocio, servicio.Nombre, out medidorCreado, out numeroMedidor);
                    }

                    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud
                    {
                        idSolicitudServicio = solicitud.idSolicitudServicio,
                        idEstadoSolicitud = estado.idEstadoSolicitud,
                        idEmpleado = ObtenerClaimEntero("idEmpleado"),
                        FechaCambio = DateTime.Now,
                        Observaciones = requiereInstalacion
                            ? "Documentacion aprobada. Pendiente de instalacion tecnica."
                            : "Documentacion aprobada. Servicio activado."
                    });

                    db.SaveChanges();
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = requiereInstalacion
                            ? "Solicitud aprobada. Pendiente de instalacion."
                            : "Solicitud aprobada.",
                        estado = estado.NombreEstado,
                        servicio = servicio.Nombre,
                        idServicioSocio = idServicioSocio,
                        medidorCreado = medidorCreado,
                        numeroMedidor = numeroMedidor
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        [HttpPut]
        [Route("rechazar/{id}")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Rechazar(int id, [FromBody] dynamic data)
        {
            try
            {
                var solicitud = db.SolicitudServicio.Find(id);
                if (solicitud == null)
                    return NotFound();

                var estadoActual = NormalizarTexto(solicitud.EstadoSolicitudServicio?.NombreEstado);
                if (estadoActual != "en revision" && estadoActual != "pendiente")
                    return BadRequest("Solo se pueden rechazar solicitudes pendientes o en revision.");

                var motivo = Convert.ToString(data?.motivo)?.Trim();
                if (string.IsNullOrWhiteSpace(motivo))
                    return BadRequest("El motivo de rechazo es obligatorio.");
                if (motivo.Length > 500)
                    return BadRequest("El motivo de rechazo no puede superar 500 caracteres.");

                var estado = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Rechazada");

                if (estado == null)
                    return BadRequest("No existe el estado Rechazada.");

                solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                solicitud.MotivoRechazo = motivo;
                solicitud.FechaResolucion = DateTime.Now;

                db.SaveChanges();

                return Ok("Solicitud rechazada.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("instalar/{id:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult MarcarInstalada(int id, [FromBody] dynamic data)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    int? idEmpleadoActual = ObtenerClaimEntero("idEmpleado");

                    if (User.IsInRole("Empleado"))
                    {
                        if (!idEmpleadoActual.HasValue)
                            return Unauthorized();

                        var empleado = db.Empleado.Find(idEmpleadoActual.Value);
                        if (empleado == null || empleado.Activo != true)
                            return Content(System.Net.HttpStatusCode.Forbidden, "El empleado no esta activo.");

                        if (NormalizarTexto(empleado.Cargo?.NombreCargo) != "tecnico")
                            return Content(System.Net.HttpStatusCode.Forbidden, "Solo empleados con cargo Tecnico pueden registrar instalaciones.");
                    }

                    var solicitud = db.SolicitudServicio.Find(id);
                    if (solicitud == null)
                        return NotFound();

                    var estadoActual = NormalizarTexto(solicitud.EstadoSolicitudServicio?.NombreEstado);
                    if (estadoActual == "aprobada")
                    {
                        trans.Commit();
                        return Ok(new { mensaje = "La solicitud ya estaba aprobada." });
                    }

                    if (estadoActual != "pendiente instalacion")
                        return BadRequest("Solo se pueden marcar como instaladas las solicitudes pendientes de instalacion.");

                    var servicio = db.Servicio.Find(solicitud.idServicio);
                    if (servicio == null)
                        return BadRequest("El servicio no existe.");

                    if (!ServicioRequiereInstalacion(servicio.Nombre))
                        return BadRequest("Este servicio no requiere instalacion tecnica.");

                    var estadoAprobada = ObtenerOCrearEstadoSolicitud("Aprobada");
                    var observaciones = ObtenerObservaciones(data);
                    bool medidorCreado = false;
                    string numeroMedidor = null;

                    var servicioSocio = ObtenerOCrearServicioSocio(solicitud.idSocio, solicitud.idServicio);
                    CrearMedidorSiCorresponde(servicioSocio, servicio.Nombre, out medidorCreado, out numeroMedidor);

                    solicitud.idEstadoSolicitud = estadoAprobada.idEstadoSolicitud;
                    solicitud.idEmpleadoRevisor = idEmpleadoActual;
                    solicitud.FechaResolucion = DateTime.Now;
                    solicitud.MotivoRechazo = null;

                    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud
                    {
                        idSolicitudServicio = solicitud.idSolicitudServicio,
                        idEstadoSolicitud = estadoAprobada.idEstadoSolicitud,
                        idEmpleado = idEmpleadoActual,
                        FechaCambio = DateTime.Now,
                        Observaciones = string.IsNullOrWhiteSpace(observaciones)
                            ? "Instalacion registrada. Servicio activado."
                            : observaciones
                    });

                    db.SaveChanges();
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Instalacion registrada. Solicitud aprobada.",
                        solicitud.idSolicitudServicio,
                        estado = estadoAprobada.NombreEstado,
                        servicio = servicio.Nombre,
                        idServicioSocio = servicioSocio.idServicioSocio,
                        medidorCreado = medidorCreado,
                        numeroMedidor = numeroMedidor
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }


        [HttpPut]
        [Route("rechazar-instalacion/{id:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult RechazarInstalacion(int id, [FromBody] dynamic data)
        {
            using (var trans = db.Database.BeginTransaction())
            {
                try
                {
                    int? idEmpleadoActual = ObtenerClaimEntero("idEmpleado");

                    if (User.IsInRole("Empleado"))
                    {
                        if (!idEmpleadoActual.HasValue)
                            return Unauthorized();

                        var empleado = db.Empleado.Find(idEmpleadoActual.Value);
                        if (empleado == null || empleado.Activo != true)
                            return Content(System.Net.HttpStatusCode.Forbidden, "El empleado no esta activo.");

                        if (NormalizarTexto(empleado.Cargo?.NombreCargo) != "tecnico")
                            return Content(System.Net.HttpStatusCode.Forbidden, "Solo empleados con cargo Tecnico pueden rechazar instalaciones.");
                    }

                    var solicitud = db.SolicitudServicio.Find(id);
                    if (solicitud == null)
                        return NotFound();

                    var estadoActual = NormalizarTexto(solicitud.EstadoSolicitudServicio?.NombreEstado);
                    if (estadoActual != "pendiente instalacion")
                        return BadRequest("Solo se pueden rechazar instalaciones pendientes.");

                    var motivo = ObtenerMotivo(data);
                    if (string.IsNullOrWhiteSpace(motivo))
                        return BadRequest("El motivo es obligatorio.");
                    if (motivo.Length > 500)
                        return BadRequest("El motivo no puede superar 500 caracteres.");

                    var estadoEnRevision = ObtenerOCrearEstadoSolicitud("En Revision");
                    solicitud.idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud;
                    solicitud.idEmpleadoRevisor = idEmpleadoActual;
                    solicitud.FechaResolucion = null;
                    solicitud.MotivoRechazo = motivo;

                    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud
                    {
                        idSolicitudServicio = solicitud.idSolicitudServicio,
                        idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud,
                        idEmpleado = idEmpleadoActual,
                        FechaCambio = DateTime.Now,
                        Observaciones = "Instalacion rechazada: " + motivo
                    });

                    db.SaveChanges();
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Instalacion rechazada. La solicitud vuelve a revision.",
                        solicitud.idSolicitudServicio,
                        estado = estadoEnRevision.NombreEstado,
                        motivo = motivo
                    });
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    return BadRequest(ex.Message);
                }
            }
        }

        private EstadoSolicitudServicio ObtenerOCrearEstadoSolicitud(string nombre)
        {
            var estado = db.EstadoSolicitudServicio.FirstOrDefault(e => e.NombreEstado == nombre);
            if (estado != null) return estado;

            estado = new EstadoSolicitudServicio { NombreEstado = nombre };
            db.EstadoSolicitudServicio.Add(estado);
            db.SaveChanges();
            return estado;
        }

        private string ObtenerObservaciones(dynamic data)
        {
            try
            {
                return Convert.ToString(data?.observaciones)?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }


        private string ObtenerMotivo(dynamic data)
        {
            try
            {
                return Convert.ToString(data?.motivo ?? data?.motivoRechazo)?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
        private bool ServicioRequiereInstalacion(string nombreServicio)
        {
            var nombre = NormalizarTexto(nombreServicio);
            return nombre.Contains("agua") ||
                   nombre.Contains("luz") ||
                   nombre.Contains("electric") ||
                   nombre.Contains("internet") ||
                   nombre.Contains("wifi") ||
                   nombre.Contains("fibra");
        }

        private bool ServicioRequiereMedidor(string nombreServicio)
        {
            var nombre = NormalizarTexto(nombreServicio);
            return nombre.Contains("agua") ||
                   nombre.Contains("luz") ||
                   nombre.Contains("electric");
        }

        private ServicioSocio ObtenerOCrearServicioSocio(int idSocio, int idServicio)
        {
            var servicioSocio = db.ServicioSocio.FirstOrDefault(ss =>
                ss.idSocio == idSocio &&
                ss.idServicio == idServicio &&
                ss.Activo == true);

            if (servicioSocio != null) return servicioSocio;

            servicioSocio = new ServicioSocio
            {
                idSocio = idSocio,
                idServicio = idServicio,
                FechaAlta = DateTime.Now,
                Activo = true
            };

            db.ServicioSocio.Add(servicioSocio);
            db.SaveChanges();
            return servicioSocio;
        }

        private void CrearMedidorSiCorresponde(ServicioSocio servicioSocio, string nombreServicio, out bool medidorCreado, out string numeroMedidor)
        {
            medidorCreado = false;
            numeroMedidor = null;

            if (!ServicioRequiereMedidor(nombreServicio)) return;

            bool yaTieneMedidor = db.Medidor.Any(m =>
                m.idServicioSocio == servicioSocio.idServicioSocio &&
                m.Activo == true);

            if (yaTieneMedidor) return;

            var tipoMedidorDefault = db.TipoMedidor.FirstOrDefault();
            if (tipoMedidorDefault == null)
                throw new InvalidOperationException("No existe un tipo de medidor para asignar al servicio.");

            var nuevoMedidor = new Medidor
            {
                idServicioSocio = servicioSocio.idServicioSocio,
                idTipoMedidor = tipoMedidorDefault.idTipoMedidor,
                Numero = GenerarNumeroMedidor(),
                FechaInstalacion = DateTime.Now,
                Activo = true
            };

            db.Medidor.Add(nuevoMedidor);
            medidorCreado = true;
            numeroMedidor = nuevoMedidor.Numero;
        }

        private string NormalizarTexto(string valor)
        {
            var normalizado = (valor ?? "").Normalize(System.Text.NormalizationForm.FormD);
            var caracteres = normalizado
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray();

            return new string(caracteres).Trim().ToLower();
        }
        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }
    }
}
