using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("zonas_reparto")]
public class ZonaReparto
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("nombre"), Required, MaxLength(100)]
    public string Nombre { get; set; } = "";

    [Column("descripcion"), MaxLength(250)]
    public string? Descripcion { get; set; }

    [Column("color"), Required, MaxLength(9)]
    public string Color { get; set; } = "#4F46E5";

    [Column("poligono", TypeName = "geography")]
    public Polygon Poligono { get; set; } = null!;

    [Column("activa")]
    public bool Activa { get; set; } = true;

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
