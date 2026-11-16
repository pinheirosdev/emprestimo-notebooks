using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmprestimoNotebooks.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class RegistrarHistoricoDeCadastros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CadastradoEmUtc",
                table: "Notebooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CadastradoEmUtc",
                table: "Alunos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManutencoesNotebook",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NotebookId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlteradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EmManutencao = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManutencoesNotebook", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManutencoesNotebook_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManutencoesNotebook_NotebookId",
                table: "ManutencoesNotebook",
                column: "NotebookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManutencoesNotebook");

            migrationBuilder.DropColumn(
                name: "CadastradoEmUtc",
                table: "Notebooks");

            migrationBuilder.DropColumn(
                name: "CadastradoEmUtc",
                table: "Alunos");
        }
    }
}
