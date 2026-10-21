using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("rutas")]
public class Ruta
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("repartidor_id")]
    public int RepartidorId { get; set; }

    [Column("fecha", TypeName = "date")]
    public DateTime Fecha { get; set; }

    /// <summary>Kilómetros.</summary>
    [Column("distancia_total", TypeName = "decimal(10,2)")]
    public decimal? DistanciaTotal { get; set; }

    /// <summary>Minutos (viaje + tiempo de servicio).</summary>
    [Column("tiempo_estimado")]
    public int? TiempoEstimado { get; set; }

    [Column("estado"), Required, MaxLength(30)]
    public string Estado { get; set; } = "Planificada";

    [Column("geometria", TypeName = "geography")]
    public LineString? Geometria { get; set; }

    [Column("fecha_inicio")]
    public DateTime? FechaInicio { get; set; }

    [Column("fecha_fin")]
    public DateTime? FechaFin { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public Repartidor Repartidor { get; set; } = null!;
    public ICollection<Parada> Paradas { get; set; } = new List<Parada>();
}
