using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("estados_pedido")]
public class EstadoPedido
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None), Column("id")]
    public int Id { get; set; }

    [Column("nombre"), Required, MaxLength(50)]
    public string Nombre { get; set; } = "";

    [Column("descripcion"), MaxLength(200)]
    public string? Descripcion { get; set; }

    [Column("orden")]
    public int Orden { get; set; }
}
