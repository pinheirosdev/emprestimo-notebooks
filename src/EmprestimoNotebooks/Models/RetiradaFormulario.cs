using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EmprestimoNotebooks.Models;

public class RetiradaFormulario
{
    [Display(Name = "Aluno")]
    [Required(ErrorMessage = "Selecione um aluno cadastrado.")]
    public int? AlunoId { get; set; }

    [Required(ErrorMessage = "Tire a foto do aluno na retirada.")]
    public IFormFile? Foto { get; set; }

    [Required(ErrorMessage = "Tire a foto do notebook na retirada.")]
    public IFormFile? FotoNotebook { get; set; }

    public string? Busca { get; set; }

    [BindNever]
    public int NotebookId { get; set; }

    [BindNever]
    public string Patrimonio { get; set; } = "";

    [BindNever]
    public string Marca { get; set; } = "";

    [BindNever]
    public string Modelo { get; set; } = "";

    [BindNever]
    public IReadOnlyList<AlunoResumo> Alunos { get; set; } = [];

    [BindNever]
    public bool Indisponivel { get; set; }
}
