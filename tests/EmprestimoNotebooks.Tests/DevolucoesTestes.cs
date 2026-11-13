using System.Globalization;
using System.Net;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

public class DevolucoesTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"devolucoes-testes-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DevolucaoGuardaAsDuasFotosEDeixaNotebookDisponivel(bool preencherObservacao)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var notebook = await banco.Notebooks.SingleAsync(item => item.Patrimonio == "NB-001", cancelamento);
        var emprestimo = NovoEmprestimo(notebook.Id, aluno.Id, DateTime.UtcNow.AddMinutes(-10));
        banco.Emprestimos.Add(emprestimo);
        await banco.SaveChangesAsync(cancelamento);

        var devolucao = $"/emprestimos/{emprestimo.Id}/devolver";
        var rotaFoto = $"/historico/{emprestimo.Id}/foto/devolucao";
        var rotaVisualizacao = $"/historico/{emprestimo.Id}/ver-foto/devolucao";
        using var semLogin = await cliente.GetAsync(devolucao, cancelamento);
        AssertRedirecionaParaEntrar(cliente, semLogin);
        using var fotoSemLogin = await cliente.GetAsync(rotaFoto, cancelamento);
        AssertRedirecionaParaEntrar(cliente, fotoSemLogin);
        using var visualizacaoSemLogin = await cliente.GetAsync(rotaVisualizacao, cancelamento);
        AssertRedirecionaParaEntrar(cliente, visualizacaoSemLogin);
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await ObterTokenAsync(cliente, devolucao);
        var inicio = DateTime.UtcNow;
        const string observacao = "Carregador entregue.";

        using var resposta = await DevolverAsync(cliente, devolucao, token, CriarFoto(3), CriarFoto(4),
            preencherObservacao ? $"  {observacao}  " : null);
        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        Assert.Equal("/", resposta.Headers.Location?.OriginalString);

        var registro = await banco.Emprestimos.AsNoTracking().SingleAsync(cancelamento);
        Assert.NotNull(registro.DevolvidoEmUtc);
        Assert.InRange(registro.DevolvidoEmUtc.Value, inicio, DateTime.UtcNow);
        Assert.Equal(emprestimo.FotoRetiradaJpeg, registro.FotoRetiradaJpeg);
        Assert.Equal(preencherObservacao ? observacao : null, registro.ObservacaoDevolucao);
        await ConferirFotoAsync(cliente, rotaFoto, registro.FotoDevolucaoJpeg, 3, cancelamento);
        var rotaFotoNotebook = $"/historico/{emprestimo.Id}/foto/notebook";
        await ConferirFotoAsync(cliente, rotaFotoNotebook, registro.FotoNotebookJpeg, 4, cancelamento);

        var inventario = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", cancelamento));
        var linha = Assert.Single(inventario.Split("</tr>"), parte => parte.Contains(">NB-001<"));
        Assert.Contains("Disponível", linha);
        Assert.DoesNotContain(devolucao, linha);

        var retirada = $"/notebooks/{notebook.Id}/retirar";
        using var novaRetirada = new MultipartFormDataContent
        {
            { new StringContent(await ObterTokenAsync(cliente, retirada)), CampoToken },
            { new StringContent(aluno.Id.ToString(CultureInfo.InvariantCulture)), "AlunoId" }
        };
        AnexarFoto(novaRetirada, JpegSintetico);
        AnexarFoto(novaRetirada, JpegSintetico, "FotoNotebook");
        using var novaResposta = await cliente.PostAsync(retirada, novaRetirada, cancelamento);
        Assert.Equal(HttpStatusCode.Redirect, novaResposta.StatusCode);

        using var devolucaoRepetida = await DevolverAsync(cliente, devolucao, token, CriarFoto(5), CriarFoto(6));
        Assert.Equal(HttpStatusCode.Conflict, devolucaoRepetida.StatusCode);
        var aberto = await banco.EmprestimosAbertos.AsNoTracking().SingleAsync(cancelamento);
        Assert.NotEqual(emprestimo.Id, aberto.Id);
        Assert.Equal(notebook.Id, aberto.NotebookId);
        var devolvido = await banco.Emprestimos.AsNoTracking().SingleAsync(item => item.Id == emprestimo.Id, cancelamento);
        Assert.Equal(registro.FotoDevolucaoJpeg, devolvido.FotoDevolucaoJpeg);
        Assert.Equal(registro.FotoNotebookJpeg, devolvido.FotoNotebookJpeg);
    }

    [Theory]
    [InlineData(null, JpegSintetico, false, "Foto")]
    [InlineData("dGV4dG8=", JpegSintetico, false, "Foto")]
    [InlineData(JpegSintetico, null, false, "FotoNotebook")]
    [InlineData(JpegSintetico, "dGV4dG8=", false, "FotoNotebook")]
    [InlineData(JpegSintetico, JpegSintetico, true, "Observacao")]
    public async Task DadosInvalidosMantemEmprestimoAberto(string? foto, string? fotoNotebook, bool observacaoLonga, string campo)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var notebookId = await banco.Notebooks.Where(item => item.Patrimonio == "NB-001").Select(item => item.Id).SingleAsync(cancelamento);
        var emprestimo = NovoEmprestimo(notebookId, aluno.Id, DateTime.UtcNow.AddMinutes(-10));
        banco.Emprestimos.Add(emprestimo);
        await banco.SaveChangesAsync(cancelamento);

        var devolucao = $"/emprestimos/{emprestimo.Id}/devolver";
        var token = await ObterTokenAsync(cliente, devolucao);
        using var resposta = await DevolverAsync(cliente, devolucao, token, foto, fotoNotebook,
            observacaoLonga ? new string('x', 501) : null);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var html = await resposta.Content.ReadAsStringAsync(cancelamento);
        Assert.Matches($$"""data-valmsg-for="{{campo}}"[^>]*>\s*[^\s<]""", html);
        Assert.True(await banco.EmprestimosAbertos.AnyAsync(item => item.Id == emprestimo.Id, cancelamento));
    }

    [Fact]
    public async Task DevolucaoExigeTokenEIdentificadorValido()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var notebookId = await banco.Notebooks.Where(item => item.Patrimonio == "NB-001").Select(item => item.Id).SingleAsync(cancelamento);
        var emprestimo = NovoEmprestimo(notebookId, aluno.Id, DateTime.UtcNow.AddMinutes(-10));
        banco.Emprestimos.Add(emprestimo);
        await banco.SaveChangesAsync(cancelamento);

        var devolucao = $"/emprestimos/{emprestimo.Id}/devolver";
        using var formularioSemToken = new MultipartFormDataContent();
        AnexarFoto(formularioSemToken, JpegSintetico);
        AnexarFoto(formularioSemToken, JpegSintetico, "FotoNotebook");
        using var semToken = await cliente.PostAsync(devolucao, formularioSemToken, cancelamento);
        Assert.Equal(HttpStatusCode.BadRequest, semToken.StatusCode);

        var token = await ObterTokenAsync(cliente, devolucao);
        var devolucaoInexistente = $"/emprestimos/{emprestimo.Id + 1}/devolver";
        using var paginaInexistente = await cliente.GetAsync(devolucaoInexistente, cancelamento);
        using var registroInexistente = await DevolverAsync(cliente, devolucaoInexistente, token, JpegSintetico, JpegSintetico);
        Assert.Equal(HttpStatusCode.NotFound, paginaInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, registroInexistente.StatusCode);
        Assert.True(await banco.EmprestimosAbertos.AnyAsync(item => item.Id == emprestimo.Id, cancelamento));
    }

    [Fact]
    public async Task HistoricoUsaMesLocalEIncluiEmprestimosAbertosDeOutrosMeses()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var semLogin = await cliente.GetAsync("/historico", cancelamento);
        AssertRedirecionaParaEntrar(cliente, semLogin);
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await CadastrarAlunoAsync(banco, cancelamento);
        var notebooks = await banco.Notebooks.OrderBy(item => item.Patrimonio).Take(3).Select(item => item.Id).ToListAsync(cancelamento);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var hoje = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, fuso);
        var mesAtual = new DateTime(hoje.Year, hoje.Month, 1);
        var limite = TimeZoneInfo.ConvertTimeToUtc(mesAtual, fuso);

        var devolvidoNoMes = NovoEmprestimo(notebooks[0], aluno.Id, limite.AddMinutes(-1));
        devolvidoNoMes.DevolvidoEmUtc = limite;
        devolvidoNoMes.FotoDevolucaoJpeg = Convert.FromBase64String(JpegSintetico);
        devolvidoNoMes.ObservacaoDevolucao = "Equipamento conferido.";
        var abertoNoMesAnterior = NovoEmprestimo(notebooks[1], aluno.Id, limite.AddDays(-2));
        var retiradoNoMes = NovoEmprestimo(notebooks[2], aluno.Id, limite.AddMinutes(1));
        banco.Emprestimos.AddRange(devolvidoNoMes, abertoNoMesAnterior, retiradoNoMes);
        await banco.SaveChangesAsync(cancelamento);

        var historico = await cliente.GetStringAsync("/historico", cancelamento);
        var mesAnterior = mesAtual.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var anterior = await cliente.GetStringAsync($"/historico?mes={mesAnterior}", cancelamento);
        var invalido = await cliente.GetStringAsync("/historico?mes=invalido", cancelamento);
        var mesComHorarioDeVerao = await cliente.GetStringAsync("/historico?mes=1994-11", cancelamento);
        var fotoDevolucao = $"/historico/{devolvidoNoMes.Id}/ver-foto/devolucao";
        var fotoRetirada = $"/historico/{devolvidoNoMes.Id}/ver-foto/retirada";
        var linkDevolver = $"/emprestimos/{abertoNoMesAnterior.Id}/devolver";

        Assert.Contains(fotoDevolucao, historico);
        Assert.Contains("Equipamento conferido.", historico);
        Assert.DoesNotContain(fotoRetirada, historico);
        Assert.Contains($"/historico/{retiradoNoMes.Id}/ver-foto/retirada", historico);
        Assert.Contains(linkDevolver, historico);
        Assert.Contains(fotoRetirada, anterior);
        Assert.DoesNotContain(fotoDevolucao, anterior);
        Assert.Contains(linkDevolver, anterior);
        Assert.Contains(fotoDevolucao, invalido);
        Assert.DoesNotContain(fotoRetirada, invalido);
        Assert.Matches("""<input\b(?=[^>]*\bname="mes")(?=[^>]*\bvalue="1994-11")[^>]*>""", mesComHorarioDeVerao);

        var visualizacao = await cliente.GetStringAsync($"{fotoRetirada}?mes={mesAnterior}", cancelamento);
        Assert.Contains($"src=\"/historico/{devolvidoNoMes.Id}/foto/retirada\"", visualizacao);
        Assert.Matches($$"""<a\b[^>]*\bhref="/historico\?mes={{mesAnterior}}"[^>]*>\s*Voltar\s*</a>""", visualizacao);
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static async Task<Aluno> CadastrarAlunoAsync(EmprestimosContexto banco, CancellationToken cancelamento)
    {
        var aluno = new Aluno
        {
            NomeCompleto = "Ana Exemplo",
            Matricula = "AL-001",
            Turma = "Turma 1",
            FotoJpeg = Convert.FromBase64String(JpegSintetico)
        };
        banco.Alunos.Add(aluno);
        await banco.SaveChangesAsync(cancelamento);
        return aluno;
    }

    private static Emprestimo NovoEmprestimo(int notebookId, int alunoId, DateTime retiradoEmUtc) => new()
    {
        NotebookId = notebookId,
        AlunoId = alunoId,
        RetiradoEmUtc = retiradoEmUtc,
        FotoRetiradaJpeg = Convert.FromBase64String(JpegSintetico)
    };

    private static async Task<HttpResponseMessage> DevolverAsync(HttpClient cliente, string rota, string token,
        string? foto, string? fotoNotebook, string? observacao = null)
    {
        using var formulario = new MultipartFormDataContent { { new StringContent(token), CampoToken } };
        AnexarFoto(formulario, foto);
        AnexarFoto(formulario, fotoNotebook, "FotoNotebook");
        if (observacao is not null)
        {
            formulario.Add(new StringContent(observacao), "Observacao");
        }

        return await cliente.PostAsync(rota, formulario, TestContext.Current.CancellationToken);
    }

    private static async Task ConferirFotoAsync(HttpClient cliente, string rota, byte[]? esperada, int largura,
        CancellationToken cancelamento)
    {
        using var resposta = await cliente.GetAsync(rota, cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("image/jpeg", resposta.Content.Headers.ContentType?.MediaType);
        Assert.True(resposta.Headers.CacheControl?.NoStore);
        var bytes = await resposta.Content.ReadAsByteArrayAsync(cancelamento);
        Assert.Equal(esperada, bytes);
        using var foto = SKBitmap.Decode(bytes);
        Assert.NotNull(foto);
        Assert.Equal(largura, foto.Width);
    }
}
