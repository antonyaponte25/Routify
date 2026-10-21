using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("pruebas_entrega")]
public class PruebaEntrega
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("parada_id")]
    public int? ParadaId { get; set; }

    /// <summary>Fotografia | Firma</summary>
    [Column("tipo"), Required, MaxLength(20)]
    public string Tipo { get; set; } = "Fotografia";

    /// <summary>Ruta relativa dentro de App_Data/uploads.</summary>
    [Column("archivo"), Required, MaxLength(255)]
    public string Archivo { get; set; } = "";

    [Column("observacion"), MaxLength(500)]
    public string? Observacion { get; set; }

    [Column("ubicacion", TypeName = "geography")]
    public Point? Ubicacion { get; set; }

    [Column("guid_cliente")]
    public Guid? GuidCliente { get; set; }

    [Column("fecha_registro")]
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    public Pedido Pedido { get; set; } = null!;
}
