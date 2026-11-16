using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class ExclusaoNotebooksTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"exclusao-notebooks-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ConfirmacaoExcluiNotebookSemMovimentacoes()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var id = await ObterNotebookAsync(banco, cancelamento);
        var rota = $"/notebooks/{id}/excluir";
        Assert.Contains(rota, await cliente.GetStringAsync("/", cancelamento));
        var token = await ObterTokenAsync(cliente, rota);
        Assert.True(await banco.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento));

        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string> { [CampoToken] = token });
        using var exclusao = await cliente.PostAsync(rota, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, exclusao.StatusCode);
        Assert.Equal("/", exclusao.Headers.Location?.OriginalString);
        Assert.False(await banco.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento));

        using var consultaInexistente = await cliente.GetAsync(rota, cancelamento);
        using var exclusaoInexistente = await cliente.PostAsync(rota, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.NotFound, consultaInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, exclusaoInexistente.StatusCode);
    }

    [Theory]
    [InlineData("retirada")]
    [InlineData("devolução")]
    [InlineData("manutenção")]
    public async Task MovimentacaoCriadaDepoisDaConfirmacaoImpedeExclusao(string tipo)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var id = await ObterNotebookAsync(banco, cancelamento);
        var rota = $"/notebooks/{id}/excluir";
        var token = await ObterTokenAsync(cliente, rota);

        if (tipo == "manutenção")
        {
            banco.ManutencoesNotebook.Add(new ManutencaoNotebook
            {
                NotebookId = id,
                AlteradoEmUtc = DateTime.UtcNow,
                EmManutencao = false
            });
        }
        else
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
            banco.Emprestimos.Add(new Emprestimo
            {
                NotebookId = id,
                AlunoId = aluno.Id,
                RetiradoEmUtc = DateTime.UtcNow.AddMinutes(-1),
                DevolvidoEmUtc = tipo == "devolução" ? DateTime.UtcNow : null,
                FotoRetiradaJpeg = aluno.FotoJpeg
            });
        }

        await banco.SaveChangesAsync(cancelamento);
        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string> { [CampoToken] = token });
        using var exclusao = await cliente.PostAsync(rota, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.Conflict, exclusao.StatusCode);
        Assert.True(await banco.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento));
        Assert.Equal(tipo == "manutenção", await banco.ManutencoesNotebook.AnyAsync(item => item.NotebookId == id, cancelamento));
        Assert.Equal(tipo != "manutenção", await banco.Emprestimos.AnyAsync(item => item.NotebookId == id, cancelamento));

        using var consulta = await cliente.GetAsync(rota, cancelamento);
        Assert.Equal(HttpStatusCode.Conflict, consulta.StatusCode);
        var html = WebUtility.HtmlDecode(await consulta.Content.ReadAsStringAsync(cancelamento));
        Assert.Contains("Este notebook tem empréstimos ou registros de manutenção.", html);
        Assert.DoesNotContain("Confirmar exclusão", html);
    }

    [Fact]
    public async Task ExclusaoExigeLoginETokenAntiforgery()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var id = await ObterNotebookAsync(banco, cancelamento);
        var rota = $"/notebooks/{id}/excluir";

        using var consultaSemLogin = await cliente.GetAsync(rota, cancelamento);
        AssertRedirecionaParaEntrar(cliente, consultaSemLogin);
        using var exclusaoSemLogin = await EnviarFormularioAsync(cliente, "/entrar", rota, []);
        AssertRedirecionaParaEntrar(cliente, exclusaoSemLogin);

        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var formulario = new FormUrlEncodedContent([]);
        using var exclusaoSemToken = await cliente.PostAsync(rota, formulario, cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, exclusaoSemToken.StatusCode);
        Assert.True(await banco.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento));
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static Task<int> ObterNotebookAsync(EmprestimosContexto banco, CancellationToken cancelamento) =>
        banco.Notebooks.Where(notebook => notebook.Patrimonio == "NB-001")
            .Select(notebook => notebook.Id)
            .SingleAsync(cancelamento);
}
