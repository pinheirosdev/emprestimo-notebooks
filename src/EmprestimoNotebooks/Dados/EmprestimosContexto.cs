using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Dados;

public class EmprestimosContexto(DbContextOptions<EmprestimosContexto> opcoes) : DbContext(opcoes)
{
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Aluno> Alunos => Set<Aluno>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        var notebook = modelo.Entity<Notebook>();
        notebook.Property(item => item.Patrimonio).UseCollation("NOCASE");
        notebook.HasIndex(item => item.Patrimonio).IsUnique();

        var aluno = modelo.Entity<Aluno>();
        aluno.Property(item => item.Matricula).UseCollation("NOCASE");
        aluno.HasIndex(item => item.Matricula).IsUnique();

        modelo.Entity<ControleCarga>().ToTable("ControleCarga");
    }
}
