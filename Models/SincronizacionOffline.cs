using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("sincronizaciones_offline")]
public class SincronizacionOffline
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None), Column("guid")]
    public Guid Guid { get; set; }

    [Column("repartidor_id")]
    public int RepartidorId { get; set; }

    [Column("tipo"), Required, MaxLength(20)]
    public string Tipo { get; set; } = "";

    [Column("fecha_recepcion")]
    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;
}
