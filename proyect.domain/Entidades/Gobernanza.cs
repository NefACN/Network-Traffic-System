namespace proyect.domain.Entidades;

// reglas eticas de simulaciones, una sola fila y cada cambio incrementa la revision
public sealed class PoliticaGobernanza
{
    public int Id { get; set; } = 1;
    public int Revision { get; set; } = 1;
    // aprobacion en dos pasos: quien crea o solicita la campania no puede aprobarla
    public bool RequiereAprobacion { get; set; } = true;
    public bool VentanaActiva { get; set; } = true;
    // hora local desde la que se permite iniciar envios
    public int HoraInicio { get; set; } = 8;
    // hora local hasta la que se permite iniciar envios
    public int HoraFin { get; set; } = 18;
    public bool SoloDiasHabiles { get; set; } = true;
    // simulaciones maximas por persona en 30 dias, 0 desactiva el limite
    public int MaxSimulacionesPorPersona { get; set; } = 2;
    // tamano minimo de grupo que puede ver un gerente
    public int UmbralAgregacion { get; set; } = 5;
    // lista JSON de terminos de pretexto prohibidos
    public string PretextosProhibidos { get; set; } = "[]";
    public DateTime ActualizadaEn { get; set; } = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
}

// persona excluida temporalmente de las simulaciones
public sealed class ExclusionSimulacion
{
    public int Id { get; set; }
    public int IdUsuario { get; set; }
    // licencia, vacaciones, incidente_reciente, solicitud, otro
    public string Motivo { get; set; } = "otro";
    public string? Nota { get; set; }
    public DateTime Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public bool Activa { get; set; } = true;
    public int CreadoPor { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public ApplicationUser Usuario { get; set; } = null!;
}

// mensaje real reportado por un colaborador (ISO/IEC 27002 6.8 y 5.24-5.27)
public sealed class IncidenteReportado
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public int IdUsuario { get; set; }
    public int? IdSucursalRegistro { get; set; }
    public int? IdAreaRegistro { get; set; }
    // correo, sms, whatsapp, llamada, otro
    public string Canal { get; set; } = "correo";
    public string? Remitente { get; set; }
    public string Asunto { get; set; } = "";
    public string Descripcion { get; set; } = "";
    // ninguna, clic, adjunto, datos
    public string Interaccion { get; set; } = "ninguna";
    // baja, media, alta
    public string Prioridad { get; set; } = "baja";
    // recibido, en_analisis, resuelto
    public string Estado { get; set; } = "recibido";
    // malicioso, spam, legitimo, simulacion (al resolver)
    public string? Clasificacion { get; set; }
    // respuesta visible para quien reporto
    public string? Respuesta { get; set; }
    public int? IdAnalista { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaAtencion { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;
    public List<IncidenteSeguimiento> Seguimientos { get; set; } = new();
}

public sealed class IncidenteSeguimiento
{
    public long Id { get; set; }
    public int IdIncidente { get; set; }
    public int? IdUsuario { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string? EstadoAnterior { get; set; }
    public string EstadoNuevo { get; set; } = "";
    public string? Nota { get; set; }
    // visible para quien reporto, las notas internas solo las ve seguridad
    public bool Publica { get; set; }
    public IncidenteReportado Incidente { get; set; } = null!;
}
