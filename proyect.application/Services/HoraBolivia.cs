namespace proyect.application.Services;

// hora local de Bolivia para filtros de fechas
public static class HoraBolivia
{
    public static readonly TimeSpan Desfase = TimeSpan.FromHours(-4);

    // conversion de hora local a UTC
    public static DateTime AUtc(DateTime fecha) => fecha.Kind switch {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha - Desfase, DateTimeKind.Utc),
    };

    // inicio del filtro en UTC
    public static DateTime? DesdeUtc(DateTime? desde) => desde is { } d ? AUtc(d) : null;

    // fin del filtro en UTC
    public static DateTime? HastaUtc(DateTime? hasta) => hasta switch {
        null => null,
        { Kind: DateTimeKind.Unspecified, TimeOfDay.Ticks: 0 } h => AUtc(h.AddDays(1)).AddTicks(-1),
        { } h => AUtc(h),
    };
}
