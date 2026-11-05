using System.ComponentModel.DataAnnotations;

namespace MiniMarket.Web.Models
{
    public class Categoria
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [Display(Name = "Nombre de Categoría")]
        public string Nombre { get; set; }

        [Display(Name = "Descripción")]
        public string Descripcion { get; set; } // Se quitó el ?

        [Display(Name = "Activo")]
        public bool Estado { get; set; } = true;
    }
}