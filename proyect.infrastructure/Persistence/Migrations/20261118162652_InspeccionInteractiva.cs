using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InspeccionInteractiva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PuntosInspeccion",
                table: "reglas_gamificacion",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EnlaceMostrado",
                table: "plantillas_phishing",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemitenteCorreo",
                table: "plantillas_phishing",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemitenteNombre",
                table: "plantillas_phishing",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInspeccion",
                table: "envios_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspeccionAciertosEnlace",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspeccionEncontradas",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspeccionOmitidas",
                table: "envios_simulacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspeccionTotal",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnsenanzaRegistro",
                table: "campanias_simulacion",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "reglas_gamificacion",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuntosInspeccion",
                value: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PuntosInspeccion",
                table: "reglas_gamificacion");

            migrationBuilder.DropColumn(
                name: "EnlaceMostrado",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "RemitenteCorreo",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "RemitenteNombre",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "FechaInspeccion",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "InspeccionAciertosEnlace",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "InspeccionEncontradas",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "InspeccionOmitidas",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "InspeccionTotal",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "EnsenanzaRegistro",
                table: "campanias_simulacion");
        }
    }
}
