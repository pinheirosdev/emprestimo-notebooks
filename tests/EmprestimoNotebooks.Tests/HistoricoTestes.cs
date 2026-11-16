using System.Globalization;
using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class HistoricoTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"historico-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CadastrosEMudancasEfetivasDeManutencaoFicamNoHistorico()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var inicio = DateTime.UtcNow;
        using var cadastroAluno = await CadastrarAlunoAsync(cliente, "AL-101", JpegSintetico);
        using var cadastroNotebook = await SalvarNotebookAsync(cliente, "/notebooks/cadastrar", "NB-007", false);
        Assert.Equal(HttpStatusCode.Redirect, cadastroAluno.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, cadastroNotebook.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await banco.Alunos.AsNoTracking().SingleAsync(cancelamento);
        var notebook = await banco.Notebooks.AsNoTracking().SingleAsync(item => item.Patrimonio == "NB-007", cancelamento);
        Assert.InRange(Assert.IsType<DateTime>(aluno.CadastradoEmUtc), inicio, DateTime.UtcNow);
        Assert.InRange(Assert.IsType<DateTime>(notebook.CadastradoEmUtc), inicio, DateTime.UtcNow);
        var legado = await banco.Notebooks.AsNoTracking().SingleAsync(item => item.Patrimonio == "NB-001", cancelamento);
        Assert.Null(legado.CadastradoEmUtc);

        var edicao = $"/notebooks/{notebook.Id}/editar";
        using var duplicada = await SalvarNotebookAsync(cliente, edicao, "NB-001", true);
        Assert.Equal(HttpStatusCode.OK, duplicada.StatusCode);
        Assert.Empty(await banco.ManutencoesNotebook.ToListAsync(cancelamento));
        Assert.False(await banco.Notebooks.Where(item => item.Id == notebook.Id).Select(item => item.EmManutencao).SingleAsync(cancelamento));

        foreach (var emManutencao in new[] { true, true, false })
        {
            using var alteracao = await SalvarNotebookAsync(cliente, edicao, notebook.Patrimonio, emManutencao);
            Assert.Equal(HttpStatusCode.Redirect, alteracao.StatusCode);
        }

        var eventos = await banco.ManutencoesNotebook.OrderBy(item => item.Id).ToListAsync(cancelamento);
        Assert.Equal(new[] { true, false }, eventos.Select(item => item.EmManutencao));
        Assert.All(eventos, item =>
        {
            Assert.Equal(notebook.Id, item.NotebookId);
            Assert.InRange(item.AlteradoEmUtc, inicio, DateTime.UtcNow);
        });

        var mesCadastroAluno = MesDeSaoPaulo(aluno.CadastradoEmUtc!.Value);
        var mesCadastroNotebook = MesDeSaoPaulo(notebook.CadastradoEmUtc!.Value);
        var historicoCadastroAluno = await LerPaginaAsync(cliente, $"/historico?mes={mesCadastroAluno}");
        Assert.Contains("Cadastro de aluno", historicoCadastroAluno);
        Assert.Contains(aluno.Matricula, historicoCadastroAluno);
        var historicoCadastroNotebook = await LerPaginaAsync(cliente, $"/historico?mes={mesCadastroNotebook}");
        Assert.Contains("Cadastro de notebook", historicoCadastroNotebook);
        Assert.Contains(notebook.Patrimonio, historicoCadastroNotebook);
        Assert.DoesNotContain(legado.Patrimonio, historicoCadastroNotebook);
        Assert.Contains("Entrada em manutenção", await LerPaginaAsync(cliente, $"/historico?mes={MesDeSaoPaulo(eventos[0].AlteradoEmUtc)}"));
        Assert.Contains("Saída de manutenção", await LerPaginaAsync(cliente, $"/historico?mes={MesDeSaoPaulo(eventos[1].AlteradoEmUtc)}"));
    }

    [Fact]
    public async Task HistoricoGeralMostraMovimentacoesDoMesEEmprestimosAbertosDeOutrosMeses()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();

        var notebooks = await banco.Notebooks.OrderBy(item => item.Patrimonio).Take(2).ToListAsync(cancelamento);
        var primeiro = notebooks[0];
        var segundo = notebooks[1];
        primeiro.CadastradoEmUtc = DataUtc(3, 1);
        segundo.CadastradoEmUtc = DataUtc(3, 1);
        var ana = NovoAluno("Ana Exemplo", "AL-101");
        var bruno = NovoAluno("Bruno Exemplo", "AL-102");
        banco.Alunos.AddRange(ana, bruno);
        banco.ManutencoesNotebook.Add(new ManutencaoNotebook
        {
            NotebookId = segundo.Id,
            AlteradoEmUtc = DataUtc(3, 2),
            EmManutencao = false
        });
        await banco.SaveChangesAsync(cancelamento);

        var devolvido = NovoEmprestimo(primeiro.Id, ana.Id, DataUtc(2, 28));
        devolvido.DevolvidoEmUtc = DataUtc(3, 4);
        devolvido.FotoDevolucaoJpeg = Convert.FromBase64String(JpegSintetico);
        var abertoEmFevereiro = NovoEmprestimo(segundo.Id, ana.Id, DataUtc(2, 27));
        var abertoEmMarco = NovoEmprestimo(primeiro.Id, bruno.Id, DataUtc(3, 5));
        abertoEmMarco.FotoNotebookRetiradaJpeg = Convert.FromBase64String(CriarFoto(3));
        banco.Emprestimos.AddRange(devolvido, abertoEmFevereiro, abertoEmMarco);
        await banco.SaveChangesAsync(cancelamento);

        var marco = await LerPaginaAsync(cliente, "/historico?mes=2025-03");
        Assert.Contains("Cadastro de notebook", marco);
        Assert.Contains("Cadastro de aluno", marco);
        Assert.Contains("Saída de manutenção", marco);
        Assert.Contains($"/historico/{devolvido.Id}/ver-foto/devolucao?mes=2025-03", marco);
        Assert.DoesNotContain($"/historico/{devolvido.Id}/ver-foto/notebook?mes=2025-03", marco);
        Assert.Contains($"/historico/{abertoEmMarco.Id}/ver-foto/retirada?mes=2025-03", marco);
        Assert.Contains($"/historico/{abertoEmMarco.Id}/ver-foto/notebook-retirada?mes=2025-03", marco);
        Assert.DoesNotContain($"/historico/{devolvido.Id}/ver-foto/retirada", marco);
        Assert.DoesNotContain($"/historico/{abertoEmFevereiro.Id}/ver-foto/retirada", marco);
        Assert.Contains($"/emprestimos/{abertoEmMarco.Id}/devolver", marco);
        Assert.Contains($"/emprestimos/{abertoEmFevereiro.Id}/devolver", marco);

        var fevereiro = await LerPaginaAsync(cliente, "/historico?mes=2025-02");
        Assert.Contains($"/historico/{devolvido.Id}/ver-foto/retirada?mes=2025-02", fevereiro);
        Assert.DoesNotContain($"/historico/{devolvido.Id}/ver-foto/devolucao", fevereiro);
        Assert.DoesNotContain($"/historico/{devolvido.Id}/ver-foto/notebook-retirada", fevereiro);
        Assert.DoesNotContain("Cadastro de aluno", fevereiro);
        Assert.DoesNotContain("Cadastro de notebook", fevereiro);
        Assert.DoesNotContain("Saída de manutenção", fevereiro);

        var rotaFotoNotebook = $"/historico/{abertoEmMarco.Id}/foto/notebook-retirada";
        var visualizacao = await LerPaginaAsync(cliente, $"/historico/{abertoEmMarco.Id}/ver-foto/notebook-retirada?mes=2025-03");
        Assert.Contains($"src=\"{rotaFotoNotebook}\"", visualizacao);
        using var fotoNotebook = await cliente.GetAsync(rotaFotoNotebook, cancelamento);
        Assert.Equal(HttpStatusCode.OK, fotoNotebook.StatusCode);
        Assert.Equal("image/jpeg", fotoNotebook.Content.Headers.ContentType?.MediaType);
        Assert.True(fotoNotebook.Headers.CacheControl?.NoStore);
        Assert.Equal(abertoEmMarco.FotoNotebookRetiradaJpeg, await fotoNotebook.Content.ReadAsByteArrayAsync(cancelamento));

        using var fotoLegadaAusente = await cliente.GetAsync($"/historico/{devolvido.Id}/ver-foto/notebook-retirada", cancelamento);
        Assert.Equal(HttpStatusCode.NotFound, fotoLegadaAusente.StatusCode);
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static Task<HttpResponseMessage> SalvarNotebookAsync(HttpClient cliente, string rota, string patrimonio, bool emManutencao) =>
        EnviarFormularioAsync(cliente, rota, rota, new Dictionary<string, string>
        {
            ["Patrimonio"] = patrimonio,
            ["Marca"] = "Dell",
            ["Modelo"] = "Latitude",
            ["EmManutencao"] = emManutencao ? "true" : "false"
        });

    private static async Task<string> LerPaginaAsync(HttpClient cliente, string rota) =>
        WebUtility.HtmlDecode(await cliente.GetStringAsync(rota, TestContext.Current.CancellationToken));

    private static string MesDeSaoPaulo(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))
            .ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateTime DataUtc(int mes, int dia) => new(2025, mes, dia, 12, 0, 0, DateTimeKind.Utc);

    private static Aluno NovoAluno(string nome, string matricula) => new()
    {
        NomeCompleto = nome,
        Matricula = matricula,
        Turma = "Turma 1",
        FotoJpeg = Convert.FromBase64String(JpegSintetico),
        CadastradoEmUtc = DataUtc(3, 1)
    };

    private static Emprestimo NovoEmprestimo(int notebookId, int alunoId, DateTime retiradoEmUtc) => new()
    {
        NotebookId = notebookId,
        AlunoId = alunoId,
        RetiradoEmUtc = retiradoEmUtc,
        FotoRetiradaJpeg = Convert.FromBase64String(JpegSintetico)
    };
}
