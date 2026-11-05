using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models
{
    public class DetalleCompra
    {
        [Key]
        public int Id { get; set; }

        public int CompraId { get; set; }
        public virtual Compra Compra { get; set; }

        public int ProductoId { get; set; }
        public virtual Producto Producto { get; set; }

        public int Cantidad { get; set; }

        // --- NORMALIZAMOS EL NOMBRE A 'PrecioUnitario' ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitario { get; set; } 

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }
    }
}