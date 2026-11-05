using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models
{
    public class Producto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        public string Nombre { get; set; }

        public string Descripcion { get; set; }

        public string CodigoBarras { get; set; }

        public string ImagenUrl { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Required(ErrorMessage = "El precio es obligatorio")]
        public decimal Precio { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Costo { get; set; }

        [Required]
        public int Stock { get; set; }

        // --- NUEVO CAMPO: Límite para la alerta ---
        [Display(Name = "Stock Mínimo")]
        public int StockMinimo { get; set; } = 5; 

        public bool Estado { get; set; } = true;

        public int CategoriaId { get; set; }
        public virtual Categoria Categoria { get; set; }
    }
}