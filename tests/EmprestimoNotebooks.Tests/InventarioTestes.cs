using System.Net;
using EmprestimoNotebooks.Dados;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EmprestimoNotebooks.Tests;

public class InventarioTestes : IDisposable
{
    private readonly string _caminhoDoBanco = Path.Combine(Path.GetTempPath(), $"notebooks-testes-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PaginaInicialListaNotebooksDisponiveisEEmManutencao()
    {
        await using var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco);
        using var cliente = aplicacao.CreateClient();
        var html = WebUtility.HtmlDecode(await cliente.GetStringAsync("/", TestContext.Current.CancellationToken));
        var linhas = html.Split("</tr>");

        var notebookDisponivel = Assert.Single(linhas, linha => linha.Contains(">NB-001<"));
        var notebookEmManutencao = Assert.Single(linhas, linha => linha.Contains(">NB-006<"));
        Assert.Contains("Disponível", notebookDisponivel);
        Assert.Contains("Em manutenção", notebookEmManutencao);
    }

    [Fact]
    public async Task ReinicioPreservaEdicaoEExclusaoDaCargaDemonstrativa()
    {
        var cancelamento = TestContext.Current.CancellationToken;

        await using (var aplicacao = new AplicacaoDeTeste(_caminhoDoBanco))
        {
            using var escopo = aplicacao.Services.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
            var notebooks = await banco.Notebooks.OrderBy(notebook => notebook.Patrimonio).ToListAsync(cancelamento);

            Assert.NotEmpty(await banco.Database.GetAppliedMigrationsAsync(cancelamento));
            Assert.Equal(6, notebooks.Count);

            notebooks[0].Modelo = "Modelo atualizado";
            banco.Notebooks.Remove(notebooks[1]);
            await banco.SaveChangesAsync(cancelamento);
        }

        await using var aplicacaoReiniciada = new AplicacaoDeTeste(_caminhoDoBanco);
        using var escopoReiniciado = aplicacaoReiniciada.Services.CreateScope();
        var bancoReiniciado = escopoReiniciado.ServiceProvider.GetRequiredService<EmprestimosContexto>();
        var notebooksPersistidos = await bancoReiniciado.Notebooks.ToListAsync(cancelamento);

        Assert.Empty(await bancoReiniciado.Database.GetPendingMigrationsAsync(cancelamento));
        Assert.Equal(5, notebooksPersistidos.Count);
        Assert.Contains(notebooksPersistidos, notebook => notebook.Patrimonio == "NB-001" && notebook.Modelo == "Modelo atualizado");
        Assert.DoesNotContain(notebooksPersistidos, notebook => notebook.Patrimonio == "NB-002");
    }

    public void Dispose() => File.Delete(_caminhoDoBanco);

    private class AplicacaoDeTeste(string caminhoDoBanco) : WebApplicationFactory<EmprestimosContexto>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var conexao = new SqliteConnectionStringBuilder
            {
                DataSource = caminhoDoBanco,
                Pooling = false
            };

            builder.ConfigureHostConfiguration(configuracao => configuracao.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Emprestimos"] = conexao.ToString() }));

            return base.CreateHost(builder);
        }
    }
}
