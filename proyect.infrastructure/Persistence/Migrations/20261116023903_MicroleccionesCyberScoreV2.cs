using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MicroleccionesCyberScoreV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsMicroleccion",
                table: "actividades_educativas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ParaCredenciales",
                table: "actividades_educativas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "asignaciones_microleccion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    IdEnvio = table.Column<int>(type: "integer", nullable: false),
                    IdActividad = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaAsignacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaCompletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IdIntento = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asignaciones_microleccion", x => x.Id);
                    table.CheckConstraint("CK_asignaciones_motivo", "\"Motivo\" IN ('clic','credenciales')");
                    table.ForeignKey(
                        name: "FK_asignaciones_microleccion_actividades_educativas_IdActividad",
                        column: x => x.IdActividad,
                        principalTable: "actividades_educativas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asignaciones_microleccion_envios_simulacion_IdEnvio",
                        column: x => x.IdEnvio,
                        principalTable: "envios_simulacion",
                        principalColumn: "id_envio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asignaciones_microleccion_intentos_curso_voluntario_IdInten~",
                        column: x => x.IdIntento,
                        principalTable: "intentos_curso_voluntario",
                        principalColumn: "id_intento",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asignaciones_microleccion_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "politica_cyberscore",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PesoClic = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    PesoCredenciales = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BonoReporte = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BonoCuestionario = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    BonoMicroleccion = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    VidaMediaDias = table.Column<int>(type: "integer", nullable: false),
                    VentanaDias = table.Column<int>(type: "integer", nullable: false),
                    MinimoSimulaciones = table.Column<int>(type: "integer", nullable: false),
                    FactorPocoDificil = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    FactorMuyDificil = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    ExposicionBaja = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    ExposicionMedia = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    ExposicionAlta = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_politica_cyberscore", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "politica_cyberscore",
                columns: new[] { "Id", "ActualizadaEn", "BonoCuestionario", "BonoMicroleccion", "BonoReporte", "ExposicionAlta", "ExposicionBaja", "ExposicionMedia", "FactorMuyDificil", "FactorPocoDificil", "MinimoSimulaciones", "PesoClic", "PesoCredenciales", "Revision", "VentanaDias", "VidaMediaDias" },
                values: new object[] { 1, new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), 4m, 3m, 8m, 1.3m, 0.8m, 1.0m, 0.75m, 1.25m, 3, 10m, 20m, 1, 180, 60 });

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdActividad",
                table: "asignaciones_microleccion",
                column: "IdActividad");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdEnvio",
                table: "asignaciones_microleccion",
                column: "IdEnvio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdIntento",
                table: "asignaciones_microleccion",
                column: "IdIntento");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdUsuario_FechaCompletado",
                table: "asignaciones_microleccion",
                columns: new[] { "IdUsuario", "FechaCompletado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignaciones_microleccion");

            migrationBuilder.DropTable(
                name: "politica_cyberscore");

            migrationBuilder.DropColumn(
                name: "EsMicroleccion",
                table: "actividades_educativas");

            migrationBuilder.DropColumn(
                name: "ParaCredenciales",
                table: "actividades_educativas");
        }
    }
}
