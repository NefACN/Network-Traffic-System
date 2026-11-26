using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstructuraOrganizacionalActiva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "activo",
                table: "areas_operativas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_normalizado",
                table: "sucursales",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                computedColumnSql: "upper(btrim(nombre))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_normalizado",
                table: "areas_operativas",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                computedColumnSql: "upper(btrim(nombre))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "UQ_sucursales_nombre_ciudad",
                table: "sucursales",
                columns: new[] { "nombre_normalizado", "id_ciudad" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_areas_nombre_normalizado",
                table: "areas_operativas",
                column: "nombre_normalizado",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_sucursales_nombre_ciudad",
                table: "sucursales");

            migrationBuilder.DropIndex(
                name: "UQ_areas_nombre_normalizado",
                table: "areas_operativas");

            migrationBuilder.DropColumn(
                name: "nombre_normalizado",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "nombre_normalizado",
                table: "areas_operativas");

            migrationBuilder.DropColumn(
                name: "activo",
                table: "areas_operativas");
        }
    }
}
