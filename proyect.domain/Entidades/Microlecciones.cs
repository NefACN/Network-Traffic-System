namespace proyect.domain.Entidades;

// microleccion asignada automaticamente tras un clic o envio de credenciales
public sealed class AsignacionMicroleccion
{
    public int Id { get; set; }
    public int IdUsuario { get; set; }
    // envio de simulacion que origino la asignacion, null si viene de una sugerencia
    public int? IdEnvio { get; set; }
    public int? IdSugerencia { get; set; }
    public int IdActividad { get; set; }
    // clic, credenciales, sugerencia o critico (plan de refuerzo)
    public string Motivo { get; set; } = "clic";
    public DateTime FechaAsignacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCompletado { get; set; }
    public int? IdIntento { get; set; }
    public ActividadEducativa Actividad { get; set; } = null!;
}

// pesos del CyberScore, una sola fila y cada cambio incrementa la revision
public sealed class PoliticaCyberScore
{
    public int Id { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public decimal PesoClic { get; set; } = 10m;
    public decimal PesoCredenciales { get; set; } = 20m;
    public decimal BonoReporte { get; set; } = 8m;
    public decimal BonoCuestionario { get; set; } = 4m;
    public decimal BonoMicroleccion { get; set; } = 3m;
    public int VidaMediaDias { get; set; } = 60;
    public int VentanaDias { get; set; } = 180;
    public int MinimoSimulaciones { get; set; } = 3;
    // multiplica el peso de caer en un mensaje poco dificil, el reporte usa 2 - factor
    public decimal FactorPocoDificil { get; set; } = 1.25m;
    public decimal FactorMuyDificil { get; set; } = 0.75m;
    public decimal ExposicionBaja { get; set; } = 0.8m;
    public decimal ExposicionMedia { get; set; } = 1.0m;
    public decimal ExposicionAlta { get; set; } = 1.3m;
    // umbrales de nivel: CyberScore >= UmbralBajo -> Bajo, >= Medio, >= Alto, resto Critico
    public decimal UmbralBajo { get; set; } = 80m;
    public decimal UmbralMedio { get; set; } = 60m;
    public decimal UmbralAlto { get; set; } = 40m;
    // maximo de microlecciones del plan de refuerzo asignadas a la vez en nivel Critico
    public int MaximoPlanCritico { get; set; } = 3;
    public DateTime ActualizadaEn { get; set; } = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
}
