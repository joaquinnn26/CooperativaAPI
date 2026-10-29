using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Cors;
namespace CooperativaAPI
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Permitimos peticiones desde Vercel y desde tu localhost para desarrollo
            var allowedOrigins = "https://cooperativa-1zwrdb37h-joaquin-86e7.vercel.app,http://localhost:5173,http://127.0.0.1:5173";

            var cors = new EnableCorsAttribute(allowedOrigins, "*", "GET,POST,PUT,DELETE,OPTIONS");
            config.EnableCors(cors);

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
