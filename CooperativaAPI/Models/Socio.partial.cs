using System.Linq;

namespace CooperativaAPI
{
    public partial class Socio
    {
        public UsuarioSistema UsuarioSistemaUnico
        {
            get { return UsuarioSistema2 != null ? UsuarioSistema2.FirstOrDefault() : null; }
        }
    }
}
