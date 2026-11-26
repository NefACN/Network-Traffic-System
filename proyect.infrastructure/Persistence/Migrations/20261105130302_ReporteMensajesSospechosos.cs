using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReporteMensajesSospechosos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_reporte",
                table: "envios_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reporte_sin_click_previo",
                table: "envios_simulacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fecha_reporte",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "reporte_sin_click_previo",
                table: "envios_simulacion");
        }
    }
}
