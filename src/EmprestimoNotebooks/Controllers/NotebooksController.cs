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
    public async Task<IActionResult> Index(int pagina, CancellationToken cancelamento)
    {
        var notebooks = await contexto.Notebooks
            .AsNoTracking()
            .OrderBy(notebook => notebook.Patrimonio)
            .Select(notebook => new NotebookResumo(notebook.Id, notebook.Patrimonio, notebook.Marca, notebook.Modelo,
                notebook.EmManutencao, contexto.EmprestimosAbertos.Where(emprestimo => emprestimo.NotebookId == notebook.Id)
                    .Select(emprestimo => (int?)emprestimo.Id).FirstOrDefault()))
            .ToListAsync(cancelamento);

        var emprestados = notebooks.Count(notebook => notebook.Emprestado);
        var emManutencao = notebooks.Count(notebook => !notebook.Emprestado && notebook.EmManutencao);
        var disponiveis = notebooks.Count - emprestados - emManutencao;
        return View(new InventarioFormulario(Pagina<NotebookResumo>.Criar(notebooks, pagina, 10),
            disponiveis, emprestados, emManutencao));
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
