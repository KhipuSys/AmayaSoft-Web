using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models // <--- VERIFICA ESTO
{
    public class DetalleVenta
    {
        [Key]
        public int Id { get; set; }
        public int VentaId { get; set; }
        [ForeignKey("VentaId")]
        public virtual Venta Venta { get; set; }
        public int ProductoId { get; set; }
        [ForeignKey("ProductoId")]
        public virtual Producto Producto { get; set; }
        public int Cantidad { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitario { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? CostoUnitarioHistorico { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }
    }
}
