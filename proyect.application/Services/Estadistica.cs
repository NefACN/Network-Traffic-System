namespace proyect.application.Services;

public static class Estadistica
{
    // prueba de McNemar exacta (binomial, bilateral) para datos pareados de muestras pequenas

    public static double McNemarExacto(int b, int c)
    {
        if (b < 0 || c < 0)
            throw new ArgumentOutOfRangeException(nameof(b));
        var n = b + c;
        if (n == 0)
            return 1d;
        var k = Math.Min(b, c);
        var logMitad = n * Math.Log(0.5);
        double suma = 0, logCombinaciones = 0;
        for (var i = 0; i <= k; i++)
        {
            suma += Math.Exp(logCombinaciones + logMitad);
            logCombinaciones += Math.Log(n - i) - Math.Log(i + 1);
        }
        return Math.Min(1d, 2 * suma);
    }

    // factor de decaimiento exponencial
    public static decimal Decaimiento(double edadDias, int vidaMediaDias) =>
        (decimal)Math.Pow(0.5, Math.Max(0, edadDias) / Math.Max(1, vidaMediaDias));

    // grupos que pueden mostrarse sin revelar grupos pequenos por diferencia
    public static HashSet<T> GruposVisibles<T>(IEnumerable<(T Clave, int Cantidad)> grupos, int sinGrupo, int umbral)
    {
        var lista = grupos.ToList();
        if (umbral <= 0)
            return lista.Select(g => g.Clave).ToHashSet();
        var visibles = lista.Where(g => g.Cantidad >= umbral).OrderBy(g => g.Cantidad).ToList();
        var oculto = sinGrupo + lista.Where(g => g.Cantidad < umbral).Sum(g => g.Cantidad);
        while (oculto > 0 && oculto < umbral && visibles.Count > 0)
        {
            oculto += visibles[0].Cantidad;
            visibles.RemoveAt(0);
        }
        return visibles.Select(g => g.Clave).ToHashSet();
    }

    // supresion cruzada para dos desgloses de las mismas filas (ej. sucursal y area)
    public static (HashSet<TA> A, HashSet<TB> B) GruposVisiblesCruzados<TA, TB>(IReadOnlyCollection<(TA A, TB B)> filas, int umbral)
    {
        var porA = filas.GroupBy(f => f.A).Select(g => (g.Key, g.Count())).ToList();
        var porB = filas.GroupBy(f => f.B).Select(g => (g.Key, g.Count())).ToList();
        if (umbral <= 0)
            return (porA.Select(g => g.Key).ToHashSet(), porB.Select(g => g.Key).ToHashSet());
        var forzadasA = new List<TA>();
        var forzadasB = new List<TB>();
        while (true)
        {
            var visA = GruposVisibles(porA.Where(g => !forzadasA.Contains(g.Key)), porA.Where(g => forzadasA.Contains(g.Key)).Sum(g => g.Item2), umbral);
            var visB = GruposVisibles(porB.Where(g => !forzadasB.Contains(g.Key)), porB.Where(g => forzadasB.Contains(g.Key)).Sum(g => g.Item2), umbral);
            var conflicto = false;
            foreach (var a in visA)
            {
                foreach (var b in visB)
                {
                    var ambos = filas.Count(f => EqualityComparer<TA>.Default.Equals(f.A, a) && EqualityComparer<TB>.Default.Equals(f.B, b));
                    var soloA = porA.First(g => EqualityComparer<TA>.Default.Equals(g.Key, a)).Item2 - ambos;
                    var soloB = porB.First(g => EqualityComparer<TB>.Default.Equals(g.Key, b)).Item2 - ambos;
                    if ((soloA <= 0 || soloA >= umbral) && (soloB <= 0 || soloB >= umbral))
                        continue;
                    if (soloA + ambos <= soloB + ambos)
                        forzadasA.Add(a);
                    else
                        forzadasB.Add(b);
                    conflicto = true;
                    break;
                }
                if (conflicto)
                    break;
            }
            if (!conflicto)
                return (visA, visB);
        }
    }
}
