using EmprestimoNotebooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace EmprestimoNotebooks.Dados;

public class EmprestimosContexto(DbContextOptions<EmprestimosContexto> opcoes) : DbContext(opcoes)
{
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Aluno> Alunos => Set<Aluno>();
    public DbSet<Emprestimo> Emprestimos => Set<Emprestimo>();
    public IQueryable<Emprestimo> EmprestimosAbertos => Emprestimos.Where(emprestimo => emprestimo.DevolvidoEmUtc == null);

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        var notebook = modelo.Entity<Notebook>();
        notebook.Property(item => item.Patrimonio).UseCollation("NOCASE");
        notebook.HasIndex(item => item.Patrimonio).IsUnique();

        var aluno = modelo.Entity<Aluno>();
        aluno.Property(item => item.Matricula).UseCollation("NOCASE");
        aluno.HasIndex(item => item.Matricula).IsUnique();

        var emprestimo = modelo.Entity<Emprestimo>();
        emprestimo.HasOne<Notebook>().WithMany().HasForeignKey(item => item.NotebookId).OnDelete(DeleteBehavior.Restrict);
        emprestimo.HasOne<Aluno>().WithMany().HasForeignKey(item => item.AlunoId).OnDelete(DeleteBehavior.Restrict);
        emprestimo.HasIndex(item => item.NotebookId).IsUnique().HasFilter("\"DevolvidoEmUtc\" IS NULL");

        modelo.Entity<ControleCarga>().ToTable("ControleCarga");
    }
}
