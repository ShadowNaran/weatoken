using System.ComponentModel.DataAnnotations;

namespace Apiclase19.Dominio;

public class Persona
{
    [Key]
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public int Edad { get; set; }
}
