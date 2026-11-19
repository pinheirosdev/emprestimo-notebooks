using System.Net;
using System.Text.RegularExpressions;
using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static EmprestimoNotebooks.Tests.FotosDeTeste;

namespace EmprestimoNotebooks.Tests;

internal class AplicacaoDeTeste(string caminhoDoBanco, IInterceptor? interceptador = null, bool cadastrarNotebooks = true)
    : WebApplicationFactory<EmprestimosContexto>
{
    internal const string CampoToken = "__RequestVerificationToken";
    private const string UsuarioDeTeste = "atendente.teste";
    private const string SenhaDeTeste = "Senha-apenas-dos-testes";

    internal HttpClient CriarCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    internal static void AssertRedirecionaParaEntrar(HttpClient cliente, HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        Assert.NotNull(resposta.Headers.Location);
        Assert.Equal("/entrar", new Uri(cliente.BaseAddress!, resposta.Headers.Location).AbsolutePath);
    }

    internal static Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string senha = SenhaDeTeste, string retorno = "") =>
        EnviarFormularioAsync(cliente, "/entrar", "/entrar", new Dictionary<string, string>
        {
            ["Usuario"] = UsuarioDeTeste,
            ["Senha"] = senha,
            ["Retorno"] = retorno
        });

    internal static async Task<HttpResponseMessage> EnviarFormularioAsync(
        HttpClient cliente, string pagina, string destino, Dictionary<string, string> campos)
    {
        campos[CampoToken] = await ObterTokenAsync(cliente, pagina);
        using var conteudo = new FormUrlEncodedContent(campos);
        return await cliente.PostAsync(destino, conteudo, TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> CadastrarAlunoAsync(
        HttpClient cliente, string matricula, string? foto, string nome = "Ana Lima", string pagina = "/alunos/cadastrar")
    {
        using var formulario = new MultipartFormDataContent
        {
            { new StringContent(await ObterTokenAsync(cliente, pagina)), CampoToken },
            { new StringContent(nome), "NomeCompleto" },
            { new StringContent(matricula), "Matricula" },
            { new StringContent("Turma 1"), "Turma" }
        };

        AnexarFoto(formulario, foto);

        return await cliente.PostAsync("/alunos/cadastrar", formulario, TestContext.Current.CancellationToken);
    }

    internal static async Task<string> ObterTokenAsync(HttpClient cliente, string pagina)
    {
        var html = await cliente.GetStringAsync(pagina, TestContext.Current.CancellationToken);
        var token = Regex.Match(html, """<input\b(?=[^>]*\bname\s*=\s*"__RequestVerificationToken")[^>]*\bvalue\s*=\s*"([^"]+)"[^>]*>""");
        Assert.True(token.Success, $"Formulário em {pagina} sem token antiforgery.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (interceptador is not null)
        {
            builder.ConfigureTestServices(servicos => servicos.ConfigureDbContext<EmprestimosContexto>(
                opcoes => opcoes.AddInterceptors(interceptador)));
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var bancoNovo = !File.Exists(caminhoDoBanco);
        var conexao = new SqliteConnectionStringBuilder
        {
            DataSource = caminhoDoBanco,
            Pooling = false
        };

        builder.ConfigureHostConfiguration(configuracao => configuracao.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Emprestimos"] = conexao.ToString(),
                ["Atendente:Usuario"] = UsuarioDeTeste,
                ["Atendente:Senha"] = SenhaDeTeste
            }));

        var host = base.CreateHost(builder);
        if (cadastrarNotebooks && bancoNovo)
        {
            using var escopo = host.Services.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
            banco.Notebooks.AddRange(
                new Notebook { Patrimonio = "NB-001", Marca = "Dell", Modelo = "Latitude 3420" },
                new Notebook { Patrimonio = "NB-002", Marca = "Dell", Modelo = "Latitude 3420" },
                new Notebook { Patrimonio = "NB-003", Marca = "Lenovo", Modelo = "ThinkPad E14" },
                new Notebook { Patrimonio = "NB-004", Marca = "Lenovo", Modelo = "ThinkPad E14" },
                new Notebook { Patrimonio = "NB-005", Marca = "HP", Modelo = "ProBook 440" },
                new Notebook { Patrimonio = "NB-006", Marca = "HP", Modelo = "ProBook 440", EmManutencao = true });
            banco.SaveChanges();
        }

        return host;
    }
}
