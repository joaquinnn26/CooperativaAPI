using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http;
using CooperativaAPI.Data;
using CooperativaAPI.Filters;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/rutas-medicion")]
    public class RutasMedicionController : ApiController
    {
        private readonly AppDbContext db = new AppDbContext();
        private readonly CooperativaEntities readDb = new CooperativaEntities();

        private static readonly string[] EstadosDetalleResueltos =
        {
            "Medido", "NoAcceso", "MedidorDanado", "Observado"
        };

        [HttpGet]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult ObtenerTodas()
        {
            try
            {
                var rutas = QueryRutas()
                    .OrderByDescending(r => r.Fecha)
                    .ThenByDescending(r => r.idRutaMedicion)
                    .ToList()
                    .Select(MapRuta)
                    .ToList();

                return Ok(rutas);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener hojas de ruta: " + ex.Message);
            }
        }

        [HttpGet]
        [Route("empleado/{idEmpleado:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult ObtenerPorEmpleado(int idEmpleado)
        {
            try
            {
                if (User.IsInRole("Empleado") && ObtenerClaimEntero("idEmpleado") != idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar hojas de otro empleado.");

                var rutas = QueryRutas()
                    .Where(r => r.idEmpleado == idEmpleado)
                    .OrderByDescending(r => r.Fecha)
                    .ThenByDescending(r => r.idRutaMedicion)
                    .ToList()
                    .Select(MapRuta)
                    .ToList();

                return Ok(rutas);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener hojas del lecturista: " + ex.Message);
            }
        }

        [HttpGet]
        [Route("{idRuta:int}")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult ObtenerPorId(int idRuta)
        {
            try
            {
                var ruta = QueryRutas().FirstOrDefault(r => r.idRutaMedicion == idRuta);
                if (ruta == null) return NotFound();
                if (User.IsInRole("Empleado") && ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");

                return Ok(MapRuta(ruta));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener hoja de ruta: " + ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Crear([FromBody] CrearRutaMedicionRequest request)
        {
            try
            {
                var error = ValidarCrearRuta(request);
                if (!string.IsNullOrWhiteSpace(error)) return BadRequest(error);

                var medidoresIds = request.Medidores.Distinct().ToList();

                using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    if (!EmpleadoActivo(request.IdEmpleado))
                    {
                        transaction.Rollback();
                        return BadRequest("El empleado no existe o esta inactivo.");
                    }

                    var medidoresActivos = db.Medidores
                        .Where(m => medidoresIds.Contains(m.idMedidor) && m.Activo)
                        .Select(m => m.idMedidor)
                        .ToList();
                    var invalidos = medidoresIds.Except(medidoresActivos).ToList();

                    if (invalidos.Any())
                    {
                        transaction.Rollback();
                        return BadRequest("Hay medidores inexistentes o inactivos: " + string.Join(", ", invalidos));
                    }

                    var fecha = request.Fecha.Date;
                    var inicioMes = new DateTime(fecha.Year, fecha.Month, 1);
                    var inicioMesSiguiente = inicioMes.AddMonths(1);
                    var periodo = fecha.ToString("yyyy-MM");

                    var ocupadosEnRuta = db.RutasMedicionDetalle
                        .Where(d =>
                            medidoresIds.Contains(d.idMedidor) &&
                            d.RutaMedicion.Fecha >= inicioMes &&
                            d.RutaMedicion.Fecha < inicioMesSiguiente &&
                            d.RutaMedicion.Estado != "Cancelada")
                        .Select(d => d.idMedidor)
                        .Distinct()
                        .ToList();

                    var medidosEnPeriodo = db.Mediciones
                        .Where(m =>
                            medidoresIds.Contains(m.idMedidor) &&
                            (m.Periodo == periodo || (m.FechaLectura >= inicioMes && m.FechaLectura < inicioMesSiguiente)))
                        .Select(m => m.idMedidor)
                        .Distinct()
                        .ToList();

                    var ocupados = ocupadosEnRuta.Concat(medidosEnPeriodo).Distinct().ToList();

                    if (ocupados.Any())
                    {
                        transaction.Rollback();
                        return Content(System.Net.HttpStatusCode.Conflict, new
                        {
                            message = "Algunos medidores ya tienen ruta o medicion registrada para ese mes.",
                            medidores = ocupados
                        });
                    }

                    var ruta = new RutaMedicion
                    {
                        idEmpleado = request.IdEmpleado,
                        Nombre = request.Nombre.Trim(),
                        Fecha = fecha,
                        Estado = "Pendiente",
                        FechaCreacion = DateTime.UtcNow,
                        Observacion = StringOrNull(request.Observacion)
                    };

                    var orden = 1;
                    foreach (var idMedidor in medidoresIds)
                    {
                        ruta.RutaMedicionDetalle.Add(new RutaMedicionDetalle
                        {
                            idMedidor = idMedidor,
                            Orden = orden++,
                            Estado = "Pendiente"
                        });
                    }

                    db.RutasMedicion.Add(ruta);
                    db.SaveChanges();
                    transaction.Commit();

                    return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == ruta.idRutaMedicion)));
                }
            }
            catch (Exception ex)
            {
                return BadRequest("Error al crear hoja de ruta: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/iniciar")]
        [JwtAuthorize(Roles = "Empleado")]
        public IHttpActionResult Iniciar(int idRuta)
        {
            try
            {
                var ruta = db.RutasMedicion.Find(idRuta);
                if (ruta == null) return NotFound();
                if (ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");
                if (ruta.Estado != "Pendiente") return BadRequest("La hoja de ruta no esta pendiente.");

                ruta.Estado = "EnCurso";
                ruta.FechaInicio = ruta.FechaInicio ?? DateTime.UtcNow;
                db.SaveChanges();

                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al iniciar hoja de ruta: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/detalle/{idMedidor:int}/lectura")]
        [JwtAuthorize(Roles = "Empleado")]
        public IHttpActionResult CargarLectura(int idRuta, int idMedidor, [FromBody] CargarLecturaRutaRequest request)
        {
            try
            {
                if (request == null) return BadRequest("La lectura es obligatoria.");
                var idEmpleadoActual = ObtenerClaimEntero("idEmpleado");
                if (!idEmpleadoActual.HasValue) return Unauthorized();
                request.IdEmpleado = idEmpleadoActual.Value;
                if (request.IdTipoLectura <= 0) return BadRequest("El tipo de lectura es obligatorio.");
                if (request.LecturaActual < 0) return BadRequest("La lectura no puede ser negativa.");

                using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    var ruta = db.RutasMedicion
                        .Include(r => r.RutaMedicionDetalle)
                        .FirstOrDefault(r => r.idRutaMedicion == idRuta);
                    if (ruta == null)
                    {
                        transaction.Rollback();
                        return NotFound();
                    }
                    if (ruta.Estado == "Finalizada" || ruta.Estado == "Cancelada")
                    {
                        transaction.Rollback();
                        return BadRequest("La hoja de ruta ya esta cerrada.");
                    }
                    if (ruta.idEmpleado != request.IdEmpleado)
                    {
                        transaction.Rollback();
                        return BadRequest("La hoja de ruta no pertenece al empleado informado.");
                    }

                    var detalle = ruta.RutaMedicionDetalle.FirstOrDefault(d => d.idMedidor == idMedidor);
                    if (detalle == null)
                    {
                        transaction.Rollback();
                        return BadRequest("El medidor no pertenece a esta hoja de ruta.");
                    }
                    if (detalle.Estado == "Medido")
                    {
                        transaction.Rollback();
                        return BadRequest("Este medidor ya fue medido en la hoja de ruta.");
                    }

                    var medidor = db.Medidores.FirstOrDefault(m => m.idMedidor == idMedidor && m.Activo && m.idServicioSocio != null);
                    if (medidor == null)
                    {
                        transaction.Rollback();
                        return BadRequest("El medidor no existe o esta inactivo.");
                    }

                    if (!db.TiposLectura.Any(t => t.idTipoLectura == request.IdTipoLectura))
                    {
                        transaction.Rollback();
                        return BadRequest("El tipo de lectura no existe.");
                    }

                    var ultimaLectura = db.Mediciones
                        .Where(m => m.idMedidor == idMedidor)
                        .OrderByDescending(m => m.FechaLectura)
                        .ThenByDescending(m => m.idMedicion)
                        .Select(m => (decimal?)m.ValorLectura)
                        .FirstOrDefault() ?? 0;

                    var baseInstalacion = db.Database.SqlQuery<decimal?>("SELECT LecturaInicial FROM dbo.AsignacionMedidor WHERE idMedidor=@id AND Cerrada=0",new System.Data.SqlClient.SqlParameter("@id",idMedidor)).SingleOrDefault();
                    ultimaLectura = Math.Max(ultimaLectura,baseInstalacion ?? 0);
                    if (request.LecturaActual < ultimaLectura)
                    {
                        transaction.Rollback();
                        return BadRequest("La lectura actual no puede ser menor a la lectura anterior.");
                    }

                    var fechaLectura = request.FechaLectura.HasValue ? request.FechaLectura.Value.Date : DateTime.Today;
                    var medicion = new Medicion
                    {
                        idServicioSocio = medidor.idServicioSocio.Value,
                        idEmpleado = request.IdEmpleado,
                        idMedidor = idMedidor,
                        idTipoLectura = request.IdTipoLectura,
                        FechaLectura = fechaLectura,
                        FechaCarga = DateTime.UtcNow,
                        ValorLectura = request.LecturaActual,
                        ConsumoCalculado = request.LecturaActual - ultimaLectura,
                        Periodo = fechaLectura.ToString("yyyy-MM"),
                        FotoURL = StringOrNull(request.FotoMedidorUrl),
                        Observaciones = StringOrNull(request.Observacion)
                    };

                    db.Mediciones.Add(medicion);
                    db.SaveChanges();

                    detalle.Estado = "Medido";
                    detalle.idMedicion = medicion.idMedicion;
                    detalle.LecturaInformada = request.LecturaActual;
                    detalle.FechaHoraLectura = DateTime.UtcNow;
                    detalle.Observacion = StringOrNull(request.Observacion);
                    detalle.Latitud = request.Latitud;
                    detalle.Longitud = request.Longitud;
                    detalle.FotoMedidorUrl = StringOrNull(request.FotoMedidorUrl);

                    if (ruta.Estado == "Pendiente")
                    {
                        ruta.Estado = "EnCurso";
                        ruta.FechaInicio = ruta.FechaInicio ?? DateTime.UtcNow;
                    }

                    db.SaveChanges();
                    transaction.Commit();

                    return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
                }
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cargar lectura: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/detalle/{idMedidor:int}/estado")]
        [JwtAuthorize(Roles = "Empleado")]
        public IHttpActionResult CambiarEstadoDetalle(int idRuta, int idMedidor, [FromBody] CambiarEstadoDetalleRequest request)
        {
            try
            {
                var estadosValidos = new[] { "Pendiente", "NoAcceso", "MedidorDanado", "Observado" };
                if (request == null || !estadosValidos.Contains(request.Estado)) return BadRequest("Estado de detalle no valido.");

                var ruta = db.RutasMedicion
                    .Include(r => r.RutaMedicionDetalle)
                    .FirstOrDefault(r => r.idRutaMedicion == idRuta);
                if (ruta == null) return NotFound();
                if (ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");
                if (ruta.Estado == "Finalizada" || ruta.Estado == "Cancelada") return BadRequest("La hoja de ruta ya esta cerrada.");

                var detalle = ruta.RutaMedicionDetalle.FirstOrDefault(d => d.idMedidor == idMedidor);
                if (detalle == null) return BadRequest("El medidor no pertenece a esta hoja de ruta.");
                if (detalle.Estado == "Medido") return BadRequest("No se puede cambiar una medicion ya registrada.");

                detalle.Estado = request.Estado;
                detalle.Observacion = StringOrNull(request.Observacion);
                detalle.FechaHoraLectura = detalle.FechaHoraLectura ?? DateTime.UtcNow;

                if (ruta.Estado == "Pendiente")
                {
                    ruta.Estado = "EnCurso";
                    ruta.FechaInicio = ruta.FechaInicio ?? DateTime.UtcNow;
                }

                db.SaveChanges();
                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al actualizar detalle: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/finalizar")]
        [JwtAuthorize(Roles = "Empleado")]
        public IHttpActionResult Finalizar(int idRuta, [FromBody] FinalizarRutaRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.FirmaBase64))
                    return BadRequest("La firma digital es obligatoria.");

                var firma = ConvertirFirma(request.FirmaBase64);

                using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    var ruta = db.RutasMedicion
                        .Include(r => r.RutaMedicionDetalle)
                        .FirstOrDefault(r => r.idRutaMedicion == idRuta);
                    if (ruta == null)
                    {
                        transaction.Rollback();
                        return NotFound();
                    }
                    if (ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");
                    }
                    if (ruta.Estado == "Finalizada")
                    {
                        transaction.Rollback();
                        return BadRequest("La hoja ya esta finalizada.");
                    }
                    if (ruta.RutaMedicionDetalle.Any(d => d.Estado == "Pendiente"))
                    {
                        transaction.Rollback();
                        return BadRequest("Todavia quedan medidores pendientes en la hoja.");
                    }
                    if (ruta.RutaMedicionDetalle.Any(d => !EstadosDetalleResueltos.Contains(d.Estado)))
                    {
                        transaction.Rollback();
                        return BadRequest("Hay estados de detalle no resueltos.");
                    }

                    ruta.Estado = "Finalizada";
                    ruta.FechaFinalizacion = DateTime.UtcNow;
                    ruta.FirmaDigital = firma.Bytes;
                    ruta.FirmaMimeType = firma.MimeType;
                    if (!string.IsNullOrWhiteSpace(request.Observacion)) ruta.Observacion = request.Observacion.Trim();

                    db.SaveChanges();
                    transaction.Commit();

                    return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
                }
            }
            catch (FormatException)
            {
                return BadRequest("La firma digital no tiene formato base64 valido.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error al finalizar hoja: " + ex.Message);
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/cancelar")]
        [JwtAuthorize(Roles = "Administrador")]
        public IHttpActionResult Cancelar(int idRuta, [FromBody] CancelarRutaRequest request)
        {
            try
            {
                var ruta = db.RutasMedicion.Find(idRuta);
                if (ruta == null) return NotFound();
                if (ruta.Estado != "Pendiente" && ruta.Estado != "EnCurso")
                    return BadRequest("La hoja no se puede cancelar en su estado actual.");

                ruta.Estado = "Cancelada";
                if (!string.IsNullOrWhiteSpace(request?.Observacion)) ruta.Observacion = request.Observacion.Trim();
                db.SaveChanges();

                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cancelar hoja: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
                readDb.Dispose();
            }
            base.Dispose(disposing);
        }

        private IQueryable<RutaMedicion> QueryRutas()
        {
            return db.RutasMedicion
                .Include(r => r.RutaMedicionDetalle);
        }

        private RutaMedicionDto MapRuta(RutaMedicion ruta)
        {
            var empleado = readDb.Empleado
                .Include(e => e.Persona)
                .FirstOrDefault(e => e.idEmpleado == ruta.idEmpleado);

            var medidorIds = ruta.RutaMedicionDetalle.Select(d => d.idMedidor).Distinct().ToList();
            var medidores = readDb.Medidor
                .Include(m => m.TipoMedidor)
                .Include(m => m.ServicioSocio.Servicio)
                .Include(m => m.ServicioSocio.Socio.Persona)
                .Where(m => medidorIds.Contains(m.idMedidor))
                .ToList()
                .ToDictionary(m => m.idMedidor);

            return new RutaMedicionDto
            {
                idRutaMedicion = ruta.idRutaMedicion,
                id = ruta.idRutaMedicion,
                idEmpleado = ruta.idEmpleado,
                empleadoNombre = NombrePersona(empleado?.Persona),
                nombre = ruta.Nombre,
                fecha = ruta.Fecha.ToString("yyyy-MM-dd"),
                estado = ruta.Estado,
                observacion = ruta.Observacion,
                creadaEn = FechaIso(ruta.FechaCreacion),
                iniciadaEn = FechaIso(ruta.FechaInicio),
                finalizadaEn = FechaIso(ruta.FechaFinalizacion),
                firmaDigital = ruta.FirmaDigital == null ? null : "data:" + (ruta.FirmaMimeType ?? "image/png") + ";base64," + Convert.ToBase64String(ruta.FirmaDigital),
                items = ruta.RutaMedicionDetalle
                    .OrderBy(d => d.Orden)
                    .Select(d => MapDetalle(d, medidores))
                    .ToList()
            };
        }

        public class ContextoHistoricoRuta {
            public int? idSocio { get; set; }
            public string SocioNombre { get; set; }
            public string Documento { get; set; }
            public string ServicioNombre { get; set; }
            public string NumeroMedidor { get; set; }
        }
        private RutaMedicionDetalleDto MapDetalle(RutaMedicionDetalle detalle, IDictionary<int, Medidor> medidores)
        {
            Medidor medidor;
            medidores.TryGetValue(detalle.idMedidor, out medidor);
            var servicioSocio = medidor?.ServicioSocio;
            var contexto = db.Database.SqlQuery<ContextoHistoricoRuta>("SELECT * FROM dbo.ContextoRutaMedidor WHERE idRutaMedicionDetalle=@id", new System.Data.SqlClient.SqlParameter("@id",detalle.idRutaMedicionDetalle)).SingleOrDefault();

            return new RutaMedicionDetalleDto
            {
                idRutaMedicionDetalle = detalle.idRutaMedicionDetalle,
                idMedidor = detalle.idMedidor,
                orden = detalle.Orden,
                estado = detalle.Estado,
                idMedicion = detalle.idMedicion,
                lecturaActual = detalle.LecturaInformada,
                fechaMedicion = FechaIso(detalle.FechaHoraLectura),
                observacion = detalle.Observacion,
                latitud = detalle.Latitud,
                longitud = detalle.Longitud,
                fotoMedidorUrl = detalle.FotoMedidorUrl,
                numeroMedidor = contexto?.NumeroMedidor ?? medidor?.Numero,
                idSocio = contexto?.idSocio ?? servicioSocio?.idSocio,
                socioNombre = contexto?.SocioNombre ?? NombrePersona(servicioSocio?.Socio?.Persona),
                socioDocumento = contexto?.Documento ?? servicioSocio?.Socio?.Persona?.Documento,
                servicioNombre = contexto?.ServicioNombre ?? servicioSocio?.Servicio?.Nombre,
                tipoMedidor = medidor?.TipoMedidor?.Nombre,
                medidorActivo = medidor?.Activo ?? false
            };
        }

        private string ValidarCrearRuta(CrearRutaMedicionRequest request)
        {
            if (request == null) return "La solicitud es obligatoria.";
            if (request.IdEmpleado <= 0) return "El lecturista es obligatorio.";
            if (string.IsNullOrWhiteSpace(request.Nombre)) return "El nombre es obligatorio.";
            if (request.Nombre.Trim().Length > 120) return "El nombre no puede superar 120 caracteres.";
            if (request.Fecha == default(DateTime)) return "La fecha es obligatoria.";
            if (request.Medidores == null || !request.Medidores.Any()) return "Selecciona al menos un medidor.";
            if (request.Medidores.Count > 300) return "Una hoja no puede superar 300 medidores.";
            if (request.Medidores.Any(id => id <= 0)) return "Hay medidores invalidos.";
            return null;
        }

        private bool EmpleadoActivo(int idEmpleado)
        {
            return db.Empleados.Any(e =>
                e.idEmpleado == idEmpleado &&
                (e.Activo ?? true));
        }

        private FirmaData ConvertirFirma(string firmaBase64)
        {
            var firma = firmaBase64.Trim();
            var mimeType = "image/png";
            var commaIndex = firma.IndexOf(',');

            if (firma.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex > 0)
            {
                var header = firma.Substring(5, commaIndex - 5);
                var separatorIndex = header.IndexOf(';');
                if (separatorIndex > 0) mimeType = header.Substring(0, separatorIndex);
                firma = firma.Substring(commaIndex + 1);
            }

            var bytes = Convert.FromBase64String(firma);
            if (bytes.Length == 0) throw new FormatException();
            if (bytes.Length > 1024 * 1024) throw new InvalidOperationException("La firma digital no puede superar 1 MB.");

            return new FirmaData { Bytes = bytes, MimeType = mimeType };
        }

        private static string NombrePersona(Persona persona)
        {
            if (persona == null) return "";
            return ((persona.Nombre ?? "") + " " + (persona.Apellido ?? "")).Trim();
        }

        private static string FechaIso(DateTime? fecha)
        {
            return fecha.HasValue ? fecha.Value.ToString("o", CultureInfo.InvariantCulture) : null;
        }

        private static string StringOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private int? ObtenerClaimEntero(string tipo)
        {
            var identity = User?.Identity as ClaimsIdentity;
            int value;
            return int.TryParse(identity?.FindFirst(tipo)?.Value, out value) ? value : (int?)null;
        }

        private sealed class FirmaData
        {
            public byte[] Bytes { get; set; }
            public string MimeType { get; set; }
        }
    }

    public class CrearRutaMedicionRequest
    {
        public int IdEmpleado { get; set; }
        public string Nombre { get; set; }
        public DateTime Fecha { get; set; }
        public List<int> Medidores { get; set; }
        public string Observacion { get; set; }
    }

    public class CargarLecturaRutaRequest
    {
        public decimal LecturaActual { get; set; }
        public int IdEmpleado { get; set; }
        public int IdTipoLectura { get; set; }
        public DateTime? FechaLectura { get; set; }
        public string Observacion { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string FotoMedidorUrl { get; set; }
    }

    public class CambiarEstadoDetalleRequest
    {
        public string Estado { get; set; }
        public string Observacion { get; set; }
    }

    public class FinalizarRutaRequest
    {
        public string FirmaBase64 { get; set; }
        public string Observacion { get; set; }
    }

    public class CancelarRutaRequest
    {
        public string Observacion { get; set; }
    }

    public class RutaMedicionDto
    {
        public int idRutaMedicion { get; set; }
        public int id { get; set; }
        public int idEmpleado { get; set; }
        public string empleadoNombre { get; set; }
        public string nombre { get; set; }
        public string fecha { get; set; }
        public string estado { get; set; }
        public string observacion { get; set; }
        public string creadaEn { get; set; }
        public string iniciadaEn { get; set; }
        public string finalizadaEn { get; set; }
        public string firmaDigital { get; set; }
        public List<RutaMedicionDetalleDto> items { get; set; }
    }

    public class RutaMedicionDetalleDto
    {
        public int idRutaMedicionDetalle { get; set; }
        public int idMedidor { get; set; }
        public int orden { get; set; }
        public string estado { get; set; }
        public int? idMedicion { get; set; }
        public decimal? lecturaActual { get; set; }
        public string fechaMedicion { get; set; }
        public string observacion { get; set; }
        public decimal? latitud { get; set; }
        public decimal? longitud { get; set; }
        public string fotoMedidorUrl { get; set; }
        public string numeroMedidor { get; set; }
        public int? idSocio { get; set; }
        public string socioNombre { get; set; }
        public string socioDocumento { get; set; }
        public string servicioNombre { get; set; }
        public string tipoMedidor { get; set; }
        public bool medidorActivo { get; set; }
    }
}
