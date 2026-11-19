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
    [RequestSizeLimit(2 * FotoJpeg.LimiteBytes + 16 * 1024)]
    public async Task<IActionResult> Retirar([FromRoute] int id, [FromForm] RetiradaFormulario formulario, CancellationToken cancelamento)
    {
        if (!ModelState.IsValid || formulario.AlunoId is not int alunoId || formulario.Foto is null || formulario.FotoNotebook is null)
        {
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        if (!await contexto.Alunos.AnyAsync(aluno => aluno.Id == alunoId, cancelamento))
        {
            ModelState.AddModelError(nameof(formulario.AlunoId), "Selecione um aluno cadastrado.");
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        var (fotoAluno, fotoNotebook) = LerFotos(formulario.Foto, formulario.FotoNotebook);
        if (fotoAluno is null || fotoNotebook is null)
        {
            return await MostrarRetirada(id, formulario, cancelamento);
        }

        var registros = await contexto.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Emprestimos" ("NotebookId", "AlunoId", "RetiradoEmUtc", "FotoRetiradaJpeg", "FotoNotebookRetiradaJpeg")
            SELECT "Id", {alunoId}, {DateTime.UtcNow}, {fotoAluno}, {fotoNotebook}
            FROM "Notebooks"
            WHERE "Id" = {id} AND "Ativo" = 1 AND "EmManutencao" = 0
            ON CONFLICT ("NotebookId") WHERE "DevolvidoEmUtc" IS NULL DO NOTHING
            """, cancelamento);

        if (registros == 0)
        {
            return await MostrarRetirada(id, formulario, cancelamento, conflito: true);
        }

        TempData["Mensagem"] = "Retirada registrada.";
        return RedirectToAction("Index", "Notebooks");
    }

    [HttpGet("/emprestimos/{id:int}/devolver")]
    public Task<IActionResult> Devolver([FromRoute] int id, CancellationToken cancelamento)
        => MostrarDevolucao(id, new DevolucaoFormulario(), cancelamento);

    [HttpPost("/emprestimos/{id:int}/devolver")]
    [RequestSizeLimit(2 * FotoJpeg.LimiteBytes + 16 * 1024)]
    public async Task<IActionResult> Devolver([FromRoute] int id, [FromForm] DevolucaoFormulario formulario, CancellationToken cancelamento)
    {
        if (!ModelState.IsValid || formulario.Foto is null || formulario.FotoNotebook is null)
        {
            return await MostrarDevolucao(id, formulario, cancelamento);
        }

        var (fotoAluno, fotoNotebook) = LerFotos(formulario.Foto, formulario.FotoNotebook);
        if (fotoAluno is null || fotoNotebook is null)
        {
            return await MostrarDevolucao(id, formulario, cancelamento);
        }

        var devolvidoEm = DateTime.UtcNow;
        var observacao = string.IsNullOrWhiteSpace(formulario.Observacao) ? null : formulario.Observacao.Trim();
        var registros = await contexto.EmprestimosAbertos.Where(emprestimo => emprestimo.Id == id)
            .ExecuteUpdateAsync(campos => campos
                .SetProperty(emprestimo => emprestimo.DevolvidoEmUtc, devolvidoEm)
                .SetProperty(emprestimo => emprestimo.FotoDevolucaoJpeg, fotoAluno)
                .SetProperty(emprestimo => emprestimo.FotoNotebookJpeg, fotoNotebook)
                .SetProperty(emprestimo => emprestimo.ObservacaoDevolucao, observacao), cancelamento);

        if (registros == 0)
        {
            return await MostrarDevolucao(id, formulario, cancelamento);
        }

        TempData["Mensagem"] = "Devolução registrada.";
        return RedirectToAction("Index", "Notebooks");
    }

    private (byte[]? Aluno, byte[]? Notebook) LerFotos(IFormFile arquivoAluno, IFormFile arquivoNotebook)
    {
        var fotoAluno = FotoJpeg.Ler(arquivoAluno);
        if (fotoAluno is null)
        {
            ModelState.AddModelError(nameof(RetiradaFormulario.Foto), "Tire uma nova foto do aluno em JPEG de até 1 MB.");
        }

        var fotoNotebook = FotoJpeg.Ler(arquivoNotebook);
        if (fotoNotebook is null)
        {
            ModelState.AddModelError(nameof(RetiradaFormulario.FotoNotebook), "Tire uma nova foto do notebook em JPEG de até 1 MB.");
        }

        return (fotoAluno, fotoNotebook);
    }

    private async Task<IActionResult> MostrarDevolucao(int id, DevolucaoFormulario formulario, CancellationToken cancelamento)
    {
        var dados = await (from emprestimo in contexto.Emprestimos
                           join notebook in contexto.Notebooks on emprestimo.NotebookId equals notebook.Id
                           join aluno in contexto.Alunos on emprestimo.AlunoId equals aluno.Id
                           where emprestimo.Id == id
                           select new { notebook.Patrimonio, notebook.Marca, notebook.Modelo, aluno.NomeCompleto, aluno.Matricula, emprestimo.DevolvidoEmUtc })
            .SingleOrDefaultAsync(cancelamento);
        if (dados is null)
        {
            return NotFound();
        }

        formulario.EmprestimoId = id;
        formulario.Patrimonio = dados.Patrimonio;
        formulario.Marca = dados.Marca;
        formulario.Modelo = dados.Modelo;
        formulario.NomeCompleto = dados.NomeCompleto;
        formulario.Matricula = dados.Matricula;
        formulario.Encerrado = dados.DevolvidoEmUtc.HasValue;

        if (formulario.Encerrado)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(string.Empty, "Este empréstimo já foi devolvido.");
        }

        return View("Devolver", formulario);
    }

    private async Task<IActionResult> MostrarRetirada(int id, RetiradaFormulario formulario, CancellationToken cancelamento, bool conflito = false)
    {
        var notebook = await contexto.Notebooks.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new NotebookResumo(item.Id, item.Patrimonio, item.Marca, item.Modelo, item.Ativo, item.EmManutencao,
                contexto.EmprestimosAbertos.Where(emprestimo => emprestimo.NotebookId == item.Id)
                    .Select(emprestimo => (int?)emprestimo.Id).FirstOrDefault()))
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
        formulario.Indisponivel = !notebook.Disponivel;

        if (conflito || formulario.Indisponivel)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            var mensagem = !notebook.Ativo ? "Este notebook está inativo."
                : notebook.EmManutencao ? "Este notebook está em manutenção."
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
