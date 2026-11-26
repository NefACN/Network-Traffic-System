using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistorialPreventivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContextoRegistrado",
                table: "intentos_curso_voluntario",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "IdAreaRegistro",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdSucursalRegistro",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContextoRegistrado",
                table: "envios_simulacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EstadoEntrega",
                table: "envios_simulacion",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaApertura",
                table: "envios_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEnvio",
                table: "envios_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdAreaRegistro",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdCategoriaRegistro",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdSucursalRegistro",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdSucursalObjetivo",
                table: "campanias_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_intentos_curso_voluntario_IdAreaRegistro",
                table: "intentos_curso_voluntario",
                column: "IdAreaRegistro");

            migrationBuilder.CreateIndex(
                name: "IX_intentos_curso_voluntario_IdSucursalRegistro_IdAreaRegistro~",
                table: "intentos_curso_voluntario",
                columns: new[] { "IdSucursalRegistro", "IdAreaRegistro", "fecha_inicio" });

            migrationBuilder.CreateIndex(
                name: "IX_envios_simulacion_IdAreaRegistro",
                table: "envios_simulacion",
                column: "IdAreaRegistro");

            migrationBuilder.CreateIndex(
                name: "IX_envios_simulacion_IdCategoriaRegistro",
                table: "envios_simulacion",
                column: "IdCategoriaRegistro");

            migrationBuilder.CreateIndex(
                name: "IX_envios_simulacion_IdSucursalRegistro_IdAreaRegistro_FechaEn~",
                table: "envios_simulacion",
                columns: new[] { "IdSucursalRegistro", "IdAreaRegistro", "FechaEnvio" });

            migrationBuilder.CreateIndex(
                name: "IX_campanias_simulacion_IdSucursalObjetivo",
                table: "campanias_simulacion",
                column: "IdSucursalObjetivo");

            migrationBuilder.AddForeignKey(
                name: "FK_campanias_simulacion_sucursales_IdSucursalObjetivo",
                table: "campanias_simulacion",
                column: "IdSucursalObjetivo",
                principalTable: "sucursales",
                principalColumn: "id_sucursal",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_envios_simulacion_areas_operativas_IdAreaRegistro",
                table: "envios_simulacion",
                column: "IdAreaRegistro",
                principalTable: "areas_operativas",
                principalColumn: "id_area",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_envios_simulacion_categorias_pregunta_IdCategoriaRegistro",
                table: "envios_simulacion",
                column: "IdCategoriaRegistro",
                principalTable: "categorias_pregunta",
                principalColumn: "id_categoria",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_envios_simulacion_sucursales_IdSucursalRegistro",
                table: "envios_simulacion",
                column: "IdSucursalRegistro",
                principalTable: "sucursales",
                principalColumn: "id_sucursal",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_curso_voluntario_areas_operativas_IdAreaRegistro",
                table: "intentos_curso_voluntario",
                column: "IdAreaRegistro",
                principalTable: "areas_operativas",
                principalColumn: "id_area",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_curso_voluntario_sucursales_IdSucursalRegistro",
                table: "intentos_curso_voluntario",
                column: "IdSucursalRegistro",
                principalTable: "sucursales",
                principalColumn: "id_sucursal",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_campanias_simulacion_sucursales_IdSucursalObjetivo",
                table: "campanias_simulacion");

            migrationBuilder.DropForeignKey(
                name: "FK_envios_simulacion_areas_operativas_IdAreaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropForeignKey(
                name: "FK_envios_simulacion_categorias_pregunta_IdCategoriaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropForeignKey(
                name: "FK_envios_simulacion_sucursales_IdSucursalRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropForeignKey(
                name: "FK_intentos_curso_voluntario_areas_operativas_IdAreaRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropForeignKey(
                name: "FK_intentos_curso_voluntario_sucursales_IdSucursalRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropIndex(
                name: "IX_intentos_curso_voluntario_IdAreaRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropIndex(
                name: "IX_intentos_curso_voluntario_IdSucursalRegistro_IdAreaRegistro~",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropIndex(
                name: "IX_envios_simulacion_IdAreaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropIndex(
                name: "IX_envios_simulacion_IdCategoriaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropIndex(
                name: "IX_envios_simulacion_IdSucursalRegistro_IdAreaRegistro_FechaEn~",
                table: "envios_simulacion");

            migrationBuilder.DropIndex(
                name: "IX_campanias_simulacion_IdSucursalObjetivo",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "ContextoRegistrado",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "IdAreaRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "IdSucursalRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "ContextoRegistrado",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "EstadoEntrega",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "FechaApertura",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "FechaEnvio",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "IdAreaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "IdCategoriaRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "IdSucursalRegistro",
                table: "envios_simulacion");

            migrationBuilder.DropColumn(
                name: "IdSucursalObjetivo",
                table: "campanias_simulacion");

        }
    }
}
