using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CicloHttpConcientizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_asignaciones_microleccion_IdEnvio",
                table: "asignaciones_microleccion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion");

            migrationBuilder.AddColumn<int>(
                name: "IdCampaniaSimulacion",
                table: "http_datasets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdSucursal",
                table: "http_datasets",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "IdEnvio",
                table: "asignaciones_microleccion",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "IdSugerencia",
                table: "asignaciones_microleccion",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "interacciones_simulacion_http",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdEnvio = table.Column<int>(type: "integer", nullable: false),
                    IdCampania = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    Metodo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ruta = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    DuracionMs = table.Column<double>(type: "double precision", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Referer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interacciones_simulacion_http", x => x.Id);
                    table.CheckConstraint("CK_interacciones_http_tipo", "\"Tipo\" IN ('apertura','clic','credenciales')");
                    table.ForeignKey(
                        name: "FK_interacciones_simulacion_http_campanias_simulacion_IdCampan~",
                        column: x => x.IdCampania,
                        principalTable: "campanias_simulacion",
                        principalColumn: "id_campania",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_interacciones_simulacion_http_envios_simulacion_IdEnvio",
                        column: x => x.IdEnvio,
                        principalTable: "envios_simulacion",
                        principalColumn: "id_envio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sugerencias_concientizacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdSucursal = table.Column<int>(type: "integer", nullable: true),
                    IdArea = table.Column<int>(type: "integer", nullable: true),
                    IdCategoria = table.Column<int>(type: "integer", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Evidencias = table.Column<string>(type: "jsonb", nullable: false),
                    CantidadEvidencias = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaEvidencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AtendidaPor = table.Column<int>(type: "integer", nullable: true),
                    FechaAtencion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdActividad = table.Column<int>(type: "integer", nullable: true),
                    PersonasAsignadas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sugerencias_concientizacion", x => x.Id);
                    table.CheckConstraint("CK_sugerencias_estado", "\"Estado\" IN ('pendiente','campania','microleccion','descartada')");
                    table.CheckConstraint("CK_sugerencias_origen", "\"Origen\" IN ('http','incidente')");
                    table.ForeignKey(
                        name: "FK_sugerencias_concientizacion_actividades_educativas_IdActivi~",
                        column: x => x.IdActividad,
                        principalTable: "actividades_educativas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sugerencias_concientizacion_areas_operativas_IdArea",
                        column: x => x.IdArea,
                        principalTable: "areas_operativas",
                        principalColumn: "id_area",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sugerencias_concientizacion_categorias_pregunta_IdCategoria",
                        column: x => x.IdCategoria,
                        principalTable: "categorias_pregunta",
                        principalColumn: "id_categoria",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sugerencias_concientizacion_sucursales_IdSucursal",
                        column: x => x.IdSucursal,
                        principalTable: "sucursales",
                        principalColumn: "id_sucursal",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sugerencias_concientizacion_usuarios_AtendidaPor",
                        column: x => x.AtendidaPor,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lineas_simulacion_http",
                columns: table => new
                {
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Linea = table.Column<long>(type: "bigint", nullable: false),
                    IdInteraccion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lineas_simulacion_http", x => new { x.DatasetId, x.Linea });
                    table.ForeignKey(
                        name: "FK_lineas_simulacion_http_http_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "http_datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lineas_simulacion_http_interacciones_simulacion_http_IdInte~",
                        column: x => x.IdInteraccion,
                        principalTable: "interacciones_simulacion_http",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_http_datasets_IdCampaniaSimulacion",
                table: "http_datasets",
                column: "IdCampaniaSimulacion");

            migrationBuilder.CreateIndex(
                name: "IX_http_datasets_IdSucursal",
                table: "http_datasets",
                column: "IdSucursal");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdEnvio",
                table: "asignaciones_microleccion",
                column: "IdEnvio",
                unique: true,
                filter: "\"IdEnvio\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdSugerencia_IdUsuario",
                table: "asignaciones_microleccion",
                columns: new[] { "IdSugerencia", "IdUsuario" },
                unique: true,
                filter: "\"IdSugerencia\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion",
                sql: "\"Motivo\" IN ('clic','credenciales','sugerencia')");

            migrationBuilder.CreateIndex(
                name: "IX_interacciones_simulacion_http_IdCampania_Fecha",
                table: "interacciones_simulacion_http",
                columns: new[] { "IdCampania", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_interacciones_simulacion_http_IdEnvio",
                table: "interacciones_simulacion_http",
                column: "IdEnvio");

            migrationBuilder.CreateIndex(
                name: "IX_lineas_simulacion_http_IdInteraccion",
                table: "lineas_simulacion_http",
                column: "IdInteraccion");

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_AtendidaPor",
                table: "sugerencias_concientizacion",
                column: "AtendidaPor");

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_Estado_IdSucursal_IdArea",
                table: "sugerencias_concientizacion",
                columns: new[] { "Estado", "IdSucursal", "IdArea" });

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_IdActividad",
                table: "sugerencias_concientizacion",
                column: "IdActividad");

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_IdArea",
                table: "sugerencias_concientizacion",
                column: "IdArea");

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_IdCategoria",
                table: "sugerencias_concientizacion",
                column: "IdCategoria");

            migrationBuilder.CreateIndex(
                name: "IX_sugerencias_concientizacion_IdSucursal",
                table: "sugerencias_concientizacion",
                column: "IdSucursal");

            migrationBuilder.AddForeignKey(
                name: "FK_asignaciones_microleccion_sugerencias_concientizacion_IdSug~",
                table: "asignaciones_microleccion",
                column: "IdSugerencia",
                principalTable: "sugerencias_concientizacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_http_datasets_campanias_simulacion_IdCampaniaSimulacion",
                table: "http_datasets",
                column: "IdCampaniaSimulacion",
                principalTable: "campanias_simulacion",
                principalColumn: "id_campania",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_http_datasets_sucursales_IdSucursal",
                table: "http_datasets",
                column: "IdSucursal",
                principalTable: "sucursales",
                principalColumn: "id_sucursal",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_asignaciones_microleccion_sugerencias_concientizacion_IdSug~",
                table: "asignaciones_microleccion");

            migrationBuilder.DropForeignKey(
                name: "FK_http_datasets_campanias_simulacion_IdCampaniaSimulacion",
                table: "http_datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_http_datasets_sucursales_IdSucursal",
                table: "http_datasets");

            migrationBuilder.DropTable(
                name: "lineas_simulacion_http");

            migrationBuilder.DropTable(
                name: "sugerencias_concientizacion");

            migrationBuilder.DropTable(
                name: "interacciones_simulacion_http");

            migrationBuilder.DropIndex(
                name: "IX_http_datasets_IdCampaniaSimulacion",
                table: "http_datasets");

            migrationBuilder.DropIndex(
                name: "IX_http_datasets_IdSucursal",
                table: "http_datasets");

            migrationBuilder.DropIndex(
                name: "IX_asignaciones_microleccion_IdEnvio",
                table: "asignaciones_microleccion");

            migrationBuilder.DropIndex(
                name: "IX_asignaciones_microleccion_IdSugerencia_IdUsuario",
                table: "asignaciones_microleccion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion");

            migrationBuilder.DropColumn(
                name: "IdCampaniaSimulacion",
                table: "http_datasets");

            migrationBuilder.DropColumn(
                name: "IdSucursal",
                table: "http_datasets");

            migrationBuilder.DropColumn(
                name: "IdSugerencia",
                table: "asignaciones_microleccion");

            migrationBuilder.AlterColumn<int>(
                name: "IdEnvio",
                table: "asignaciones_microleccion",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_asignaciones_microleccion_IdEnvio",
                table: "asignaciones_microleccion",
                column: "IdEnvio",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_asignaciones_motivo",
                table: "asignaciones_microleccion",
                sql: "\"Motivo\" IN ('clic','credenciales')");
        }
    }
}
