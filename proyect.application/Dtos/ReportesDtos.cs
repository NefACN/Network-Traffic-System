using System.ComponentModel.DataAnnotations;

namespace proyect.application.Dtos;

public sealed record GenerarReporteRequest(
    [Required, RegularExpression("^(preventivo|http)$")] string Tipo,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    int? IdSucursal = null,
    int? IdArea = null,
    Guid? IdEjecucion = null,
    bool SoloCandidatas = false
    );

public sealed record IndicadorReporte(string Nombre, string Valor);

public sealed record TablaReporte(string Titulo, IReadOnlyList<string> Columnas, IReadOnlyList<IReadOnlyList<string>> Filas);

public sealed record DocumentoReporte(
    Guid Id,
    string Tipo,
    DateTime GeneradoEn,
    DateTime Desde,
    DateTime Hasta,
    bool Parcial,
    IReadOnlyList<IndicadorReporte> Criterios,
    IReadOnlyList<IndicadorReporte> Indicadores,
    IReadOnlyList<TablaReporte> Tablas,
    IReadOnlyList<string> Notas
    );
