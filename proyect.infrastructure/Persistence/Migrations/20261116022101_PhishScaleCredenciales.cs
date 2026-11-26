using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhishScaleCredenciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alineacion_general",
                table: "plantillas_phishing",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "media");

            migrationBuilder.AddColumn<string>(
                name: "pagina_senuelo",
                table: "plantillas_phishing",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ninguna");

            migrationBuilder.AddColumn<string>(
                name: "alineacion_registro",
                table: "envios_simulacion",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dificultad_nist_registro",
                table: "envios_simulacion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_ingreso_credenciales",
                table: "envios_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ingreso_credenciales",
                table: "envios_simulacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pagina_senuelo_registro",
                table: "campanias_simulacion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phish_scale_registro",
                table: "campanias_simulacion",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alineaciones_plantilla_area",
                columns: table => new
                {
                    id_plantilla = table.Column<int>(type: "integer", nullable: false),
                    id_area = table.Column<int>(type: "integer", nullable: false),
                    nivel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alineaciones_plantilla_area", x => new { x.id_plantilla, x.id_area });
                    table.CheckConstraint("CK_alineaciones_nivel", "nivel IN ('debil','media','fuerte')");
                    table.ForeignKey(
                        name: "FK_alineaciones_plantilla_area_areas_operativas_id_area",
                        column: x => x.id_area,
                        principalTable: "areas_operativas",
                        principalColumn: "id_area",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alineaciones_plantilla_area_plantillas_phishing_id_plantilla",
                        column: x => x.id_plantilla,
                        principalTable: "plantillas_phishing",
                        principalColumn: "id_plantilla",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "senales_alerta_plantilla",
                columns: table => new
                {
                    id_senal = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_plantilla = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    fragmento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_senales_alerta_plantilla", x => x.id_senal);
                    table.CheckConstraint("CK_senales_tipo", "tipo IN ('errores','tecnico','visual','lenguaje','tacticas')");
                    table.ForeignKey(
                        name: "FK_senales_alerta_plantilla_plantillas_phishing_id_plantilla",
                        column: x => x.id_plantilla,
                        principalTable: "plantillas_phishing",
                        principalColumn: "id_plantilla",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_plantillas_alineacion_general",
                table: "plantillas_phishing",
                sql: "alineacion_general IN ('debil','media','fuerte')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_plantillas_pagina_senuelo",
                table: "plantillas_phishing",
                sql: "pagina_senuelo IN ('ninguna','credenciales')");

            migrationBuilder.CreateIndex(
                name: "IX_alineaciones_plantilla_area_id_area",
                table: "alineaciones_plantilla_area",
                column: "id_area");

            migrationBuilder.CreateIndex(
                name: "IX_senales_alerta_plantilla_id_plantilla_orden",
                table: "senales_alerta_plantilla",
                columns: new[] { "id_plantilla", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alineaciones_plantilla_area");

            migrationBuilder.DropTable(
                name: "senales_alerta_plantilla");

            migrationBuilder.DropCheckConstraint(
                name: "CK_plantillas_alineacion_general",
                table: "plantillas_phishing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_plantillas_pagina_senuelo",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "alineacion_general",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "pagina_senuelo",
                table: "plantillas_phishing");

            migrationBuilder.DropColumn(
                name: "alineacion_registro",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "dificultad_nist_registro",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "fecha_ingreso_credenciales",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "ingreso_credenciales",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "pagina_senuelo_registro",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "phish_scale_registro",
                table: "campanias_simulacion");
        }
    }
}
