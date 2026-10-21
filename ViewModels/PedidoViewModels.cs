using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Routify.Web.Models;

namespace Routify.Web.ViewModels;

public class DetalleVm
{
    [StringLength(200)]
    public string? Producto { get; set; }

    [Range(1, 9999, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; } = 1;

    [StringLength(250)]
    public string? Observacion { get; set; }
}

public class PedidoFormVm
{
    public int? Id { get; set; }
    public string? Codigo { get; set; }

    /// <summary>existente | nuevo</summary>
    public string ModoCliente { get; set; } = "existente";

    public int? ClienteId { get; set; }

    // Datos del cliente nuevo
    [StringLength(150)] public string? NuevoNombre { get; set; }
    [StringLength(20)] public string? NuevoTelefono { get; set; }
    [StringLength(150), EmailAddress(ErrorMessage = "Correo no válido.")] public string? NuevoCorreo { get; set; }
    public bool NuevoNotificarCorreo { get; set; } = true;
    public bool NuevoNotificarWhatsapp { get; set; } = true;
    public bool NuevoConsentimiento { get; set; }

    [Required(ErrorMessage = "Ingrese la dirección de entrega."), StringLength(250)]
    [Display(Name = "Dirección de entrega")]
    public string DireccionEntrega { get; set; } = "";

    [StringLength(250)]
    public string? Referencia { get; set; }

    /// <summary>Coordenadas como texto (cultura invariante) para evitar problemas de separador decimal.</summary>
    public string? Lat { get; set; }
    public string? Lng { get; set; }

    /// <summary>Formato HH:mm (hora de Lima).</summary>
    public string? VentanaInicio { get; set; }
    public string? VentanaFin { get; set; }

    [StringLength(500)]
    public string? Observaciones { get; set; }

    public List<DetalleVm> Detalles { get; set; } = new() { new DetalleVm() };

    // Solo para la vista
    public List<SelectListItem> Clientes { get; set; } = new();
}

public class PedidoFiltroVm
{
    public string? Texto { get; set; }
    public int? EstadoId { get; set; }
    public int? ZonaId { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public int Pagina { get; set; } = 1;
}

public class PedidoFilaVm
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string? Zona { get; set; }
    public int EstadoId { get; set; }
    public string Estado { get; set; } = "";
    public string? Repartidor { get; set; }
    public DateTime FechaRegistro { get; set; }
}

public class PedidoListaVm
{
    public PedidoFiltroVm Filtro { get; set; } = new();
    public List<PedidoFilaVm> Items { get; set; } = new();
    public Dictionary<int, int> Conteos { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TotalPaginas { get; set; }
    public List<SelectListItem> Zonas { get; set; } = new();
}

public class PedidoDetalleVm
{
    public Pedido Pedido { get; set; } = null!;
    public List<HistorialEstado> Historial { get; set; } = new();
    public List<Notificacion> Notificaciones { get; set; } = new();
    public List<PruebaEntrega> Pruebas { get; set; } = new();
    public Parada? Parada { get; set; }
    public string? UrlSeguimiento { get; set; }
    public string? EnlaceWhatsApp { get; set; }
}

public class CandidatoAsignacionVm
{
    public int RepartidorId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Zona { get; set; }
    public string? Vehiculo { get; set; }
    public int ParadasAsignadas { get; set; }
    public int Capacidad { get; set; }
    public double? DistanciaRecogidaKm { get; set; }
    public double? DistanciaEntregaKm { get; set; }
    public double? RutaKm { get; set; }
    public int? RutaMin { get; set; }
    public DateTime? EtaUtc { get; set; }
    public bool Disponible { get; set; }
    public bool MismaZona { get; set; }
}

public class AsignacionVm
{
    public Pedido Pedido { get; set; } = null!;
    public string AlmacenNombre { get; set; } = "";
    public List<CandidatoAsignacionVm> Candidatos { get; set; } = new();
}
