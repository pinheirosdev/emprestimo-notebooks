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
    public async Task<IActionResult> Index(CancellationToken cancelamento)
    {
        var notebooks = await contexto.Notebooks
            .AsNoTracking()
            .OrderBy(notebook => notebook.Patrimonio)
            .Select(notebook => new NotebookResumo(notebook.Id, notebook.Patrimonio, notebook.Marca, notebook.Modelo,
                notebook.EmManutencao, contexto.EmprestimosAbertos.Any(emprestimo => emprestimo.NotebookId == notebook.Id)))
            .ToListAsync(cancelamento);

        return View(notebooks);
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
                var editados = await contexto.Notebooks.Where(notebook => notebook.Id == notebookId
                        && (!formulario.EmManutencao || !contexto.EmprestimosAbertos.Any(emprestimo => emprestimo.NotebookId == notebook.Id)))
                    .ExecuteUpdateAsync(campos => campos
                        .SetProperty(notebook => notebook.Patrimonio, patrimonio)
                        .SetProperty(notebook => notebook.Marca, marca)
                        .SetProperty(notebook => notebook.Modelo, modelo)
                        .SetProperty(notebook => notebook.EmManutencao, formulario.EmManutencao), cancelamento);

                if (editados == 0)
                {
                    if (!await contexto.Notebooks.AnyAsync(notebook => notebook.Id == notebookId, cancelamento))
                    {
                        return NotFound();
                    }

                    ModelState.AddModelError(nameof(formulario.EmManutencao), "Devolva o notebook antes de marcá-lo em manutenção.");
                    return View("Formulario", formulario);
                }
            }
            else
            {
                contexto.Notebooks.Add(new Notebook { Patrimonio = patrimonio, Marca = marca, Modelo = modelo, EmManutencao = formulario.EmManutencao });
                await contexto.SaveChangesAsync(cancelamento);
            }
        }
        catch (Exception erro) when (erro is SqliteException { SqliteExtendedErrorCode: RestricaoUnicaSqlite }
            or DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: RestricaoUnicaSqlite } })
        {
            ModelState.AddModelError(nameof(formulario.Patrimonio), "Já existe um notebook com esse patrimônio.");
            return View("Formulario", formulario);
        }

        TempData["Mensagem"] = id.HasValue ? "Notebook atualizado." : "Notebook cadastrado.";
        return RedirectToAction(nameof(Index));
    }
}
