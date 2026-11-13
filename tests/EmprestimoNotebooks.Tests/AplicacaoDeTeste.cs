using System.Net;
using System.Text.RegularExpressions;
using EmprestimoNotebooks.Dados;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EmprestimoNotebooks.Tests;

internal class AplicacaoDeTeste(string caminhoDoBanco) : WebApplicationFactory<EmprestimosContexto>
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

    internal static async Task<string> ObterTokenAsync(HttpClient cliente, string pagina)
    {
        var html = await cliente.GetStringAsync(pagina, TestContext.Current.CancellationToken);
        var token = Regex.Match(html, """<input\b(?=[^>]*\bname\s*=\s*"__RequestVerificationToken")[^>]*\bvalue\s*=\s*"([^"]+)"[^>]*>""");
        Assert.True(token.Success, $"Formulário em {pagina} sem token antiforgery.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
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

        return base.CreateHost(builder);
    }
}
