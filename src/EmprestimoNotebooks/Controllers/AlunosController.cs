using EmprestimoNotebooks.Dados;
using EmprestimoNotebooks.Dominio;
using EmprestimoNotebooks.Models;
using EmprestimoNotebooks.Servicos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AlunosController(EmprestimosContexto contexto) : Controller
{
    private const int RestricaoUnicaSqlite = 2067;

    [HttpGet("/alunos")]
    public async Task<IActionResult> Index(string? busca, CancellationToken cancelamento)
    {
        ViewData["Busca"] = busca?.Trim();
        return View(await ConsultaAlunos.Listar(contexto, busca, cancelamento));
    }

    [HttpGet("/alunos/cadastrar")]
    public IActionResult Cadastrar() => View(new AlunoFormulario());

    [HttpPost("/alunos/cadastrar")]
    [RequestSizeLimit(FotoJpeg.LimiteBytes + 16 * 1024)]
    public async Task<IActionResult> Cadastrar([FromForm] AlunoFormulario formulario, CancellationToken cancelamento)
    {
        if (!ModelState.IsValid || formulario.Foto is null)
        {
            return View(formulario);
        }

        var foto = FotoJpeg.Ler(formulario.Foto);
        if (foto is null)
        {
            ModelState.AddModelError(nameof(formulario.Foto), "Tire uma nova foto em JPEG de até 1 MB.");
            return View(formulario);
        }

        contexto.Alunos.Add(new Aluno
        {
            NomeCompleto = formulario.NomeCompleto.Trim(),
            Matricula = formulario.Matricula.Trim(),
            Turma = formulario.Turma.Trim(),
            FotoJpeg = foto,
            CadastradoEmUtc = DateTime.UtcNow
        });

        try
        {
            await contexto.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqliteException { SqliteExtendedErrorCode: RestricaoUnicaSqlite })
        {
            ModelState.AddModelError(nameof(formulario.Matricula), "Já existe um aluno com essa matrícula.");
            return View(formulario);
        }

        TempData["Mensagem"] = "Aluno cadastrado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/alunos/{id:int}/ver-foto")]
    public async Task<IActionResult> VerFoto([FromRoute] int id, [FromQuery] string? busca, CancellationToken cancelamento)
    {
        var nome = await contexto.Alunos.Where(aluno => aluno.Id == id)
            .Select(aluno => aluno.NomeCompleto).SingleOrDefaultAsync(cancelamento);
        if (nome is null)
        {
            return NotFound();
        }

        return View(new FotoPagina($"Foto de {nome}", Url.Action(nameof(Foto), new { id })!,
            Url.Action(nameof(Index), new { busca })!));
    }

    [HttpGet("/alunos/{id:int}/foto")]
    public async Task<IActionResult> Foto([FromRoute] int id, CancellationToken cancelamento)
    {
        var foto = await contexto.Alunos.Where(aluno => aluno.Id == id)
            .Select(aluno => aluno.FotoJpeg).SingleOrDefaultAsync(cancelamento);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return foto is null ? NotFound() : File(foto, "image/jpeg");
    }
}
