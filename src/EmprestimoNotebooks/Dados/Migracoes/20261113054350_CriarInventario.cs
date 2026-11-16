using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmprestimoNotebooks.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CriarInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControleCarga",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControleCarga", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notebooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Patrimonio = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Marca = table.Column<string>(type: "TEXT", nullable: false),
                    Modelo = table.Column<string>(type: "TEXT", nullable: false),
                    EmManutencao = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notebooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_Patrimonio",
                table: "Notebooks",
                column: "Patrimonio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ControleCarga");

            migrationBuilder.DropTable(
                name: "Notebooks");
        }
    }
}
