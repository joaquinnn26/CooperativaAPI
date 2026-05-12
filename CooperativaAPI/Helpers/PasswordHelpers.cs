using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Security.Cryptography;
using System.Text;
namespace CooperativaAPI.Helpers
{
    public class PasswordHelpers
    {

        public static string Hashear(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public static bool Verificar(string passwordIngresada, string hashGuardado)
        {
            var hashIngresado = Hashear(passwordIngresada);
            return hashIngresado == hashGuardado;
        }
    }
}