using System.Net;
using System.Net.Http.Headers;
using EmprestimoNotebooks.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EmprestimoNotebooks.Tests.AplicacaoDeTeste;

namespace EmprestimoNotebooks.Tests;

public class AlunosTestes : IDisposable
{
    private const string Cadastro = "/alunos/cadastrar";
    private const string JpegSintetico = """
        /9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsK
        CwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQU
        FBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAACAAIDASIA
        AhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA
        AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3
        ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWm
        p6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEA
        AwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSEx
        BhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElK
        U1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3
        uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDS+EXw
        i8C+JvhR4L1jWPBfh7VtX1DRLK7vL++0qCae5mkgRpJZJGQs7sxLFiSSSSaKKK/Ccb/vVX/E/wA2
        fo2H/gw9F+R//9k=
        """;

    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"alunos-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CadastroGravaAlunoEFotoDisponivelApenasAoAtendente()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var cadastro = await CadastrarAsync(cliente, "AL-101", JpegSintetico);
        Assert.Equal(HttpStatusCode.Redirect, cadastro.StatusCode);
        Assert.Equal("/alunos", cadastro.Headers.Location?.OriginalString);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await banco.Alunos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal("Ana Lima", aluno.NomeCompleto);
        Assert.Equal("AL-101", aluno.Matricula);
        Assert.Equal("Turma 1", aluno.Turma);
        Assert.True(aluno.FotoJpeg.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }));

        var rotaFoto = $"/alunos/{aluno.Id}/foto";
        using var foto = await cliente.GetAsync(rotaFoto, cancelamento);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/jpeg", foto.Content.Headers.ContentType?.MediaType);
        Assert.True(foto.Headers.CacheControl?.NoStore is true);
        Assert.Equal(aluno.FotoJpeg, await foto.Content.ReadAsByteArrayAsync(cancelamento));

        using var anonimo = aplicacao.CriarCliente();
        using var acessoSemLogin = await anonimo.GetAsync(rotaFoto, cancelamento);
        AssertRedirecionaParaEntrar(anonimo, acessoSemLogin);
    }

    [Fact]
    public async Task BuscaFiltraAlunosPorNomeOuMatricula()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var primeiro = await CadastrarAsync(cliente, "AL-101", JpegSintetico, nome: "Ana Conceição");
        using var segundo = await CadastrarAsync(cliente, "AL-202", JpegSintetico, nome: "Bruno Rocha");
        Assert.Equal(HttpStatusCode.Redirect, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segundo.StatusCode);

        foreach (var busca in new[] { "conceicao", "al-101" })
        {
            var html = WebUtility.HtmlDecode(await cliente.GetStringAsync($"/alunos?busca={busca}", cancelamento));
            Assert.Contains("Ana Conceição", html);
            Assert.Contains("AL-101", html);
            Assert.DoesNotContain("Bruno Rocha", html);
        }
    }

    [Fact]
    public async Task SemLoginListagemECadastroDeAlunosSaoProtegidos()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();

        using var listagem = await cliente.GetAsync("/alunos", cancelamento);
        AssertRedirecionaParaEntrar(cliente, listagem);
        using var formulario = await cliente.GetAsync(Cadastro, cancelamento);
        AssertRedirecionaParaEntrar(cliente, formulario);
        using var cadastro = await CadastrarAsync(cliente, "AL-101", JpegSintetico, pagina: "/entrar");
        AssertRedirecionaParaEntrar(cliente, cadastro);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        Assert.False(await banco.Alunos.AnyAsync(cancelamento));
    }

    [Fact]
    public async Task MatriculaDuplicadaRetornaErroEMantemCadastroOriginal()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var cadastro = await CadastrarAsync(cliente, "AL-101", JpegSintetico);
        Assert.Equal(HttpStatusCode.Redirect, cadastro.StatusCode);

        using var duplicado = await CadastrarAsync(cliente, "al-101", JpegSintetico, nome: "Bruno Rocha");
        Assert.Equal(HttpStatusCode.OK, duplicado.StatusCode);
        var html = await duplicado.Content.ReadAsStringAsync(cancelamento);
        Assert.Matches("""data-valmsg-for="Matricula"[^>]*>\s*[^\s<]""", html);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var aluno = await banco.Alunos.AsNoTracking().SingleAsync(cancelamento);
        Assert.Equal("Ana Lima", aluno.NomeCompleto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dGV4dG8=")]
    [InlineData("/9j/4AAQ")]
    public async Task FotoAusenteOuInvalidaRetornaErroENaoCadastraAluno(string? foto)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CriarCliente();
        using var login = await EntrarAsync(cliente);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var cadastro = await CadastrarAsync(cliente, "AL-101", foto);
        Assert.Equal(HttpStatusCode.OK, cadastro.StatusCode);
        var html = await cadastro.Content.ReadAsStringAsync(cancelamento);
        Assert.Matches("""data-valmsg-for="Foto"[^>]*>\s*[^\s<]""", html);

        using var escopo = aplicacao.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        Assert.False(await banco.Alunos.AnyAsync(cancelamento));
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private static async Task<HttpResponseMessage> CadastrarAsync(
        HttpClient cliente, string matricula, string? foto, string nome = "Ana Lima", string pagina = Cadastro)
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(await ObterTokenAsync(cliente, pagina)), CampoToken },
            { new StringContent(nome), "NomeCompleto" },
            { new StringContent(matricula), "Matricula" },
            { new StringContent("Turma 1"), "Turma" }
        };

        if (foto is not null)
        {
            var arquivo = new ByteArrayContent(Convert.FromBase64String(foto));
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            formulario.Add(arquivo, "Foto", "foto.jpg");
        }

        return await cliente.PostAsync(Cadastro, formulario, TestContext.Current.CancellationToken);
    }
}
