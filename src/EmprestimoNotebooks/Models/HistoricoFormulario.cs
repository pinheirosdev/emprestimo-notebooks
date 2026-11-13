namespace EmprestimoNotebooks.Models;

public record HistoricoFormulario(string Mes, string TituloMes, IReadOnlyList<MovimentacaoResumo> Movimentacoes,
    IReadOnlyList<EmprestimoAbertoResumo> EmAberto, IReadOnlyList<EventoCadastroResumo> Eventos);

public record MovimentacaoResumo(int EmprestimoId, string Patrimonio, string NomeCompleto, string Matricula,
    DateTime DataLocal, bool Devolucao, string? Observacao, bool TemFotoNotebook);

public record EmprestimoAbertoResumo(int EmprestimoId, string Patrimonio, string NomeCompleto, string Matricula,
    DateTime RetiradoEmLocal);

public record EventoCadastroResumo(DateTime DataLocal, string Descricao, string Referencia);
