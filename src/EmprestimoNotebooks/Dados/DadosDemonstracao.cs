using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Dados;

internal static class DadosDemonstracao
{
    private const string Carga = "demonstracao";

    internal static void Inserir(DbContext contexto)
    {
        if (!contexto.Database.GetAppliedMigrations().Any()
            || contexto.Set<ControleCarga>().Any(controle => controle.Id == Carga))
        {
            return;
        }

        var notebooks = contexto.Set<Notebook>();
        if (!notebooks.Any())
        {
            notebooks.AddRange(
                new Notebook { Patrimonio = "NB-001", Marca = "Dell", Modelo = "Latitude 3420" },
                new Notebook { Patrimonio = "NB-002", Marca = "Dell", Modelo = "Latitude 3420" },
                new Notebook { Patrimonio = "NB-003", Marca = "Lenovo", Modelo = "ThinkPad E14" },
                new Notebook { Patrimonio = "NB-004", Marca = "Lenovo", Modelo = "ThinkPad E14" },
                new Notebook { Patrimonio = "NB-005", Marca = "HP", Modelo = "ProBook 440" },
                new Notebook { Patrimonio = "NB-006", Marca = "HP", Modelo = "ProBook 440", EmManutencao = true });
        }

        contexto.Add(new ControleCarga { Id = Carga });
        contexto.SaveChanges();
    }
}

internal class ControleCarga
{
    public required string Id { get; set; }
}
