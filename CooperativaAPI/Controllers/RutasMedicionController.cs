using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult ObtenerTodas()
        {
            ExpirarRutas();
            try
            {
                var rutas = QueryRutas()
                    .OrderByDescending(r => r.Fecha)
                    .ThenByDescending(r => r.idRutaMedicion)
                    .ToList()
                    .Where(r => RutaVisible(r.idRutaMedicion)).Select(MapRuta)
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
            ExpirarRutas();
            try
            {
                if (User.IsInRole("Empleado") && ObtenerClaimEntero("idEmpleado") != idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "No puedes consultar hojas de otro empleado.");

                var rutas = QueryRutas()
                    .Where(r => r.idEmpleado == idEmpleado)
                    .OrderByDescending(r => r.Fecha)
                    .ThenByDescending(r => r.idRutaMedicion)
                    .ToList()
                    .Where(r => RutaVisible(r.idRutaMedicion)).Select(MapRuta)
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
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.Gerencia(permisos,(ClaimsPrincipal)User) || LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,false) || LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,true)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

            try
            {
                var ruta = QueryRutas().FirstOrDefault(r => r.idRutaMedicion == idRuta);
                if (ruta == null) return NotFound();

                return Ok(MapRuta(ruta));
            }
            catch (Exception ex)
            {
                return BadRequest("Error al obtener hoja de ruta: " + ex.Message);
            }
        }

        [HttpPost]
        [Route("")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult Crear([FromBody] CrearRutaMedicionRequest request)
        {
            try
            {
                var error = ValidarCrearRuta(request);
                if (!string.IsNullOrWhiteSpace(error)) return BadRequest(error);

                var medidoresIds = request.Medidores.Distinct().ToList();
                using(var permisos=new CooperativaEntities()) {
                    var areas=medidoresIds.Select(id=>LaboralAccess.AreaMedidor(permisos,id)).Distinct().ToList();
                    if(areas.Count!=1 || !new[]{"AGUA","ENERGIA"}.Contains(areas[0]) || !LaboralAccess.Encargado(permisos,LaboralAccess.Empleado(permisos,(ClaimsPrincipal)User),areas[0]) || !LaboralAccess.Tiene(permisos,request.IdEmpleado,areas[0],"LECTURISTA"))return StatusCode(System.Net.HttpStatusCode.Forbidden);
                }


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

                    var ambiguos = medidoresIds.Where(id => db.Database.SqlQuery<int>(@"
                        SELECT COUNT(*) FROM dbo.AsignacionMedidor a
                        JOIN dbo.Medidor m ON m.idMedidor=a.idMedidor
                        WHERE a.idMedidor=@id AND a.Cerrada=0 AND a.PendienteConciliacion=0
                          AND m.Activo=1 AND m.idServicioSocio=a.idServicioSocio
                          AND NOT EXISTS (SELECT 1 FROM dbo.AsignacionMedidor p
                                          WHERE p.idServicioSocio=a.idServicioSocio AND p.Cerrada=0
                                            AND p.PendienteConciliacion=1)",
                        new System.Data.SqlClient.SqlParameter("@id", id)).Single() != 1).ToList();
                    if (ambiguos.Any())
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.Conflict, new { message = "Hay medidores con una instalacion pendiente de conciliacion; no se pueden incluir en una ruta hasta verificar fisicamente la conexion.", medidores = ambiguos });
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
                            d.Estado == "Pendiente" &&
                            (d.RutaMedicion.Estado == "Pendiente" || d.RutaMedicion.Estado == "EnCurso"))
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
                    AuditarRuta(ruta,"AsignarRuta",null);
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
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,false)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

            try
            {
                using(var transaction=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                var ruta = db.RutasMedicion.Find(idRuta);
                if (ruta == null) return NotFound();
                if (ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");
                if (ruta.Estado != "Pendiente") return BadRequest("La hoja de ruta no esta pendiente.");

                var estadoAnterior=ruta.Estado;
                ruta.Estado = "EnCurso";
                ruta.FechaInicio = ruta.FechaInicio ?? DateTime.UtcNow;
                db.SaveChanges();
                AuditarRuta(ruta,"IniciarRuta",estadoAnterior);
                transaction.Commit();

                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
                }
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
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,false)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

            try
            {
                if (request == null) return BadRequest("La lectura es obligatoria.");
                if (String.IsNullOrWhiteSpace(request.FotoBase64)) return BadRequest("Adjuntá la foto del medidor antes de guardar la lectura.");
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
                    if (ruta.Estado == "Finalizada" || ruta.Estado == "Cancelada" || ruta.Estado == "TiempoCaducado")
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
                    var asignacionVigente = db.Database.SqlQuery<int>(@"
                        SELECT COUNT(*) FROM dbo.AsignacionMedidor a
                        WHERE a.idMedidor=@id AND a.Cerrada=0 AND a.PendienteConciliacion=0
                          AND a.idServicioSocio=@conexion
                          AND NOT EXISTS (SELECT 1 FROM dbo.AsignacionMedidor p
                                          WHERE p.idServicioSocio=a.idServicioSocio AND p.Cerrada=0
                                            AND p.PendienteConciliacion=1)",
                        new System.Data.SqlClient.SqlParameter("@id", idMedidor),
                        new System.Data.SqlClient.SqlParameter("@conexion", medidor.idServicioSocio.Value)).Single();
                    if (asignacionVigente != 1)
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.Conflict, "La instalacion del medidor esta pendiente de conciliacion y no puede recibir lecturas.");
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

                    if(request.FechaLectura.HasValue && request.FechaLectura.Value.Date!=DateTime.Today)return BadRequest("La lectura debe registrarse en la fecha actual.");
                    var fechaLectura = DateTime.UtcNow;
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
                    var conexionLectura=readDb.ServicioSocio.Find(medicion.idServicioSocio);
                    string areaLectura=LaboralAccess.AreaServicio(readDb,conexionLectura.idServicio);
                    EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                        Contexto="LECTURA_REALIZADA",CodigoEvento="LecturaRealizada",Entidad="Medicion",idEntidad=medicion.idMedicion.ToString(),
                        Area=areaLectura,idSocio=conexionLectura.idSocio,idServicio=conexionLectura.idServicio,idServicioSocio=medicion.idServicioSocio,idMedidor=idMedidor,idRutaMedicion=idRuta,
                        EstadoAnterior="Pendiente",EstadoNuevo="Medido",Resultado="LecturaRealizada",Datos=new{medicion.ValorLectura,medicion.ConsumoCalculado,medicion.Periodo,medicion.FechaLectura}
                    });

                    if(!String.IsNullOrWhiteSpace(request.FotoBase64)) {
                        byte[] bytes;try{bytes=Convert.FromBase64String(request.FotoBase64);}catch(FormatException){transaction.Rollback();return BadRequest("La foto adjunta no tiene formato válido.");}
                        var tipo=request.TipoFoto;
                        bool valido=bytes.Length>0 && bytes.Length<=2*1024*1024 && ((tipo=="image/jpeg" && bytes.Length>3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255) || (tipo=="image/png" && bytes.Length>=8 && bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})));
                        if(!valido){transaction.Rollback();return BadRequest("Adjuntá una foto JPEG o PNG válida de hasta 2 MB.");}
                        db.Database.ExecuteSqlCommand("INSERT dbo.EvidenciaLecturaLaboral(idMedicion,idUsuarioSistema,Nombre,Tipo,Contenido) VALUES(@m,@u,@n,@t,@b)",LaboralAccess.P("@m",medicion.idMedicion),LaboralAccess.P("@u",LaboralAccess.Usuario((ClaimsPrincipal)User)),LaboralAccess.P("@n",Path.GetFileName(request.NombreFoto??"medidor")),LaboralAccess.P("@t",tipo),LaboralAccess.P("@b",bytes));
                    }

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
                    AuditarRuta(ruta,"RegistrarLectura");
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
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,false)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

            try
            {
                using(var transaction=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                if (request == null || request.Estado!="Pendiente" && (!request.OperacionId.HasValue || request.OperacionId==Guid.Empty)) return BadRequest("Seleccioná un motivo y una clave de operación válidos.");

                var ruta = db.RutasMedicion
                    .Include(r => r.RutaMedicionDetalle)
                    .FirstOrDefault(r => r.idRutaMedicion == idRuta);
                if (ruta == null) return NotFound();
                if (ObtenerClaimEntero("idEmpleado") != ruta.idEmpleado)
                    return Content(HttpStatusCode.Forbidden, "La hoja de ruta no pertenece al empleado autenticado.");
                if (ruta.Estado == "Finalizada" || ruta.Estado == "Cancelada" || ruta.Estado == "TiempoCaducado") return BadRequest("La hoja de ruta ya esta cerrada.");

                var detalle = ruta.RutaMedicionDetalle.FirstOrDefault(d => d.idMedidor == idMedidor);
                if (detalle == null) return BadRequest("El medidor no pertenece a esta hoja de ruta.");
                if (detalle.Estado == "Medido") return BadRequest("No se puede cambiar una medicion ya registrada.");
                if(request.OperacionId.HasValue && db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EventoOperativo WHERE ClaveOrigen=@key",LaboralAccess.P("@key","API:"+request.OperacionId.Value.ToString("N"))).Any()) {
                    transaction.Commit();
                    return Ok(MapRuta(QueryRutas().First(r=>r.idRutaMedicion==idRuta)));
                }

                if (request.Estado == "Pendiente")
                {
                    if (request.idMotivoEvento.HasValue) return BadRequest("El estado pendiente no lleva motivo de no lectura.");
                }
                else
                {
                    var motivo=db.Database.SqlQuery<MotivoEventoOperativoDto>(@"SELECT idMotivoEvento,Contexto,Codigo,Nombre,Activo,ConfirmadoCooperativa,RequiereDetalle
                        FROM dbo.MotivoEventoOperativo WHERE idMotivoEvento=@id AND Contexto='LECTURA_NO_REALIZADA' AND Activo=1 AND ConfirmadoCooperativa=1",
                        LaboralAccess.P("@id",request.idMotivoEvento)).SingleOrDefault();
                    if(motivo==null)return BadRequest("Seleccioná un motivo confirmado para la lectura no realizada.");
                    if(new[]{"MEDIDOR_OBSTRUIDO","DOMICILIO_CERRADO","ACCESO_DENEGADO"}.Contains(motivo.Codigo))request.Estado="NoAcceso";
                    else if(new[]{"MEDIDOR_AVERIADO","MEDIDOR_ILEGIBLE"}.Contains(motivo.Codigo))request.Estado="MedidorDanado";
                    else if(new[]{"RIESGO_EMPLEADO","MEDIDOR_NO_LOCALIZADO","DIRECCION_NO_COINCIDE"}.Contains(motivo.Codigo))request.Estado="Observado";
                    else return BadRequest("El motivo no es válido para una lectura no realizada.");
                }

                detalle.Estado = request.Estado;
                detalle.Observacion = StringOrNull(request.Observacion);
                detalle.FechaHoraLectura = detalle.FechaHoraLectura ?? DateTime.UtcNow;

                if (ruta.Estado == "Pendiente")
                {
                    ruta.Estado = "EnCurso";
                    ruta.FechaInicio = ruta.FechaInicio ?? DateTime.UtcNow;
                }

                db.SaveChanges();
                AuditarRuta(ruta,"IncidenciaRuta");
                if(request.Estado!="Pendiente") {
                    var medidor=readDb.Medidor.Find(idMedidor);
                    var conexion=medidor?.idServicioSocio.HasValue==true?readDb.ServicioSocio.Find(medidor.idServicioSocio.Value):null;
                    var servicio=conexion==null?null:readDb.Servicio.Find(conexion.idServicio);
                    var tipo=medidor==null?null:readDb.TipoMedidor.Find(medidor.idTipoMedidor);
                    var area=tipo!=null && OperacionesMedidor.Normalizar(tipo.Nombre).Contains("agua")?"AGUA":"ENERGIA";
                    EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                        Contexto="LECTURA_NO_REALIZADA",CodigoEvento="LecturaNoRealizada",Entidad="Medidor",idEntidad=idMedidor.ToString(),
                        Area=area,idSocio=conexion?.idSocio,idServicio=servicio?.idServicio,idServicioSocio=conexion?.idServicioSocio,idMedidor=idMedidor,idRutaMedicion=idRuta,
                        EstadoAnterior="Pendiente",EstadoNuevo=request.Estado,idMotivoEvento=request.idMotivoEvento,MotivoRequerido=true,
                        OperacionId=request.OperacionId,ClaveIdempotencia=request.OperacionId.Value.ToString("N"),Resultado="LecturaNoRealizada",Detalle=StringOrNull(request.Observacion),Datos=new{ruta.idRutaMedicion,idMedidor,request.Estado}
                    });
                }
                transaction.Commit();
                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
                }
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
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,false)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

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
                    if (ruta.Estado == "TiempoCaducado")
                    {
                        transaction.Rollback();
                        return Content(HttpStatusCode.Conflict, "La hoja de ruta supero su plazo de 24 horas y ya no puede cerrarse. Cree una nueva hoja con los medidores pendientes.");
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

                    var sinFoto=db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.RutaMedicionDetalle d WHERE d.idRutaMedicion=@id AND d.Estado='Medido'
                     AND (d.idMedicion IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.EvidenciaLecturaLaboral f WHERE f.idMedicion=d.idMedicion AND DATALENGTH(f.Contenido)>0))",LaboralAccess.P("@id",idRuta)).Single();
                    if(sinFoto>0)return BadRequest("No se puede finalizar: hay "+sinFoto+" lecturas realizadas sin foto del medidor. Adjuntá las fotos pendientes.");

                    var estadoAnterior=ruta.Estado;
                    ruta.Estado = "Finalizada";
                    ruta.FechaFinalizacion = DateTime.UtcNow;
                    ruta.FirmaDigital = firma.Bytes;
                    ruta.FirmaMimeType = firma.MimeType;
                    if (!string.IsNullOrWhiteSpace(request.Observacion)) ruta.Observacion = request.Observacion.Trim();

                    db.SaveChanges();
                    AuditarRuta(ruta,"FinalizarRuta",estadoAnterior);
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

        [HttpGet]
        [Route("mediciones/{idMedicion:int}/foto")]
        [JwtAuthorize(Roles = "Empleado,Administrador")]
        public IHttpActionResult DescargarFotoLectura(int idMedicion) {
            var detalle=db.RutasMedicionDetalle.Include(d=>d.RutaMedicion).FirstOrDefault(d=>d.idMedicion==idMedicion);
            if(detalle==null)return NotFound();
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.Gerencia(permisos,(ClaimsPrincipal)User) || LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,detalle.idRutaMedicion) || LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,detalle.idRutaMedicion,true)))return StatusCode(HttpStatusCode.Forbidden); }
            var file=db.Database.SqlQuery<EvidenciaFotoRuta>("SELECT TOP 1 Nombre,Tipo,Contenido FROM dbo.EvidenciaLecturaLaboral WHERE idMedicion=@id ORDER BY idEvidencia DESC",new System.Data.SqlClient.SqlParameter("@id",idMedicion)).FirstOrDefault();
            if(file==null)return NotFound();var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(file.Contenido)};response.Content.Headers.ContentType=new MediaTypeHeaderValue(file.Tipo);response.Headers.Add("X-Content-Type-Options","nosniff");response.Headers.CacheControl=new CacheControlHeaderValue{NoStore=true};return ResponseMessage(response);
        }

        [HttpPost,Route("mediciones/{idMedicion:int}/foto")]
        [JwtAuthorize(Roles="Empleado")]
        public IHttpActionResult AdjuntarFotoLectura(int idMedicion,[FromBody] CargarLecturaRutaRequest request) {
            if(request==null || String.IsNullOrWhiteSpace(request.FotoBase64))return BadRequest("Adjuntá una foto del medidor.");
            byte[] bytes;try{bytes=Convert.FromBase64String(request.FotoBase64);}catch(FormatException){return BadRequest("La foto no tiene formato válido.");}
            bool valido=bytes.Length>0 && bytes.Length<=2*1024*1024 && ((request.TipoFoto=="image/jpeg" && bytes.Length>3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255) || (request.TipoFoto=="image/png" && bytes.Length>=8 && bytes.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})));
            if(!valido)return BadRequest("Adjuntá una foto JPEG o PNG válida de hasta 2 MB.");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                var detalle=db.RutasMedicionDetalle.Include(d=>d.RutaMedicion).FirstOrDefault(d=>d.idMedicion==idMedicion);
                if(detalle==null)return NotFound();
                var ruta=detalle.RutaMedicion;
                if(!LaboralAccess.LecturistaRuta(readDb,(ClaimsPrincipal)User,ruta.idRutaMedicion,false))return StatusCode(HttpStatusCode.Forbidden);
                if(ruta.Estado!="Pendiente" && ruta.Estado!="EnCurso")return BadRequest("La hoja de ruta ya está cerrada.");
                if(db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EvidenciaLecturaLaboral WHERE idMedicion=@id",LaboralAccess.P("@id",idMedicion)).Any())return BadRequest("Esta lectura ya tiene una foto registrada.");
                db.Database.ExecuteSqlCommand("INSERT dbo.EvidenciaLecturaLaboral(idMedicion,idUsuarioSistema,Nombre,Tipo,Contenido) VALUES(@m,@u,@n,@t,@b)",LaboralAccess.P("@m",idMedicion),LaboralAccess.P("@u",LaboralAccess.Usuario((ClaimsPrincipal)User)),LaboralAccess.P("@n",Path.GetFileName(request.NombreFoto??"medidor")),LaboralAccess.P("@t",request.TipoFoto),LaboralAccess.P("@b",bytes));
                AuditarRuta(ruta,"AdjuntarFotoLectura");tx.Commit();return Ok(new{Guardada=true});
            }
        }

        [HttpPut]
        [Route("{idRuta:int}/cancelar")]
        [JwtAuthorize(Roles = "Administrador,Empleado")]
        public IHttpActionResult Cancelar(int idRuta, [FromBody] CancelarRutaRequest request)
        {
            ExpirarRutas(idRuta);
            using(var permisos=new CooperativaEntities()) { if(!(LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,idRuta,true)))return StatusCode(System.Net.HttpStatusCode.Forbidden); }

            try
            {
                using(var transaction=db.Database.BeginTransaction(IsolationLevel.Serializable)) {
                var ruta = db.RutasMedicion.Find(idRuta);
                if (ruta == null) return NotFound();
                if (ruta.Estado != "Pendiente" && ruta.Estado != "EnCurso")
                    return BadRequest("La hoja no se puede cancelar en su estado actual.");

                var estadoAnterior=ruta.Estado;
                ruta.Estado = "Cancelada";
                if (!string.IsNullOrWhiteSpace(request?.Observacion)) ruta.Observacion = request.Observacion.Trim();
                db.SaveChanges();
                AuditarRuta(ruta,"CancelarRuta",estadoAnterior);
                transaction.Commit();

                return Ok(MapRuta(QueryRutas().First(r => r.idRutaMedicion == idRuta)));
                }
            }
            catch (Exception ex)
            {
                return BadRequest("Error al cancelar hoja: " + ex.Message);
            }
        }

        private void AuditarRuta(RutaMedicion ruta,string accion,string estadoAnterior=null) {
            var area=db.Database.SqlQuery<string>(@"SELECT DISTINCT a.Area FROM dbo.RutaMedicionDetalle d JOIN dbo.ContextoRutaMedidor c ON c.idRutaMedicionDetalle=d.idRutaMedicionDetalle JOIN dbo.Servicio se ON se.Nombre=c.ServicioNombre JOIN dbo.AreaServicio a ON a.idServicio=se.idServicio WHERE d.idRutaMedicion=@id",LaboralAccess.P("@id",ruta.idRutaMedicion)).Single();
            int? task=null;
            if(accion=="FinalizarRuta") task=db.Database.SqlQuery<int>(@"INSERT dbo.TareaLaboral(Area,Categoria,OrigenId,idEmpleadoAsignado,Titulo,Estado,Resultado) OUTPUT INSERTED.idTarea VALUES(@a,'Ruta',@id,@e,@titulo,'EnRevision',@resultado)",LaboralAccess.P("@a",area),LaboralAccess.P("@id",ruta.idRutaMedicion),LaboralAccess.P("@e",ruta.idEmpleado),LaboralAccess.P("@titulo","Revisar hoja: "+ruta.Nombre),LaboralAccess.P("@resultado",ruta.Observacion??"Recorrido finalizado. Revisar lecturas e incidencias.")).Single();
            db.Database.ExecuteSqlCommand(@"INSERT dbo.AuditoriaLaboral(idTarea,Area,idUsuarioSistema,idEmpleado,Accion,EstadoNuevo,Motivo,Datos) VALUES(@t,@a,@u,@e,@accion,@estado,@motivo,@datos)",LaboralAccess.P("@t",task),LaboralAccess.P("@a",area),LaboralAccess.P("@u",LaboralAccess.Usuario((ClaimsPrincipal)User)),LaboralAccess.P("@e",ObtenerClaimEntero("idEmpleado")),LaboralAccess.P("@accion",accion),LaboralAccess.P("@estado",task.HasValue?"EnRevision":ruta.Estado),LaboralAccess.P("@motivo",ruta.Observacion),LaboralAccess.P("@datos",Newtonsoft.Json.JsonConvert.SerializeObject(new{ruta.idRutaMedicion,ruta.idEmpleado})));
            if(accion!="RegistrarLectura" && accion!="IncidenciaRuta") EventoOperativoStore.Registrar(db,(ClaimsPrincipal)User,new EventoOperativoNuevo {
                Contexto="HOJA_RUTA",CodigoEvento=accion,Entidad="RutaMedicion",idEntidad=ruta.idRutaMedicion.ToString(),idEmpleado=ruta.idEmpleado,
                Area=area,idRutaMedicion=ruta.idRutaMedicion,EstadoAnterior=estadoAnterior,EstadoNuevo=ruta.Estado,Resultado=accion,
                Detalle=ruta.Observacion,Datos=new{ruta.idRutaMedicion,ruta.idEmpleado,ruta.Nombre,ruta.Fecha}
            });
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

        private void ExpirarRutas(int? idRuta = null)
        {
            db.Database.ExecuteSqlCommand("EXEC dbo.ExpirarRutasMedicion @idRutaMedicion",
                new System.Data.SqlClient.SqlParameter("@idRutaMedicion", (object)idRuta ?? DBNull.Value));
        }

        private IQueryable<RutaMedicion> QueryRutas()
        {
            ExpirarRutas();
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
                fechaCaducidadUtc = db.Database.SqlQuery<DateTime>("SELECT FechaCaducidadUtc FROM dbo.RutaMedicion WHERE idRutaMedicion=@id", new System.Data.SqlClient.SqlParameter("@id", ruta.idRutaMedicion)).Single(),
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
                fotoMedidorUrl = detalle.idMedicion.HasValue && db.Database.SqlQuery<int>("SELECT 1 FROM dbo.EvidenciaLecturaLaboral WHERE idMedicion=@id",new System.Data.SqlClient.SqlParameter("@id",detalle.idMedicion.Value)).Any() ? "mediciones/"+detalle.idMedicion.Value+"/foto" : detalle.FotoMedidorUrl,
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

        private bool RutaVisible(int id) {
            using(var permisos=new CooperativaEntities())return LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,id) || LaboralAccess.LecturistaRuta(permisos,(ClaimsPrincipal)User,id,true);
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
        public string FotoBase64 { get; set; }
        public string NombreFoto { get; set; }
        public string TipoFoto { get; set; }
    }

    public class CambiarEstadoDetalleRequest
    {
        public string Estado { get; set; }
        public int? idMotivoEvento { get; set; }
        public Guid? OperacionId { get; set; }
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

    public class EvidenciaFotoRuta { public string Nombre {get;set;} public string Tipo {get;set;} public byte[] Contenido {get;set;} }

    public class RutaMedicionDto
    {
        public int idRutaMedicion { get; set; }
        public int id { get; set; }
        public int idEmpleado { get; set; }
        public string empleadoNombre { get; set; }
        public string nombre { get; set; }
        public string fecha { get; set; }
        public string estado { get; set; }
        public DateTime fechaCaducidadUtc { get; set; }
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
