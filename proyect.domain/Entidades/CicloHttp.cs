namespace proyect.domain.Entidades;

// peticion HTTP, IP y token se guardan seudonimizados
public sealed class InteraccionSimulacionHttp
{
    public long Id { get; set; }
    public int IdEnvio { get; set; }
    public int IdCampania { get; set; }
    // apertura, clic, credenciales
    public string Tipo { get; set; } = "clic";
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    // IPv4 seudonima 10.x.y.z derivada por HMAC, conserva la agrupacion por origen
    public string Ip { get; set; } = "";
    public string Metodo { get; set; } = "GET";
    // ruta con el token sustituido por un seudonimo estable del envio
    public string Ruta { get; set; } = "";
    public int Status { get; set; }
    public long Bytes { get; set; }
    public double DuracionMs { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
}

// relaciona una linea de un conjunto HTTP generado con su interaccion de origen
public sealed class LineaSimulacionHttp
{
    public Guid DatasetId { get; set; }
    public long Linea { get; set; }
    public long IdInteraccion { get; set; }
}

// sugerencia de concientizacion por anomalia HTTP o incidente, decide el administrador
public sealed class SugerenciaConcientizacion
{
    public int Id { get; set; }
    // http o incidente
    public string Origen { get; set; } = "http";
    public int? IdSucursal { get; set; }
    public int? IdArea { get; set; }
    public int? IdCategoria { get; set; }
    public string Motivo { get; set; } = "";
    // lista JSON de evidencias (maximo 20) referencia y descripcion breve
    public string Evidencias { get; set; } = "[]";
    public int CantidadEvidencias { get; set; } = 1;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime UltimaEvidencia { get; set; } = DateTime.UtcNow;
    // pendiente, campania, microleccion, descartada
    public string Estado { get; set; } = "pendiente";
    public int? AtendidaPor { get; set; }
    public DateTime? FechaAtencion { get; set; }
    public string? Nota { get; set; }
    public int? IdActividad { get; set; }
    public int PersonasAsignadas { get; set; }
}
