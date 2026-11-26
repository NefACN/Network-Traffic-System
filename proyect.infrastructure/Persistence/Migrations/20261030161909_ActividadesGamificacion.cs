using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActividadesGamificacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_intentos_curso_voluntario_id_usuario",
                table: "intentos_curso_voluntario");

            migrationBuilder.AddColumn<int>(
                name: "PuntosEducativos",
                table: "usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Aciertos",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AprobacionConfigurada",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContenidoRegistro",
                table: "intentos_curso_voluntario",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContenidoRevisado",
                table: "intentos_curso_voluntario",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CuestionarioRegistro",
                table: "intentos_curso_voluntario",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasRepeticionConfigurados",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEvaluacion",
                table: "intentos_curso_voluntario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdActividad",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Porcentaje",
                table: "intentos_curso_voluntario",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuntosConfigurados",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RespuestasRegistro",
                table: "intentos_curso_voluntario",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionActividad",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TituloRegistro",
                table: "intentos_curso_voluntario",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalPreguntas",
                table: "intentos_curso_voluntario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Activa",
                table: "insignias",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoCondicion",
                table: "insignias",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PuntosRefuerzoConfigurados",
                table: "envios_simulacion",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actividades_educativas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IdCategoria = table.Column<int>(type: "integer", nullable: false),
                    Secciones = table.Column<string>(type: "jsonb", nullable: false),
                    PreguntasPublicadas = table.Column<string>(type: "jsonb", nullable: false),
                    Habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Puntos = table.Column<int>(type: "integer", nullable: false),
                    DiasRepeticion = table.Column<int>(type: "integer", nullable: false),
                    PorcentajeAprobacion = table.Column<int>(type: "integer", nullable: false),
                    PermitirRepaso = table.Column<bool>(type: "boolean", nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actividades_educativas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_actividades_educativas_categorias_pregunta_IdCategoria",
                        column: x => x.IdCategoria,
                        principalTable: "categorias_pregunta",
                        principalColumn: "id_categoria",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_puntos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdUsuario = table.Column<int>(type: "integer", nullable: false),
                    Clave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Concepto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Puntos = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimientos_puntos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_movimientos_puntos_usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reglas_gamificacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PuntosCursoLibre = table.Column<int>(type: "integer", nullable: false),
                    DiasCursoLibre = table.Column<int>(type: "integer", nullable: false),
                    PuntosRefuerzo = table.Column<int>(type: "integer", nullable: false),
                    MetaSemanalTemas = table.Column<int>(type: "integer", nullable: false),
                    PuntosPorNivel = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reglas_gamificacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "actividad_preguntas",
                columns: table => new
                {
                    IdActividad = table.Column<int>(type: "integer", nullable: false),
                    IdPregunta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actividad_preguntas", x => new { x.IdActividad, x.IdPregunta });
                    table.ForeignKey(
                        name: "FK_actividad_preguntas_actividades_educativas_IdActividad",
                        column: x => x.IdActividad,
                        principalTable: "actividades_educativas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_actividad_preguntas_preguntas_concientizacion_IdPregunta",
                        column: x => x.IdPregunta,
                        principalTable: "preguntas_concientizacion",
                        principalColumn: "id_pregunta",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "insignias",
                keyColumn: "id_insignia",
                keyValue: 1,
                columns: new[] { "Activa", "TipoCondicion" },
                values: new object[] { true, "puntos" });

            migrationBuilder.UpdateData(
                table: "insignias",
                keyColumn: "id_insignia",
                keyValue: 2,
                columns: new[] { "Activa", "TipoCondicion" },
                values: new object[] { true, "puntos" });

            migrationBuilder.InsertData(
                table: "reglas_gamificacion",
                columns: new[] { "Id", "DiasCursoLibre", "MetaSemanalTemas", "PuntosCursoLibre", "PuntosPorNivel", "PuntosRefuerzo", "Revision" },
                values: new object[] { 1, 30, 2, 20, 100, 0, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_curso_voluntario_id_usuario_IdActividad",
                table: "intentos_curso_voluntario",
                columns: new[] { "id_usuario", "IdActividad" },
                unique: true,
                filter: "\"IdActividad\" IS NOT NULL AND fecha_completado IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_intentos_curso_voluntario_IdActividad",
                table: "intentos_curso_voluntario",
                column: "IdActividad");

            migrationBuilder.CreateIndex(
                name: "IX_actividad_preguntas_IdPregunta",
                table: "actividad_preguntas",
                column: "IdPregunta");

            migrationBuilder.CreateIndex(
                name: "IX_actividades_educativas_IdCategoria",
                table: "actividades_educativas",
                column: "IdCategoria");

            migrationBuilder.CreateIndex(
                name: "IX_movimientos_puntos_IdUsuario_Clave",
                table: "movimientos_puntos",
                columns: new[] { "IdUsuario", "Clave" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_curso_voluntario_actividades_educativas_IdActividad",
                table: "intentos_curso_voluntario",
                column: "IdActividad",
                principalTable: "actividades_educativas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Conservar el saldo previo sin reconstruir ni inventar eventos anteriores.
            migrationBuilder.Sql("""
                UPDATE usuarios SET "PuntosEducativos" = GREATEST(cyber_score, 0);
                INSERT INTO movimientos_puntos ("IdUsuario", "Clave", "Concepto", "Puntos", "Fecha")
                SELECT id_usuario, 'saldo-previo', 'Saldo previo a la nueva gamificación', "PuntosEducativos", now()
                FROM usuarios WHERE "PuntosEducativos" > 0;
                UPDATE insignias SET "TipoCondicion" = 'puntos' WHERE "TipoCondicion" = '';
                UPDATE intentos_curso_voluntario SET "PuntosConfigurados" = 20, "DiasRepeticionConfigurados" = 30
                WHERE "IdActividad" IS NULL;
                UPDATE envios_simulacion e SET "PuntosRefuerzoConfigurados" = LEAST(10000, GREATEST(0, 25 * c.nivel_dificultad))
                FROM campanias_simulacion c WHERE c.id_campania = e.id_campania AND e."PuntosRefuerzoConfigurados" IS NULL;
                LOCK TABLE insignias IN SHARE ROW EXCLUSIVE MODE;
                INSERT INTO insignias (id_insignia, nombre, descripcion, icono_svg, puntos_requeridos, "Activa", "TipoCondicion")
                SELECT (SELECT COALESCE(MAX(id_insignia), 0) FROM insignias) + ROW_NUMBER() OVER (), v.nombre, v.descripcion, '', v.objetivo, true, v.condicion
                FROM (VALUES
                    ('Primer paso', 'Aprueba tu primera actividad voluntaria de aprendizaje.', 1, 'actividades'),
                    ('Aprendiz constante', 'Participa y aprueba actividades en dos semanas diferentes.', 2, 'semanas')
                ) AS v(nombre, descripcion, objetivo, condicion)
                WHERE NOT EXISTS (SELECT 1 FROM insignias i WHERE lower(i.nombre) = lower(v.nombre));
                SELECT setval(pg_get_serial_sequence('insignias', 'id_insignia'), GREATEST((SELECT COALESCE(MAX(id_insignia), 1) FROM insignias), 1), true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_intentos_curso_voluntario_actividades_educativas_IdActividad",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropTable(
                name: "actividad_preguntas");

            migrationBuilder.DropTable(
                name: "movimientos_puntos");

            migrationBuilder.DropTable(
                name: "reglas_gamificacion");

            migrationBuilder.DropTable(
                name: "actividades_educativas");

            migrationBuilder.DropIndex(
                name: "IX_intentos_curso_voluntario_id_usuario_IdActividad",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropIndex(
                name: "IX_intentos_curso_voluntario_IdActividad",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "PuntosEducativos",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "Aciertos",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "AprobacionConfigurada",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "ContenidoRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "ContenidoRevisado",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "CuestionarioRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "DiasRepeticionConfigurados",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "FechaEvaluacion",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "IdActividad",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "Porcentaje",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "PuntosConfigurados",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "RespuestasRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "RevisionActividad",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "TituloRegistro",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "TotalPreguntas",
                table: "intentos_curso_voluntario");

            migrationBuilder.DropColumn(
                name: "Activa",
                table: "insignias");

            migrationBuilder.DropColumn(
                name: "TipoCondicion",
                table: "insignias");

            migrationBuilder.DropColumn(
                name: "PuntosRefuerzoConfigurados",
                table: "envios_simulacion");

            migrationBuilder.CreateIndex(
                name: "IX_intentos_curso_voluntario_id_usuario",
                table: "intentos_curso_voluntario",
                column: "id_usuario");
        }
    }
}
