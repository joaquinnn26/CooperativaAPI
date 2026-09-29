using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web.Http;
using CooperativaAPI.Filters;
using Newtonsoft.Json;

namespace CooperativaAPI.Controllers
{
    // Consultas parametrizadas sobre la conexión EDMX vigente: no modifica archivos generados.
    [RoutePrefix("api/territorio")]
    [JwtAuthorize(Roles = "Administrador")]
    public class TerritorioController : ApiController
    {
        private readonly CooperativaEntities db = new CooperativaEntities();
        public class Catalogo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public bool Activo { get; set; }
            public int? idZona { get; set; }
            public byte[] Version { get; set; }
        }
        public class Cambio
        {
            public string Nombre { get; set; }
            public bool? Activo { get; set; }
            public int? idZona { get; set; }
            public int? idBarrio { get; set; }
            public string Direccion { get; set; }
            public string Version { get; set; }
            public string Motivo { get; set; }
        }
        public class Conexion
        {
            public int idServicioSocio { get; set; }
            public int idSocio { get; set; }
            public string Socio { get; set; }
            public string Servicio { get; set; }
            public bool Activo { get; set; }
            public int? idBarrio { get; set; }
            public int? idZona { get; set; }
            public string Barrio { get; set; }
            public string Zona { get; set; }
            public string Direccion { get; set; }
            public byte[] Version { get; set; }
        }
        public class Ubicacion
        {
            public int idServicioSocio { get; set; }
            public int? idBarrio { get; set; }
            public string Direccion { get; set; }
            public byte[] Version { get; set; }
        }
        public class Movimiento
        {
            public long idHistorial { get; set; }
            public DateTime FechaUtc { get; set; }
            public int idUsuarioSistema { get; set; }
            public string Usuario { get; set; }
            public string Entidad { get; set; }
            public int idEntidad { get; set; }
            public string Accion { get; set; }
            public string Motivo { get; set; }
            public string Anterior { get; set; }
            public string Nuevo { get; set; }
        }
        private static SqlParameter P(string name, object value)
        { return new SqlParameter(name, value ?? DBNull.Value); }
        private static string Texto(string value)
        { return Regex.Replace((value ?? "").Trim(), @"\s+", " "); }
        private static bool MismaVersion(byte[] actual, string enviada)
        { return actual == null ? string.IsNullOrEmpty(enviada) : Convert.ToBase64String(actual) == enviada; }
        private static bool EsBarrio(string tipo) { return tipo == "barrios"; }
        private static bool TipoValido(string tipo) { return tipo == "zonas" || tipo == "barrios"; }
        private Catalogo Obtener(string tipo, int id)
        {
            return EsBarrio(tipo)
                ? db.Database.SqlQuery<Catalogo>("SELECT idBarrio Id, Nombre, Activo, idZona, Version FROM dbo.Barrio WHERE idBarrio=@id", P("@id",id)).SingleOrDefault()
                : db.Database.SqlQuery<Catalogo>("SELECT idZona Id, Nombre, Activo, CAST(NULL AS int) idZona, Version FROM dbo.Zona WHERE idZona=@id", P("@id",id)).SingleOrDefault();
        }
        private bool Existe(string sql, params object[] parameters)
        { return db.Database.SqlQuery<int>(sql, parameters).Any(); }
        private void Auditar(string entidad, int id, string accion, string motivo, object anterior, object nuevo)
        {
            var identity = (ClaimsIdentity)User.Identity;
            int usuarioId = int.Parse(identity.FindFirst("idUsuarioSistema").Value);
            var usuario = db.UsuarioSistema.Where(u => u.idUsuarioSistema == usuarioId).Select(u => u.Usuario).Single();
            db.Database.ExecuteSqlCommand(@"INSERT dbo.HistorialTerritorial
                (idUsuarioSistema,Usuario,Entidad,idEntidad,Accion,Motivo,Anterior,Nuevo)
                VALUES (@usuarioId,@usuario,@entidad,@id,@accion,@motivo,@anterior,@nuevo)",
                P("@usuarioId",usuarioId),P("@usuario",usuario),P("@entidad",entidad),P("@id",id),
                P("@accion",accion),P("@motivo",motivo),P("@anterior",anterior == null ? null : JsonConvert.SerializeObject(anterior)),
                P("@nuevo",JsonConvert.SerializeObject(nuevo)));
        }

        [HttpGet, Route("catalogos")]
        public IHttpActionResult Catalogos()
        {
            return Ok(new {
                zonas = db.Database.SqlQuery<Catalogo>("SELECT idZona Id,Nombre,Activo,CAST(NULL AS int) idZona,Version FROM dbo.Zona ORDER BY Nombre").ToList(),
                barrios = db.Database.SqlQuery<Catalogo>("SELECT idBarrio Id,Nombre,Activo,idZona,Version FROM dbo.Barrio ORDER BY Nombre").ToList()
            });
        }

        [HttpPost, Route("{tipo:regex(^(zonas|barrios)$)}")]
        public IHttpActionResult Crear(string tipo, Cambio cambio) { return Guardar(tipo, null, cambio); }
        [HttpPut, Route("{tipo:regex(^(zonas|barrios)$)}/{id:int}")]
        public IHttpActionResult Editar(string tipo, int id, Cambio cambio) { return Guardar(tipo, id, cambio); }

        private IHttpActionResult Guardar(string tipo, int? id, Cambio cambio)
        {
            if (!TipoValido(tipo)) return NotFound();
            if (cambio == null || !ModelState.IsValid) return BadRequest("Datos inválidos.");
            var nombre = Texto(cambio.Nombre);
            var motivo = Texto(cambio.Motivo);
            if (nombre.Length == 0 || nombre.Length > 100 || motivo.Length == 0 || motivo.Length > 300 || !cambio.Activo.HasValue)
                return BadRequest("Indicá nombre (hasta 100 caracteres), estado y motivo (hasta 300 caracteres).");
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    var anterior = id.HasValue ? Obtener(tipo,id.Value) : null;
                    if (id.HasValue && anterior == null) return NotFound();
                    if (!MismaVersion(anterior == null ? null : anterior.Version,cambio.Version))
                        return Content(HttpStatusCode.Conflict,"El registro cambió. Actualizá los datos antes de editar.");
                    // Los nombres de tablas provienen exclusivamente de estas constantes.
                    var tabla = EsBarrio(tipo) ? "Barrio" : "Zona";
                    var clave = EsBarrio(tipo) ? "idBarrio" : "idZona";
                    if (Existe("SELECT 1 FROM dbo."+tabla+" WHERE Nombre=@nombre AND "+clave+"<>@id",P("@nombre",nombre),P("@id",id ?? 0)))
                        return Content(HttpStatusCode.Conflict,"Ya existe ese nombre, incluso entre registros inactivos.");
                    if (EsBarrio(tipo))
                    {
                        var zona = cambio.idZona.HasValue ? Obtener("zonas",cambio.idZona.Value) : null;
                        if (zona == null || (!zona.Activo && (cambio.Activo.Value || anterior == null || anterior.idZona != cambio.idZona)))
                            return BadRequest("Seleccioná una zona activa.");
                        if (!cambio.Activo.Value && id.HasValue && Existe(@"SELECT 1 FROM dbo.UbicacionServicio u JOIN dbo.ServicioSocio s ON s.idServicioSocio=u.idServicioSocio
                            WHERE u.idBarrio=@id AND s.Activo=1",P("@id",id.Value)))
                            return Content(HttpStatusCode.Conflict,"El barrio tiene conexiones activas. Reasignalas antes de desactivarlo.");
                    }
                    else if (!cambio.Activo.Value && id.HasValue && Existe("SELECT 1 FROM dbo.Barrio WHERE idZona=@id AND Activo=1",P("@id",id.Value)))
                        return Content(HttpStatusCode.Conflict,"La zona tiene barrios activos. Reasignalos o desactivalos primero.");
                    int resultado;
                    if (id.HasValue)
                    {
                        db.Database.ExecuteSqlCommand("UPDATE dbo."+tabla+" SET Nombre=@nombre,Activo=@activo"+(EsBarrio(tipo)?",idZona=@zona":"")+" WHERE "+clave+"=@id",
                            P("@nombre",nombre),P("@activo",cambio.Activo.Value),P("@zona",cambio.idZona),P("@id",id.Value));
                        resultado=id.Value;
                    }
                    else
                    {
                        resultado=db.Database.SqlQuery<int>("INSERT dbo."+tabla+" (Nombre,Activo"+(EsBarrio(tipo)?",idZona":"")+") OUTPUT INSERTED."+clave+
                            " VALUES (@nombre,@activo"+(EsBarrio(tipo)?",@zona":"")+")",P("@nombre",nombre),P("@activo",cambio.Activo.Value),P("@zona",cambio.idZona)).Single();
                    }
                    var nuevo=Obtener(tipo,resultado);
                    Auditar(tabla,resultado,id.HasValue?"Actualizar":"Crear",motivo,anterior,nuevo);
                    tx.Commit();
                    return Ok(nuevo);
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627 || ex.Number == 1205)
                { return Content(HttpStatusCode.Conflict,"Otro cambio entró en conflicto. Actualizá y reintentá."); }
            }
        }

        private const string DesdeConexiones = @" FROM dbo.ServicioSocio s
            JOIN dbo.Socio so ON so.idSocio=s.idSocio JOIN dbo.Persona p ON p.idPersona=so.idPersona
            JOIN dbo.Servicio se ON se.idServicio=s.idServicio
            LEFT JOIN dbo.UbicacionServicio u ON u.idServicioSocio=s.idServicioSocio
            LEFT JOIN dbo.Barrio b ON b.idBarrio=u.idBarrio LEFT JOIN dbo.Zona z ON z.idZona=b.idZona
            WHERE (@zona IS NULL OR b.idZona=@zona) AND (@barrio IS NULL OR u.idBarrio=@barrio)
            AND (@pendientes=0 OR u.idBarrio IS NULL OR NULLIF(LTRIM(RTRIM(u.Direccion)),'') IS NULL)
            AND (@q='' OR CHARINDEX(@q, CONCAT(p.Nombre,' ',p.Apellido,' ',se.Nombre,' ',u.Direccion,' ',s.idSocio,' ',s.idServicioSocio))>0)";
        [HttpGet, Route("conexiones")]
        public IHttpActionResult Conexiones(int pagina=1, int? zona=null, int? barrio=null, bool pendientes=false, string q="")
        {
            if (pagina<1 || pagina>100000 || (q ?? "").Length>100) return BadRequest("Filtros inválidos.");
            Func<object[]> parametros=()=>new object[]{P("@zona",zona),P("@barrio",barrio),P("@pendientes",pendientes),P("@q",Texto(q))};
            var total=db.Database.SqlQuery<int>("SELECT COUNT(*)"+DesdeConexiones,parametros()).Single();
            var args=parametros().Concat(new object[]{P("@offset",(pagina-1)*25)}).ToArray();
            var items=db.Database.SqlQuery<Conexion>(@"SELECT s.idServicioSocio,s.idSocio,CONCAT(p.Apellido,', ',p.Nombre) Socio,se.Nombre Servicio,
                ISNULL(s.Activo,0) Activo,u.idBarrio,b.idZona,b.Nombre Barrio,z.Nombre Zona,u.Direccion,u.Version"+DesdeConexiones+
                " ORDER BY s.idServicioSocio OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",args).ToList();
            return Ok(new {items,total,pagina});
        }

        [HttpPut, Route("conexiones/{id:int}")]
        public IHttpActionResult Ubicar(int id,Cambio cambio)
        {
            if(cambio==null || !ModelState.IsValid) return BadRequest("Datos inválidos.");
            var direccion=Texto(cambio.Direccion); var motivo=Texto(cambio.Motivo);
            if(direccion.Length==0 || direccion.Length>250 || motivo.Length==0 || motivo.Length>300 || !cambio.idBarrio.HasValue)
                return BadRequest("Indicá dirección (hasta 250 caracteres), barrio y motivo (hasta 300 caracteres).");
            using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    if(!Existe("SELECT 1 FROM dbo.ServicioSocio WHERE idServicioSocio=@id",P("@id",id))) return NotFound();
                    var anterior=db.Database.SqlQuery<Ubicacion>("SELECT * FROM dbo.UbicacionServicio WHERE idServicioSocio=@id",P("@id",id)).SingleOrDefault();
                    if(!MismaVersion(anterior==null?null:anterior.Version,cambio.Version))
                        return Content(HttpStatusCode.Conflict,"La ubicación cambió. Actualizá antes de editar.");
                    if(!Existe("SELECT 1 FROM dbo.Barrio b JOIN dbo.Zona z ON z.idZona=b.idZona WHERE b.idBarrio=@id AND b.Activo=1 AND z.Activo=1",P("@id",cambio.idBarrio.Value)))
                        return BadRequest("Seleccioná un barrio y una zona activos.");
                    db.Database.ExecuteSqlCommand(anterior==null
                        ? "INSERT dbo.UbicacionServicio(idServicioSocio,idBarrio,Direccion) VALUES(@id,@barrio,@direccion)"
                        : "UPDATE dbo.UbicacionServicio SET idBarrio=@barrio,Direccion=@direccion WHERE idServicioSocio=@id",
                        P("@id",id),P("@barrio",cambio.idBarrio.Value),P("@direccion",direccion));
                    var nuevo=db.Database.SqlQuery<Ubicacion>("SELECT * FROM dbo.UbicacionServicio WHERE idServicioSocio=@id",P("@id",id)).Single();
                    Auditar("UbicacionServicio",id,anterior==null?"Asignar":"Actualizar",motivo,anterior,nuevo);
                    tx.Commit(); return Ok(nuevo);
                }
                catch(SqlException ex) when(ex.Number==2601 || ex.Number==2627 || ex.Number==1205)
                { return Content(HttpStatusCode.Conflict,"Otra operación cambió la ubicación. Actualizá y reintentá."); }
            }
        }

        [HttpGet, Route("historial")]
        public IHttpActionResult Historial(int pagina=1,string entidad=null,int? idEntidad=null,int? usuario=null,DateTime? desde=null,DateTime? hasta=null)
        {
            if(pagina<1 || pagina>100000 || (desde.HasValue && hasta.HasValue && desde>hasta)) return BadRequest("Filtros inválidos.");
            var where=@" FROM dbo.HistorialTerritorial WHERE (@entidad IS NULL OR Entidad=@entidad)
                AND (@id IS NULL OR idEntidad=@id) AND (@usuario IS NULL OR idUsuarioSistema=@usuario)
                AND (@desde IS NULL OR FechaUtc>=@desde) AND (@hasta IS NULL OR FechaUtc<@hasta)";
            Func<object[]> parametros=()=>new object[]{P("@entidad",entidad),P("@id",idEntidad),P("@usuario",usuario),P("@desde",desde),P("@hasta",hasta)};
            var total=db.Database.SqlQuery<int>("SELECT COUNT(*)"+where,parametros()).Single();
            var items=db.Database.SqlQuery<Movimiento>("SELECT *"+where+" ORDER BY idHistorial DESC OFFSET @offset ROWS FETCH NEXT 25 ROWS ONLY",
                parametros().Concat(new object[]{P("@offset",(pagina-1)*25)}).ToArray()).ToList();
            foreach(var item in items) item.FechaUtc=DateTime.SpecifyKind(item.FechaUtc,DateTimeKind.Utc);
            return Ok(new {items,total,pagina});
        }
        protected override void Dispose(bool disposing) { if(disposing) db.Dispose(); base.Dispose(disposing); }
    }
}
