using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/reportes")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class ReportesController(ReportesService service, IMemoryCache cache, IAuditoriaService auditoria) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos(CancellationToken ct) => Ok(await service.CatalogosAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Generar(GenerarReporteRequest request, CancellationToken ct)
    {
        var report = await service.GenerarAsync(request, Actor, ct);
        cache.Set($"report:{Actor}:{report.Id}", report, TimeSpan.FromMinutes(15));
        await auditoria.RegistrarAsync("GenerarReporte", true, Actor, entidad: "Reporte", idEntidadAfectada: report.Id.ToString(),
            detalles: new {
                report.Tipo,
                report.Desde,
                report.Hasta
            });
        return Ok(report);
    }

    [HttpGet("{id:guid}/exportar")]
    public async Task<IActionResult> Exportar(Guid id, string formato = "json")
    {
        if (formato is not ("json" or "csv"))
            return BadRequest(new { mensaje = "Selecciona JSON o CSV." });

        if (!cache.TryGetValue<DocumentoReporte>($"report:{Actor}:{id}", out var report) || report is null)
            return NotFound(new { mensaje = "El reporte no está disponible para tu cuenta o su copia expiró. Genera uno nuevo." });

        byte[] content;
        if (formato == "json")
            content = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        else
        {
            var csv = new StringBuilder();
            string Cell(string value)
            {
                var text = value.TrimStart();
                if (text.Length > 0 && "=+-@".Contains(text[0]) && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            void Row(IEnumerable<string> values) => csv.AppendLine(string.Join(",", values.Select(Cell)));
            Row(["Reporte", report.Tipo]);
            Row(["Generado UTC", report.GeneradoEn.ToString("O")]);
            Row(["Desde UTC", report.Desde.ToString("O")]);
            Row(["Hasta UTC", report.Hasta.ToString("O")]);
            Row(["Estado", report.Parcial ? "Parcial" : "Copia consolidada"]);

            foreach (var field in report.Criterios.Concat(report.Indicadores))
                Row([field.Nombre, field.Valor]);

            foreach (var table in report.Tablas)
            {
                Row([table.Titulo]); Row(table.Columnas);
                foreach (var row in table.Filas) Row(row);
            }

            foreach (var note in report.Notas)
                Row(["Nota", note]);
            content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }
        await auditoria.RegistrarAsync("ExportarReporte", true, Actor, entidad: "Reporte", idEntidadAfectada: id.ToString(), detalles: new { formato });
        return File(content, formato == "json" ? "application/json" : "text/csv; charset=utf-8", $"reporte-{report.Tipo}-{id.ToString("N")[..8]}.{formato}");
    }
}
