using Microsoft.EntityFrameworkCore;

namespace proyect.application.Abstractions;

public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Items, int Total, int Pagina, int Tamanio);

public static class Paginacion
{
    public const int TamanioPorDefecto = 25;
    public const int TamanioMaximo = 100;

    public static (int Pagina, int Tamanio) Normalizar(int? pagina, int? tamanio) =>
        (Math.Max(1, pagina ?? 1),
        Math.Clamp(tamanio ?? TamanioPorDefecto, 1, TamanioMaximo)
        );

    public static async Task<ResultadoPaginado<T>> PaginarAsync<T>(this IQueryable<T> consulta, int? pagina, int? tamanio, CancellationToken ct = default)
    {
        var (p, t) = Normalizar(pagina, tamanio);
        var total = await consulta.CountAsync(ct);
        var items = await consulta.Skip((p - 1) * t).Take(t).ToListAsync(ct);
        return new ResultadoPaginado<T>(items, total, p, t);
    }

    public static ResultadoPaginado<T> Paginar<T>(this IEnumerable<T> lista, int? pagina, int? tamanio)
    {
        var (p, t) = Normalizar(pagina, tamanio);
        var todos = lista as IReadOnlyCollection<T> ?? lista.ToList();
        return new ResultadoPaginado<T>(todos.Skip((p - 1) * t).Take(t).ToList(), todos.Count, p, t);
    }
}
