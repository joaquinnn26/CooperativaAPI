using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;


namespace CooperativaAPI.DTOs
{
    public class FacturaCreateRequest
    {
        public int idSocio { get; set; }
        public int idEmpleado { get; set; }
        public int idServicio { get; set; }
        public decimal MontoTotal { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime FechaCaducidad { get; set; }
        public string LinkPago { get; set; }
        public string Observaciones { get; set; }
    }

    public class FacturaUpdateRequest
    {
        public int idServicio { get; set; }
        public decimal MontoTotal { get; set; }
        public DateTime FechaCaducidad { get; set; }
        public string LinkPago { get; set; }
        public string Observaciones { get; set; }
    }

    public class FacturaPagoRequest
    {
        public bool Pagada { get; set; }
        public DateTime? FechaPago { get; set; }
    }
}