namespace MiniMarket.Web.Models.ViewModels
{
    public class PrediccionViewModel
    {
        public int ProductoId { get; set; }
        public string CodigoBarras { get; set; }
        public string ProductoNombre { get; set; }
        public string CategoriaNombre { get; set; }
        public int StockActual { get; set; }
        public int StockMinimo { get; set; }
        public decimal CostoUnitario { get; set; }
        public decimal PromedioVentaDiaria { get; set; }
        public int DemandaEstimada7Dias { get; set; }
        public int CantidadSugeridaComprar { get; set; }
        public decimal InversionEstimada => CantidadSugeridaComprar * CostoUnitario;
        public string EstadoStock { get; set; } // "Crítico", "Bajo", "Óptimo"
    }
}   