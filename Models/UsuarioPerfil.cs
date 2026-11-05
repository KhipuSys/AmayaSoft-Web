using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace MiniMarket.Web.Models;

public class UsuarioPerfil
{
    [Key, MaxLength(450)]
    public string UsuarioId { get; set; }
    [ForeignKey(nameof(UsuarioId))]
    public IdentityUser Usuario { get; set; }
    [Required, MaxLength(8)]
    public string Dni { get; set; }
    [Required, MaxLength(100)]
    public string Nombres { get; set; }
    [Required, MaxLength(100)]
    public string Apellidos { get; set; }
    [NotMapped]
    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
}
