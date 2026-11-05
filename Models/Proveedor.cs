using System.ComponentModel.DataAnnotations;

namespace MiniMarket.Web.Models
{
    public class Proveedor
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "RUC")]
        public string Ruc { get; set; }

        [Required]
        [Display(Name = "Razón Social")]
        public string RazonSocial { get; set; }

        public string Contacto { get; set; }  // Se quitó el ?
        public string Telefono { get; set; }  // Se quitó el ?
        public string Correo { get; set; }    // Se quitó el ?
        public string Direccion { get; set; } // Se quitó el ?

        public bool Estado { get; set; } = true;
    }
}