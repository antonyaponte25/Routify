using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("notificaciones")]
public class Notificacion
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("pedido_id")]
    public int PedidoId { get; set; }

    [Column("cliente_id")]
    public int? ClienteId { get; set; }

    [Column("evento"), Required, MaxLength(30)]
    public string Evento { get; set; } = "";

    /// <summary>Correo | WhatsApp</summary>
    [Column("canal"), Required, MaxLength(15)]
    public string Canal { get; set; } = "";

    [Column("destino"), Required, MaxLength(150)]
    public string Destino { get; set; } = "";

    [Column("asunto"), MaxLength(200)]
    public string? Asunto { get; set; }

    [Column("mensaje"), Required, MaxLength(1000)]
    public string Mensaje { get; set; } = "";

    /// <summary>Pendiente | Enviada | Simulada | Fallida</summary>
    [Column("estado"), Required, MaxLength(15)]
    public string Estado { get; set; } = "Pendiente";

    [Column("intentos")]
    public int Intentos { get; set; }

    [Column("error"), MaxLength(500)]
    public string? Error { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [Column("fecha_envio")]
    public DateTime? FechaEnvio { get; set; }

    public Pedido Pedido { get; set; } = null!;
}
