using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("historial_estados")]
public class HistorialEstado
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("estado_anterior_id")]
    public int? EstadoAnteriorId { get; set; }

    [Column("estado_nuevo_id")]
    public int EstadoNuevoId { get; set; }

    [Column("usuario_id")]
    public int? UsuarioId { get; set; }

    [Column("fecha")]
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    [Column("comentario"), MaxLength(300)]
    public string? Comentario { get; set; }

    public Pedido Pedido { get; set; } = null!;
    public EstadoPedido? EstadoAnterior { get; set; }
    public EstadoPedido EstadoNuevo { get; set; } = null!;
    public Usuario? Usuario { get; set; }
}
