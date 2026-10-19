using System;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace CooperativaAPI.Helpers
{
    public sealed class RecoveryMailService
    {
        private readonly RecoverySmtpSettings settings;
        private readonly Action<MailMessage> send;

        public RecoveryMailService(RecoverySmtpSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            this.settings = settings;
            send = SendSmtp;
        }

        internal RecoveryMailService(RecoverySmtpSettings settings, Action<MailMessage> send) : this(settings)
        {
            if (send == null) throw new ArgumentNullException(nameof(send));
            this.send = send;
        }

        public void SendRecovery(string email, string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("El destinatario y el token son obligatorios.");
            var link = settings.FrontendUrl + "/recuperar-contrasena?email=" +
                Uri.EscapeDataString(email) + "&token=" + Uri.EscapeDataString(token);
            using (var message = new MailMessage())
            {
                message.From = new MailAddress(settings.From, "C.O.S.P.R.A.L.");
                message.To.Add(email);
                message.Subject = "Recuperacion de contrasena";
                message.Body = "Se solicito restablecer tu contrasena. El enlace vence en 30 minutos:\r\n\r\n" +
                    link + "\r\n\r\nSi no realizaste esta solicitud, ignora este mensaje.";
                message.SubjectEncoding = Encoding.UTF8;
                message.BodyEncoding = Encoding.UTF8;
                message.IsBodyHtml = false;
                send(message);
            }
        }

        private void SendSmtp(MailMessage message)
        {
            using (var smtp = new SmtpClient(settings.Host, settings.Port))
            {
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);
                smtp.EnableSsl = settings.EnableTls;
                smtp.Timeout = 10000;
                smtp.Send(message);
            }
        }
    }
}