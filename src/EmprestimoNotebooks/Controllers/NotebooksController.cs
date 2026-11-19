using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using EmprestimoNotebooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotebooksController(EmprestimosContexto contexto) : Controller
{
    private const int RestricaoUnicaSqlite = 2067;

    [HttpGet("/")]
    public Task<IActionResult> Index(int pagina, CancellationToken cancelamento)
        => MostrarInventario(pagina, cancelamento);

    private async Task<IActionResult> MostrarInventario(int pagina, CancellationToken cancelamento)
    {
        var notebooks = await contexto.Notebooks
            .AsNoTracking()
            .OrderBy(notebook => notebook.Patrimonio)
            .Select(notebook => new NotebookResumo(notebook.Id, notebook.Patrimonio, notebook.Marca, notebook.Modelo,
                notebook.Ativo, notebook.EmManutencao, contexto.EmprestimosAbertos.Where(emprestimo => emprestimo.NotebookId == notebook.Id)
                    .Select(emprestimo => (int?)emprestimo.Id).FirstOrDefault()))
            .ToListAsync(cancelamento);

        var emprestados = notebooks.Count(notebook => notebook.Ativo && notebook.Emprestado);
        var emManutencao = notebooks.Count(notebook => notebook.Ativo && !notebook.Emprestado && notebook.EmManutencao);
        var disponiveis = notebooks.Count(notebook => notebook.Disponivel);
        var inativos = notebooks.Count(notebook => !notebook.Ativo);
        return View(nameof(Index), new InventarioFormulario(Pagina<NotebookResumo>.Criar(notebooks, pagina, 10),
            disponiveis, emprestados, emManutencao, inativos));
    }

    [HttpGet("/notebooks/cadastrar")]
    public IActionResult Cadastrar() => View("Formulario", new NotebookFormulario());

    [HttpPost("/notebooks/cadastrar")]
    public Task<IActionResult> Cadastrar([FromForm] NotebookFormulario formulario, CancellationToken cancelamento)
        => Salvar(formulario, null, cancelamento);

    [HttpGet("/notebooks/{id:int}/editar")]
    public async Task<IActionResult> Editar([FromRoute] int id, CancellationToken cancelamento)
    {
        var formulario = await contexto.Notebooks
            .Where(notebook => notebook.Id == id)
            .Select(notebook => new NotebookFormulario
            {
                Id = notebook.Id,
                Patrimonio = notebook.Patrimonio,
                Marca = notebook.Marca,
                Modelo = notebook.Modelo,
                EmManutencao = notebook.EmManutencao
            })
            .SingleOrDefaultAsync(cancelamento);

        return formulario is null ? NotFound() : View("Formulario", formulario);
    }

    [HttpPost("/notebooks/{id:int}/editar")]
    public Task<IActionResult> Editar([FromRoute] int id, [FromForm] NotebookFormulario formulario, CancellationToken cancelamento)
        => Salvar(formulario, id, cancelamento);

    [HttpPost("/notebooks/{id:int}/inativar")]
    public Task<IActionResult> Inativar([FromRoute] int id, int pagina, CancellationToken cancelamento)
        => AlterarAtividade(id, false, pagina, cancelamento);

    [HttpPost("/notebooks/{id:int}/reativar")]
    public Task<IActionResult> Reativar([FromRoute] int id, int pagina, CancellationToken cancelamento)
        => AlterarAtividade(id, true, pagina, cancelamento);

    private async Task<IActionResult> AlterarAtividade(int id, bool ativo, int pagina, CancellationToken cancelamento)
    {
        var notebooks = contexto.Notebooks.Where(notebook => notebook.Id == id);
        if (!ativo)
        {
            notebooks = notebooks.Where(notebook => !contexto.EmprestimosAbertos.Any(emprestimo => emprestimo.NotebookId == notebook.Id));
        }

        var alterados = await notebooks.ExecuteUpdateAsync(campos => campos.SetProperty(notebook => notebook.Ativo, ativo), cancelamento);
        if (alterados == 0)
        {
            if (!await contexto.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento))
            {
                return NotFound();
            }

            Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(string.Empty, "Devolva o notebook antes de inativá-lo.");
            return await MostrarInventario(pagina, cancelamento);
        }

        TempData["Mensagem"] = ativo ? "Notebook reativado." : "Notebook inativado.";
        return RedirectToAction(nameof(Index), new { pagina = pagina > 1 ? (int?)pagina : null });
    }

    [HttpGet("/notebooks/{id:int}/excluir")]
    public async Task<IActionResult> Excluir([FromRoute] int id, CancellationToken cancelamento)
    {
        var semMovimentacoes = NotebooksSemMovimentacoes();
        var resultado = await contexto.Notebooks.AsNoTracking()
            .Where(notebook => notebook.Id == id)
            .Select(notebook => new { Notebook = notebook, PodeExcluir = semMovimentacoes.Any(item => item.Id == notebook.Id) })
            .SingleOrDefaultAsync(cancelamento);
        if (resultado is null)
        {
            return NotFound();
        }

        if (!resultado.PodeExcluir)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(string.Empty, "Este notebook tem empréstimos ou registros de manutenção. O cadastro foi preservado.");
        }

        return View(nameof(Excluir), resultado.Notebook);
    }

    [HttpPost("/notebooks/{id:int}/excluir")]
    public async Task<IActionResult> ConfirmarExclusao([FromRoute] int id, CancellationToken cancelamento)
    {
        var excluidos = await NotebooksSemMovimentacoes()
            .Where(notebook => notebook.Id == id)
            .ExecuteDeleteAsync(cancelamento);
        if (excluidos == 0)
        {
            return await Excluir(id, cancelamento);
        }

        TempData["Mensagem"] = "Notebook excluído.";
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<Notebook> NotebooksSemMovimentacoes() =>
        contexto.Notebooks.Where(notebook =>
            !contexto.Emprestimos.Any(emprestimo => emprestimo.NotebookId == notebook.Id)
            && !contexto.ManutencoesNotebook.Any(manutencao => manutencao.NotebookId == notebook.Id));

    private async Task<IActionResult> Salvar(NotebookFormulario formulario, int? id, CancellationToken cancelamento)
    {
        formulario.Id = id;

        if (!ModelState.IsValid)
        {
            if (id.HasValue && !await contexto.Notebooks.AnyAsync(notebook => notebook.Id == id, cancelamento))
            {
                return NotFound();
            }

            return View("Formulario", formulario);
        }

        var patrimonio = formulario.Patrimonio.Trim();
        var marca = formulario.Marca.Trim();
        var modelo = formulario.Modelo.Trim();

        try
        {
            if (id is int notebookId)
            {
                await using var transacao = await contexto.Database.BeginTransactionAsync(cancelamento);
                var notebook = await contexto.Notebooks.SingleOrDefaultAsync(item => item.Id == notebookId, cancelamento);
                if (notebook is null)
                {
                    return NotFound();
                }

                if (formulario.EmManutencao && await contexto.EmprestimosAbertos.AnyAsync(emprestimo => emprestimo.NotebookId == notebookId, cancelamento))
                {
                    ModelState.AddModelError(nameof(formulario.EmManutencao), "Devolva o notebook antes de marcá-lo em manutenção.");
                    return View("Formulario", formulario);
                }

                if (notebook.EmManutencao != formulario.EmManutencao)
                {
                    contexto.ManutencoesNotebook.Add(new ManutencaoNotebook
                    {
                        NotebookId = notebookId,
                        AlteradoEmUtc = DateTime.UtcNow,
                        EmManutencao = formulario.EmManutencao
                    });
                }

                notebook.Patrimonio = patrimonio;
                notebook.Marca = marca;
                notebook.Modelo = modelo;
                notebook.EmManutencao = formulario.EmManutencao;
                await contexto.SaveChangesAsync(cancelamento);
                await transacao.CommitAsync(cancelamento);
            }
            else
            {
                contexto.Notebooks.Add(new Notebook
                {
                    Patrimonio = patrimonio,
                    Marca = marca,
                    Modelo = modelo,
                    EmManutencao = formulario.EmManutencao,
                    CadastradoEmUtc = DateTime.UtcNow
                });
                await contexto.SaveChangesAsync(cancelamento);
            }
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqliteException { SqliteExtendedErrorCode: RestricaoUnicaSqlite })
        {
            ModelState.AddModelError(nameof(formulario.Patrimonio), "Já existe um notebook com esse patrimônio.");
            return View("Formulario", formulario);
        }

        TempData["Mensagem"] = id.HasValue ? "Notebook atualizado." : "Notebook cadastrado.";
        return RedirectToAction(nameof(Index));
    }
}
