using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

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

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                }, out SecurityToken validatedToken);

                // Si llega acá → token válido
                base.OnAuthorization(actionContext);
            }
            catch
            {
                actionContext.Response = actionContext.Request
                    .CreateResponse(HttpStatusCode.Unauthorized);
            }
        }
    }
}
