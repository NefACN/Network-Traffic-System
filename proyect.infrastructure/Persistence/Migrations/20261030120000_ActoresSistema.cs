using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace proyect.infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261030120000_ActoresSistema")]
public sealed class ActoresSistema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE roles IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO roles (id_rol, nombre, descripcion, activo, fecha_creacion)
        SELECT (SELECT COALESCE(MAX(id_rol), 0) FROM roles) + ROW_NUMBER() OVER (), v.nombre, v.descripcion, true, now()
        FROM (VALUES
          ('Admin', 'Administración completa del sistema'),
          ('GerenteSucursal', 'Seguimiento preventivo consolidado de su sucursal, solo lectura'),
          ('Colaborador', 'Portal personal de concientización y aprendizaje')
        ) AS v(nombre, descripcion)
        WHERE NOT EXISTS (SELECT 1 FROM roles r WHERE r.nombre = v.nombre);
        SELECT setval(pg_get_serial_sequence('roles', 'id_rol'), (SELECT MAX(id_rol) FROM roles), true);
        """);

    // Mantiene el catálogo y sus asignaciones al revertir para no retirar permisos existentes.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
