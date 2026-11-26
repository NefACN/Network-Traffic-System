using System.ComponentModel.DataAnnotations;

namespace proyect.application.Dtos
{
    public record PlantillaDto(
        int IdPlantilla,
        string Nombre,
        string Asunto,
        string NivelCriticidad,
        bool Habilitada = true,
        int CantidadSenales = 0,
        string AlineacionGeneral = "media",
        string PaginaSenuelo = "ninguna",
        string? DificultadGeneral = null
    );

    public record CrearPlantillaRequest(
        [Required, StringLength(100)] string Nombre,
        [Required, StringLength(150)] string AsuntoCorreo,
        [Required, StringLength(200000)] string CuerpoHtml,
        [Range(1, int.MaxValue)] int IdCriticidad,
        [Range(1, int.MaxValue)] int IdCategoriaPregunta,
        bool Habilitada = true,
        // null conserva las senales y alineaciones existentes (clientes anteriores)
        List<SenalAlertaRequest>? Senales = null,
        List<AlineacionAreaRequest>? Alineaciones = null,
        [RegularExpression("^(debil|media|fuerte)$")] string AlineacionGeneral = "media",
        [RegularExpression("^(ninguna|credenciales)$")] string PaginaSenuelo = "ninguna",
        [StringLength(120)] string? RemitenteNombre = null,
        [StringLength(254)] string? RemitenteCorreo = null,
        [StringLength(300)] string? EnlaceMostrado = null
    );
    public record SenalAlertaRequest(
        [Required, RegularExpression("^(errores|tecnico|visual|lenguaje|tacticas)$")] string Tipo,
        [Required, StringLength(300)] string Descripcion,
        [StringLength(500)] string? Fragmento = null
    );
    public record AlineacionAreaRequest(
        [Range(1, int.MaxValue)] int IdArea,
        [Required, RegularExpression("^(debil|media|fuerte)$")] string Nivel
    );
    public record SenalAlertaDto(string Tipo, string Descripcion, string? Fragmento);
    public record DificultadAreaDto(int IdArea, string Area, bool Activa, string Alineacion, bool Especifica, string? Dificultad, string Etiqueta);
    public record PhishScaleDto(
        int CantidadSenales,
        string CategoriaSenales,
        string AlineacionGeneral,
        string? DificultadGeneral,
        List<DificultadAreaDto> PorArea,
        Dictionary<string, int> SenalesPorTipo
        );

    public record PlantillaDetalleDto(
        int IdPlantilla,
        string Nombre,
        string AsuntoCorreo,
        string CuerpoHtml,
        int IdCriticidad,
        int IdCategoriaPregunta,
        bool Habilitada,
        string AlineacionGeneral,
        string PaginaSenuelo,
        List<SenalAlertaDto> Senales,
        List<AlineacionAreaRequest> Alineaciones,
        PhishScaleDto PhishScale,
        string? RemitenteNombre = null,
        string? RemitenteCorreo = null,
        string? EnlaceMostrado = null
        );

    // resultado de la inspeccion interactiva: senales y anatomia del enlace
    public record InspeccionRequest(
        [Range(0, 50)] int Encontradas,
        [Range(0, 50)] int Total,
        [Range(0, 500)] int Fallos,
        [Range(0, 10)] int AciertosEnlace,
        [Range(0, 10)] int PreguntasEnlace,
        List<string>? Omitidas = null
        );

    public record CampaniaListItemDto(
        int IdCampania,
        string Titulo,
        string Plantilla,
        string Estado,
        DateTime? FechaInicio,
        DateTime? FechaFin,
        int TotalAreasSegmentadas,
        string? SucursalObjetivo = null,
        int? IdSucursalObjetivo = null,
        int IdEstado = 1,
        string Objetivo = "",
        string TipoEvaluacion = "sin_clasificar",
        DateTime? FechaFinalizacion = null,
        int TotalEnvios = 0,
        int EnviosAceptados = 0,
        int EnviosFallidos = 0,
        int EnviosPendientes = 0,
        string EstadoAprobacion = "borrador",
        bool PuedeRevisar = false
    );

    public record CampaniaDetalleDto(
        int IdCampania,
        string Titulo,
        string Plantilla,
        string Estado,
        string Creador,
        DateTime? FechaInicio,
        DateTime? FechaFin,
        List<string> AreasSegmentadas,
        int TotalEnviosGenerados,
        int TotalCorreosAbiertos,
        int TotalClicsVulnerados,
        string? SucursalObjetivo = null,
        int? IdSucursalObjetivo = null,
        int IdPlantilla = 0,
        int IdEstado = 1,
        string Objetivo = "",
        string TipoEvaluacion = "sin_clasificar",
        List<int>? IdsAreas = null,
        List<int>? IdsParticipantes = null,
        DateTime? FechaFinalizacion = null,
        bool FinalizacionAnticipada = false,
        int EnviosAceptados = 0,
        int EnviosFallidos = 0,
        int EnviosPendientes = 0,
        string? AsuntoRegistro = null,
        string? CuerpoHtmlRegistro = null,
        int TotalReportes = 0,
        int ReportesSinClickPrevio = 0,
        int NivelDificultad = 1,
        int TotalCredenciales = 0,
        string? PaginaSenuelo = null,
        string? PhishScaleRegistro = null,
        string EstadoAprobacion = "borrador",
        string? Solicitante = null,
        DateTime? FechaSolicitud = null,
        string? Revisor = null,
        DateTime? FechaRevision = null,
        string? ComentarioRevision = null,
        bool PuedeRevisar = false
    );

    public record CrearCampaniaRequest(
        [Required, StringLength(100)] string Titulo,
        [Range(1, int.MaxValue)] int IdPlantilla,
        DateTime? FechaInicioProgramada,
        DateTime? FechaFinProgramada,
        [Required, MinLength(1)] List<int> AreasSeleccionadas,
        int? IdSucursalObjetivo = null,
        [Required, StringLength(1000)] string Objetivo = "",
        [Required, RegularExpression("^(inicial|posterior|seguimiento)$")] string TipoEvaluacion = "",
        List<int>? ParticipantesSeleccionados = null,
        [Range(1, 5)] int NivelDificultad = 1
    );

    public record EstadoPlantillaRequest(bool Habilitada);
    public record FinalizarCampaniaRequest(bool Confirmar);
    public record ParticipantesCampaniaRequest(
        [Required, MinLength(1)] List<int> AreasSeleccionadas,
        int? IdSucursalObjetivo = null);
}

namespace proyect.application.Dtos
{
    public record PoliticaGobernanzaRequest(
        bool RequiereAprobacion, bool VentanaActiva,
        [System.ComponentModel.DataAnnotations.Range(0, 23)] int HoraInicio,
        [System.ComponentModel.DataAnnotations.Range(1, 24)] int HoraFin,
        bool SoloDiasHabiles,
        [System.ComponentModel.DataAnnotations.Range(0, 30)] int MaxSimulacionesPorPersona,
        [System.ComponentModel.DataAnnotations.Range(1, 50)] int UmbralAgregacion,
        List<string>? PretextosProhibidos);

    public record ExclusionRequest(
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int IdUsuario,
        [System.ComponentModel.DataAnnotations.Required] string Motivo,
        [System.ComponentModel.DataAnnotations.StringLength(200)] string? Nota = null,
        DateTime? Desde = null, DateTime? Hasta = null);

    public record RevisionCampaniaRequest(
        bool Aprobar,
        [System.ComponentModel.DataAnnotations.StringLength(500)] string? Comentario = null);
    public record ReporteIncidenteRequest(
        [System.ComponentModel.DataAnnotations.Required] string Canal,
        [System.ComponentModel.DataAnnotations.StringLength(200)] string? Remitente,
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string Asunto,
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(2000)] string Descripcion,
        [System.ComponentModel.DataAnnotations.Required] string Interaccion);
    public record ActualizarIncidenteRequest(
        string? Estado,
        [System.ComponentModel.DataAnnotations.StringLength(2000)] string? Nota,
        bool NotaPublica = false,
        string? Clasificacion = null,
        [System.ComponentModel.DataAnnotations.StringLength(2000)] string? Respuesta = null,
        string? Prioridad = null);
}
