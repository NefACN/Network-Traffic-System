using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgramaConcientizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "constancias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    IdIntento = table.Column<int>(type: "integer", nullable: false),
                    NombreCompleto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actividad = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Tema = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Porcentaje = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    FechaAprobacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_constancias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_constancias_intentos_curso_voluntario_IdIntento",
                        column: x => x.IdIntento,
                        principalTable: "intentos_curso_voluntario",
                        principalColumn: "id_intento",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_constancias_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "encuestas_cultura",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Periodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Preguntas = table.Column<string>(type: "jsonb", nullable: false),
                    CreadoPor = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encuestas_cultura", x => x.Id);
                    table.CheckConstraint("CK_encuestas_estado", "\"Estado\" IN ('borrador','abierta','cerrada')");
                    table.ForeignKey(
                        name: "FK_encuestas_cultura_usuarios_CreadoPor",
                        column: x => x.CreadoPor,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_eventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdCategoria = table.Column<int>(type: "integer", nullable: true),
                    IdSucursal = table.Column<int>(type: "integer", nullable: true),
                    Descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Temporada = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IdCampania = table.Column<int>(type: "integer", nullable: true),
                    CreadoPor = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_eventos", x => x.Id);
                    table.CheckConstraint("CK_plan_estado", "\"Estado\" IN ('planificado','realizado','cancelado')");
                    table.CheckConstraint("CK_plan_mes", "\"Mes\" BETWEEN 1 AND 12 AND \"Anio\" BETWEEN 2020 AND 2100");
                    table.CheckConstraint("CK_plan_tipo", "\"Tipo\" IN ('campania','capacitacion','encuesta','comunicacion','temporada')");
                    table.ForeignKey(
                        name: "FK_plan_eventos_campanias_simulacion_IdCampania",
                        column: x => x.IdCampania,
                        principalTable: "campanias_simulacion",
                        principalColumn: "id_campania",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_eventos_categorias_pregunta_IdCategoria",
                        column: x => x.IdCategoria,
                        principalTable: "categorias_pregunta",
                        principalColumn: "id_categoria",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_eventos_sucursales_IdSucursal",
                        column: x => x.IdSucursal,
                        principalTable: "sucursales",
                        principalColumn: "id_sucursal",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_eventos_usuarios_CreadoPor",
                        column: x => x.CreadoPor,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recordatorios_aprendizaje",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Pendientes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recordatorios_aprendizaje", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recordatorios_aprendizaje_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rutas_aprendizaje",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdArea = table.Column<int>(type: "integer", nullable: true),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rutas_aprendizaje", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rutas_aprendizaje_areas_operativas_IdArea",
                        column: x => x.IdArea,
                        principalTable: "areas_operativas",
                        principalColumn: "id_area",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "participaciones_encuesta",
                columns: table => new
                {
                    IdEncuesta = table.Column<int>(type: "integer", nullable: false),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participaciones_encuesta", x => new { x.IdEncuesta, x.IdUsuario });
                    table.ForeignKey(
                        name: "FK_participaciones_encuesta_encuestas_cultura_IdEncuesta",
                        column: x => x.IdEncuesta,
                        principalTable: "encuestas_cultura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_participaciones_encuesta_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "respuestas_encuesta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdEncuesta = table.Column<int>(type: "integer", nullable: false),
                    IdSucursal = table.Column<int>(type: "integer", nullable: true),
                    IdArea = table.Column<int>(type: "integer", nullable: true),
                    Respuestas = table.Column<string>(type: "jsonb", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_respuestas_encuesta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_respuestas_encuesta_areas_operativas_IdArea",
                        column: x => x.IdArea,
                        principalTable: "areas_operativas",
                        principalColumn: "id_area",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_respuestas_encuesta_encuestas_cultura_IdEncuesta",
                        column: x => x.IdEncuesta,
                        principalTable: "encuestas_cultura",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_respuestas_encuesta_sucursales_IdSucursal",
                        column: x => x.IdSucursal,
                        principalTable: "sucursales",
                        principalColumn: "id_sucursal",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rutas_requisitos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdRuta = table.Column<int>(type: "integer", nullable: false),
                    IdActividad = table.Column<int>(type: "integer", nullable: false),
                    EsInduccion = table.Column<bool>(type: "boolean", nullable: false),
                    PlazoDias = table.Column<int>(type: "integer", nullable: false),
                    FrecuenciaDias = table.Column<int>(type: "integer", nullable: false),
                    VigenteDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rutas_requisitos", x => x.Id);
                    table.CheckConstraint("CK_requisitos_plazos", "\"PlazoDias\" BETWEEN 1 AND 365 AND \"FrecuenciaDias\" BETWEEN 0 AND 730");
                    table.ForeignKey(
                        name: "FK_rutas_requisitos_actividades_educativas_IdActividad",
                        column: x => x.IdActividad,
                        principalTable: "actividades_educativas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rutas_requisitos_rutas_aprendizaje_IdRuta",
                        column: x => x.IdRuta,
                        principalTable: "rutas_aprendizaje",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_constancias_Codigo",
                table: "constancias",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_constancias_IdIntento",
                table: "constancias",
                column: "IdIntento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_constancias_IdUsuario",
                table: "constancias",
                column: "IdUsuario");

            migrationBuilder.CreateIndex(
                name: "IX_encuestas_cultura_CreadoPor",
                table: "encuestas_cultura",
                column: "CreadoPor");

            migrationBuilder.CreateIndex(
                name: "IX_participaciones_encuesta_IdUsuario",
                table: "participaciones_encuesta",
                column: "IdUsuario");

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_Anio_Mes",
                table: "plan_eventos",
                columns: new[] { "Anio", "Mes" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_Anio_Temporada",
                table: "plan_eventos",
                columns: new[] { "Anio", "Temporada" },
                unique: true,
                filter: "\"Temporada\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_CreadoPor",
                table: "plan_eventos",
                column: "CreadoPor");

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_IdCampania",
                table: "plan_eventos",
                column: "IdCampania");

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_IdCategoria",
                table: "plan_eventos",
                column: "IdCategoria");

            migrationBuilder.CreateIndex(
                name: "IX_plan_eventos_IdSucursal",
                table: "plan_eventos",
                column: "IdSucursal");

            migrationBuilder.CreateIndex(
                name: "IX_recordatorios_aprendizaje_IdUsuario_Fecha",
                table: "recordatorios_aprendizaje",
                columns: new[] { "IdUsuario", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_respuestas_encuesta_IdArea",
                table: "respuestas_encuesta",
                column: "IdArea");

            migrationBuilder.CreateIndex(
                name: "IX_respuestas_encuesta_IdEncuesta",
                table: "respuestas_encuesta",
                column: "IdEncuesta");

            migrationBuilder.CreateIndex(
                name: "IX_respuestas_encuesta_IdSucursal",
                table: "respuestas_encuesta",
                column: "IdSucursal");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_aprendizaje_IdArea",
                table: "rutas_aprendizaje",
                column: "IdArea");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_requisitos_IdActividad",
                table: "rutas_requisitos",
                column: "IdActividad");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_requisitos_IdRuta_IdActividad",
                table: "rutas_requisitos",
                columns: new[] { "IdRuta", "IdActividad" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "constancias");

            migrationBuilder.DropTable(
                name: "participaciones_encuesta");

            migrationBuilder.DropTable(
                name: "plan_eventos");

            migrationBuilder.DropTable(
                name: "recordatorios_aprendizaje");

            migrationBuilder.DropTable(
                name: "respuestas_encuesta");

            migrationBuilder.DropTable(
                name: "rutas_requisitos");

            migrationBuilder.DropTable(
                name: "encuestas_cultura");

            migrationBuilder.DropTable(
                name: "rutas_aprendizaje");
        }
    }
}
