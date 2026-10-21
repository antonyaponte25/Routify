using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("ubicaciones_repartidor")]
public class UbicacionRepartidor
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("repartidor_id")]
    public int RepartidorId { get; set; }

    [Column("ruta_id")]
    public int? RutaId { get; set; }

    [Column("ubicacion", TypeName = "geography")]
    public Point Ubicacion { get; set; } = null!;

    [Column("precision_m")]
    public float? PrecisionM { get; set; }

    [Column("velocidad_kmh")]
    public float? VelocidadKmh { get; set; }

    [Column("fecha_hora")]
    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    public Repartidor Repartidor { get; set; } = null!;
}
