using System.Collections.Generic;

namespace MiniMarket.Web.Models
{
    public class DashboardViewModel
    {
        // Tarjetas Superiores
        public decimal VentasHoy { get; set; }
        public int TotalProductos { get; set; }
        public int TotalCategorias { get; set; }
        public decimal CapitalEstimado { get; set; }

        // Gráfico de Líneas (Tendencia)
        public List<string> FechasGrafico { get; set; }
        public List<decimal> VentasGrafico { get; set; }

        // Gráfico Circular (Top Productos)
        public List<string> TopProductosNombres { get; set; }
        public List<int> TopProductosCantidades { get; set; }

        // Alertas: Productos con stock bajo
        public List<Producto> ProductosStockBajo { get; set; } = new List<Producto>();

        // Analíticas Avanzadas
        public Dictionary<string, decimal> VentasPorCajero { get; set; } = new Dictionary<string, decimal>();
        public decimal VentasMesActual { get; set; }
        public decimal VentasMesAnterior { get; set; }
        public decimal PorcentajeCrecimiento { get; set; }
    }
}