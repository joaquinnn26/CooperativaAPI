using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
namespace CooperativaAPI.Helpers
{
    public class PasswordHelpers
    {
        public static string Hashear(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("La contrasena es obligatoria.", nameof(password));

            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        public static bool Verificar(string passwordIngresada, string hashGuardado)
        {
            if (string.IsNullOrEmpty(passwordIngresada) || string.IsNullOrWhiteSpace(hashGuardado))
                return false;

            if (hashGuardado.StartsWith("$2", StringComparison.Ordinal))
                return BCrypt.Net.BCrypt.Verify(passwordIngresada, hashGuardado);

            return VerificarSha256Legado(passwordIngresada, hashGuardado);
        }

        private static bool VerificarSha256Legado(string password, string hashGuardado)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                var calculado = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return string.Equals(calculado, hashGuardado, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
