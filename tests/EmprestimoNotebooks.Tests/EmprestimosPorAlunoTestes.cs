using System.Data.Common;
using System.Globalization;
using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class EmprestimosPorAlunoTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"emprestimos-aluno-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RetiradasSimultaneasDeNotebooksDiferentesParaMesmoAlunoGravamApenasUmEmprestimo()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var encontro = new EncontroAntesDeGravar();
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco, encontro);
        using var primeiroCliente = aplicacao.CriarCliente();
        using var segundoCliente = aplicacao.CriarCliente();
        using var primeiroLogin = await EntrarAsync(primeiroCliente);
        using var segundoLogin = await EntrarAsync(segundoCliente);
        Assert.Equal(HttpStatusCode.Redirect, primeiroLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segundoLogin.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var primeiroNotebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var segundoNotebook = await BuscarNotebookAsync(banco, "NB-002", cancelamento);
        var primeiraRetirada = $"/notebooks/{primeiroNotebook.Id}/retirar";
        var segundaRetirada = $"/notebooks/{segundoNotebook.Id}/retirar";
        var primeiroToken = await ObterTokenAsync(primeiroCliente, primeiraRetirada);
        var segundoToken = await ObterTokenAsync(segundoCliente, segundaRetirada);

        var primeiraTentativa = RetirarAsync(primeiroCliente, primeiraRetirada, primeiroToken, aluno.Id);
        var segundaTentativa = RetirarAsync(segundoCliente, segundaRetirada, segundoToken, aluno.Id);
        await Task.WhenAll(primeiraTentativa, segundaTentativa);
        using var primeiraResposta = await primeiraTentativa;
        using var segundaResposta = await segundaTentativa;
        var respostas = new[] { primeiraResposta, segundaResposta };
        Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Redirect);
        var conflito = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("Este aluno já tem um notebook emprestado.",
            WebUtility.HtmlDecode(await conflito.Content.ReadAsStringAsync(cancelamento)));
        Assert.Equal(2, encontro.Tentativas);

        var emprestimo = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(aluno.Id, emprestimo.AlunoId);
        Assert.Null(emprestimo.DevolvidoEmUtc);
        var vencedor = primeiraResposta.StatusCode == HttpStatusCode.Redirect ? primeiroNotebook : segundoNotebook;
        var disponivel = vencedor.Id == primeiroNotebook.Id ? segundoNotebook : primeiroNotebook;
        Assert.Equal(vencedor.Id, emprestimo.NotebookId);
        var inventario = WebUtility.HtmlDecode(await primeiroCliente.GetStringAsync("/", cancelamento));
        Assert.Contains("Emprestado", LinhaDoNotebook(inventario, vencedor.Patrimonio));
        var linhaDisponivel = LinhaDoNotebook(inventario, disponivel.Patrimonio);
        Assert.Contains("Disponível", linhaDisponivel);
        Assert.Contains($"/notebooks/{disponivel.Id}/retirar", linhaDisponivel);
    }

    [Fact]
    public async Task DevolucaoLiberaNovaRetiradaDoAlunoSemAlterarHistorico()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var primeiroNotebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var segundoNotebook = await BuscarNotebookAsync(banco, "NB-002", cancelamento);
        var primeiraRetirada = $"/notebooks/{primeiroNotebook.Id}/retirar";
        var segundaRetirada = $"/notebooks/{segundoNotebook.Id}/retirar";
        var tokenAntigo = await ObterTokenAsync(cliente, segundaRetirada);
        using var primeiraResposta = await RetirarAsync(cliente, primeiraRetirada,
            await ObterTokenAsync(cliente, primeiraRetirada), aluno.Id);
        Assert.Equal(HttpStatusCode.Redirect, primeiraResposta.StatusCode);
        var original = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);

        using var bloqueio = await RetirarAsync(cliente, segundaRetirada, tokenAntigo, aluno.Id, "Ana");
        Assert.Equal(HttpStatusCode.Conflict, bloqueio.StatusCode);
        var html = WebUtility.HtmlDecode(await bloqueio.Content.ReadAsStringAsync(cancelamento));
        Assert.Contains("Este aluno já tem um notebook emprestado.", html);
        Assert.Matches("""data-valmsg-for="AlunoId"[^>]*>\s*[^\s<]""", html);
        Assert.Matches($$"""<option\b(?=[^>]*\bvalue="{{aluno.Id}}")(?=[^>]*\bselected)[^>]*>""", html);
        Assert.Matches("""<input\b(?=[^>]*\bname="Busca")(?=[^>]*\bvalue="Ana")[^>]*>""", html);
        var preservado = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(original.Id, preservado.Id);
        Assert.Equal(original.RetiradoEmUtc, preservado.RetiradoEmUtc);
        Assert.Equal(original.FotoRetiradaJpeg, preservado.FotoRetiradaJpeg);
        Assert.Equal(original.FotoNotebookRetiradaJpeg, preservado.FotoNotebookRetiradaJpeg);
        Assert.Null(preservado.DevolvidoEmUtc);

        using var devolucao = await DevolverAsync(cliente, original.Id);
        Assert.Equal(HttpStatusCode.Redirect, devolucao.StatusCode);
        using var novaRetirada = await RetirarAsync(cliente, segundaRetirada,
            await ObterTokenAsync(cliente, segundaRetirada), aluno.Id);
        Assert.Equal(HttpStatusCode.Redirect, novaRetirada.StatusCode);

        var registros = await banco.Emprestimos.AsNoTracking().ToListAsync(cancelamento);
        Assert.Equal(2, registros.Count);
        var devolvido = Assert.Single(registros, item => item.Id == original.Id);
        Assert.NotNull(devolvido.DevolvidoEmUtc);
        Assert.NotNull(devolvido.FotoDevolucaoJpeg);
        Assert.NotNull(devolvido.FotoNotebookJpeg);
        Assert.Equal(original.FotoRetiradaJpeg, devolvido.FotoRetiradaJpeg);
        Assert.Equal(original.FotoNotebookRetiradaJpeg, devolvido.FotoNotebookRetiradaJpeg);
        var aberto = Assert.Single(registros, item => item.DevolvidoEmUtc == null);
        Assert.Equal(aluno.Id, aberto.AlunoId);
        Assert.Equal(segundoNotebook.Id, aberto.NotebookId);
    }

    [Fact]
    public async Task EmprestimosAntigosDoMesmoAlunoSaoPreservadosEApenasDevolucoesLiberamNovaRetirada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var primeiroNotebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var segundoNotebook = await BuscarNotebookAsync(banco, "NB-002", cancelamento);
        var terceiroNotebook = await BuscarNotebookAsync(banco, "NB-003", cancelamento);
        var primeiro = NovoEmprestimo(primeiroNotebook.Id, aluno.Id, 3);
        var segundo = NovoEmprestimo(segundoNotebook.Id, aluno.Id, 4);
        banco.Emprestimos.AddRange(primeiro, segundo);
        await banco.SaveChangesAsync(cancelamento);
        var terceiraRetirada = $"/notebooks/{terceiroNotebook.Id}/retirar";
        var token = await ObterTokenAsync(cliente, terceiraRetirada);

        using var primeiraTentativa = await RetirarAsync(cliente, terceiraRetirada, token, aluno.Id);
        Assert.Equal(HttpStatusCode.Conflict, primeiraTentativa.StatusCode);
        Assert.Equal(2, await banco.EmprestimosAbertos.CountAsync(cancelamento));
        Assert.Equal(2, await banco.Emprestimos.CountAsync(cancelamento));
        foreach (var original in new[] { primeiro, segundo })
        {
            var preservado = await banco.Emprestimos.AsNoTracking().SingleAsync(item => item.Id == original.Id, cancelamento);
            Assert.Equal(original.RetiradoEmUtc, preservado.RetiradoEmUtc);
            Assert.Equal(original.FotoRetiradaJpeg, preservado.FotoRetiradaJpeg);
            Assert.Equal(original.FotoNotebookRetiradaJpeg, preservado.FotoNotebookRetiradaJpeg);
            Assert.Null(preservado.DevolvidoEmUtc);
        }

        using var primeiraDevolucao = await DevolverAsync(cliente, primeiro.Id);
        Assert.Equal(HttpStatusCode.Redirect, primeiraDevolucao.StatusCode);
        using var segundaTentativa = await RetirarAsync(cliente, terceiraRetirada, token, aluno.Id);
        Assert.Equal(HttpStatusCode.Conflict, segundaTentativa.StatusCode);
        Assert.Equal(segundo.Id, await banco.EmprestimosAbertos.Select(item => item.Id).SingleAsync(cancelamento));
        Assert.Equal(2, await banco.Emprestimos.CountAsync(cancelamento));

        using var segundaDevolucao = await DevolverAsync(cliente, segundo.Id);
        Assert.Equal(HttpStatusCode.Redirect, segundaDevolucao.StatusCode);
        using var novaRetirada = await RetirarAsync(cliente, terceiraRetirada, token, aluno.Id);
        Assert.Equal(HttpStatusCode.Redirect, novaRetirada.StatusCode);
        var aberto = await banco.EmprestimosAbertos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(terceiroNotebook.Id, aberto.NotebookId);
        Assert.Equal(aluno.Id, aberto.AlunoId);
        Assert.Equal(3, await banco.Emprestimos.CountAsync(cancelamento));
        foreach (var original in new[] { primeiro, segundo })
        {
            var devolvido = await banco.Emprestimos.AsNoTracking().SingleAsync(item => item.Id == original.Id, cancelamento);
            Assert.NotNull(devolvido.DevolvidoEmUtc);
            Assert.Equal(original.FotoRetiradaJpeg, devolvido.FotoRetiradaJpeg);
            Assert.Equal(original.FotoNotebookRetiradaJpeg, devolvido.FotoNotebookRetiradaJpeg);
        }
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static string LinhaDoNotebook(string html, string patrimonio) =>
        Assert.Single(html.Split("</tr>"), linha => linha.Contains($">{patrimonio}<"));

    private static Task<Notebook> BuscarNotebookAsync(EmprestimosContexto banco, string patrimonio,
        CancellationToken cancelamento) =>
        banco.Notebooks.AsNoTracking().SingleAsync(notebook => notebook.Patrimonio == patrimonio, cancelamento);

    private static async Task<Aluno> CadastrarAlunoAsync(EmprestimosContexto banco, CancellationToken cancelamento)
    {
        var aluno = new Aluno
        {
            NomeCompleto = "Ana Lima",
            Matricula = "AL-001",
            Turma = "Turma 1",
            FotoJpeg = Convert.FromBase64String(JpegSintetico)
        };
        banco.Alunos.Add(aluno);
        await banco.SaveChangesAsync(cancelamento);
        return aluno;
    }

    private static Emprestimo NovoEmprestimo(int notebookId, int alunoId, int largura) => new()
    {
        NotebookId = notebookId,
        AlunoId = alunoId,
        RetiradoEmUtc = DateTime.UtcNow.AddMinutes(-10),
        FotoRetiradaJpeg = Convert.FromBase64String(CriarFoto(largura)),
        FotoNotebookRetiradaJpeg = Convert.FromBase64String(CriarFoto(largura + 2))
    };

    private static async Task<HttpResponseMessage> RetirarAsync(HttpClient cliente, string destino, string token,
        int alunoId, string busca = "")
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(token), CampoToken },
            { new StringContent(alunoId.ToString(CultureInfo.InvariantCulture)), "AlunoId" },
            { new StringContent(busca), "Busca" }
        };
        AnexarFoto(formulario, JpegSintetico);
        AnexarFoto(formulario, JpegSintetico, "FotoNotebook");
        return await cliente.PostAsync(destino, formulario, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> DevolverAsync(HttpClient cliente, int emprestimoId)
    {
        var destino = $"/emprestimos/{emprestimoId}/devolver";
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(await ObterTokenAsync(cliente, destino)), CampoToken }
        };
        AnexarFoto(formulario, JpegSintetico);
        AnexarFoto(formulario, JpegSintetico, "FotoNotebook");
        return await cliente.PostAsync(destino, formulario, TestContext.Current.CancellationToken);
    }

    private class EncontroAntesDeGravar : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _prontos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tentativas;

        public int Tentativas => Volatile.Read(ref _tentativas);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("INSERT INTO \"Emprestimos\"", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _tentativas) == 2)
                {
                    _prontos.SetResult();
                }

                await _prontos.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }
}
