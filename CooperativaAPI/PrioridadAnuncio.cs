using System.Collections.Generic;

namespace CooperativaAPI
{
    public class PrioridadAnuncio
    {
        public PrioridadAnuncio()
        {
            Anuncios = new HashSet<Anuncio>();
        }

        public int idPrioridadAnuncio { get; set; }
        public string Nombre { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }

        public virtual ICollection<Anuncio> Anuncios { get; set; }
    }
}
