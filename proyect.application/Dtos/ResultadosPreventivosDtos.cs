namespace proyect.application.Dtos;

public sealed record ResultadosFiltro(
    int? IdSucursal = null,
    int? IdArea = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null
    );

public sealed record ParticipanteResultadoDto(
    int IdEnvio,
    int IdUsuario,
    string Nombre,
    string? Correo,
    int? IdSucursal,
    string? Sucursal,
    int? IdArea,
    string? Area,
    string EstadoEntrega,
    bool ContextoRegistrado,
    DateTime? FechaEnvio,
    bool FueAbierto,
    DateTime? FechaApertura,
    bool HizoClick,
    DateTime? FechaVulneracion,
    DateTime? FechaReporte,
    bool ReporteSinClickPrevio,
    bool CapacitacionCompleta,
    DateTime? FechaCapacitacion,
    int PuntosRecuperados,
    int? IdCategoria,
    bool IngresoCredenciales = false,
    DateTime? FechaIngresoCredenciales = null,
    string? DificultadNist = null,
    string? Alineacion = null
    );

public sealed record IndicadoresPareadosDto(
    int Participantes,
    int Aperturas,
    int Clics,
    int Reportes,
    int ReportesSinClickPrevio,
    decimal? TasaApertura,
    decimal? TasaClic,
    decimal? TasaReporte,
    int Credenciales = 0,
    decimal? TasaCredenciales = null
    );
