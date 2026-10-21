using System.ComponentModel.DataAnnotations;

namespace Routify.Web.ViewModels;

public class LoginVm
{
    [Required(ErrorMessage = "Ingrese su correo electrónico."), EmailAddress(ErrorMessage = "Correo no válido.")]
    [Display(Name = "Correo electrónico")]
    public string Correo { get; set; } = "";

    [Required(ErrorMessage = "Ingrese su contraseña.")]
    [DataType(DataType.Password), Display(Name = "Contraseña")]
    public string Clave { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class RecuperarVm
{
    [Required(ErrorMessage = "Ingrese su correo electrónico."), EmailAddress(ErrorMessage = "Correo no válido.")]
    [Display(Name = "Correo electrónico")]
    public string Correo { get; set; } = "";
}

public class RestablecerVm
{
    [Required, EmailAddress]
    public string Correo { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Ingrese la nueva contraseña.")]
    [DataType(DataType.Password), Display(Name = "Nueva contraseña")]
    public string NuevaClave { get; set; } = "";

    [Required(ErrorMessage = "Confirme la contraseña.")]
    [DataType(DataType.Password), Display(Name = "Confirmar contraseña")]
    [Compare(nameof(NuevaClave), ErrorMessage = "Las contraseñas no coinciden.")]
    public string Confirmar { get; set; } = "";
}

public class CambiarClaveVm
{
    [Required(ErrorMessage = "Ingrese su contraseña actual.")]
    [DataType(DataType.Password), Display(Name = "Contraseña actual")]
    public string ClaveActual { get; set; } = "";

    [Required(ErrorMessage = "Ingrese la nueva contraseña.")]
    [DataType(DataType.Password), Display(Name = "Nueva contraseña")]
    public string NuevaClave { get; set; } = "";

    [Required(ErrorMessage = "Confirme la contraseña.")]
    [DataType(DataType.Password), Display(Name = "Confirmar contraseña")]
    [Compare(nameof(NuevaClave), ErrorMessage = "Las contraseñas no coinciden.")]
    public string Confirmar { get; set; } = "";
}
