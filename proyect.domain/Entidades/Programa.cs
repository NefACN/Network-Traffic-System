namespace proyect.domain.Entidades;

// ruta de aprendizaje obligatoria por area, sin area aplica a todo el personal
public sealed class RutaAprendizaje
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public int? IdArea { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public List<RutaRequisito> Requisitos { get; set; } = new();
}

// actividad requerida en una ruta: plazo y frecuencia de refresco
public sealed class RutaRequisito
{
    public int Id { get; set; }
    public int IdRuta { get; set; }
    public int IdActividad { get; set; }
    // induccion: el plazo corre desde el alta de la persona
    public bool EsInduccion { get; set; }
    public int PlazoDias { get; set; } = 30;
    // dias hasta el siguiente refresco, 0 se completa una sola vez
    public int FrecuenciaDias { get; set; }
    public DateTime VigenteDesde { get; set; } = DateTime.UtcNow;
    public RutaAprendizaje Ruta { get; set; } = null!;
    public ActividadEducativa Actividad { get; set; } = null!;
}

public sealed class RecordatorioAprendizaje
{
    public long Id { get; set; }
    public int IdUsuario { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public int Pendientes { get; set; }
}

// constancia verificable de una actividad aprobada
public sealed class Constancia
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public int IdUsuario { get; set; }
    public int IdIntento { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Actividad { get; set; } = "";
    public string Tema { get; set; } = "";
    public decimal? Porcentaje { get; set; }
    public DateTime FechaAprobacion { get; set; }
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
}

// actividad del calendario anual del programa
public sealed class EventoPlan
{
    public int Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public string Titulo { get; set; } = "";
    // campania, capacitacion, encuesta, comunicacion, temporada
    public string Tipo { get; set; } = "campania";
    public int? IdCategoria { get; set; }
    public int? IdSucursal { get; set; }
    public string? Descripcion { get; set; }
    // planificado, realizado, cancelado
    public string Estado { get; set; } = "planificado";
    // clave de la temporada sugerida que origino el evento (evita duplicados)
    public string? Temporada { get; set; }
    public int? IdCampania { get; set; }
    public int CreadoPor { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

// encuesta de cultura de seguridad con escala Likert (nivel 1 de Kirkpatrick)
public sealed class EncuestaCultura
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Periodo { get; set; } = "";
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    // borrador, abierta, cerrada
    public string Estado { get; set; } = "borrador";
    // lista JSON: id, texto, dimension
    public string Preguntas { get; set; } = "[]";
    public int CreadoPor { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

// solo registra que la persona respondio, sin vincularla con su respuesta
public sealed class ParticipacionEncuesta
{
    public int IdEncuesta { get; set; }
    public int IdUsuario { get; set; }
}

// respuesta anonima: id aleatorio, fecha sin hora y contexto para agregados
public sealed class RespuestaEncuesta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int IdEncuesta { get; set; }
    public int? IdSucursal { get; set; }
    public int? IdArea { get; set; }
    // JSON: id de pregunta -> valor 1-5
    public string Respuestas { get; set; } = "{}";
    public DateOnly Fecha { get; set; }
}
