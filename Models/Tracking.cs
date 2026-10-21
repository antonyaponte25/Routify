using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("tracking")]
public class Tracking
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("token"), Required, MaxLength(64)]
    public string Token { get; set; } = "";

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [Column("fecha_expiracion")]
    public DateTime FechaExpiracion { get; set; }

    [Column("activo")]
    public bool Activo { get; set; } = true;

    public Pedido Pedido { get; set; } = null!;

    [NotMapped]
    public bool Vigente => Activo && FechaExpiracion > DateTime.UtcNow;
}
