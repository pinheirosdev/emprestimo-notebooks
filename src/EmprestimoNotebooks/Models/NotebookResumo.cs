namespace EmprestimoNotebooks.Models;

public record NotebookResumo(int Id, string Patrimonio, string Marca, string Modelo, bool Ativo, bool EmManutencao, int? EmprestimoAbertoId)
{
    public bool Emprestado => EmprestimoAbertoId.HasValue;
    public bool Disponivel => Ativo && !EmManutencao && !Emprestado;
}
