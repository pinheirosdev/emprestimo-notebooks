using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EmprestimoNotebooks.Models;

public class DevolucaoFormulario
{
    [Required(ErrorMessage = "Tire a foto do aluno na devolução.")]
    public IFormFile? Foto { get; set; }

    [Required(ErrorMessage = "Tire a foto do notebook na devolução.")]
    public IFormFile? FotoNotebook { get; set; }

    [Display(Name = "Observação")]
    [StringLength(500, ErrorMessage = "Use até 500 caracteres na observação.")]
    public string? Observacao { get; set; }

    [BindNever]
    public int EmprestimoId { get; set; }

    [BindNever]
    public string Patrimonio { get; set; } = "";

    [BindNever]
    public string Marca { get; set; } = "";

    [BindNever]
    public string Modelo { get; set; } = "";

    [BindNever]
    public string NomeCompleto { get; set; } = "";

    [BindNever]
    public string Matricula { get; set; } = "";

    [BindNever]
    public bool Encerrado { get; set; }
}
