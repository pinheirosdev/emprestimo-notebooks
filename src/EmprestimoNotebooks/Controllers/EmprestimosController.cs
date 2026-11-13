using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Models;
using EmprestimoNotebooks.Servicos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EmprestimosController(EmprestimosContexto contexto) : Controller
{
    [HttpGet("/notebooks/{id:int}/retirar")]
    public Task<IActionResult> Retirar([FromRoute] int id, string? busca, CancellationToken cancelamento)
        => MostrarRetirada(id, new RetiradaFormulario { Busca = busca }, cancelamento);

    [HttpPost("/notebooks/{id:int}/retirar")]
    [RequestSizeLimit(FotoJpeg.LimiteBytes + 16 * 1024)]
    public async Task<IActionResult> Retirar([FromRoute] int id, [FromForm] RetiradaFormulario formulario, CancellationToken cancelamento)
    {
        if (!ModelState.IsValid || formulario.AlunoId is not int alunoId || formulario.Foto is null)
        {
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        if (!await contexto.Alunos.AnyAsync(aluno => aluno.Id == alunoId, cancelamento))
        {
            ModelState.AddModelError(nameof(formulario.AlunoId), "Selecione um aluno cadastrado.");
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        var foto = FotoJpeg.Ler(formulario.Foto);
        if (foto is null)
        {
            ModelState.AddModelError(nameof(formulario.Foto), "Tire uma nova foto em JPEG de até 1 MB.");
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        var registros = await contexto.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Emprestimos" ("NotebookId", "AlunoId", "RetiradoEmUtc", "FotoRetiradaJpeg")
            SELECT "Id", {alunoId}, {DateTime.UtcNow}, {foto}
            FROM "Notebooks"
            WHERE "Id" = {id} AND "EmManutencao" = 0
            ON CONFLICT ("NotebookId") WHERE "DevolvidoEmUtc" IS NULL DO NOTHING
            """, cancelamento);

        if (registros == 0)
        {
            return await MostrarRetirada(id, formulario, cancelamento, conflito: true);
        }

        TempData["Mensagem"] = "Retirada registrada.";
        return RedirectToAction("Index", "Notebooks");
    }

    private async Task<IActionResult> MostrarRetirada(int id, RetiradaFormulario formulario, CancellationToken cancelamento, bool conflito = false)
    {
        var notebook = await contexto.Notebooks.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new NotebookResumo(item.Id, item.Patrimonio, item.Marca, item.Modelo, item.EmManutencao,
                contexto.EmprestimosAbertos.Any(emprestimo => emprestimo.NotebookId == item.Id)))
            .SingleOrDefaultAsync(cancelamento);
        if (notebook is null)
        {
            return NotFound();
        }

        formulario.NotebookId = notebook.Id;
        formulario.Patrimonio = notebook.Patrimonio;
        formulario.Marca = notebook.Marca;
        formulario.Modelo = notebook.Modelo;
        formulario.Busca = formulario.Busca?.Trim();
        formulario.Indisponivel = notebook.Emprestado || notebook.EmManutencao;

        if (conflito || formulario.Indisponivel)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            var mensagem = notebook.EmManutencao ? "Este notebook está em manutenção."
                : notebook.Emprestado ? "Este notebook já está emprestado."
                : "A disponibilidade do notebook mudou. Tente novamente.";
            ModelState.AddModelError(string.Empty, mensagem);
        }

        if (!formulario.Indisponivel)
        {
            formulario.Alunos = await ConsultaAlunos.Listar(contexto, formulario.Busca, cancelamento);
        }

        return View("Retirar", formulario);
    }
}
