using System.Data.Common;
using System.Globalization;
using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class AtividadeNotebooksTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"atividade-notebooks-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task InativarEReativarPreservaCadastroHistoricoEFotos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var emprestimo = NovoEmprestimo(notebook.Id, aluno.Id);
        emprestimo.DevolvidoEmUtc = DateTime.UtcNow.AddMinutes(-1);
        emprestimo.FotoDevolucaoJpeg = Convert.FromBase64String(CriarFoto(5));
        emprestimo.FotoNotebookJpeg = Convert.FromBase64String(CriarFoto(6));
        emprestimo.ObservacaoDevolucao = "Equipamento conferido.";
        banco.Emprestimos.Add(emprestimo);
        banco.ManutencoesNotebook.Add(new ManutencaoNotebook
        {
            NotebookId = notebook.Id,
            AlteradoEmUtc = DateTime.UtcNow.AddMinutes(-3),
            EmManutencao = false
        });
        await banco.SaveChangesAsync(cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";
        var tokenAntesDeInativar = await ObterTokenAsync(cliente, retirada);

        using var inativacao = await EnviarFormularioAsync(cliente, "/", $"/notebooks/{notebook.Id}/inativar", []);
        Assert.Equal(HttpStatusCode.Redirect, inativacao.StatusCode);
        var inativo = await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento);
        Assert.False(inativo.Ativo);
        Assert.Equal(notebook.CadastradoEmUtc, inativo.CadastradoEmUtc);
        var inventario = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", cancelamento));
        var linha = LinhaDoNotebook(inventario, notebook.Patrimonio);
        Assert.Contains("Inativo", linha);
        Assert.Contains($"/notebooks/{notebook.Id}/reativar", linha);
        Assert.DoesNotContain(retirada, linha);
        Assert.Matches("""<dt>Inativos</dt>\s*<dd>1</dd>""", inventario);
        Assert.Matches("""<dt>Disponíveis</dt>\s*<dd>4</dd>""", inventario);

        using var paginaRetirada = await cliente.GetAsync(retirada, cancelamento);
        using var tentativaRetirada = await RetirarAsync(cliente, retirada, tokenAntesDeInativar, aluno.Id);
        Assert.Equal(HttpStatusCode.Conflict, paginaRetirada.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tentativaRetirada.StatusCode);
        Assert.Contains("inativo", await tentativaRetirada.Content.ReadAsStringAsync(cancelamento), StringComparison.OrdinalIgnoreCase);
        Assert.False(await banco.EmprestimosAbertos.AnyAsync(cancelamento));

        using var edicao = await EnviarFormularioAsync(cliente, $"/notebooks/{notebook.Id}/editar",
            $"/notebooks/{notebook.Id}/editar", new Dictionary<string, string>
            {
                ["Patrimonio"] = notebook.Patrimonio,
                ["Marca"] = notebook.Marca,
                ["Modelo"] = "Modelo atualizado",
                ["EmManutencao"] = "false",
                ["Ativo"] = "true"
            });
        Assert.Equal(HttpStatusCode.Redirect, edicao.StatusCode);
        var editado = await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento);
        Assert.Equal("Modelo atualizado", editado.Modelo);
        Assert.False(editado.Ativo);

        var historico = WebUtility.HtmlDecode(await cliente.GetStringAsync("/historico", cancelamento));
        Assert.Contains(notebook.Patrimonio, historico);
        Assert.Contains(emprestimo.ObservacaoDevolucao, historico);
        foreach (var (tipo, foto) in new[]
        {
            ("retirada", emprestimo.FotoRetiradaJpeg),
            ("notebook-retirada", emprestimo.FotoNotebookRetiradaJpeg!),
            ("devolucao", emprestimo.FotoDevolucaoJpeg),
            ("notebook", emprestimo.FotoNotebookJpeg)
        })
        {
            using var respostaFoto = await cliente.GetAsync($"/historico/{emprestimo.Id}/foto/{tipo}", cancelamento);
            Assert.Equal(HttpStatusCode.OK, respostaFoto.StatusCode);
            Assert.Equal(foto, await respostaFoto.Content.ReadAsByteArrayAsync(cancelamento));
        }

        using var reativacao = await EnviarFormularioAsync(cliente, "/", $"/notebooks/{notebook.Id}/reativar", []);
        Assert.Equal(HttpStatusCode.Redirect, reativacao.StatusCode);
        Assert.True((await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento)).Ativo);
        var reativado = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", cancelamento));
        Assert.Contains("Disponível", LinhaDoNotebook(reativado, notebook.Patrimonio));
        Assert.Contains(retirada, LinhaDoNotebook(reativado, notebook.Patrimonio));
        Assert.Matches("""<dt>Inativos</dt>\s*<dd>0</dd>""", reativado);
        Assert.Matches("""<dt>Disponíveis</dt>\s*<dd>5</dd>""", reativado);
        var preservado = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(emprestimo.DevolvidoEmUtc, preservado.DevolvidoEmUtc);
        Assert.Equal(emprestimo.ObservacaoDevolucao, preservado.ObservacaoDevolucao);
        Assert.Equal(emprestimo.FotoRetiradaJpeg, preservado.FotoRetiradaJpeg);
        Assert.Equal(emprestimo.FotoNotebookRetiradaJpeg, preservado.FotoNotebookRetiradaJpeg);
        Assert.Equal(emprestimo.FotoDevolucaoJpeg, preservado.FotoDevolucaoJpeg);
        Assert.Equal(emprestimo.FotoNotebookJpeg, preservado.FotoNotebookJpeg);
        Assert.Equal(1, await banco.ManutencoesNotebook.CountAsync(cancelamento));
        Assert.Equal(6, await banco.Notebooks.CountAsync(cancelamento));
    }

    [Fact]
    public async Task NotebookEmprestadoSoPodeSerInativadoDepoisDaDevolucao()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var emprestimo = NovoEmprestimo(notebook.Id, aluno.Id);
        banco.Emprestimos.Add(emprestimo);
        await banco.SaveChangesAsync(cancelamento);
        var inativar = $"/notebooks/{notebook.Id}/inativar";

        using var bloqueio = await EnviarFormularioAsync(cliente, "/", inativar, []);
        Assert.Equal(HttpStatusCode.Conflict, bloqueio.StatusCode);
        Assert.Contains("devol", await bloqueio.Content.ReadAsStringAsync(cancelamento), StringComparison.OrdinalIgnoreCase);
        Assert.True((await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento)).Ativo);
        Assert.True(await banco.EmprestimosAbertos.AnyAsync(item => item.Id == emprestimo.Id, cancelamento));

        var devolver = $"/emprestimos/{emprestimo.Id}/devolver";
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(await ObterTokenAsync(cliente, devolver)), CampoToken }
        };
        AnexarFoto(formulario, JpegSintetico);
        AnexarFoto(formulario, JpegSintetico, "FotoNotebook");
        using var devolucao = await cliente.PostAsync(devolver, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, devolucao.StatusCode);
        using var inativacao = await EnviarFormularioAsync(cliente, "/", inativar, []);
        Assert.Equal(HttpStatusCode.Redirect, inativacao.StatusCode);
        Assert.False((await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento)).Ativo);
    }

    [Fact]
    public async Task ReativacaoPreservaManutencaoENaoLiberaRetirada()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await BuscarNotebookAsync(banco, "NB-006", cancelamento);
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);

        using var inativacao = await EnviarFormularioAsync(cliente, "/", $"/notebooks/{notebook.Id}/inativar", []);
        Assert.Equal(HttpStatusCode.Redirect, inativacao.StatusCode);
        using var reativacao = await EnviarFormularioAsync(cliente, "/", $"/notebooks/{notebook.Id}/reativar", []);
        Assert.Equal(HttpStatusCode.Redirect, reativacao.StatusCode);
        var reativado = await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento);
        Assert.True(reativado.Ativo);
        Assert.True(reativado.EmManutencao);
        var inventario = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", cancelamento));
        var linha = LinhaDoNotebook(inventario, notebook.Patrimonio);
        var retirada = $"/notebooks/{notebook.Id}/retirar";
        Assert.Contains("Em manutenção", linha);
        Assert.DoesNotContain(retirada, linha);
        using var pagina = await cliente.GetAsync(retirada, cancelamento);
        using var tentativa = await RetirarAsync(cliente, retirada, await ObterTokenAsync(cliente, "/"), aluno.Id);
        Assert.Equal(HttpStatusCode.Conflict, pagina.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tentativa.StatusCode);
        Assert.False(await banco.Emprestimos.AnyAsync(cancelamento));
    }

    [Theory]
    [InlineData("inativar")]
    [InlineData("reativar")]
    public async Task AlterarAtividadeExigeLoginTokenEIdentificadorValido(string acao)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var rota = $"/notebooks/{notebook.Id}/{acao}";
        var ativoInicial = acao == "inativar";
        if (!ativoInicial)
        {
            await banco.Notebooks.Where(item => item.Id == notebook.Id)
                .ExecuteUpdateAsync(campos => campos.SetProperty(item => item.Ativo, false), cancelamento);
        }

        using var semLogin = await EnviarFormularioAsync(cliente, "/entrar", rota, []);
        AssertRedirecionaParaEntrar(cliente, semLogin);
        using var login = await EntrarAsync(cliente);
        using var formulario = new FormUrlEncodedContent([]);
        using var semToken = await cliente.PostAsync(rota, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, semToken.StatusCode);
        using var inexistente = await EnviarFormularioAsync(cliente, "/", $"/notebooks/2147483647/{acao}", []);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(ativoInicial, (await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento)).Ativo);
    }

    [Fact]
    public async Task RetiradaEInativacaoSimultaneasNaoDeixamNotebookInativoEmprestado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var encontro = new EncontroAntesDeAlterar();
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco, encontro);
        using var clienteRetirada = aplicacao.CriarCliente();
        using var clienteInativacao = aplicacao.CriarCliente();
        using var primeiroLogin = await EntrarAsync(clienteRetirada);
        using var segundoLogin = await EntrarAsync(clienteInativacao);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";
        var tokenRetirada = await ObterTokenAsync(clienteRetirada, retirada);
        var tokenInativacao = await ObterTokenAsync(clienteInativacao, "/");
        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [CampoToken] = tokenInativacao
        });

        var tentativaRetirada = RetirarAsync(clienteRetirada, retirada, tokenRetirada, aluno.Id);
        var tentativaInativacao = clienteInativacao.PostAsync($"/notebooks/{notebook.Id}/inativar", formulario, cancelamento);
        await Task.WhenAll(tentativaRetirada, tentativaInativacao);
        using var respostaRetirada = await tentativaRetirada;
        using var respostaInativacao = await tentativaInativacao;
        var respostas = new[] { respostaRetirada, respostaInativacao };
        Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, encontro.Tentativas);
        var estado = await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento);
        var emprestado = await banco.EmprestimosAbertos.AnyAsync(item => item.NotebookId == notebook.Id, cancelamento);
        Assert.Equal(estado.Ativo, emprestado);
        Assert.Equal(respostaRetirada.StatusCode == HttpStatusCode.Redirect, emprestado);
    }

    [Fact]
    public async Task MigracaoMantemNotebooksExistentesAtivos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var opcoes = new DbContextOptionsBuilder<EmprestimosContexto>()
            .UseSqlite($"Data Source={_caminhoDoBanco};Pooling=False")
            .Options;
        await using var banco = new EmprestimosContexto(opcoes);
        var migrador = banco.GetService<IMigrator>();
        await migrador.MigrateAsync("20261113073840_AdicionarFotoNotebookNaRetirada", cancelamento);
        await banco.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Notebooks" ("Patrimonio", "Marca", "Modelo", "EmManutencao")
            VALUES ('NB-ANTIGO', 'Marca antiga', 'Modelo antigo', 1)
            """, cancelamento);

        await migrador.MigrateAsync(cancellationToken: cancelamento);

        var notebook = await banco.Notebooks.AsNoTracking().SingleAsync(cancelamento);
        Assert.True(notebook.Ativo);
        Assert.True(notebook.EmManutencao);
        Assert.Equal("NB-ANTIGO", notebook.Patrimonio);
        Assert.Equal("Modelo antigo", notebook.Modelo);
        Assert.Empty(await banco.Database.GetPendingMigrationsAsync(cancelamento));
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

    private static Emprestimo NovoEmprestimo(int notebookId, int alunoId) => new()
    {
        NotebookId = notebookId,
        AlunoId = alunoId,
        RetiradoEmUtc = DateTime.UtcNow.AddMinutes(-2),
        FotoRetiradaJpeg = Convert.FromBase64String(CriarFoto(3)),
        FotoNotebookRetiradaJpeg = Convert.FromBase64String(CriarFoto(4))
    };

    private static async Task<HttpResponseMessage> RetirarAsync(HttpClient cliente, string destino, string token, int alunoId)
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(token), CampoToken },
            { new StringContent(alunoId.ToString(CultureInfo.InvariantCulture)), "AlunoId" }
        };
        AnexarFoto(formulario, JpegSintetico);
        AnexarFoto(formulario, JpegSintetico, "FotoNotebook");
        return await cliente.PostAsync(destino, formulario, TestContext.Current.CancellationToken);
    }

    private class EncontroAntesDeAlterar : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _prontos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tentativas;

        public int Tentativas => Volatile.Read(ref _tentativas);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText.TrimStart();
            if (sql.StartsWith("INSERT INTO \"Emprestimos\"", StringComparison.OrdinalIgnoreCase)
                || sql.StartsWith("UPDATE \"Notebooks\"", StringComparison.OrdinalIgnoreCase) && sql.Contains("\"Ativo\""))
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
