using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GobernanzaIncidentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comentario_revision",
                table: "campanias_simulacion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado_aprobacion",
                table: "campanias_simulacion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "borrador");

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_revision",
                table: "campanias_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_solicitud",
                table: "campanias_simulacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revisada_por",
                table: "campanias_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "solicitada_por",
                table: "campanias_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exclusiones_simulacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Nota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    CreadoPor = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exclusiones_simulacion", x => x.Id);
                    table.CheckConstraint("CK_exclusiones_motivo", "\"Motivo\" IN ('licencia','vacaciones','incidente_reciente','solicitud','otro')");
                    table.ForeignKey(
                        name: "FK_exclusiones_simulacion_usuarios_CreadoPor",
                        column: x => x.CreadoPor,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exclusiones_simulacion_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incidentes_reportados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    IdSucursalRegistro = table.Column<int>(type: "integer", nullable: true),
                    IdAreaRegistro = table.Column<int>(type: "integer", nullable: true),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Remitente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Interaccion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Prioridad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Clasificacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Respuesta = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IdAnalista = table.Column<int>(type: "integer", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaAtencion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidentes_reportados", x => x.Id);
                    table.CheckConstraint("CK_incidentes_canal", "\"Canal\" IN ('correo','sms','whatsapp','llamada','otro')");
                    table.CheckConstraint("CK_incidentes_clasificacion", "\"Clasificacion\" IS NULL OR \"Clasificacion\" IN ('malicioso','spam','legitimo','simulacion')");
                    table.CheckConstraint("CK_incidentes_estado", "\"Estado\" IN ('recibido','en_analisis','resuelto')");
                    table.CheckConstraint("CK_incidentes_interaccion", "\"Interaccion\" IN ('ninguna','clic','adjunto','datos')");
                    table.CheckConstraint("CK_incidentes_prioridad", "\"Prioridad\" IN ('baja','media','alta')");
                    table.ForeignKey(
                        name: "FK_incidentes_reportados_areas_operativas_IdAreaRegistro",
                        column: x => x.IdAreaRegistro,
                        principalTable: "areas_operativas",
                        principalColumn: "id_area",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incidentes_reportados_sucursales_IdSucursalRegistro",
                        column: x => x.IdSucursalRegistro,
                        principalTable: "sucursales",
                        principalColumn: "id_sucursal",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incidentes_reportados_usuarios_IdAnalista",
                        column: x => x.IdAnalista,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_incidentes_reportados_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "politica_gobernanza",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    RequiereAprobacion = table.Column<bool>(type: "boolean", nullable: false),
                    VentanaActiva = table.Column<bool>(type: "boolean", nullable: false),
                    HoraInicio = table.Column<int>(type: "integer", nullable: false),
                    HoraFin = table.Column<int>(type: "integer", nullable: false),
                    SoloDiasHabiles = table.Column<bool>(type: "boolean", nullable: false),
                    MaxSimulacionesPorPersona = table.Column<int>(type: "integer", nullable: false),
                    UmbralAgregacion = table.Column<int>(type: "integer", nullable: false),
                    PretextosProhibidos = table.Column<string>(type: "jsonb", nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_politica_gobernanza", x => x.Id);
                    table.CheckConstraint("CK_gobernanza_horas", "\"HoraInicio\" BETWEEN 0 AND 23 AND \"HoraFin\" BETWEEN 1 AND 24 AND \"HoraInicio\" < \"HoraFin\"");
                    table.CheckConstraint("CK_gobernanza_limites", "\"MaxSimulacionesPorPersona\" >= 0 AND \"UmbralAgregacion\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "incidentes_seguimiento",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdIncidente = table.Column<int>(type: "integer", nullable: false),
                    IdUsuario = table.Column<int>(type: "integer", nullable: true),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstadoAnterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EstadoNuevo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nota = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Publica = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidentes_seguimiento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incidentes_seguimiento_incidentes_reportados_IdIncidente",
                        column: x => x.IdIncidente,
                        principalTable: "incidentes_reportados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incidentes_seguimiento_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "politica_gobernanza",
                columns: new[] { "Id", "ActualizadaEn", "HoraFin", "HoraInicio", "MaxSimulacionesPorPersona", "PretextosProhibidos", "RequiereAprobacion", "Revision", "SoloDiasHabiles", "UmbralAgregacion", "VentanaActiva" },
                values: new object[] { 1, new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), 18, 8, 2, "[\"bono\",\"bonificación\",\"aguinaldo\",\"aumento de sueldo\",\"despido\",\"desvinculación\",\"salud\",\"enfermedad\",\"diagnóstico\",\"covid\",\"vacuna\",\"fallecimiento\",\"duelo\"]", true, 1, true, 5, true });

            migrationBuilder.CreateIndex(
                name: "IX_campanias_simulacion_revisada_por",
                table: "campanias_simulacion",
                column: "revisada_por");

            migrationBuilder.CreateIndex(
                name: "IX_campanias_simulacion_solicitada_por",
                table: "campanias_simulacion",
                column: "solicitada_por");

            migrationBuilder.AddCheckConstraint(
                name: "CK_campanias_estado_aprobacion",
                table: "campanias_simulacion",
                sql: "estado_aprobacion IN ('borrador','pendiente','aprobada','rechazada')");

            migrationBuilder.CreateIndex(
                name: "IX_exclusiones_simulacion_CreadoPor",
                table: "exclusiones_simulacion",
                column: "CreadoPor");

            migrationBuilder.CreateIndex(
                name: "IX_exclusiones_simulacion_IdUsuario_Activa",
                table: "exclusiones_simulacion",
                columns: new[] { "IdUsuario", "Activa" });

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_Codigo",
                table: "incidentes_reportados",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_Estado_Prioridad_FechaCreacion",
                table: "incidentes_reportados",
                columns: new[] { "Estado", "Prioridad", "FechaCreacion" });

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_IdAnalista",
                table: "incidentes_reportados",
                column: "IdAnalista");

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_IdAreaRegistro",
                table: "incidentes_reportados",
                column: "IdAreaRegistro");

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_IdSucursalRegistro",
                table: "incidentes_reportados",
                column: "IdSucursalRegistro");

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_reportados_IdUsuario_FechaCreacion",
                table: "incidentes_reportados",
                columns: new[] { "IdUsuario", "FechaCreacion" });

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_seguimiento_IdIncidente",
                table: "incidentes_seguimiento",
                column: "IdIncidente");

            migrationBuilder.CreateIndex(
                name: "IX_incidentes_seguimiento_IdUsuario",
                table: "incidentes_seguimiento",
                column: "IdUsuario");

            migrationBuilder.AddForeignKey(
                name: "FK_campanias_simulacion_usuarios_revisada_por",
                table: "campanias_simulacion",
                column: "revisada_por",
                principalTable: "usuarios",
                principalColumn: "id_usuario",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_campanias_simulacion_usuarios_solicitada_por",
                table: "campanias_simulacion",
                column: "solicitada_por",
                principalTable: "usuarios",
                principalColumn: "id_usuario",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_campanias_simulacion_usuarios_revisada_por",
                table: "campanias_simulacion");

            migrationBuilder.DropForeignKey(
                name: "FK_campanias_simulacion_usuarios_solicitada_por",
                table: "campanias_simulacion");

            migrationBuilder.DropTable(
                name: "exclusiones_simulacion");

            migrationBuilder.DropTable(
                name: "incidentes_seguimiento");

            migrationBuilder.DropTable(
                name: "politica_gobernanza");

            migrationBuilder.DropTable(
                name: "incidentes_reportados");

            migrationBuilder.DropIndex(
                name: "IX_campanias_simulacion_revisada_por",
                table: "campanias_simulacion");

            migrationBuilder.DropIndex(
                name: "IX_campanias_simulacion_solicitada_por",
                table: "campanias_simulacion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_campanias_estado_aprobacion",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "comentario_revision",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "estado_aprobacion",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "fecha_revision",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "fecha_solicitud",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "revisada_por",
                table: "campanias_simulacion");

            migrationBuilder.DropColumn(
                name: "solicitada_por",
                table: "campanias_simulacion");
        }
    }
}
