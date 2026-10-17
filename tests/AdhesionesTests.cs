using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using CooperativaAPI;
using CooperativaAPI.Data;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Net.Http;
using System.Web.Http;
using CooperativaAPI.Controllers;

// Ejecutar con Run-AdhesionesTests.ps1. Los datos de prueba se revierten.
public class AdhesionesTests
{
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); count++; Console.WriteLine("OK: "+name); }
    static void Reject(Action action,string name) { try { action(); } catch(ArgumentException) { count++; Console.WriteLine("OK: "+name); return; } throw new Exception("Se acepto un caso invalido: "+name); }
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender,ResolveEventArgs e) { string p=Path.Combine(args[0],new AssemblyName(e.Name).Name+".dll"); return File.Exists(p)?Assembly.LoadFrom(p):null; };
        Run();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Run()
    {
        var cargos=JArray.Parse("[{\"Tipo\":\"SEPELIO\",\"Importe\":5000},{\"Tipo\":\"ORTOPEDIA\",\"Importe\":1000}]");
        var json="[{\"Tipo\":\"SEPELIO\",\"Descripcion\":\"Servicio de Sepelio\",\"Importe\":5000},{\"Tipo\":\"ORTOPEDIA\",\"Descripcion\":\"Servicio de Ortopedia\",\"Importe\":1000}]";
        Check(ConceptosBeneficios.Validar(json,15000,cargos).Count==2,"se conserva el total sin sumar otra vez las cuotas");
        Reject(()=>ConceptosBeneficios.Validar(json,4000,cargos),"subtotal mayor que total");
        Reject(()=>ConceptosBeneficios.Validar(json,15000,new JArray()),"no cobrar servicios no exportados");
        Reject(()=>ConceptosBeneficios.Validar(json.Replace("5000","5001"),15000,cargos),"cuota distinta al padron exportado");
        Reject(()=>ConceptosBeneficios.Validar("[null]",15000,cargos),"concepto mal formado");
        Reject(()=>ConceptosBeneficios.Validar("[]",15000,cargos),"no omitir cuotas del padron exportado");
        Reject(()=>ConceptosBeneficios.Validar("[{\"Tipo\":\"SEPELIO\",\"Descripcion\":\"Cuota\",\"Importe\":5000},{\"Tipo\":\"SEPELIO\",\"Descripcion\":\"Duplicada\",\"Importe\":5000}]",15000,cargos),"no repetir un cargo de Sepelio");
        using(var db=new CooperativaEntities())
        using(var tx=db.Database.BeginTransaction(IsolationLevel.Serializable))
        {
            try
            {
                if(db.Database.Connection.Database.IndexOf("test",StringComparison.OrdinalIgnoreCase)<0) throw new Exception("Estas pruebas requieren una base de testing.");
                foreach(string tipo in new[]{"Sepelio","Ortopedia"}) if(!db.Servicio.Any(s=>s.Nombre==tipo)) {
                    var tarifa=db.TipoTarifa.First();
                    db.Servicio.Add(new Servicio {Nombre=tipo,Descripcion="Solo prueba reversible",TarifaBase=1000,idTipoTarifa=tarifa.idTipoTarifa}); db.SaveChanges();
                }
                var existing=db.ServicioSocio.Where(s=>s.Activo==true && s.Socio.Activo==true).ToList().First(s=>AdhesionesBeneficios.Energia(s.Servicio.Nombre));
                int socio=existing.idSocio,conexion=existing.idServicioSocio;
                int user=db.UsuarioSistema.Where(u=>u.idSocio==socio).Select(u=>u.idUsuarioSistema).First();
                string dni=db.Socio.Where(s=>s.idSocio==socio).Select(s=>s.Persona.Documento).Single();
                var before=db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficio").Single();
                var orto=JObject.Parse("{\"Ortopedia\":true,\"Consentimiento\":true}");
                orto["Direccion"]="Prueba ortopedia "+Guid.NewGuid().ToString("N");
                int id=AdhesionesBeneficios.Crear(db,socio,user,null,conexion,orto).Single();
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@p0",id).Single()==0,"ortopedia sin DNI ni familiares");
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficio WHERE idAdhesion=@p0 AND Estado='EnRevision' AND ImporteMensual IS NULL",id).Single()==1,"no activar ni cobrar al solicitar");
                Reject(()=>AdhesionesBeneficios.Crear(db,socio,user,null,conexion,orto),"bloquear adhesiones duplicadas");
                var noConsent=(JObject)orto.DeepClone();noConsent["Consentimiento"]=false;
                Reject(()=>AdhesionesBeneficios.Crear(db,socio,user,null,conexion,noConsent),"rechazar contratacion sin consentimiento");
                Reject(()=>AdhesionesBeneficios.Crear(db,socio,user,null,-1,orto),"rechazar conexion inexistente");
                var miembro=new JObject { {"Nombre","Titular de prueba"},{"Documento",new string(dni.Where(char.IsDigit).ToArray())},{"Vinculo","Titular"},{"MismoDomicilio",true},{"Archivo",new JObject{{"Nombre","dni.pdf"},{"Tipo","application/pdf"},{"Base64",Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 test"))}}} };
                var sepelio=new JObject{{"Sepelio",true},{"Consentimiento",true},{"Direccion","Prueba sepelio "+Guid.NewGuid().ToString("N")},{"Familiares",new JArray(miembro)}};
                int sep=AdhesionesBeneficios.Crear(db,socio,user,null,conexion,sepelio).Single();
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@p0",sep).Single()==1,"guardar titular y copia de DNI en sepelio");
                var familiar=(JObject)miembro.DeepClone();familiar["Documento"]="990000999";familiar["Vinculo"]="Hija";familiar["MismoDomicilio"]=false;familiar["ConservaAdhesion"]=true;
                AdhesionesBeneficios.GuardarFamilia(db,sep,socio,new JArray(familiar));
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@p0 AND MismoDomicilio=0 AND ConservaAdhesion=1",sep).Single()==1,"continuidad de hija fuera de la localidad con DNI");
                var soloFamiliar=(JObject)miembro.DeepClone();soloFamiliar["Documento"]="990000998";soloFamiliar["Vinculo"]="Cónyuge";
                AdhesionesBeneficios.GuardarFamilia(db,sep,socio,new JArray(soloFamiliar));
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficiario WHERE idAdhesion=@p0 AND Documento='990000998'",sep).Single()==1,"grupo elegido sin obligar a incluir al titular");
                var wrong=(JObject)miembro.DeepClone();wrong["MismoDomicilio"]=false;
                Reject(()=>AdhesionesBeneficios.GuardarFamilia(db,sep,socio,new JArray(wrong)),"rechazar personas de otro domicilio");
                Check(db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.AdhesionBeneficio").Single()==before+2,"solo las dos adhesiones esperadas en la transaccion");
            }
            finally { tx.Rollback(); }
        }
        using(var db=new CooperativaEntities())
        {
            var account=db.UsuarioSistema.First(u=>u.idSocio!=null);
            var socioClaims=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"Socio"),new Claim("idSocio",account.idSocio.Value.ToString()),new Claim("idUsuarioSistema",account.idUsuarioSistema.ToString())},"Prueba"));
            using(var controller=new AdhesionesController())
            {
                controller.Configuration=new HttpConfiguration();controller.Request=new HttpRequestMessage();controller.User=socioClaims;
                var response=controller.Catalogo().ExecuteAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Check(response.StatusCode==System.Net.HttpStatusCode.OK,"catalogo autenticado del socio");
                var list=controller.Mias().ExecuteAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                var items=JArray.Parse(list.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Check(items.All(x=>(int)x["idSocio"]==account.idSocio.Value),"consulta limitada al titular autenticado");
            }
            using(var controller=new AdhesionesController())
            {
                controller.Configuration=new HttpConfiguration();controller.Request=new HttpRequestMessage();
                controller.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"Socio"),new Claim("idSocio",account.idSocio.Value.ToString()),new Claim("idUsuarioSistema",account.idUsuarioSistema.ToString())},"Prueba"));
                var response=controller.Todas().ExecuteAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Check(response.StatusCode==System.Net.HttpStatusCode.Forbidden,"un socio no puede consultar la bandeja de gestion");
            }
        }
        Console.WriteLine("Pruebas aprobadas: "+count+". Datos revertidos.");
    }
}
