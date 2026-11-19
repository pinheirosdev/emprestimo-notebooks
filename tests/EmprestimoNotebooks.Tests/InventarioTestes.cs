using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;

namespace EmprestimoNotebooks.Tests;

public class InventarioTestes : IDisposable
{
    private const string Cadastro = "/notebooks/cadastrar";

    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"notebooks-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task BancoNovoIniciaSemCadastrosOuMovimentacoes()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco, cadastrarNotebooks: false);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();

        Assert.NotEmpty(await banco.Database.GetAppliedMigrationsAsync(cancelamento));
        Assert.Empty(await banco.Database.GetPendingMigrationsAsync(cancelamento));
        Assert.False(await banco.Notebooks.AnyAsync(cancelamento));
        Assert.False(await banco.Alunos.AnyAsync(cancelamento));
        Assert.False(await banco.Emprestimos.AnyAsync(cancelamento));
        Assert.False(await banco.ManutencoesNotebook.AnyAsync(cancelamento));
        var inventario = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", cancelamento));
        Assert.Contains("Nenhum notebook cadastrado.", inventario);
        Assert.Contains("Cadastrar notebook", inventario);
    }

    [Fact]
    public async Task PaginaInicialListaNotebooksDisponiveisEEmManutencao()
    {
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var html = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", TestContext.Current.CancellationToken));
        var linhas = html.Split("</tr>");

        var notebookDisponivel = Assert.Single(linhas, linha => linha.Contains(">NB-001<"));
        var notebookEmManutencao = Assert.Single(linhas, linha => linha.Contains(">NB-006<"));
        Assert.Contains("Disponível", notebookDisponivel);
        Assert.Contains("Em manutenção", notebookEmManutencao);
    }

    [Fact]
    public async Task ReinicioPreservaCadastrosEdicoesEExclusoes()
    {
        var cancelamento = TestContext.Current.CancellationToken;

        await using (var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco, cadastrarNotebooks: false))
        {
            using var cliente = aplicacao.CriarCliente();
            using var login = await EntrarAsync(cliente);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            foreach (var patrimonio in new[] { "NOVO-001", "NOVO-002" })
            {
                using var cadastro = await EnviarFormularioAsync(cliente, Cadastro, Cadastro, CamposDoNotebook(patrimonio));
                Assert.Equal(HttpStatusCode.Redirect, cadastro.StatusCode);
            }

            using var escopo = aplicacao.Services.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
            var notebooks = await banco.Notebooks.OrderBy(notebook => notebook.Patrimonio).ToListAsync(cancelamento);

            var campos = CamposDoNotebook(notebooks[0].Patrimonio);
            campos["Modelo"] = "Modelo atualizado";
            var edicao = $"/notebooks/{notebooks[0].Id}/editar";
            using var alteracao = await EnviarFormularioAsync(cliente, edicao, edicao, campos);
            using var exclusao = await EnviarFormularioAsync(cliente, "/", $"/notebooks/{notebooks[1].Id}/excluir", []);
            Assert.Equal(HttpStatusCode.Redirect, alteracao.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, exclusao.StatusCode);
        }

        await using var aplicacaoReiniciada = new AplicacaoDeTeste(_caminhoDoBanco, cadastrarNotebooks: false);
        using var escopoReiniciado = aplicacaoReiniciada.Services.CreateScope();
        var bancoReiniciado = escopoReiniciado.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebooksPersistidos = await bancoReiniciado.Notebooks.ToListAsync(cancelamento);

        Assert.Empty(await bancoReiniciado.Database.GetPendingMigrationsAsync(cancelamento));
        var persistido = Assert.Single(notebooksPersistidos);
        Assert.Equal("NOVO-001", persistido.Patrimonio);
        Assert.Equal("Modelo atualizado", persistido.Modelo);
    }

    [Fact]
    public async Task RemoverControleDeCargaPreservaCadastrosHistoricoEFotos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var opcoes = new DbContextOptionsBuilder<EmprestimosContexto>()
            .UseSqlite($"Data Source={_caminhoDoBanco};Pooling=False").Options;
        await using var banco = new EmprestimosContexto(opcoes);
        var migrador = banco.GetService<IMigrator>();
        await migrador.MigrateAsync("20261119044906_AdicionarAtividadeDoNotebook", cancelamento);
        await banco.Database.ExecuteSqlRawAsync("INSERT INTO ControleCarga (Id) VALUES ('demonstracao')", cancelamento);

        var foto = Convert.FromBase64String(FotosDeTeste.JpegSintetico);
        var notebook = new Notebook { Patrimonio = "LEGADO-001", Marca = "Dell", Modelo = "Latitude", Ativo = false };
        var aluno = new Aluno { NomeCompleto = "Ana Lima", Matricula = "LEGADO-001", Turma = "Turma 1", FotoJpeg = foto };
        banco.AddRange(notebook, aluno);
        await banco.SaveChangesAsync(cancelamento);
        banco.Emprestimos.Add(new Emprestimo
        {
            NotebookId = notebook.Id,
            AlunoId = aluno.Id,
            RetiradoEmUtc = DateTime.UtcNow.AddMinutes(-2),
            DevolvidoEmUtc = DateTime.UtcNow.AddMinutes(-1),
            FotoRetiradaJpeg = foto,
            FotoDevolucaoJpeg = foto
        });
        banco.ManutencoesNotebook.Add(new ManutencaoNotebook
        {
            NotebookId = notebook.Id,
            AlteradoEmUtc = DateTime.UtcNow,
            EmManutencao = true
        });
        await banco.SaveChangesAsync(cancelamento);

        await migrador.MigrateAsync(cancellationToken: cancelamento);

        var notebookPreservado = await banco.Notebooks.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal("LEGADO-001", notebookPreservado.Patrimonio);
        Assert.False(notebookPreservado.Ativo);
        Assert.Equal(foto, (await banco.Alunos.AsNoTracking().SingleAsync(cancelamento)).FotoJpeg);
        var emprestimo = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(foto, emprestimo.FotoRetiradaJpeg);
        Assert.Equal(foto, emprestimo.FotoDevolucaoJpeg);
        Assert.True((await banco.ManutencoesNotebook.AsNoTracking().SingleAsync(cancelamento)).EmManutencao);
        Assert.Empty(await banco.Database.GetPendingMigrationsAsync(cancelamento));
        var tabelasDeCarga = await banco.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'ControleCarga'").SingleAsync(cancelamento);
        Assert.Equal(0, tabelasDeCarga);
    }

    [Fact]
    public async Task SemLoginInventarioEFormulariosRedirecionamParaEntrar()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await banco.Notebooks.AsNoTracking().SingleAsync(item => item.Patrimonio == "NB-001", cancelamento);
        var edicao = $"/notebooks/{notebook.Id}/editar";

        foreach (var pagina in new[] { "/", Cadastro, edicao })
        {
            using var resposta = await cliente.GetAsync(pagina, cancelamento);
            AssertRedirecionaParaEntrar(cliente, resposta);
        }

        using var cadastro = await EnviarFormularioAsync(cliente, "/entrar", Cadastro, CamposDoNotebook("NB-007"));
        AssertRedirecionaParaEntrar(cliente, cadastro);

        using var alteracao = await EnviarFormularioAsync(cliente, "/entrar", edicao, CamposDoNotebook(notebook.Patrimonio));
        AssertRedirecionaParaEntrar(cliente, alteracao);
        Assert.Equal(6, await banco.Notebooks.CountAsync(cancelamento));
        var modelo = await banco.Notebooks.Where(item => item.Id == notebook.Id).Select(item => item.Modelo).SingleAsync(cancelamento);
        Assert.Equal(notebook.Modelo, modelo);
    }

    [Fact]
    public async Task SenhaIncorretaNaoAbreSessao()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();

        using var login = await EntrarAsync(cliente, senha: "senha incorreta");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var html = WebUtility.HtmlDecode(await login.Content.ReadAsStringAsync(cancelamento));
        Assert.Contains("Usuário ou senha inválidos.", html);

        using var inventario = await cliente.GetAsync("/", cancelamento);
        AssertRedirecionaParaEntrar(cliente, inventario);
    }

    [Theory]
    [InlineData(Cadastro, Cadastro)]
    [InlineData("https://example.invalid/", "/")]
    public async Task LoginRedirecionaParaPaginaLocalESairEncerraSessao(string retorno, string destino)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();

        using var login = await EntrarAsync(cliente, retorno: retorno);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(destino, login.Headers.Location?.OriginalString);

        using var inventario = await cliente.GetAsync("/", cancelamento);
        Assert.Equal(HttpStatusCode.OK, inventario.StatusCode);

        using var logout = await EnviarFormularioAsync(cliente, "/", "/sair", []);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        using var aposSair = await cliente.GetAsync("/", cancelamento);
        AssertRedirecionaParaEntrar(cliente, aposSair);
    }

    [Fact]
    public async Task CadastroEEdicaoGravamNotebookNoSqlite()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var cadastro = await EnviarFormularioAsync(cliente, Cadastro, Cadastro, CamposDoNotebook("NB-007"));
        Assert.Equal(HttpStatusCode.Redirect, cadastro.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebook = await banco.Notebooks.AsNoTracking().SingleAsync(item => item.Patrimonio == "NB-007", cancelamento);
        Assert.Equal("Dell", notebook.Marca);
        Assert.Equal("Latitude 3440", notebook.Modelo);
        Assert.False(notebook.EmManutencao);

        var edicao = $"/notebooks/{notebook.Id}/editar";
        var campos = CamposDoNotebook(notebook.Patrimonio);
        campos["Modelo"] = "Latitude 3450";
        campos["EmManutencao"] = "true";
        campos["Id"] = (await banco.Notebooks.MinAsync(item => item.Id, cancelamento)).ToString();
        using var alteracao = await EnviarFormularioAsync(cliente, edicao, edicao, campos);
        Assert.Equal(HttpStatusCode.Redirect, alteracao.StatusCode);

        var atualizado = await banco.Notebooks.AsNoTracking().SingleAsync(item => item.Id == notebook.Id, cancelamento);
        Assert.Equal("NB-007", atualizado.Patrimonio);
        Assert.Equal("Latitude 3450", atualizado.Modelo);
        Assert.True(atualizado.EmManutencao);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatrimonioDuplicadoExibeErroEPreservaInventario(bool editar)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var id = await banco.Notebooks.Where(item => item.Patrimonio == "NB-002").Select(item => item.Id).SingleAsync(cancelamento);
        var pagina = editar ? $"/notebooks/{id}/editar" : Cadastro;

        using var resposta = await EnviarFormularioAsync(cliente, pagina, pagina, CamposDoNotebook("nb-001"));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var html = await resposta.Content.ReadAsStringAsync(cancelamento);
        Assert.Matches("""data-valmsg-for="Patrimonio"[^>]*>\s*[^\s<]""", html);
        Assert.Equal(6, await banco.Notebooks.CountAsync(cancelamento));
        Assert.Equal("NB-002", await banco.Notebooks.Where(item => item.Id == id).Select(item => item.Patrimonio).SingleAsync(cancelamento));
    }

    [Fact]
    public async Task CadastroSemTokenAntiforgeryNaoGravaNotebook()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var conteudo = new FormUrlEncodedContent(CamposDoNotebook("NB-007"));
        using var resposta = await cliente.PostAsync(Cadastro, conteudo, cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        Assert.False(await banco.Notebooks.AnyAsync(item => item.Patrimonio == "NB-007", cancelamento));
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static Dictionary<string, string> CamposDoNotebook(string patrimonio) => new()
    {
        ["Patrimonio"] = patrimonio,
        ["Marca"] = "Dell",
        ["Modelo"] = "Latitude 3440",
        ["EmManutencao"] = "false"
    };
}
