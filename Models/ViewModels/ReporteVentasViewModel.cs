using System;

namespace MiniMarket.Web.Models.ViewModels
{
    public class ReporteVentasViewModel
    {
        public int VentaId { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public string Cajero { get; set; }
        public string MetodoPago { get; set; }
        public int CantidadArticulos { get; set; }
        public decimal TotalVenta { get; set; }
        public decimal? CostoTotal { get; set; }
        public decimal? GananciaNeta => TotalVenta - CostoTotal;
        public decimal? MargenPorcentaje => !GananciaNeta.HasValue ? null : TotalVenta > 0 ? Math.Round((GananciaNeta.Value / TotalVenta) * 100, 2) : 0;
    }
}
