using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("repartidores")]
public class Repartidor
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("usuario_id")]
    public int UsuarioId { get; set; }

    [Column("zona_id")]
    public int? ZonaId { get; set; }

    [Column("disponibilidad")]
    public bool Disponibilidad { get; set; } = true;

    /// <summary>Máximo de pedidos simultáneos que puede llevar.</summary>
    [Column("capacidad")]
    public int Capacidad { get; set; } = 15;

    [Column("vehiculo"), MaxLength(50)]
    public string? Vehiculo { get; set; }

    [Column("placa"), MaxLength(15)]
    public string? Placa { get; set; }

    [Column("ubicacion_actual", TypeName = "geography")]
    public Point? UbicacionActual { get; set; }

    [Column("ultima_actualizacion")]
    public DateTime? UltimaActualizacion { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public ZonaReparto? Zona { get; set; }
    public ICollection<Ruta> Rutas { get; set; } = new List<Ruta>();
}
