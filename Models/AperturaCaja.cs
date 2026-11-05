using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models
{
    public class AperturaCaja
    {
        public int Id { get; set; }

        public string UsuarioId { get; set; } 
        
        public DateTime FechaApertura { get; set; } = HoraPeru.Ahora;
        public DateTime? FechaCierre { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Required]
        [Display(Name = "Monto Inicial (Sencillo)")]
        public decimal MontoInicial { get; set; } 

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalVentas { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? VentasEfectivo { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal AjustesAnulaciones { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalGastos { get; set; }

        // ↓↓↓ VERIFICA ESTA LÍNEA (El signo de interrogación es clave) ↓↓↓
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Dinero en Caja (Real)")]
        public decimal? MontoCierre { get; set; } 

        public bool Estado { get; set; } = true;
    }
}
