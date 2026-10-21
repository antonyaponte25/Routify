using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("usuarios")]
public class Usuario
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("rol_id")]
    public int RolId { get; set; }

    [Column("nombre"), Required, MaxLength(100)]
    public string Nombre { get; set; } = "";

    [Column("apellido"), Required, MaxLength(100)]
    public string Apellido { get; set; } = "";

    [Column("correo"), Required, MaxLength(150)]
    public string Correo { get; set; } = "";

    [Column("telefono"), MaxLength(20)]
    public string? Telefono { get; set; }

    [Column("password_hash"), Required, MaxLength(255)]
    public string PasswordHash { get; set; } = "";

    /// <summary>Activo | Inactivo</summary>
    [Column("estado"), Required, MaxLength(20)]
    public string Estado { get; set; } = "Activo";

    [Column("intentos_fallidos")]
    public int IntentosFallidos { get; set; }

    [Column("bloqueado_hasta")]
    public DateTime? BloqueadoHasta { get; set; }

    [Column("token_recuperacion_hash"), MaxLength(100)]
    public string? TokenRecuperacionHash { get; set; }

    [Column("token_recuperacion_expira")]
    public DateTime? TokenRecuperacionExpira { get; set; }

    [Column("ultimo_acceso")]
    public DateTime? UltimoAcceso { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public Rol Rol { get; set; } = null!;
    public Repartidor? Repartidor { get; set; }

    [NotMapped]
    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();

    [NotMapped]
    public bool EstaActivo => Estado == "Activo";
}
