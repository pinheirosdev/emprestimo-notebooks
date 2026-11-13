namespace EmprestimoNotebooks.Models;

public record NotebookResumo(int Id, string Patrimonio, string Marca, string Modelo, bool EmManutencao, int? EmprestimoAbertoId)
{
    public bool Emprestado => EmprestimoAbertoId.HasValue;
}
