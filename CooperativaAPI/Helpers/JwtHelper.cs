using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.Configuration;

namespace CooperativaAPI.Helpers
{
    public static class JwtHelper
    {
        public static string GenerarToken(UsuarioSistema usuario)
        {
            var claveSecreta = ConfigurationManager.AppSettings["JwtSecret"];

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(claveSecreta));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, usuario.Usuario),
                new Claim(ClaimTypes.Role, usuario.Rol.NombreRol),
                new Claim("idUsuarioSistema", usuario.idUsuarioSistema.ToString()),
                new Claim("idSocio", usuario.Rol.NombreRol=="Socio" ? usuario.idSocio.ToString() : ""),
                new Claim("idEmpleado", usuario.Rol.NombreRol!="Socio" && usuario.idEmpleado.HasValue ? usuario.idEmpleado.Value.ToString() : "")
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddHours(2),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
