using System.ComponentModel.DataAnnotations;

namespace MiniMarket.Web.Models;

public class UsuarioDatosModel
{
    [Display(Name = "Correo de acceso")]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", ErrorMessage = "Escribe solo el usuario, sin @ ni espacios. Usa letras, números, punto, guion o guion bajo.")]
    public string UsuarioAcceso { get; set; }
    [Required(ErrorMessage = "Ingrese el correo de acceso."), EmailAddress, Display(Name = "Correo electrónico")]
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}@amayasoft\.com$", ErrorMessage = "Use un correo con el dominio @amayasoft.com, por ejemplo nombre@amayasoft.com.")]
    public string Email { get; set; }
    [Required(ErrorMessage = "Ingrese el DNI."), RegularExpression(@"^[0-9]{8}$", ErrorMessage = "El DNI debe contener 8 dígitos."), Display(Name = "DNI")]
    public string Dni { get; set; }
    [Required(ErrorMessage = "Ingrese los nombres."), StringLength(100), Display(Name = "Nombres")]
    public string Nombres { get; set; }
    [Required(ErrorMessage = "Ingrese al menos un apellido."), StringLength(100), Display(Name = "Apellidos")]
    public string Apellidos { get; set; }
}
public class UsuarioRegistroModel : UsuarioDatosModel
{
    [Required, MinLength(6), DataType(DataType.Password), Display(Name = "Contraseña temporal")]
    public string Password { get; set; }
    [Required(ErrorMessage = "Seleccione el rol.")]
    public string Rol { get; set; }
}
public class UsuarioEditModel : UsuarioDatosModel
{
    [Required]
    public string Id { get; set; }
    public string RolActual { get; set; }
    [Required(ErrorMessage = "Seleccione el rol.")]
    public string NuevoRol { get; set; }
    [MinLength(6), DataType(DataType.Password)]
    public string NuevaPassword { get; set; }
}
public class UsuarioViewModel
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string Rol { get; set; }
    public string Dni { get; set; }
    public string NombreCompleto { get; set; }
}
