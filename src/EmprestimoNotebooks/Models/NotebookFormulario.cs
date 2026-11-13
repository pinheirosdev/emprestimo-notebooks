using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EmprestimoNotebooks.Models;

public class NotebookFormulario
{
    [BindNever]
    public int? Id { get; set; }

    [Display(Name = "Patrimônio")]
    [Required(ErrorMessage = "Informe o patrimônio.")]
    [StringLength(30, ErrorMessage = "Use até 30 caracteres no patrimônio.")]
    public string Patrimonio { get; set; } = "";

    [Required(ErrorMessage = "Informe a marca.")]
    [StringLength(60, ErrorMessage = "Use até 60 caracteres na marca.")]
    public string Marca { get; set; } = "";

    [Required(ErrorMessage = "Informe o modelo.")]
    [StringLength(80, ErrorMessage = "Use até 80 caracteres no modelo.")]
    public string Modelo { get; set; } = "";

    [Display(Name = "Em manutenção")]
    public bool EmManutencao { get; set; }
}
