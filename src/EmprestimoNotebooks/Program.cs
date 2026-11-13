using EmprestimoNotebooks.Dados;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var conexao = builder.Configuration.GetConnectionString("Emprestimos")
    ?? throw new InvalidOperationException("Configure a conexão com o banco de empréstimos.");

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<EmprestimosContexto>(opcoes => opcoes
    .UseSqlite(conexao)
    .UseSeeding((contexto, _) => DadosDemonstracao.Inserir(contexto)));

var app = builder.Build();

using (var escopo = app.Services.CreateScope())
{
    var contexto = escopo.ServiceProvider.GetRequiredService<EmprestimosContexto>();
    contexto.Database.Migrate();
}

app.MapStaticAssets();
app.MapControllers();
app.Run();
