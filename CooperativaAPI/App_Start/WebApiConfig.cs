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

            var frontendUrl = ConfigurationManager.AppSettings["FrontendUrl"] ?? "http://localhost:5173";
            var cors = new EnableCorsAttribute(
                frontendUrl.TrimEnd('/') + ",http://127.0.0.1:5173",
                "*",
                "GET,POST,PUT,DELETE,OPTIONS");

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
