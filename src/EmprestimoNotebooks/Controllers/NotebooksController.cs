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
    public async Task<IActionResult> Editar([FromRoute] int id, [FromForm] NotebookFormulario formulario, CancellationToken cancelamento)
    {
        var notebook = await contexto.Notebooks.FindAsync([id], cancelamento);
        return notebook is null ? NotFound() : await Salvar(formulario, notebook, cancelamento);
    }

    private async Task<IActionResult> Salvar(NotebookFormulario formulario, Notebook? notebook, CancellationToken cancelamento)
    {
        formulario.Id = notebook?.Id;

        if (!ModelState.IsValid)
        {
            return View("Formulario", formulario);
        }

        var novo = notebook is null;
        notebook ??= new Notebook { Patrimonio = "", Marca = "", Modelo = "" };
        notebook.Patrimonio = formulario.Patrimonio.Trim();
        notebook.Marca = formulario.Marca.Trim();
        notebook.Modelo = formulario.Modelo.Trim();
        notebook.EmManutencao = formulario.EmManutencao;

        if (novo)
        {
            contexto.Notebooks.Add(notebook);
        }

        try
        {
            await contexto.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqliteException { SqliteExtendedErrorCode: RestricaoUnicaSqlite })
        {
            ModelState.AddModelError(nameof(formulario.Patrimonio), "Já existe um notebook com esse patrimônio.");
            return View("Formulario", formulario);
        }

        TempData["Mensagem"] = novo ? "Notebook cadastrado." : "Notebook atualizado.";
        return RedirectToAction(nameof(Index));
    }
}
