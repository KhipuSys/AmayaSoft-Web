using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace MiniMarket.Web.Models
{
    public class Gasto
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Concepto de Gasto")]
        public string Concepto { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Monto (S/.)")]
        public decimal Monto { get; set; }

        public DateTime Fecha { get; set; } = HoraPeru.Ahora;

        // Relación con el usuario que registró este gasto
        public string UsuarioId { get; set; }
        [ForeignKey("UsuarioId")]
        public IdentityUser Usuario { get; set; }
        
        // Relación opcional con la caja (si fue dinero extraído de un turno abierto)
        public int? AperturaCajaId { get; set; }
        [ForeignKey("AperturaCajaId")]
        public AperturaCaja AperturaCaja { get; set; }
    }
}
