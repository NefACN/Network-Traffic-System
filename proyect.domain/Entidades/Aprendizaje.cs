namespace proyect.domain.Entidades;

public sealed class ActividadEducativa
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public int IdCategoria { get; set; }
    public CategoriaPregunta Categoria { get; set; } = null!;
    public string Secciones { get; set; } = "[]";
    public string PreguntasPublicadas { get; set; } = "[]";
    public bool Habilitada { get; set; }
    public int Revision { get; set; } = 1;
    public int Puntos { get; set; } = 20;
    public int DiasRepeticion { get; set; } = 7;
    public int PorcentajeAprobacion { get; set; } = 70;
    public bool PermitirRepaso { get; set; } = true;
    // leccion breve (menos de 2 minutos) que se asigna tras caer en una simulacion
    public bool EsMicroleccion { get; set; }
    // microleccion preferente cuando la persona envio el formulario senuelo
    public bool ParaCredenciales { get; set; }
    public DateTime ActualizadaEn { get; set; } = DateTime.UtcNow;
    public List<ActividadPregunta> Preguntas { get; set; } = new();
}
public sealed class ActividadPregunta
{
    public int IdActividad { get; set; }
    public int IdPregunta { get; set; }
    public ActividadEducativa Actividad { get; set; } = null!;
    public PreguntaConcientizacion Pregunta { get; set; } = null!;
}
public sealed class ReglasGamificacion
{
    public int Id { get; set; } = 1;
    public int PuntosCursoLibre { get; set; } = 20;
    public int DiasCursoLibre { get; set; } = 30;
    public int PuntosRefuerzo { get; set; }
    public int MetaSemanalTemas { get; set; } = 2;
    public int PuntosPorNivel { get; set; } = 100;
    // puntos por reportar una simulacion, una vez por envio
    public int PuntosReporte { get; set; } = 15;
    // puntos por completar una microleccion asignada o del plan de refuerzo
    public int PuntosMicroleccion { get; set; } = 10;
    // puntos por encontrar todas las senales en la inspeccion tras un clic, una vez por envio
    public int PuntosInspeccion { get; set; } = 10;
    public int Revision { get; set; } = 1;
}
public sealed class MovimientoPuntos
{
    public long Id { get; set; }
    public int IdUsuario { get; set; }
    public string Clave { get; set; } = "";
    public string Concepto { get; set; } = "";
    public int Puntos { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
