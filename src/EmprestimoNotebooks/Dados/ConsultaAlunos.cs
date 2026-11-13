using System.Globalization;
using EmprestimoNotebooks.Models;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Dados;

internal static class ConsultaAlunos
{
    private static readonly CompareInfo ComparacaoDeNomes = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    internal static async Task<IReadOnlyList<AlunoResumo>> Listar(EmprestimosContexto contexto, string? busca, CancellationToken cancelamento)
    {
        var alunos = await contexto.Alunos.AsNoTracking()
            .OrderBy(aluno => aluno.NomeCompleto).ThenBy(aluno => aluno.Matricula)
            .Select(aluno => new AlunoResumo(aluno.Id, aluno.NomeCompleto, aluno.Matricula, aluno.Turma))
            .ToListAsync(cancelamento);

        var termo = busca?.Trim();
        return string.IsNullOrEmpty(termo) ? alunos : alunos.Where(aluno =>
            ComparacaoDeNomes.IndexOf(aluno.NomeCompleto, termo, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
            || aluno.Matricula.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
