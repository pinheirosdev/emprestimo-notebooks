using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmprestimoNotebooks.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class RegistrarDevolucoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FotoDevolucaoJpeg",
                table: "Emprestimos",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoNotebookJpeg",
                table: "Emprestimos",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacaoDevolucao",
                table: "Emprestimos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoDevolucaoJpeg",
                table: "Emprestimos");

            migrationBuilder.DropColumn(
                name: "FotoNotebookJpeg",
                table: "Emprestimos");

            migrationBuilder.DropColumn(
                name: "ObservacaoDevolucao",
                table: "Emprestimos");
        }
    }
}
