using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("detalle_pedido")]
public class DetallePedido
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("producto"), Required, MaxLength(200)]
    public string Producto { get; set; } = "";

    [Column("cantidad")]
    public int Cantidad { get; set; } = 1;

    [Column("observacion"), MaxLength(250)]
    public string? Observacion { get; set; }

    public Pedido Pedido { get; set; } = null!;
}
