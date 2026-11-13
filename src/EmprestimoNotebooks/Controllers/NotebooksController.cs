using EmprestimoNotebooks.Dados;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Controllers;

public class NotebooksController(EmprestimosContexto contexto) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancelamento)
    {
        var notebooks = await contexto.Notebooks
            .AsNoTracking()
            .OrderBy(notebook => notebook.Patrimonio)
            .ToListAsync(cancelamento);

        return View(notebooks);
    }
}
