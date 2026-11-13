using System.ComponentModel.DataAnnotations;

namespace EmprestimoNotebooks.Models;

public class AlunoFormulario
{
    [Display(Name = "Nome completo")]
    [Required(ErrorMessage = "Informe o nome completo.")]
    [StringLength(120, ErrorMessage = "Use até 120 caracteres no nome.")]
    public string NomeCompleto { get; set; } = "";

    [Display(Name = "Matrícula")]
    [Required(ErrorMessage = "Informe a matrícula.")]
    [StringLength(40, ErrorMessage = "Use até 40 caracteres na matrícula.")]
    public string Matricula { get; set; } = "";

    [Required(ErrorMessage = "Informe a turma.")]
    [StringLength(60, ErrorMessage = "Use até 60 caracteres na turma.")]
    public string Turma { get; set; } = "";

    [Required(ErrorMessage = "Tire a foto do aluno.")]
    public IFormFile? Foto { get; set; }
}
