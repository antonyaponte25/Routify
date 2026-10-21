using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("paradas")]
public class Parada
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("ruta_id")]
    public int RutaId { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("orden")]
    public int Orden { get; set; }

    [Column("eta")]
    public DateTime? Eta { get; set; }

    /// <summary>Kilómetros desde la parada anterior.</summary>
    [Column("distancia", TypeName = "decimal(10,2)")]
    public decimal? Distancia { get; set; }

    [Column("estado"), Required, MaxLength(30)]
    public string Estado { get; set; } = "Pendiente";

    [Column("ubicacion", TypeName = "geography")]
    public Point Ubicacion { get; set; } = null!;

    [Column("fecha_llegada")]
    public DateTime? FechaLlegada { get; set; }

    public Ruta Ruta { get; set; } = null!;
    public Pedido Pedido { get; set; } = null!;
}
