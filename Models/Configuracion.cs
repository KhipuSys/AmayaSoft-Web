using System.ComponentModel.DataAnnotations;

namespace MiniMarket.Web.Models
{
    public class Configuracion
    {
        public int Id { get; set; }

        [Required]
        public string NombreEmpresa { get; set; } = "AmayaSoft";

        [Required]
        public string Ruc { get; set; } = "00000000000";

        public string Direccion { get; set; } = "Dirección por defecto";

        public string Telefono { get; set; }

        public string EmailContacto { get; set; }

        public decimal IgvPorcentaje { get; set; } = 18.00m;

        // CAMBIO: Renombrado a MonedaSimbolo para corregir el error en Vistas
        public string MonedaSimbolo { get; set; } = "S/.";
        
        public string LogoUrl { get; set; }
    }
}
