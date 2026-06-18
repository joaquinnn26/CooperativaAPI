using System;

namespace CooperativaAPI
{
    public class Anuncio
    {
        public int idAnuncio { get; set; }
        public string Titulo { get; set; }
        public string Contenido { get; set; }
        public int idPrioridadAnuncio { get; set; }
        public DateTime FechaPublicacion { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public bool Activo { get; set; }
        public bool Fijo { get; set; }
        public int? idEmpleado { get; set; }

        public virtual PrioridadAnuncio PrioridadAnuncio { get; set; }
    }
}
