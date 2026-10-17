using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Data
{
    // Operaciones compartidas por el alta conjunta y la adhesión de un socio existente.
    // El llamador debe abrir una transacción Serializable: no hay guardado parcial.
    public static class AdhesionesBeneficios
    {
        public static SqlParameter P(string key, object value) { return new SqlParameter(key, value ?? DBNull.Value); }
        public static List<JObject> Rows(CooperativaEntities db, string sql, params SqlParameter[] args)
        {
            var connection = (SqlConnection)db.Database.Connection;
            if (connection.State != ConnectionState.Open) connection.Open();
            using (var command = connection.CreateCommand())
            {
                if (db.Database.CurrentTransaction != null) command.Transaction = (SqlTransaction)db.Database.CurrentTransaction.UnderlyingTransaction;
                command.CommandText = sql; command.Parameters.AddRange(args);
                var rows = new List<JObject>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var row = new JObject();
                    for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? JValue.CreateNull() : JToken.FromObject(reader.GetValue(i));
                    rows.Add(row);
                }
                return rows;
            }
        }
        public static string Text(JObject data, string field, int max, bool required = true)
        {
            var value = ((string)data?[field] ?? "").Trim();
            if (value.Length > max || (required && value.Length == 0)) throw new ArgumentException("Revisá " + field + ".");
            return value;
        }
        public static bool Energia(string nombre)
        {
            var value = (nombre ?? "").ToLowerInvariant();
            return value == "luz" || value.Contains("energ") || value.Contains("electric") || value.Contains("eléctric");
        }
        public static void History(CooperativaEntities db, int id, string estado, int user, string motivo)
        {
            db.Database.ExecuteSqlCommand("INSERT dbo.AdhesionHistorial(idAdhesion,Estado,idUsuarioSistema,Motivo) VALUES(@id,@e,@u,@m)", P("@id", id), P("@e", estado), P("@u", user), P("@m", motivo));
        }
        public static List<int> Crear(CooperativaEntities db, int socio, int user, int? solicitud, int? conexion, JObject data)
        {
            if (data == null) throw new ArgumentException("Completá la adhesión.");
            var tipos = new List<string>();
            if ((bool?)data["Sepelio"] == true) tipos.Add("SEPELIO");
            if ((bool?)data["Ortopedia"] == true) tipos.Add("ORTOPEDIA");
            if (tipos.Count == 0) throw new ArgumentException("Elegí al menos un servicio adicional.");
            if ((bool?)data["Consentimiento"] != true) throw new ArgumentException("Confirmá la contratación de los servicios y sus cargos mensuales.");
            var direccion = Text(data, "Direccion", 200);
            if (!db.Socio.Any(s => s.idSocio == socio && s.Activo == true)) throw new ArgumentException("El socio no está activo.");
            if (solicitud.HasValue)
            {
                var request = db.SolicitudServicio.Find(solicitud.Value);
                if (request == null || request.idSocio != socio || !Energia(request.Servicio.Nombre) || request.FechaResolucion.HasValue)
                    throw new ArgumentException("La solicitud eléctrica no está disponible.");
            }
            else
            {
                var contract = db.ServicioSocio.Find(conexion ?? 0);
                if (contract == null || contract.idSocio != socio || contract.Activo != true || !Energia(contract.Servicio.Nombre))
                    throw new ArgumentException("Seleccioná tu conexión eléctrica activa.");
            }
            var miembros = data["Familiares"] as JArray ?? new JArray();
            if (tipos.Contains("SEPELIO") && (miembros.Count == 0 || miembros.Count > 15)) throw new ArgumentException("Elegí entre una y quince personas para la cobertura de Sepelio.");
            var ids = new List<int>();
            foreach (var tipo in tipos)
            {
                var servicio = db.Servicio.ToList().SingleOrDefault(s => s.Nombre.Trim().Equals(tipo, StringComparison.OrdinalIgnoreCase));
                if (servicio == null) throw new ArgumentException("Administración debe configurar el servicio " + tipo + " y su tarifa antes del alta.");
                if (db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficio WHERE idSocio=@s AND Tipo=@t AND (Direccion=@d OR idConexionEnergia=@c OR idSolicitudEnergia=@sol) AND Vigente=1", P("@s", socio), P("@t", tipo), P("@d", direccion),P("@c",conexion),P("@sol",solicitud)).Single() != 0)
                    throw new ArgumentException("Ya existe una adhesión vigente de " + tipo + " en ese domicilio.");
                int id = db.Database.SqlQuery<int>(@"INSERT dbo.AdhesionBeneficio(idSocio,idServicio,Tipo,idSolicitudEnergia,idConexionEnergia,Direccion,Consentimiento)
                    VALUES(@s,@serv,@t,@sol,@c,@d,1); SELECT CAST(SCOPE_IDENTITY() AS int);", P("@s", socio), P("@serv", servicio.idServicio), P("@t", tipo), P("@sol", solicitud), P("@c", conexion), P("@d", direccion)).Single();
                if (tipo == "SEPELIO") GuardarFamilia(db, id, socio, miembros);
                History(db, id, "EnRevision", user, "Adhesión voluntaria solicitada. No genera cargos hasta su activación.");
                ids.Add(id);
            }
            return ids;
        }
        public static void GuardarFamilia(CooperativaEntities db, int id, int socio, JArray miembros)
        {
            if (miembros == null || miembros.Count < 1 || miembros.Count > 15) throw new ArgumentException("Elegí entre una y quince personas para la cobertura.");
            var documentos = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int totalBytes = 0;
            foreach (var token in miembros)
            {
                var m = token as JObject;
                string nombre = Text(m, "Nombre", 150), documento = Text(m, "Documento", 20), vinculo = Text(m, "Vinculo", 60);
                if (documento.Any(c => !char.IsDigit(c)) || !documentos.Add(documento)) throw new ArgumentException("Los DNI deben ser numéricos y no repetirse.");
                bool mismo=(bool?)m["MismoDomicilio"]==true, conserva=(bool?)m["ConservaAdhesion"]==true;
                if (!mismo && !(conserva && vinculo.StartsWith("hij",StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Fuera del domicilio solo se admite la continuidad de adhesión de hijos, que Administración debe verificar.");
                var f = m["Archivo"] as JObject;
                var name = Path.GetFileName(Text(f, "Nombre", 150)); var mime = Text(f, "Tipo", 50).ToLowerInvariant();
                byte[] bytes; try { bytes = Convert.FromBase64String((string)f?["Base64"] ?? ""); } catch (FormatException) { throw new ArgumentException("El DNI adjunto no es válido."); }
                totalBytes += bytes.Length;
                bool signature = mime == "application/pdf" && bytes.Take(5).SequenceEqual(Encoding.ASCII.GetBytes("%PDF-"))
                    || mime == "image/png" && bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                    || mime == "image/jpeg" && bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
                if (!signature || bytes.Length == 0 || bytes.Length > 2*1024*1024 || totalBytes > 6*1024*1024) throw new ArgumentException("Adjuntá DNI en PDF, PNG o JPEG, hasta 2 MB cada uno y 6 MB en total.");
                db.Database.ExecuteSqlCommand(@"INSERT dbo.AdhesionBeneficiario(idAdhesion,Nombre,Documento,Vinculo,MismoDomicilio,ConservaAdhesion,NombreArchivo,Mime,Contenido)
                    VALUES(@id,@n,@d,@v,@same,@keep,@f,@m,@b)", P("@id", id), P("@n", nombre), P("@d", documento), P("@v", vinculo),P("@same",mismo),P("@keep",conserva), P("@f", name), P("@m", mime), P("@b", bytes));
            }
        }
    }
}
