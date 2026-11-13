using System.Globalization;
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
    private static readonly CompareInfo ComparacaoDeNomes = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    [HttpGet("/alunos")]
    public async Task<IActionResult> Index(string? busca, CancellationToken cancelamento)
    {
        var alunos = await contexto.Alunos.AsNoTracking()
            .OrderBy(aluno => aluno.NomeCompleto).ThenBy(aluno => aluno.Matricula)
            .Select(aluno => new AlunoResumo(aluno.Id, aluno.NomeCompleto, aluno.Matricula, aluno.Turma))
            .ToListAsync(cancelamento);

        var termo = busca?.Trim();
        if (!string.IsNullOrEmpty(termo))
        {
            alunos = alunos.Where(aluno => ComparacaoDeNomes.IndexOf(aluno.NomeCompleto, termo,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                || aluno.Matricula.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        ViewData["Busca"] = termo;
        return View(alunos);
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
            FotoJpeg = foto
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

    [HttpGet("/alunos/{id:int}/foto")]
    public async Task<IActionResult> Foto([FromRoute] int id, CancellationToken cancelamento)
    {
        var foto = await contexto.Alunos.Where(aluno => aluno.Id == id)
            .Select(aluno => aluno.FotoJpeg).SingleOrDefaultAsync(cancelamento);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return foto is null ? NotFound() : File(foto, "image/jpeg");
    }
}
