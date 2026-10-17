using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using CooperativaAPI.Filters;
using CooperativaAPI.Data;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Controllers
{
    [RoutePrefix("api/facturacion/intercambio")]
    [JwtAuthorize(Roles = "Administrador,Empleado")]
    public class IntercambioFacturacionController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        private const string Version = "SOCIOAPP-1";

        private IHttpActionResult VerificarPermisoFacturacion()
        {
            if (User.IsInRole("Administrador")) return null;
            var principal = User as ClaimsPrincipal;
            var empleado = LaboralAccess.Empleado(db, principal);
            bool permitido = LaboralAccess.Gerencia(db, principal)
                || LaboralAccess.Encargado(db, empleado, "ADMINISTRACION")
                || LaboralAccess.Tiene(db, empleado, "ADMINISTRACION", "FACTURACION")
                || LaboralAccess.Encargado(db, empleado, "INTERNET")
                || LaboralAccess.Tiene(db, empleado, "INTERNET", "FACTURACION");
            return permitido ? null : Content(HttpStatusCode.Forbidden, "No tenés permisos para gestionar el intercambio de facturación.");
        }

        public sealed class ImportarArchivoRequest
        {
            public string NombreArchivo { get; set; }
            public string Contenido { get; set; }
        }

        private sealed class MedicionExportada
        {
            public int idMedicion { get; set; }
            public int idServicioSocio { get; set; }
            public string NumeroSocio { get; set; }
            public int idServicio { get; set; }
            public string Servicio { get; set; }
            public string Periodo { get; set; }
            public DateTime FechaLectura { get; set; }
            public decimal ValorLectura { get; set; }
            public decimal? ConsumoCalculado { get; set; }
            public JArray Conceptos { get; set; }
        }

        private int UsuarioSistema
        {
            get
            {
                int id;
                if (!int.TryParse(User.Identity is System.Security.Claims.ClaimsIdentity
                    ? ((System.Security.Claims.ClaimsIdentity)User.Identity).FindFirst("idUsuarioSistema")?.Value : null, out id))
                    throw new UnauthorizedAccessException("No se pudo identificar al usuario que realiza el intercambio.");
                return id;
            }
        }

        [HttpGet, Route("exportar/{periodo}")]
        public IHttpActionResult Exportar(string periodo, int? idServicio = null, bool incluirAdhesiones = false)
        {
            var permiso = VerificarPermisoFacturacion();
            if (permiso != null) return permiso;
            if (string.IsNullOrWhiteSpace(periodo) || periodo.Length > 10)
                return BadRequest("Indicá un período válido.");
            if (idServicio.HasValue && !db.Servicio.Any(s => s.idServicio == idServicio.Value))
                return BadRequest("El servicio seleccionado no existe.");

            DateTime inicio;
            if (incluirAdhesiones && !DateTime.TryParseExact(periodo,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out inicio)) return BadRequest("Para incluir adhesiones usá un período yyyy-MM.");
            string formato=incluirAdhesiones?"SOCIOAPP-2":Version;
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    var pendientes=db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.Medicion m
JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=m.idServicioSocio JOIN dbo.Socio s ON s.idSocio=ss.idSocio
WHERE m.Periodo=@p AND s.NumeroSocio IS NOT NULL AND (@s IS NULL OR ss.idServicio=@s)
AND NOT EXISTS(SELECT 1 FROM dbo.AprobacionLecturaLaboral a WHERE a.idMedicion=m.idMedicion AND a.VersionLectura=m.VersionRevision)",
                        new System.Data.SqlClient.SqlParameter("@p",periodo),new System.Data.SqlClient.SqlParameter("@s",(object)idServicio??DBNull.Value)).Single();
                    if(pendientes>0)return Content(HttpStatusCode.Conflict,"No se puede exportar: hay "+pendientes+" lecturas del período pendientes de aprobación del encargado. Revisalas antes de generar el archivo.");
                    var filas = db.Database.SqlQuery<MedicionExportada>(@"
SELECT m.idMedicion, m.idServicioSocio, s.NumeroSocio, ss.idServicio,
       se.Nombre AS Servicio, m.Periodo, m.FechaLectura, m.ValorLectura, m.ConsumoCalculado
FROM dbo.Medicion m
JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=m.idServicioSocio
JOIN dbo.Socio s ON s.idSocio=ss.idSocio
JOIN dbo.Servicio se ON se.idServicio=ss.idServicio
WHERE m.Periodo=@p AND s.NumeroSocio IS NOT NULL AND (@s IS NULL OR ss.idServicio=@s)
ORDER BY s.NumeroSocio, ss.idServicio, m.idMedicion",
                        new System.Data.SqlClient.SqlParameter("@p", periodo),
                        new System.Data.SqlClient.SqlParameter("@s", (object)idServicio ?? DBNull.Value)).ToList();

                    if(incluirAdhesiones) {
                        if(filas.GroupBy(f=>f.idServicioSocio).Any(g=>g.Count()>1)) return BadRequest("Hay más de una medición por conexión en ese período. Revisá la consolidación contable antes de incluir cuotas mensuales.");
                        var fecha=DateTime.ParseExact(periodo,"yyyy-MM",CultureInfo.InvariantCulture);
                        foreach(var f in filas) f.Conceptos=JArray.FromObject(AdhesionesBeneficios.Rows(db,@"SELECT a.Tipo,CASE WHEN a.Tipo='SEPELIO' THEN 'Servicio de Sepelio' ELSE 'Servicio de Ortopedia' END Descripcion,a.ImporteMensual Importe
                            FROM (SELECT *,ROW_NUMBER() OVER(PARTITION BY Tipo ORDER BY FechaActivacionUtc DESC,idAdhesion DESC) Posicion FROM dbo.AdhesionBeneficio WHERE idConexionEnergia=@c AND FechaActivacionUtc<@fin AND (FechaBajaUtc IS NULL OR FechaBajaUtc>=@inicio) AND ImporteMensual IS NOT NULL) a WHERE a.Posicion=1",AdhesionesBeneficios.P("@c",f.idServicioSocio),AdhesionesBeneficios.P("@inicio",fecha),AdhesionesBeneficios.P("@fin",fecha.AddMonths(1))));
                    }
                    int lote = db.Database.SqlQuery<int>(@"
INSERT dbo.LoteExportacionContable(Periodo,FormatoVersion,idUsuarioSistema,CantidadMediciones)
VALUES(@p,@v,@u,@c); SELECT CAST(SCOPE_IDENTITY() AS int);",
                        new System.Data.SqlClient.SqlParameter("@p", periodo),
                        new System.Data.SqlClient.SqlParameter("@v", formato),
                        new System.Data.SqlClient.SqlParameter("@u", UsuarioSistema),
                        new System.Data.SqlClient.SqlParameter("@c", filas.Count)).Single();

                    foreach (var f in filas)
                    {
                        string snapshot = Newtonsoft.Json.JsonConvert.SerializeObject(f);
                        db.Database.ExecuteSqlCommand(@"INSERT dbo.LoteExportacionContableDetalle
(idLoteExportacion,idMedicion,idServicioSocio,DatosExportados) VALUES(@l,@m,@ss,@d)",
                            new System.Data.SqlClient.SqlParameter("@l", lote),
                            new System.Data.SqlClient.SqlParameter("@m", f.idMedicion),
                            new System.Data.SqlClient.SqlParameter("@ss", f.idServicioSocio),
                            new System.Data.SqlClient.SqlParameter("@d", snapshot));
                    }
                    tx.Commit();

                    var sb = new StringBuilder();
                    sb.AppendLine("FormatoVersion\t" + formato);
                    sb.AppendLine("IdLoteExportacion\tIdMedicion\tIdServicioSocio\tNumeroSocio\tIdServicio\tServicio\tPeriodo\tFechaLectura\tValorLectura\tConsumoCalculado" + (incluirAdhesiones ? "\tConceptosJson" : ""));
                    foreach (var f in filas)
                    {
                        sb.Append(lote).Append('\t').Append(f.idMedicion).Append('\t').Append(f.idServicioSocio).Append('\t')
                          .Append(Celda(f.NumeroSocio)).Append('\t').Append(f.idServicio).Append('\t').Append(Celda(f.Servicio)).Append('\t')
                          .Append(Celda(f.Periodo)).Append('\t').Append(f.FechaLectura.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t')
                          .Append(f.ValorLectura.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(f.ConsumoCalculado.HasValue ? f.ConsumoCalculado.Value.ToString(CultureInfo.InvariantCulture) : "");
                        if(incluirAdhesiones) sb.Append('\t').Append(f.Conceptos.ToString(Newtonsoft.Json.Formatting.None));
                        sb.AppendLine();
                    }
                    var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
                    response.Content = new System.Net.Http.StringContent(sb.ToString(), Encoding.UTF8, "text/plain");
                    response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
                    { FileName = "mediciones_" + periodo + (idServicio.HasValue ? "_servicio" + idServicio.Value : "") + "_lote" + lote + ".txt" };
                    return ResponseMessage(response);
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return InternalServerError(ex);
                }
            }
        }

        [HttpPost, Route("importaciones/validar")]
        public IHttpActionResult Validar(ImportarArchivoRequest request)
        {
            var permiso = VerificarPermisoFacturacion();
            if (permiso != null) return permiso;
            if (request == null || string.IsNullOrWhiteSpace(request.Contenido) || request.Contenido.Length > 20 * 1024 * 1024)
                return BadRequest("El archivo está vacío o supera el límite de 20 MB.");

            string[] lineas = request.Contenido.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool v2=lineas.Length>0 && lineas[0]=="FormatoVersion\tSOCIOAPP-2";
            string formato=v2?"SOCIOAPP-2":Version;
            if (lineas.Length < 3 || lineas[0] != "FormatoVersion\t" + formato ||
                lineas[1] != "CodigoFacturaExterno\tIdLoteExportacion\tIdMedicion\tIdServicioSocio\tNumeroSocio\tPeriodo\tMonto\tFechaEmision\tFechaVencimiento\tLinkPago"+(v2?"\tConceptosJson":""))
                return BadRequest("El archivo no respeta el formato de respuesta SOCIOAPP-1.");

            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(request.Contenido));
            string hashHex = BitConverter.ToString(hash).Replace("-", "");
            int usuario = UsuarioSistema;
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.LoteImportacionFactura WHERE HashContenido=@h",
                        new System.Data.SqlClient.SqlParameter("@h", hashHex)).Single() > 0)
                        return BadRequest("Este mismo archivo ya fue recibido anteriormente.");

                    int lote = db.Database.SqlQuery<int>(@"
INSERT dbo.LoteImportacionFactura(NombreArchivo,HashContenido,FormatoVersion,idUsuarioSistema,Estado)
VALUES(@n,@h,@v,@u,'Recibido'); SELECT CAST(SCOPE_IDENTITY() AS int);",
                        new System.Data.SqlClient.SqlParameter("@n", (object)request.NombreArchivo ?? DBNull.Value),
                        new System.Data.SqlClient.SqlParameter("@h", hashHex),
                        new System.Data.SqlClient.SqlParameter("@v", formato),
                        new System.Data.SqlClient.SqlParameter("@u", usuario)).Single();

                    int rows = 0, valid = 0;
                    var codigosEnArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var conexionesEnArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 2; i < lineas.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lineas[i])) continue;
                        rows++;
                        var c = lineas[i].Split('\t');
                        string code = c.Length > 0 ? c[0].Trim() : "";
                        string numero = c.Length > 4 ? c[4].Trim() : "";
                        int idMedicion = 0, idConexion = 0, idExportacion = 0;
                        string periodo = c.Length > 5 ? c[5].Trim() : "";
                        decimal monto = 0m;
                        DateTime emision = new DateTime(1900, 1, 1), vence = new DateTime(1900, 1, 1);
                        string error = null;
                        bool parse = c.Length == (v2 ? 11 : 10) && !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(numero)
                            && code.Length <= 100 && numero.Length <= 80 && periodo.Length <= 10 && (c.Length <= 9 || c[9].Length <= 1000)
                            && int.TryParse(c[1], out idExportacion) && int.TryParse(c[2], out idMedicion)
                            && int.TryParse(c[3], out idConexion)
                            && decimal.TryParse(c[6], NumberStyles.Number, CultureInfo.InvariantCulture, out monto)
                            && DateTime.TryParseExact(c[7], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out emision)
                            && DateTime.TryParseExact(c[8], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out vence);
                        if (!parse) error = "Fila inválida: verificá columnas, identificadores, monto y fechas ISO (yyyy-MM-dd).";
                        else if (monto < 0 || vence < emision) error = "El monto no puede ser negativo y el vencimiento debe ser posterior a la emisión.";
                        else if (monto != decimal.Round(monto, 2)) error = "El monto debe tener como máximo dos decimales.";
                        else if (!codigosEnArchivo.Add(code)) error = "El código externo de factura está duplicado dentro del archivo.";
                        else if (db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.LoteExportacionContableDetalle d
JOIN dbo.LoteExportacionContable l ON l.idLoteExportacion=d.idLoteExportacion
JOIN dbo.Medicion m ON m.idMedicion=d.idMedicion
JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=d.idServicioSocio
JOIN dbo.Socio s ON s.idSocio=ss.idSocio
WHERE d.idLoteExportacion=@l AND d.idMedicion=@m AND d.idServicioSocio=@ss
AND m.Periodo=@p AND s.NumeroSocio=@n AND l.FormatoVersion=@formato",
                            new System.Data.SqlClient.SqlParameter("@l", idExportacion), new System.Data.SqlClient.SqlParameter("@m", idMedicion),
                            new System.Data.SqlClient.SqlParameter("@ss", idConexion), new System.Data.SqlClient.SqlParameter("@p", periodo),
                            new System.Data.SqlClient.SqlParameter("@n", numero), new System.Data.SqlClient.SqlParameter("@formato", formato)).Single() != 1)
                            error = "El socio, período o conexión no coincide con la medición exportada.";
                        else if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Factura WHERE CodigoExternoContable=@c",
                            new System.Data.SqlClient.SqlParameter("@c", code)).Single() > 0)
                            error = "El código externo de factura ya está aplicado.";
                        else if(v2 && (!conexionesEnArchivo.Add(idConexion+":"+periodo) || db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Factura WHERE Activo=1 AND idServicioSocio=@c AND Periodo=@p",AdhesionesBeneficios.P("@c",idConexion),AdhesionesBeneficios.P("@p",periodo)).Single()>0)) error="Ya hay una factura para esa conexión y período, en el archivo o aplicada.";
                        else if(v2) {
                            try {
                                var snapshot=db.Database.SqlQuery<string>("SELECT DatosExportados FROM dbo.LoteExportacionContableDetalle WHERE idLoteExportacion=@l AND idMedicion=@m AND idServicioSocio=@c",AdhesionesBeneficios.P("@l",idExportacion),AdhesionesBeneficios.P("@m",idMedicion),AdhesionesBeneficios.P("@c",idConexion)).Single();
                                ConceptosBeneficios.Validar(c[10],monto,JObject.Parse(snapshot)["Conceptos"] as JArray);
                            } catch(ArgumentException ex) { error=ex.Message; }
                        }
                        if(error==null) valid++;

                        object[] p = {
                            new System.Data.SqlClient.SqlParameter("@l", lote), new System.Data.SqlClient.SqlParameter("@fila", i + 1),
                            new System.Data.SqlClient.SqlParameter("@code", code), new System.Data.SqlClient.SqlParameter("@num", numero),
                            new System.Data.SqlClient.SqlParameter("@ss", parse ? (object)idConexion : DBNull.Value),
                            new System.Data.SqlClient.SqlParameter("@per", periodo),
                            new System.Data.SqlClient.SqlParameter("@monto", parse ? (object)monto : 0m),
                            new System.Data.SqlClient.SqlParameter("@emi", parse ? (object)emision : new DateTime(1900,1,1)),
                            new System.Data.SqlClient.SqlParameter("@ven", parse ? (object)vence : new DateTime(1900,1,1)),
                            new System.Data.SqlClient.SqlParameter("@url", c.Length > 9 && c[9].Length <= 1000 ? (object)c[9] : DBNull.Value),
                            new System.Data.SqlClient.SqlParameter("@med", parse ? (object)idMedicion : DBNull.Value),
                            new System.Data.SqlClient.SqlParameter("@estado", error == null ? "Valida" : "Error"),
                            new System.Data.SqlClient.SqlParameter("@error", (object)error ?? DBNull.Value),
                            AdhesionesBeneficios.P("@conceptos",v2 && parse && error==null ? c[10] : null)
                        };
                        db.Database.ExecuteSqlCommand(@"INSERT dbo.LoteImportacionFacturaDetalle
(idLoteImportacion,NumeroFila,CodigoFacturaExterno,NumeroSocio,idServicioSocio,Periodo,Monto,FechaEmision,FechaVencimiento,LinkPago,idMedicion,Estado,ErrorValidacion,ConceptosJson)
VALUES(@l,@fila,@code,@num,@ss,@per,@monto,@emi,@ven,@url,@med,@estado,@error,@conceptos)", p);
                    }
                    string estado = rows > 0 && valid == rows ? "Validado" : "ConErrores";
                    db.Database.ExecuteSqlCommand("UPDATE dbo.LoteImportacionFactura SET CantidadFilas=@r,CantidadValidas=@v,Estado=@e WHERE idLoteImportacion=@l",
                        new System.Data.SqlClient.SqlParameter("@r", rows), new System.Data.SqlClient.SqlParameter("@v", valid),
                        new System.Data.SqlClient.SqlParameter("@e", estado), new System.Data.SqlClient.SqlParameter("@l", lote));
                    tx.Commit();
                    return Ok(new { idLoteImportacion = lote, Estado = estado, CantidadFilas = rows, CantidadValidas = valid,
                        Errores = db.Database.SqlQuery<string>("SELECT CONCAT('Fila ',NumeroFila,': ',ErrorValidacion) FROM dbo.LoteImportacionFacturaDetalle WHERE idLoteImportacion=@l AND Estado='Error' ORDER BY NumeroFila",
                            new System.Data.SqlClient.SqlParameter("@l", lote)).ToList() });
                }
                catch (Exception ex) { tx.Rollback(); return InternalServerError(ex); }
            }
        }

        [HttpPost, Route("importaciones/{id:int}/aplicar")]
        public IHttpActionResult Aplicar(int id)
        {
            var permiso = VerificarPermisoFacturacion();
            if (permiso != null) return permiso;
            int idUsuario = UsuarioSistema;
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    var lote = db.Database.SqlQuery<ImportacionResumen>("SELECT idLoteImportacion,Estado,CantidadFilas FROM dbo.LoteImportacionFactura WITH (UPDLOCK,HOLDLOCK) WHERE idLoteImportacion=@l",
                        new System.Data.SqlClient.SqlParameter("@l", id)).SingleOrDefault();
                    if (lote == null) return NotFound();
                    if (lote.Estado != "Validado" || lote.CantidadFilas == 0) return BadRequest("Solo se puede aplicar un lote validado sin errores.");
                    if(db.Database.SqlQuery<int>(@"SELECT COUNT(*) FROM dbo.LoteImportacionFacturaDetalle d JOIN dbo.LoteImportacionFactura l ON l.idLoteImportacion=d.idLoteImportacion JOIN dbo.Factura f ON f.idServicioSocio=d.idServicioSocio AND f.Periodo=d.Periodo AND f.Activo=1 WHERE d.idLoteImportacion=@l AND l.FormatoVersion='SOCIOAPP-2'",AdhesionesBeneficios.P("@l",id)).Single()>0) return BadRequest("La conexión y período ya tienen una factura aplicada. Volvé a validar el intercambio.");
                    int empleado = db.Database.SqlQuery<int>("SELECT idEmpleado FROM dbo.UsuarioSistema WHERE idUsuarioSistema=@u AND idEmpleado IS NOT NULL",
                        new System.Data.SqlClient.SqlParameter("@u", idUsuario)).SingleOrDefault();
                    if (empleado == 0) return BadRequest("El usuario administrador no está vinculado a un empleado y no puede quedar como responsable de las facturas.");

                    var result = db.Database.ExecuteSqlCommand(@"
INSERT dbo.Factura(idSocio,idEmpleado,idServicio,MontoTotal,FechaEmision,FechaCaducidad,LinkPago,Pagada,FechaPago,Observaciones,Activo,idServicioSocio,CodigoExternoContable,Periodo,idMedicion)
SELECT ss.idSocio, @empleado, ss.idServicio, d.Monto, CAST(d.FechaEmision AS datetime), CAST(d.FechaVencimiento AS datetime), d.LinkPago, 0, NULL, NULL, 1,
       d.idServicioSocio,d.CodigoFacturaExterno,d.Periodo,d.idMedicion
FROM dbo.LoteImportacionFacturaDetalle d
JOIN dbo.ServicioSocio ss ON ss.idServicioSocio=d.idServicioSocio
WHERE d.idLoteImportacion=@l AND d.Estado='Valida';
UPDATE d SET Estado='Aplicada',idFactura=f.idFactura
FROM dbo.LoteImportacionFacturaDetalle d JOIN dbo.Factura f ON f.CodigoExternoContable=d.CodigoFacturaExterno
WHERE d.idLoteImportacion=@l;
UPDATE dbo.LoteImportacionFactura SET Estado='Aplicado',CantidadAplicadas=@count WHERE idLoteImportacion=@l;",
                        new System.Data.SqlClient.SqlParameter("@empleado", empleado),
                        new System.Data.SqlClient.SqlParameter("@l", id),
                        new System.Data.SqlClient.SqlParameter("@count", lote.CantidadFilas));
                    var detalles=AdhesionesBeneficios.Rows(db,"SELECT idFactura,ConceptosJson FROM dbo.LoteImportacionFacturaDetalle WHERE idLoteImportacion=@l AND ConceptosJson IS NOT NULL",AdhesionesBeneficios.P("@l",id));
                    foreach(var d in detalles) foreach(var c in JArray.Parse((string)d["ConceptosJson"]))
                        db.Database.ExecuteSqlCommand("INSERT dbo.FacturaConcepto(idFactura,Tipo,Descripcion,Importe) VALUES(@f,@t,@d,@i)",AdhesionesBeneficios.P("@f",(int)d["idFactura"]),AdhesionesBeneficios.P("@t",(string)c["Tipo"]),AdhesionesBeneficios.P("@d",(string)c["Descripcion"]),AdhesionesBeneficios.P("@i",(decimal)c["Importe"]));
                    tx.Commit();
                    return Ok(new { idLoteImportacion = id, Estado = "Aplicado", CantidadAplicadas = lote.CantidadFilas });
                }
                catch (Exception ex) { tx.Rollback(); return InternalServerError(ex); }
            }
        }

        private sealed class ImportacionResumen { public int idLoteImportacion { get; set; } public string Estado { get; set; } public int CantidadFilas { get; set; } }
        private static string Celda(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
