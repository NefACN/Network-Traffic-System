namespace proyect.application.Services;


// dificultad de deteccion segun NIST Phish Scale y NIST TN 2276
public static class PhishScale
{
    public const string Version = "nist-phish-scale-tn2276";

    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string> {
        ["errores"] = "Errores (ortografía, gramática, inconsistencias)",
        ["tecnico"] = "Indicadores técnicos (remitente, dominio, enlaces, adjuntos)",
        ["visual"] = "Presentación visual (logotipos, formato, diseño)",
        ["lenguaje"] = "Lenguaje y contenido (saludo genérico, petición de datos, tono)",
        ["tacticas"] = "Tácticas comunes (urgencia, autoridad, recompensa, amenaza)",
    };

    public static readonly IReadOnlyList<string> Alineaciones = ["debil", "media", "fuerte"];


    public static string CategoriaSenales(int cantidad) => cantidad switch {
        <= 8 => "pocas",
        <= 14 => "algunas",
        _ => "muchas",
    };

    // tabla de dificultad de NIST Phish Scale (cantidad de senales x alineacion)
    public static string? Dificultad(int cantidadSenales, string alineacion)
    {
        if (cantidadSenales < 1 || !Alineaciones.Contains(alineacion))
            return null;

        return (CategoriaSenales(cantidadSenales), alineacion) switch {
            ("pocas", "fuerte" or "media") => "muy_dificil",
            ("pocas", _) => "moderada",
            ("algunas", "fuerte") => "muy_dificil",
            ("algunas", "media") => "moderada",
            ("algunas", _) => "poco_dificil",
            (_, "fuerte") => "moderada",
            _ => "poco_dificil",
        };
    }

    public static string Etiqueta(string? dificultad) => dificultad switch {
        "muy_dificil" => "Muy difícil",
        "moderada" => "Moderadamente difícil",
        "poco_dificil" => "Poco difícil",
        _ => "Sin calificar",
    };

    // orden numerico para agrupar y comparar
    public static int? Rango(string? dificultad) => dificultad switch {
        "poco_dificil" => 1,
        "moderada" => 2,
        "muy_dificil" => 3,
        _ => null,
    };
}
