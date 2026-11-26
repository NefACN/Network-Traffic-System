using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CyberScoreV3Gamificacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion");

            migrationBuilder.AddColumn<int>(
                name: "PuntosMicroleccion",
                table: "reglas_gamificacion",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PuntosReporte",
                table: "reglas_gamificacion",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaximoPlanCritico",
                table: "politica_cyberscore",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "UmbralAlto",
                table: "politica_cyberscore",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UmbralBajo",
                table: "politica_cyberscore",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UmbralMedio",
                table: "politica_cyberscore",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "politica_cyberscore",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaximoPlanCritico", "UmbralAlto", "UmbralBajo", "UmbralMedio" },
                values: new object[] { 3, 40m, 80m, 60m });

            migrationBuilder.UpdateData(
                table: "reglas_gamificacion",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuntosMicroleccion", "PuntosReporte" },
                values: new object[] { 10, 15 });

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_plan_critico_pendiente",
                table: "asignaciones_microleccion",
                columns: new[] { "IdUsuario", "IdActividad" },
                unique: true,
                filter: "\"Motivo\" = 'critico' AND \"FechaCompletado\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion",
                sql: "\"Motivo\" IN ('clic','credenciales','sugerencia','critico')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_asignaciones_plan_critico_pendiente",
                table: "asignaciones_microleccion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion");

            migrationBuilder.DropColumn(
                name: "PuntosMicroleccion",
                table: "reglas_gamificacion");

            migrationBuilder.DropColumn(
                name: "PuntosReporte",
                table: "reglas_gamificacion");

            migrationBuilder.DropColumn(
                name: "MaximoPlanCritico",
                table: "politica_cyberscore");

            migrationBuilder.DropColumn(
                name: "UmbralAlto",
                table: "politica_cyberscore");

            migrationBuilder.DropColumn(
                name: "UmbralBajo",
                table: "politica_cyberscore");

            migrationBuilder.DropColumn(
                name: "UmbralMedio",
                table: "politica_cyberscore");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion",
                sql: "\"Motivo\" IN ('clic','credenciales','sugerencia')");
        }
    }
}
