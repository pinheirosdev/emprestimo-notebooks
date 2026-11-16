using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmprestimoNotebooks.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class AdicionarFotoNotebookNaRetirada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FotoNotebookRetiradaJpeg",
                table: "Emprestimos",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoNotebookRetiradaJpeg",
                table: "Emprestimos");
        }
    }
}
