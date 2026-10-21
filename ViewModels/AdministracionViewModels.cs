using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Routify.Web.Models;
using Routify.Web.Services.Indicadores;

namespace Routify.Web.ViewModels;

public class UsuarioFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ingrese el nombre."), StringLength(100)]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingrese el apellido."), StringLength(100)]
    public string Apellido { get; set; } = "";

    [Required(ErrorMessage = "Ingrese el correo."), EmailAddress(ErrorMessage = "Correo no válido."), StringLength(150)]
    public string Correo { get; set; } = "";

    [StringLength(20)]
    public string? Telefono { get; set; }

    [Required(ErrorMessage = "Seleccione un rol.")]
    [Display(Name = "Rol")]
    public int RolId { get; set; }

    /// <summary>Contraseña (obligatoria al crear; opcional al editar para restablecerla).</summary>
    [DataType(DataType.Password)]
    public string? Clave { get; set; }

    // Datos operativos si el rol es Repartidor
    public int? ZonaId { get; set; }
    [Range(1, 200, ErrorMessage = "La capacidad debe estar entre 1 y 200.")]
    public int Capacidad { get; set; } = 15;
    [StringLength(50)] public string? Vehiculo { get; set; }
    [StringLength(15)] public string? Placa { get; set; }

    public List<SelectListItem> Roles { get; set; } = new();
    public List<SelectListItem> Zonas { get; set; } = new();
}

public class UsuarioListaVm
{
    public List<Usuario> Usuarios { get; set; } = new();
    public string? Texto { get; set; }
    public int? RolId { get; set; }
    public Dictionary<int, int> ConteoPorRol { get; set; } = new();
    public int Total { get; set; }
}

public class ClienteFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ingrese el nombre."), StringLength(150)]
    public string Nombre { get; set; } = "";

    [StringLength(20)] public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "Correo no válido."), StringLength(150)]
    public string? Correo { get; set; }

    [Required(ErrorMessage = "Ingrese la dirección."), StringLength(250)]
    public string Direccion { get; set; } = "";

    [StringLength(250)] public string? Referencia { get; set; }
    public string? Lat { get; set; }
    public string? Lng { get; set; }
    public bool NotificarCorreo { get; set; } = true;
    public bool NotificarWhatsapp { get; set; } = true;
    public bool Consentimiento { get; set; }
}

public class ZonaFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ingrese el nombre de la zona."), StringLength(100)]
    public string Nombre { get; set; } = "";

    [StringLength(250)] public string? Descripcion { get; set; }

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color no válido.")]
    public string Color { get; set; } = "#4F46E5";

    /// <summary>JSON con los vértices del polígono: [[lat,lng],[lat,lng],...]</summary>
    [Required(ErrorMessage = "Dibuje el polígono de la zona en el mapa.")]
    public string Vertices { get; set; } = "[]";

    public bool Activa { get; set; } = true;
}

public class RepartidorFormVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public int? ZonaId { get; set; }

    [Range(1, 200, ErrorMessage = "La capacidad debe estar entre 1 y 200.")]
    public int Capacidad { get; set; } = 15;

    [StringLength(50)] public string? Vehiculo { get; set; }
    [StringLength(15)] public string? Placa { get; set; }
    public bool Disponibilidad { get; set; } = true;
    public List<SelectListItem> Zonas { get; set; } = new();
}

public class RepartidorFilaVm
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Zona { get; set; }
    public string? Vehiculo { get; set; }
    public string? Placa { get; set; }
    public bool Disponible { get; set; }
    public bool Activo { get; set; }
    public int Capacidad { get; set; }
    public int Carga { get; set; }
    public DateTime? UltimaActualizacion { get; set; }
    public string? Telefono { get; set; }
}

public class DashboardVm
{
    public Kpis Kpis { get; set; } = null!;
    public List<ZonaReparto> Zonas { get; set; } = new();
    public List<PedidoFilaVm> Activos { get; set; } = new();
}

public class RutaFilaVm
{
    public int Id { get; set; }
    public string Repartidor { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Estado { get; set; } = "";
    public int Paradas { get; set; }
    public int Entregadas { get; set; }
    public decimal? DistanciaKm { get; set; }
    public int? TiempoMin { get; set; }
}

public class RutaDetalleVm
{
    public Ruta Ruta { get; set; } = null!;
    public string RepartidorNombre { get; set; } = "";
    public List<Parada> Paradas { get; set; } = new();
    public double AlmacenLat { get; set; }
    public double AlmacenLng { get; set; }
    public string AlmacenNombre { get; set; } = "";
}
