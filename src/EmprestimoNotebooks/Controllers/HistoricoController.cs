using System.Globalization;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class HistoricoController(EmprestimosContexto contexto) : Controller
{
    private static readonly CultureInfo Portugues = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private const int ItensPorPagina = 5;

    [HttpGet("/historico")]
    public async Task<IActionResult> Index(string? mes, int paginaAbertos, int paginaMovimentacoes, int paginaEventos,
        CancellationToken cancelamento)
    {
        if (!DateTime.TryParseExact(mes, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio)
            || inicio.Year == 9999)
        {
            var hoje = HoraLocal(DateTime.UtcNow);
            inicio = new DateTime(hoje.Year, hoje.Month, 1);
        }

        var inicioUtc = InicioDoMesEmUtc(inicio);
        var fimUtc = InicioDoMesEmUtc(inicio.AddMonths(1));
        var emprestimos = from emprestimo in contexto.Emprestimos
                          join notebook in contexto.Notebooks on emprestimo.NotebookId equals notebook.Id
                          join aluno in contexto.Alunos on emprestimo.AlunoId equals aluno.Id
                          select new
                          {
                              emprestimo.Id,
                              notebook.Patrimonio,
                              aluno.NomeCompleto,
                              aluno.Matricula,
                              emprestimo.RetiradoEmUtc,
                              emprestimo.DevolvidoEmUtc,
                              emprestimo.ObservacaoDevolucao,
                              TemFotoNotebookNaRetirada = emprestimo.FotoNotebookRetiradaJpeg != null,
                              TemFotoNotebookNaDevolucao = emprestimo.FotoNotebookJpeg != null
                          };

        var registrosDoMes = await emprestimos.Where(item =>
                (item.RetiradoEmUtc >= inicioUtc && item.RetiradoEmUtc < fimUtc)
                || (item.DevolvidoEmUtc >= inicioUtc && item.DevolvidoEmUtc < fimUtc))
            .ToListAsync(cancelamento);

        var movimentacoes = new List<MovimentacaoResumo>();
        foreach (var item in registrosDoMes)
        {
            if (item.RetiradoEmUtc >= inicioUtc && item.RetiradoEmUtc < fimUtc)
            {
                movimentacoes.Add(new MovimentacaoResumo(item.Id, item.Patrimonio, item.NomeCompleto, item.Matricula,
                    HoraLocal(item.RetiradoEmUtc), false, null, item.TemFotoNotebookNaRetirada));
            }

            if (item.DevolvidoEmUtc is DateTime devolucao && devolucao >= inicioUtc && devolucao < fimUtc)
            {
                movimentacoes.Add(new MovimentacaoResumo(item.Id, item.Patrimonio, item.NomeCompleto, item.Matricula,
                    HoraLocal(devolucao), true, item.ObservacaoDevolucao, item.TemFotoNotebookNaDevolucao));
            }
        }

        var emAberto = await emprestimos.Where(item => item.DevolvidoEmUtc == null)
            .OrderByDescending(item => item.RetiradoEmUtc).ThenByDescending(item => item.Id)
            .Select(item => new EmprestimoAbertoResumo(item.Id, item.Patrimonio, item.NomeCompleto, item.Matricula,
                HoraLocal(item.RetiradoEmUtc))).ToListAsync(cancelamento);

        var eventos = await ListarEventos(inicioUtc, fimUtc, cancelamento);
        var movimentacoesOrdenadas = movimentacoes.OrderByDescending(item => item.DataLocal)
            .ThenByDescending(item => item.EmprestimoId).ThenByDescending(item => item.Devolucao).ToList();
        return View(new HistoricoFormulario(inicio.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            inicio.ToString("MMMM 'de' yyyy", Portugues),
            Pagina<MovimentacaoResumo>.Criar(movimentacoesOrdenadas, paginaMovimentacoes, ItensPorPagina),
            Pagina<EmprestimoAbertoResumo>.Criar(emAberto, paginaAbertos, ItensPorPagina),
            Pagina<EventoCadastroResumo>.Criar(eventos, paginaEventos, ItensPorPagina)));
    }

    private async Task<IReadOnlyList<EventoCadastroResumo>> ListarEventos(DateTime inicio, DateTime fim, CancellationToken cancelamento)
    {
        var notebooks = await contexto.Notebooks.Where(notebook => notebook.CadastradoEmUtc >= inicio && notebook.CadastradoEmUtc < fim)
            .Select(notebook => new EventoCadastroResumo(HoraLocal(notebook.CadastradoEmUtc!.Value),
                "Cadastro de notebook", notebook.Patrimonio)).ToListAsync(cancelamento);

        var alunos = await contexto.Alunos.Where(aluno => aluno.CadastradoEmUtc >= inicio && aluno.CadastradoEmUtc < fim)
            .Select(aluno => new EventoCadastroResumo(HoraLocal(aluno.CadastradoEmUtc!.Value),
                "Cadastro de aluno", aluno.NomeCompleto + " · " + aluno.Matricula)).ToListAsync(cancelamento);

        var manutencoes = await (from manutencao in contexto.ManutencoesNotebook
                                 join notebook in contexto.Notebooks on manutencao.NotebookId equals notebook.Id
                                 where manutencao.AlteradoEmUtc >= inicio && manutencao.AlteradoEmUtc < fim
                                 select new EventoCadastroResumo(HoraLocal(manutencao.AlteradoEmUtc),
                                     manutencao.EmManutencao ? "Entrada em manutenção" : "Saída de manutenção", notebook.Patrimonio))
            .ToListAsync(cancelamento);

        return notebooks.Concat(alunos).Concat(manutencoes).OrderByDescending(evento => evento.DataLocal).ToList();
    }

    [HttpGet("/historico/{id:int}/ver-foto/{tipo}")]
    public async Task<IActionResult> VerFoto([FromRoute] int id, [FromRoute] string tipo, string? mes,
        int? paginaAbertos, int? paginaMovimentacoes, int? paginaEventos, CancellationToken cancelamento)
    {
        var titulo = tipo switch
        {
            "retirada" => "Foto do aluno na retirada",
            "devolucao" => "Foto do aluno na devolução",
            "notebook-retirada" => "Foto do notebook na retirada",
            "notebook" => "Foto do notebook na devolução",
            _ => null
        };

        var foto = ConsultarFoto(id, tipo);
        if (titulo is null || foto is null || !await foto.AnyAsync(imagem => imagem != null, cancelamento))
        {
            return NotFound();
        }

        return View(new FotoPagina(titulo, Url.Action(nameof(Foto), new { id, tipo })!,
            Url.Action(nameof(Index), new { mes, paginaAbertos, paginaMovimentacoes, paginaEventos })!));
    }

    [HttpGet("/historico/{id:int}/foto/{tipo}")]
    public async Task<IActionResult> Foto([FromRoute] int id, [FromRoute] string tipo, CancellationToken cancelamento)
    {
        var consulta = ConsultarFoto(id, tipo);
        if (consulta is null)
        {
            return NotFound();
        }

        var foto = await consulta.SingleOrDefaultAsync(cancelamento);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return foto is null ? NotFound() : File(foto, "image/jpeg");
    }

    private IQueryable<byte[]?>? ConsultarFoto(int id, string tipo)
    {
        var emprestimo = contexto.Emprestimos.Where(item => item.Id == id);
        return tipo switch
        {
            "retirada" => emprestimo.Select(item => (byte[]?)item.FotoRetiradaJpeg),
            "devolucao" => emprestimo.Select(item => item.FotoDevolucaoJpeg),
            "notebook-retirada" => emprestimo.Select(item => item.FotoNotebookRetiradaJpeg),
            "notebook" => emprestimo.Select(item => item.FotoNotebookJpeg),
            _ => null
        };
    }

    private static DateTime HoraLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Fuso);

    private static DateTime InicioDoMesEmUtc(DateTime inicio)
    {
        if (Fuso.IsInvalidTime(inicio))
        {
            return new DateTimeOffset(inicio, Fuso.GetUtcOffset(inicio.AddDays(-1))).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(inicio, Fuso);
    }
}
