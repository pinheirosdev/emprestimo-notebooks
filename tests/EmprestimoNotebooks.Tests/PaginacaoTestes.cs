using System.Net;
using System.Text.RegularExpressions;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class PaginacaoTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"paginacao-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task InventarioMostraDezNotebooksELimitaAPaginaSolicitada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        banco.Notebooks.AddRange(Enumerable.Range(1, 6).Select(numero => NovoNotebook($"PAG-{numero:00}")));
        await banco.SaveChangesAsync(cancelamento);

        var inicio = new[] { "NB-001", "NB-002", "NB-003", "NB-004", "NB-005", "NB-006", "PAG-01", "PAG-02", "PAG-03", "PAG-04" };
        var fim = new[] { "PAG-05", "PAG-06" };
        Assert.Equal(inicio, CabecalhosDeLinha(await LerPaginaAsync(cliente, "/")));
        Assert.Equal(inicio, CabecalhosDeLinha(await LerPaginaAsync(cliente, "/?pagina=-3")));
        Assert.Equal(fim, CabecalhosDeLinha(await LerPaginaAsync(cliente, "/?pagina=2")));
        Assert.Equal(fim, CabecalhosDeLinha(await LerPaginaAsync(cliente, "/?pagina=999")));
    }

    [Fact]
    public async Task AlunosSaoPaginadosDepoisDaBuscaENavegacaoMantemOTermo()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        banco.Alunos.AddRange(Enumerable.Range(1, 11).Select(numero => NovoAluno($"Turma QA {numero:00}", $"PAG-{numero:00}")));
        banco.Alunos.Add(NovoAluno("Aluna fora da busca", "OUTRO-01"));
        await banco.SaveChangesAsync(cancelamento);

        var primeira = await LerPaginaAsync(cliente, "/alunos?busca=turma%20qa");
        Assert.Equal(Enumerable.Range(1, 10).Select(numero => $"Turma QA {numero:00}"), CabecalhosDeLinha(primeira));
        var segundaPagina = LinkDePagina(primeira, cliente.BaseAddress!, "/alunos", "pagina", "2");
        Assert.Equal("turma qa", QueryHelpers.ParseQuery(segundaPagina.Query)["busca"].ToString());
        Assert.Equal(new[] { "Turma QA 11" }, CabecalhosDeLinha(await LerPaginaAsync(cliente, segundaPagina.PathAndQuery)));
    }

    [Fact]
    public async Task HistoricoPaginaAsTresSecoesIndependentesEMantemOMes()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        for (var numero = 1; numero <= 6; numero++)
        {
            var notebook = NovoNotebook($"PAG-{numero:00}");
            var aluno = NovoAluno($"Aluno {numero:00}", $"MAT-{numero:00}");
            aluno.CadastradoEmUtc = DataUtc(3, numero);
            banco.AddRange(notebook, aluno);
            await banco.SaveChangesAsync(cancelamento);

            var jpeg = Convert.FromBase64String(JpegSintetico);
            banco.Emprestimos.AddRange(
                new Emprestimo { NotebookId = notebook.Id, AlunoId = aluno.Id, RetiradoEmUtc = DataUtc(2, numero), FotoRetiradaJpeg = jpeg },
                new Emprestimo
                {
                    NotebookId = notebook.Id,
                    AlunoId = aluno.Id,
                    RetiradoEmUtc = DataUtc(2, numero),
                    DevolvidoEmUtc = DataUtc(3, numero),
                    FotoRetiradaJpeg = jpeg,
                    FotoDevolucaoJpeg = jpeg
                });
        }
        await banco.SaveChangesAsync(cancelamento);

        var historico = await LerPaginaAsync(cliente, "/historico?mes=2025-03&paginaAbertos=2&paginaMovimentacoes=2");
        Assert.Equal(new[] { "PAG-01" }, CabecalhosDeLinha(Secao(historico, "em-aberto")));
        Assert.Equal(new[] { "PAG-01" }, CabecalhosDeLinha(Secao(historico, "movimentacoes")));
        var eventos = Secao(historico, "cadastros-manutencao");
        Assert.Equal(5, CabecalhosDeLinha(eventos).Count);
        Assert.Contains("MAT-06", eventos);
        Assert.DoesNotContain("MAT-01", eventos);

        var proxima = LinkDePagina(eventos, cliente.BaseAddress!, "/historico", "paginaEventos", "2");
        var parametros = QueryHelpers.ParseQuery(proxima.Query);
        Assert.Equal("2025-03", parametros["mes"].ToString());
        Assert.Equal("2", parametros["paginaAbertos"].ToString());
        Assert.Equal("2", parametros["paginaMovimentacoes"].ToString());

        var ultima = await LerPaginaAsync(cliente, proxima.PathAndQuery);
        Assert.Single(CabecalhosDeLinha(Secao(ultima, "cadastros-manutencao")));
        Assert.Contains("MAT-01", Secao(ultima, "cadastros-manutencao"));
        Assert.Equal(new[] { "PAG-01" }, CabecalhosDeLinha(Secao(ultima, "em-aberto")));
        Assert.Equal(new[] { "PAG-01" }, CabecalhosDeLinha(Secao(ultima, "movimentacoes")));
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static Notebook NovoNotebook(string patrimonio) => new() { Patrimonio = patrimonio, Marca = "Dell", Modelo = "Latitude" };

    private static Aluno NovoAluno(string nome, string matricula) => new()
    {
        NomeCompleto = nome,
        Matricula = matricula,
        Turma = "Turma de teste",
        FotoJpeg = Convert.FromBase64String(JpegSintetico)
    };

    private static DateTime DataUtc(int mes, int dia) => new(2025, mes, dia, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<string> LerPaginaAsync(HttpClient cliente, string rota) =>
        WebUtility.HtmlDecode(await cliente.GetStringAsync(rota, TestContext.Current.CancellationToken));

    private static IReadOnlyList<string> CabecalhosDeLinha(string html) =>
        Regex.Matches(html, """<th scope="row">([^<]+)</th>""")
            .Select(cabecalho => cabecalho.Groups[1].Value).ToArray();

    private static string Secao(string html, string id)
    {
        var secao = Regex.Match(html, $$"""<section\b[^>]*aria-labelledby="{{id}}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(secao.Success, $"Seção {id} não encontrada.");
        return secao.Groups[1].Value;
    }

    private static Uri LinkDePagina(string html, Uri baseUri, string rota, string parametro, string valor) =>
        Regex.Matches(html, """<a\b[^>]*\bhref="([^"]+)"[^>]*>""")
            .Select(link => new Uri(baseUri, link.Groups[1].Value))
            .First(link => link.AbsolutePath == rota && QueryHelpers.ParseQuery(link.Query).TryGetValue(parametro, out var pagina)
                && pagina == valor);
}
