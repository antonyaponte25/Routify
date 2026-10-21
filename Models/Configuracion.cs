using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace Routify.Web.Models;

[Table("configuracion")]
public class Configuracion
{
    [Key, Column("clave"), MaxLength(60)]
    public string Clave { get; set; } = "";

    [Column("valor"), Required, MaxLength(200)]
    public string Valor { get; set; } = "";

    [Column("descripcion"), MaxLength(250)]
    public string? Descripcion { get; set; }
}
