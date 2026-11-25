namespace proyect.application.Security;

public sealed record BloqueoDesconocido(int Fallos, DateTimeOffset? BloqueadoHasta)
{
    public static (BloqueoDesconocido Estado, bool Bloqueado) Fallo(BloqueoDesconocido? actual, DateTimeOffset ahora, int maximo, TimeSpan duracion)
    {
        var estado = actual ?? new BloqueoDesconocido(0, null);

        if (estado.BloqueadoHasta is { } hasta && hasta > ahora)
            return (estado, true);
        var fallos = estado.Fallos + 1;
        return fallos >= maximo
            ? (new BloqueoDesconocido(0, ahora + duracion), true)
            : (new BloqueoDesconocido(fallos, null), false);
    }
}
