using System;
using System.Net.Mail;

namespace CooperativaAPI.Helpers
{
    public sealed class RecoverySmtpSettings
    {
        public string Host { get; private set; }
        public int Port { get; private set; }
        public string From { get; private set; }
        public string Username { get; private set; }
        internal string Password { get; private set; }
        public bool EnableTls { get; private set; }
        public string FrontendUrl { get; private set; }

        public static bool TryLoad(out RecoverySmtpSettings settings)
        {
            return TryLoad(Environment.GetEnvironmentVariable, out settings);
        }

        internal static bool TryLoad(Func<string, string> read, out RecoverySmtpSettings settings)
        {
            settings = null;
            var host = (read("COSPRAL_RECOVERY_SMTP_HOST") ?? "").Trim();
            var from = (read("COSPRAL_RECOVERY_SMTP_FROM") ?? "").Trim();
            var username = (read("COSPRAL_RECOVERY_SMTP_USERNAME") ?? "").Trim();
            var password = read("COSPRAL_RECOVERY_SMTP_PASSWORD");
            int port;
            bool tls;
            Uri frontend;
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown ||
                !int.TryParse(read("COSPRAL_RECOVERY_SMTP_PORT"), out port) || port < 1 || port > 65535 ||
                !bool.TryParse(read("COSPRAL_RECOVERY_SMTP_ENABLE_TLS"), out tls) || !tls ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ||
                !Uri.TryCreate(read("COSPRAL_RECOVERY_FRONTEND_URL"), UriKind.Absolute, out frontend) ||
                !(frontend.Scheme == Uri.UriSchemeHttps || (frontend.Scheme == Uri.UriSchemeHttp && frontend.IsLoopback)) ||
                !string.IsNullOrEmpty(frontend.UserInfo) || !string.IsNullOrEmpty(frontend.Query) || !string.IsNullOrEmpty(frontend.Fragment))
                return false;

            try
            {
                var address = new MailAddress(from);
                if (!string.Equals(address.Address, from, StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }

            settings = new RecoverySmtpSettings
            {
                Host = host, Port = port, From = from, Username = username,
                Password = password, EnableTls = tls, FrontendUrl = frontend.AbsoluteUri.TrimEnd('/')
            };
            return true;
        }
    }
}