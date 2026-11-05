using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models
{
    public class Compra
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProveedorId { get; set; }
        public virtual Proveedor Proveedor { get; set; }

        public DateTime Fecha { get; set; } = HoraPeru.Ahora;

        // --- CAMPO NUEVO: NÚMERO DE FACTURA ---
        [MaxLength(50)]
        public string NumeroDocumento { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        // --- CAMPO NUEVO: USUARIO QUE REGISTRA LA COMPRA ---
        public string UsuarioId { get; set; }
        [ForeignKey("UsuarioId")]
        public virtual IdentityUser Usuario { get; set; }
        // ---------------------------------------------------

        public virtual List<DetalleCompra> Detalles { get; set; }
    }
}
