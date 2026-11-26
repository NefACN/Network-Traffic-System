using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlantillasCampaniasCicloCompleto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "habilitada",
                table: "plantillas_phishing",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "asunto_registro",
                table: "campanias_simulacion",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cuerpo_html_registro",
                table: "campanias_simulacion",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_finalizacion",
                table: "campanias_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "finalizacion_anticipada",
                table: "campanias_simulacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "objetivo",
                table: "campanias_simulacion",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "participantes_seleccionados",
                table: "campanias_simulacion",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "tipo_evaluacion",
                table: "campanias_simulacion",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "sin_clasificar");

            // La ejecución existente identifica Pendiente/En ejecución con 1/2.
            // Se completan los catálogos vacíos sin reemplazar estados existentes.
            migrationBuilder.Sql("""
                INSERT INTO estados_flujo (id_estado, nombre_estado, categoria)
                VALUES (1, 'Pendiente', 'Campania'), (2, 'En Ejecución', 'Campania')
                ON CONFLICT (id_estado) DO NOTHING;
                SELECT setval(pg_get_serial_sequence('estados_flujo', 'id_estado'),
                    GREATEST((SELECT COALESCE(MAX(id_estado), 1) FROM estados_flujo), 1), true);
                INSERT INTO estados_flujo (nombre_estado, categoria)
                SELECT 'Finalizada', 'Campania'
                WHERE NOT EXISTS (SELECT 1 FROM estados_flujo WHERE categoria = 'Campania' AND nombre_estado = 'Finalizada');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "habilitada",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "asunto_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "cuerpo_html_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "fecha_finalizacion",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "finalizacion_anticipada",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "objetivo",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "participantes_seleccionados",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "tipo_evaluacion",
                table: "campanias_simulacion");
        }
    }
}
