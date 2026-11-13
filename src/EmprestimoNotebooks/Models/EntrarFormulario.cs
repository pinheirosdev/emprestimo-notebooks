using System.ComponentModel.DataAnnotations;

namespace EmprestimoNotebooks.Models;

public class EntrarFormulario
{
    [Display(Name = "Usuário")]
    [Required(ErrorMessage = "Informe o usuário.")]
    public string Usuario { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [DataType(DataType.Password)]
    public string Senha { get; set; } = string.Empty;

    public string? Retorno { get; set; }
}
