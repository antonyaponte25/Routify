using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("clientes")]
public class Cliente
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("nombre"), Required, MaxLength(150)]
    public string Nombre { get; set; } = "";

    [Column("telefono"), MaxLength(20)]
    public string? Telefono { get; set; }

    [Column("correo"), MaxLength(150)]
    public string? Correo { get; set; }

    [Column("direccion"), Required, MaxLength(250)]
    public string Direccion { get; set; } = "";

    [Column("referencia"), MaxLength(250)]
    public string? Referencia { get; set; }

    [Column("ubicacion", TypeName = "geography")]
    public Point? Ubicacion { get; set; }

    [Column("notificar_correo")]
    public bool NotificarCorreo { get; set; } = true;

    [Column("notificar_whatsapp")]
    public bool NotificarWhatsapp { get; set; } = true;

    [Column("consentimiento_datos")]
    public bool ConsentimientoDatos { get; set; }

    [Column("fecha_consentimiento")]
    public DateTime? FechaConsentimiento { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
}
