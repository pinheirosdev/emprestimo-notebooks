using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Dados;

public class EmprestimosContexto(DbContextOptions<EmprestimosContexto> opcoes) : DbContext(opcoes)
{
    public DbSet<Notebook> Notebooks => Set<Notebook>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        var notebook = modelo.Entity<Notebook>();
        notebook.Property(item => item.Patrimonio).UseCollation("NOCASE");
        notebook.HasIndex(item => item.Patrimonio).IsUnique();

        modelo.Entity<ControleCarga>().ToTable("ControleCarga");
    }
}
