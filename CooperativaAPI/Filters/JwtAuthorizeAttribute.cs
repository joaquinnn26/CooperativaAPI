using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Data.Entity;

namespace CooperativaAPI.Filters
{
    public class JwtAuthorizeAttribute : AuthorizeAttribute
    {
        private readonly string claveSecreta;

        public JwtAuthorizeAttribute()
        {
            claveSecreta = ConfigurationManager.AppSettings["JwtSecret"];
        }

        public override void OnAuthorization(HttpActionContext actionContext)
        {
            try
            {
                var request = actionContext.Request;

                // Validar header Authorization
                var authHeader = request.Headers.Authorization;

                if (authHeader == null || authHeader.Scheme != "Bearer")
                {
                    actionContext.Response = request.CreateResponse(HttpStatusCode.Unauthorized);
                    return;
                }

                var token = authHeader.Parameter;

                // Validar token
                var key = Encoding.UTF8.GetBytes(claveSecreta);
                var tokenHandler = new JwtSecurityTokenHandler();

                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                }, out SecurityToken validatedToken);

                var jwt = validatedToken as JwtSecurityToken;
                if (jwt == null || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                {
                    actionContext.Response = request.CreateResponse(HttpStatusCode.Unauthorized);
                    return;
                }

                Thread.CurrentPrincipal = principal;
                actionContext.RequestContext.Principal = principal;

                var identity = principal.Identity as ClaimsIdentity;
                int idUsuario;
                if (!int.TryParse(identity?.FindFirst("idUsuarioSistema")?.Value, out idUsuario))
                {
                    actionContext.Response = request.CreateResponse(HttpStatusCode.Unauthorized);
                    return;
                }

                using (var db = new CooperativaEntities())
                {
                    var usuario = db.UsuarioSistema
                        .Include(u => u.Rol)
                        .Include(u => u.Socio2)
                        .Include(u => u.Empleado)
                        .FirstOrDefault(u => u.idUsuarioSistema == idUsuario);

                    var rolToken = identity.FindFirst(ClaimTypes.Role)?.Value;
                    var rolActual = usuario?.Rol?.NombreRol;
                    var altaPendiente = usuario != null && CooperativaAPI.Controllers.AltaSocioAcceso.Pendiente(db, usuario);

                    if (usuario == null ||
                        string.IsNullOrWhiteSpace(rolActual) ||
                        !string.Equals(rolToken, rolActual, StringComparison.OrdinalIgnoreCase) ||
                        (rolActual=="Socio" && (usuario.Socio2==null || usuario.Socio2.Activo!=true) && !altaPendiente) ||
                        (rolActual!="Socio" && usuario.Empleado!=null && usuario.Empleado.Activo!=true) || (rolActual=="Empleado" && usuario.Empleado==null))
                    {
                        actionContext.Response = request.CreateResponse(HttpStatusCode.Unauthorized);
                        return;
                    }
                    if (altaPendiente)
                    {
                        var controller = actionContext.ControllerContext.ControllerDescriptor.ControllerName;
                        var action = actionContext.ActionDescriptor.ActionName;
                        var permitido = controller == "SocioAlta" || controller == "AltaSocio" || (controller == "Auth" && (action == "ObtenerSesion" || action == "CambiarContrasena"));
                        if (!permitido)
                        {
                            actionContext.Response = request.CreateResponse(HttpStatusCode.Forbidden, new { Message = "El alta de socio está pendiente. Completá la documentación y esperá su aprobación." });
                            return;
                        }
                    }
                }

                using(var permisos=new CooperativaEntities()) {
                    // El vínculo del token también debe coincidir con la cuenta actual.
                    var actual=permisos.UsuarioSistema.Find(idUsuario);
                    int tokenEmployee,tokenSocio;
                    var e=identity.FindFirst("idEmpleado")?.Value; var s=identity.FindFirst("idSocio")?.Value;
                    if((!string.IsNullOrEmpty(e) && (!int.TryParse(e,out tokenEmployee) || actual.idEmpleado!=tokenEmployee || actual.Rol.NombreRol=="Socio")) ||
                       (!string.IsNullOrEmpty(s) && (!int.TryParse(s,out tokenSocio) || actual.idSocio!=tokenSocio))) {
                        actionContext.Response=request.CreateResponse(HttpStatusCode.Unauthorized); return;
                    }
                    if(!CooperativaAPI.Controllers.LaboralAccess.LegacyGate(permisos,principal,actionContext.ControllerContext.ControllerDescriptor.ControllerName,actionContext.ActionDescriptor.ActionName,request.Method.Method)) {
                        actionContext.Response=request.CreateResponse(HttpStatusCode.Forbidden,new {message="Esta operación requiere el flujo laboral y la autorización del área correspondiente."}); return;
                    }
                }
                if (!string.IsNullOrWhiteSpace(Roles))
                {
                    var rolesPermitidos = Roles
                        .Split(',')
                        .Select(role => role.Trim())
                        .Where(role => role.Length > 0)
                        .ToArray();

                    if (!rolesPermitidos.Any(principal.IsInRole))
                    {
                        actionContext.Response = request.CreateResponse(HttpStatusCode.Forbidden);
                        return;
                    }
                }
            }
            catch
            {
                actionContext.Response = actionContext.Request
                    .CreateResponse(HttpStatusCode.Unauthorized);
            }
        }
    }
}
