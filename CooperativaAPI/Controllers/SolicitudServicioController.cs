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

                var solicitudActiva = db.SolicitudServicio.Any(s =>
                    s.idSocio == solicitud.idSocio &&
                    s.idServicio == solicitud.idServicio &&
                    s.FechaResolucion == null);
                if (solicitudActiva)
                    return BadRequest("Ya existe una solicitud activa para este servicio.");

                using (var tx = db.Database.BeginTransaction())
                {
                solicitud.FechaSolicitud = DateTime.Now;

                var areaServicio = LaboralAccess.AreaServicio(db, solicitud.idServicio);
                var derivaDirecto = new[] { "AGUA", "ENERGIA", "INTERNET" }.Contains(areaServicio);
                var estadoEnRevision = ObtenerOCrearEstadoSolicitud(derivaDirecto ? "Pendiente instalacion" : "En Revision");
                solicitud.idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud;
                solicitud.MotivoRechazo = null;

                db.SolicitudServicio.Add(solicitud);
                db.SaveChanges();
                db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud {
                    idSolicitudServicio = solicitud.idSolicitudServicio,
                    idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud,
                    FechaCambio = DateTime.Now,
                    Observaciones = derivaDirecto
                        ? "Solicitud derivada automaticamente al area " + areaServicio + " para coordinar la instalacion."
                        : "Solicitud recibida; pendiente de clasificacion administrativa."
                });
                db.SaveChanges();
                if (derivaDirecto)
                {
                    var servicioNombre = db.Servicio.Find(solicitud.idServicio).Nombre;
                    var tarea = db.Database.SqlQuery<int>(@"INSERT dbo.TareaLaboral(Area,Categoria,OrigenId,idSocio,Titulo,Estado)
                        OUTPUT INSERTED.idTarea VALUES(@area,'Instalacion',@origen,@socio,@titulo,'PendienteAsignacion')",
                        new System.Data.SqlClient.SqlParameter("@area", areaServicio),
                        new System.Data.SqlClient.SqlParameter("@origen", solicitud.idSolicitudServicio),
                        new System.Data.SqlClient.SqlParameter("@socio", solicitud.idSocio),
                        new System.Data.SqlClient.SqlParameter("@titulo", "Instalacion de " + servicioNombre)).Single();
                    LaboralAccess.Auditar(db, (ClaimsPrincipal)User, areaServicio, tarea, "DerivarSolicitud", null, "PendienteAsignacion", "Solicitud derivada automaticamente al area responsable.");
                }
                EventoOperativoStore.Registrar(db, (ClaimsPrincipal)User, new EventoOperativoNuevo {
                    Contexto = "SOLICITUD_SERVICIO", CodigoEvento = "SolicitudCreada", Entidad = "SolicitudServicio",
                    idEntidad = solicitud.idSolicitudServicio.ToString(), Area = areaServicio,
                    idSocio = solicitud.idSocio, idServicio = solicitud.idServicio,
                    EstadoNuevo = estadoEnRevision.NombreEstado, Detalle = "Solicitud recibida por el sistema.",
                    ClaveIdempotencia = "SolicitudServicioCreada:" + solicitud.idSolicitudServicio
                });
                tx.Commit();

                return Ok(new
                {
                    mensaje = "Solicitud creada correctamente.",
                    solicitud.idSolicitudServicio
                });
                }
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
            using (var trans = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
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
                        var servicioSocio = ObtenerOCrearServicioSocio(solicitud.idSocio, solicitud.idServicio, solicitud.idSolicitudServicio);
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

                int? idMotivo=(int?)data?.idMotivoEvento;
                var motivo=db.Database.SqlQuery<MotivoEventoOperativoDto>(@"SELECT idMotivoEvento,Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,RequiereDetalle
                    FROM dbo.MotivoEventoOperativo WHERE idMotivoEvento=@id AND Contexto='SOLICITUD_SERVICIO_RECHAZO' AND Activo=1 AND ConfirmadoCooperativa=1",
                    new System.Data.SqlClient.SqlParameter("@id",(object)idMotivo??DBNull.Value)).SingleOrDefault();
                if (motivo==null) return BadRequest("Seleccioná un motivo confirmado para rechazar la solicitud.");
                var servicioSolicitado=db.Servicio.Find(solicitud.idServicio);
                if(motivo.Codigo=="ENERGIA_REQUISITO_PILAR" && (servicioSolicitado==null || !OperacionesMedidor.Normalizar(servicioSolicitado.Nombre).Contains("luz") && !OperacionesMedidor.Normalizar(servicioSolicitado.Nombre).Contains("electric")))
                    return BadRequest("El motivo de requisitos del pilar solo corresponde a solicitudes de Energía.");

                var estado = db.EstadoSolicitudServicio
                    .FirstOrDefault(e => e.NombreEstado == "Rechazada");

                if (estado == null)
                    return BadRequest("No existe el estado Rechazada.");

                using(var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable)) {
                    var anterior=solicitud.EstadoSolicitudServicio?.NombreEstado;
                    solicitud.idEstadoSolicitud = estado.idEstadoSolicitud;
                    solicitud.MotivoRechazo = motivo.Nombre;
                    solicitud.FechaResolucion = DateTime.Now;
                    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud { idSolicitudServicio=solicitud.idSolicitudServicio,idEstadoSolicitud=estado.idEstadoSolicitud,idEmpleado=ObtenerClaimEntero("idEmpleado"),FechaCambio=DateTime.Now,Observaciones=motivo.Nombre });
                    db.SaveChanges();
                    var area=LaboralAccess.AreaServicio(db,solicitud.idServicio);
                    EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                        Contexto="SOLICITUD_SERVICIO_RECHAZO",CodigoEvento="SolicitudRechazada",Entidad="SolicitudServicio",idEntidad=id.ToString(),Area=area,
                        idSocio=solicitud.idSocio,idServicio=solicitud.idServicio,EstadoAnterior=anterior,EstadoNuevo=estado.NombreEstado,
                        idMotivoEvento=idMotivo,MotivoRequerido=true,Resultado="Rechazada",MotivoNombre=motivo.Nombre
                    });
                    tx.Commit();
                }

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
            using (var trans = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
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
                    var operacionInstalacion=Guid.NewGuid();

                    var servicioSocio = ObtenerOCrearServicioSocio(solicitud.idSocio, solicitud.idServicio, solicitud.idSolicitudServicio);
                    if(ServicioRequiereMedidor(servicio.Nombre)) {
                        var json = (Newtonsoft.Json.Linq.JObject)data;
                        var op = json?.ToObject<SeleccionMedidor>();
                        if(op==null) return BadRequest("Elegí un medidor disponible o registrá el equipo nuevo.");
                        op.Fecha=DateTimeOffset.UtcNow;
                        medidorCreado=op.NuevoMedidor!=null;
                        var ficha=OperacionesMedidor.Colocar(db,(System.Security.Claims.ClaimsPrincipal)User,op,servicioSocio.idServicioSocio,"Instalar",operacionInstalacion);
                        numeroMedidor=ficha.Numero;
                    }

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
                    EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                        Contexto="SOLICITUD_SERVICIO_INSTALACION",CodigoEvento="InstalacionRealizada",Entidad="SolicitudServicio",idEntidad=id.ToString(),
                        Area=LaboralAccess.AreaServicio(db,solicitud.idServicio),idSocio=solicitud.idSocio,idServicio=solicitud.idServicio,idServicioSocio=servicioSocio.idServicioSocio,
                        EstadoAnterior="Pendiente instalacion",EstadoNuevo="Aprobada",Resultado="InstalacionRealizada",OperacionId=operacionInstalacion,
                        Datos=new{solicitud.idSolicitudServicio,servicioSocio.idServicioSocio,medidorCreado,numeroMedidor}
                    });
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
            using (var trans = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
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

                    int? idMotivo=(int?)data?.idMotivoEvento;
                    var motivo=db.Database.SqlQuery<MotivoEventoOperativoDto>(@"SELECT idMotivoEvento,Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,RequiereDetalle
                        FROM dbo.MotivoEventoOperativo WHERE idMotivoEvento=@id AND Contexto='INSTALACION_NO_REALIZADA' AND Activo=1 AND ConfirmadoCooperativa=1",
                        new System.Data.SqlClient.SqlParameter("@id",(object)idMotivo??DBNull.Value)).SingleOrDefault();
                    if(motivo==null)return BadRequest("Seleccioná un motivo confirmado para la instalación no realizada.");

                    var estadoEnRevision = ObtenerOCrearEstadoSolicitud("En Revision");
                    solicitud.idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud;
                    solicitud.idEmpleadoRevisor = idEmpleadoActual;
                    solicitud.FechaResolucion = null;
                    solicitud.MotivoRechazo = motivo.Nombre;

                    db.HistorialEstadoSolicitud.Add(new HistorialEstadoSolicitud
                    {
                        idSolicitudServicio = solicitud.idSolicitudServicio,
                        idEstadoSolicitud = estadoEnRevision.idEstadoSolicitud,
                        idEmpleado = idEmpleadoActual,
                        FechaCambio = DateTime.Now,
                        Observaciones = "Instalacion no realizada: " + motivo.Nombre
                    });

                    db.SaveChanges();
                    var area=LaboralAccess.AreaServicio(db,solicitud.idServicio);
                    EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                        Contexto="INSTALACION_NO_REALIZADA",CodigoEvento="InstalacionNoRealizada",Entidad="SolicitudServicio",idEntidad=id.ToString(),Area=area,
                        idSocio=solicitud.idSocio,idServicio=solicitud.idServicio,EstadoAnterior="Pendiente instalacion",EstadoNuevo=estadoEnRevision.NombreEstado,
                        idMotivoEvento=idMotivo,MotivoRequerido=true,Resultado="DevueltaParaRevision",MotivoNombre=motivo.Nombre
                    });
                    trans.Commit();

                    return Ok(new
                    {
                        mensaje = "Instalacion rechazada. La solicitud vuelve a revision.",
                        solicitud.idSolicitudServicio,
                        estado = estadoEnRevision.NombreEstado,
                        motivo = motivo.Nombre
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

        private ServicioSocio ObtenerOCrearServicioSocio(int idSocio, int idServicio, int idSolicitud)
        {
            var servicioSocio = new ServicioSocio
            {
                idSocio = idSocio,
                idServicio = idServicio,
                FechaAlta = DateTime.Now,
                Activo = true
            };

            db.ServicioSocio.Add(servicioSocio);
            db.SaveChanges();
            db.Database.ExecuteSqlCommand("INSERT dbo.SolicitudConexion(idSolicitudServicio,idServicioSocio) VALUES(@solicitud,@conexion)",new System.Data.SqlClient.SqlParameter("@solicitud",idSolicitud),new System.Data.SqlClient.SqlParameter("@conexion",servicioSocio.idServicioSocio));
            return servicioSocio;
        }

        private void CrearMedidorSiCorresponde(ServicioSocio servicioSocio, string nombreServicio, out bool medidorCreado, out string numeroMedidor)
        { medidorCreado=false; numeroMedidor=null;
          if(ServicioRequiereMedidor(nombreServicio)) throw new InvalidOperationException("Seleccioná un medidor real desde la instalación técnica.");
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
