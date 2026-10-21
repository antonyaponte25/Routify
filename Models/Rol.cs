using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("roles")]
public class Rol
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None), Column("id")]
    public int Id { get; set; }

    [Column("nombre"), Required, MaxLength(50)]
    public string Nombre { get; set; } = "";

    [Column("descripcion"), MaxLength(200)]
    public string? Descripcion { get; set; }

    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
