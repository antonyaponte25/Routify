using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("pedidos")]
public class Pedido
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("codigo"), Required, MaxLength(30)]
    public string Codigo { get; set; } = "";

    [Column("cliente_id")]
    public int ClienteId { get; set; }

    [Column("estado_id")]
    public int EstadoId { get; set; }

    [Column("zona_id")]
    public int? ZonaId { get; set; }

    [Column("repartidor_id")]
    public int? RepartidorId { get; set; }

    [Column("creado_por")]
    public int? CreadoPor { get; set; }

    [Column("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    [Column("fecha_actualizacion")]
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    [Column("direccion_entrega"), Required, MaxLength(250)]
    public string DireccionEntrega { get; set; } = "";

    [Column("referencia"), MaxLength(250)]
    public string? Referencia { get; set; }

    [Column("ubicacion", TypeName = "geography")]
    public Point? Ubicacion { get; set; }

    /// <summary>Franja horaria de entrega (hora de Lima), opcional.</summary>
    [Column("ventana_inicio")]
    public TimeOnly? VentanaInicio { get; set; }

    [Column("ventana_fin")]
    public TimeOnly? VentanaFin { get; set; }

    [Column("observaciones"), MaxLength(500)]
    public string? Observaciones { get; set; }

    public Cliente Cliente { get; set; } = null!;
    public EstadoPedido Estado { get; set; } = null!;
    public ZonaReparto? Zona { get; set; }
    public Repartidor? Repartidor { get; set; }
    public Usuario? Creador { get; set; }

    public ICollection<DetallePedido> Detalles { get; set; } = new List<DetallePedido>();
    public ICollection<HistorialEstado> Historial { get; set; } = new List<HistorialEstado>();
    public ICollection<Tracking> Enlaces { get; set; } = new List<Tracking>();
    public ICollection<PruebaEntrega> Pruebas { get; set; } = new List<PruebaEntrega>();
    public ICollection<Notificacion> Notificaciones { get; set; } = new List<Notificacion>();
}
