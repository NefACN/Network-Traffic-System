using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace proyect.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HttpAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "http_datasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Origin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_datasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_http_datasets_usuarios_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ml_modelos",
                columns: table => new
                {
                    Version = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ml_modelos", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "ml_ejecuciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ModelMetadata = table.Column<string>(type: "jsonb", nullable: true),
                    WindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    Speed = table.Column<double>(type: "double precision", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    To = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Read = table.Column<long>(type: "bigint", nullable: false),
                    Valid = table.Column<long>(type: "bigint", nullable: false),
                    Excluded = table.Column<long>(type: "bigint", nullable: false),
                    Rejected = table.Column<long>(type: "bigint", nullable: false),
                    Empty = table.Column<long>(type: "bigint", nullable: false),
                    LastLine = table.Column<long>(type: "bigint", nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reasons = table.Column<string>(type: "jsonb", nullable: false),
                    Checkpoint = table.Column<string>(type: "text", nullable: false),
                    Lease = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ml_ejecuciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ml_ejecuciones_http_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "http_datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ml_ejecuciones_ml_modelos_ModelVersion",
                        column: x => x.ModelVersion,
                        principalTable: "ml_modelos",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ml_ejecuciones_usuarios_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "http_resultados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Line = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Route = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Cluster = table.Column<int>(type: "integer", nullable: false),
                    Strength = table.Column<double>(type: "double precision", nullable: false),
                    AnomalyScore = table.Column<double>(type: "double precision", nullable: false),
                    Noise = table.Column<bool>(type: "boolean", nullable: false),
                    Candidate = table.Column<bool>(type: "boolean", nullable: false),
                    Threshold = table.Column<double>(type: "double precision", nullable: false),
                    Features = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_resultados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_http_resultados_ml_ejecuciones_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "ml_ejecuciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "http_revisiones",
                columns: table => new
                {
                    ResultId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AnalystId = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_revisiones", x => x.ResultId);
                    table.ForeignKey(
                        name: "FK_http_revisiones_http_resultados_ResultId",
                        column: x => x.ResultId,
                        principalTable: "http_resultados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_http_revisiones_usuarios_AnalystId",
                        column: x => x.AnalystId,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_http_datasets_CreatedAt",
                table: "http_datasets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_http_datasets_CreatedBy",
                table: "http_datasets",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_http_resultados_ExecutionId_Cluster",
                table: "http_resultados",
                columns: new[] { "ExecutionId", "Cluster" });

            migrationBuilder.CreateIndex(
                name: "IX_http_resultados_ExecutionId_Line",
                table: "http_resultados",
                columns: new[] { "ExecutionId", "Line" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_http_resultados_ExecutionId_Noise_Candidate",
                table: "http_resultados",
                columns: new[] { "ExecutionId", "Noise", "Candidate" });

            migrationBuilder.CreateIndex(
                name: "IX_http_resultados_ExecutionId_OccurredAt",
                table: "http_resultados",
                columns: new[] { "ExecutionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_http_resultados_ExecutionId_Subject",
                table: "http_resultados",
                columns: new[] { "ExecutionId", "Subject" });

            migrationBuilder.CreateIndex(
                name: "IX_http_revisiones_AnalystId",
                table: "http_revisiones",
                column: "AnalystId");

            migrationBuilder.CreateIndex(
                name: "IX_ml_ejecuciones_CreatedBy",
                table: "ml_ejecuciones",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ml_ejecuciones_DatasetId_CreatedAt",
                table: "ml_ejecuciones",
                columns: new[] { "DatasetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ml_ejecuciones_ModelVersion",
                table: "ml_ejecuciones",
                column: "ModelVersion");

            migrationBuilder.CreateIndex(
                name: "IX_ml_ejecuciones_State_LeaseUntil",
                table: "ml_ejecuciones",
                columns: new[] { "State", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "http_revisiones");

            migrationBuilder.DropTable(
                name: "http_resultados");

            migrationBuilder.DropTable(
                name: "ml_ejecuciones");

            migrationBuilder.DropTable(
                name: "http_datasets");

            migrationBuilder.DropTable(
                name: "ml_modelos");
        }
    }
}
