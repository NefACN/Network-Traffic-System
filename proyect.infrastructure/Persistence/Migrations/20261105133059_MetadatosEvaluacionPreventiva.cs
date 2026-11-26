using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetadatosEvaluacionPreventiva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "id_categoria_registro",
                table: "campanias_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "nivel_dificultad_registro",
                table: "campanias_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version_medicion_registro",
                table: "campanias_simulacion",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_campanias_simulacion_id_categoria_registro",
                table: "campanias_simulacion",
                column: "id_categoria_registro");

            migrationBuilder.AddForeignKey(
                name: "FK_campanias_simulacion_categorias_pregunta_id_categoria_regis~",
                table: "campanias_simulacion",
                column: "id_categoria_registro",
                principalTable: "categorias_pregunta",
                principalColumn: "id_categoria",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_campanias_simulacion_categorias_pregunta_id_categoria_regis~",
                table: "campanias_simulacion");

            migrationBuilder.DropIndex(
                name: "IX_campanias_simulacion_id_categoria_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "id_categoria_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "nivel_dificultad_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "version_medicion_registro",
                table: "campanias_simulacion");
        }
    }
}
