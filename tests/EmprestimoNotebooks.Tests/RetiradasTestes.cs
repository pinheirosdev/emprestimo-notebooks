using System.Data.Common;
using System.Globalization;
using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class RetiradasTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"retiradas-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RetiradasSimultaneasDoMesmoNotebookGravamApenasUmEmprestimo()
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
        var primeiroAluno = await CadastrarAlunoAsync(banco, "Ana Conceição", "AL-001", cancelamento);
        var segundoAluno = await CadastrarAlunoAsync(banco, "Bruno Oliveira", "AL-002", cancelamento);
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";
        var primeiroToken = await ObterTokenAsync(primeiroCliente, retirada);
        var segundoToken = await ObterTokenAsync(segundoCliente, retirada);

        var antesDaRetirada = DateTime.UtcNow;
        var primeiraTentativa = RetirarAsync(primeiroCliente, retirada, primeiroToken, primeiroAluno.Id, CriarFoto(3), CriarFoto(5));
        var segundaTentativa = RetirarAsync(segundoCliente, retirada, segundoToken, segundoAluno.Id, CriarFoto(4), CriarFoto(6));
        await Task.WhenAll(primeiraTentativa, segundaTentativa);
        using var primeiraResposta = await primeiraTentativa;
        using var segundaResposta = await segundaTentativa;

        var respostas = new[] { primeiraResposta, segundaResposta };
        var sucesso = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Redirect);
        Assert.Equal("/", sucesso.Headers.Location?.OriginalString);
        var conflito = Assert.Single(respostas, resposta => resposta.StatusCode == HttpStatusCode.Conflict);
        var aviso = WebUtility.HtmlDecode(await conflito.Content.ReadAsStringAsync(cancelamento));
        Assert.Contains("Este notebook já está emprestado.", aviso);
        Assert.Equal(2, encontro.Tentativas);

        var emprestimo = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal(notebook.Id, emprestimo.NotebookId);
        Assert.Equal(sucesso == primeiraResposta ? primeiroAluno.Id : segundoAluno.Id, emprestimo.AlunoId);
        Assert.Null(emprestimo.DevolvidoEmUtc);
        Assert.InRange(emprestimo.RetiradoEmUtc, antesDaRetirada.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        using var foto = SKBitmap.Decode(emprestimo.FotoRetiradaJpeg);
        Assert.NotNull(foto);
        Assert.Equal(sucesso == primeiraResposta ? 3 : 4, foto.Width);
        Assert.NotNull(emprestimo.FotoNotebookRetiradaJpeg);
        using var fotoNotebook = SKBitmap.Decode(emprestimo.FotoNotebookRetiradaJpeg);
        Assert.NotNull(fotoNotebook);
        Assert.Equal(sucesso == primeiraResposta ? 5 : 6, fotoNotebook.Width);

        var inventario = WebUtility.HtmlDecode(await primeiroCliente.GetStringAsync("/", cancelamento));
        var linha = Assert.Single(inventario.Split("</tr>"), parte => parte.Contains(">NB-001<"));
        Assert.Contains("Emprestado", linha);
        Assert.DoesNotContain(retirada, linha);

        var edicao = $"/notebooks/{notebook.Id}/editar";
        using var alteracao = await EnviarFormularioAsync(primeiroCliente, edicao, edicao, new Dictionary<string, string>
        {
            ["Patrimonio"] = notebook.Patrimonio,
            ["Marca"] = notebook.Marca,
            ["Modelo"] = notebook.Modelo,
            ["EmManutencao"] = "true"
        });
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);
        Assert.Contains("Devolva o notebook antes de marcá-lo em manutenção.",
            WebUtility.HtmlDecode(await alteracao.Content.ReadAsStringAsync(cancelamento)));
        Assert.False((await BuscarNotebookAsync(banco, notebook.Patrimonio, cancelamento)).EmManutencao);
    }

    [Fact]
    public async Task NotebookEmManutencaoNaoPodeSerRetirado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, "Ana Lima", "AL-001", cancelamento);
        var notebook = await BuscarNotebookAsync(banco, "NB-006", cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";

        using var formulario = await cliente.GetAsync(retirada, cancelamento);
        Assert.Equal(HttpStatusCode.Conflict, formulario.StatusCode);
        var token = await ObterTokenAsync(cliente, "/");
        using var resposta = await RetirarAsync(cliente, retirada, token, aluno.Id, JpegSintetico, JpegSintetico);
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.False(await banco.Emprestimos.AnyAsync(cancelamento));
    }

    [Theory]
    [InlineData(false, JpegSintetico, JpegSintetico, "AlunoId")]
    [InlineData(true, null, JpegSintetico, "Foto")]
    [InlineData(true, "dGV4dG8=", JpegSintetico, "Foto")]
    [InlineData(true, JpegSintetico, null, "FotoNotebook")]
    [InlineData(true, JpegSintetico, "dGV4dG8=", "FotoNotebook")]
    public async Task RetiradaComDadosInvalidosExibeErroENaoGrava(bool alunoValido, string? foto, string? fotoNotebook, string campo)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, "Ana Lima", "AL-001", cancelamento);
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";
        var token = await ObterTokenAsync(cliente, retirada);

        using var resposta = await RetirarAsync(cliente, retirada, token, alunoValido ? aluno.Id : aluno.Id + 1, foto, fotoNotebook);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var html = await resposta.Content.ReadAsStringAsync(cancelamento);
        Assert.Matches($$"""data-valmsg-for="{{campo}}"[^>]*>\s*[^\s<]""", html);
        Assert.False(await banco.Emprestimos.AnyAsync(cancelamento));
    }

    [Fact]
    public async Task SemLoginRetiradaRedirecionaParaEntrar()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, "Ana Lima", "AL-001", cancelamento);
        var notebook = await BuscarNotebookAsync(banco, "NB-001", cancelamento);
        var retirada = $"/notebooks/{notebook.Id}/retirar";

        using var formulario = await cliente.GetAsync(retirada, cancelamento);
        AssertRedirecionaParaEntrar(cliente, formulario);
        var token = await ObterTokenAsync(cliente, "/entrar");
        using var resposta = await RetirarAsync(cliente, retirada, token, aluno.Id, JpegSintetico, JpegSintetico);
        AssertRedirecionaParaEntrar(cliente, resposta);
        Assert.False(await banco.Emprestimos.AnyAsync(cancelamento));
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static async Task<Aluno> CadastrarAlunoAsync(
        EmprestimosContexto banco, string nome, string matricula, CancellationToken cancelamento)
    {
        var aluno = new Aluno
        {
            NomeCompleto = nome,
            Matricula = matricula,
            Turma = "Turma 1",
            FotoJpeg = Convert.FromBase64String(JpegSintetico)
        };
        banco.Alunos.Add(aluno);
        await banco.SaveChangesAsync(cancelamento);
        return aluno;
    }

    private static Task<Notebook> BuscarNotebookAsync(
        EmprestimosContexto banco, string patrimonio, CancellationToken cancelamento) =>
        banco.Notebooks.AsNoTracking().SingleAsync(notebook => notebook.Patrimonio == patrimonio, cancelamento);

    private static async Task<HttpResponseMessage> RetirarAsync(
        HttpClient cliente, string destino, string token, int alunoId, string? foto, string? fotoNotebook)
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(token), CampoToken },
            { new StringContent(alunoId.ToString(CultureInfo.InvariantCulture)), "AlunoId" }
        };
        AnexarFoto(formulario, foto);
        AnexarFoto(formulario, fotoNotebook, "FotoNotebook");
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
