namespace proyecto.api.Configuration;

// valores que en produccion no pueden quedar con su valor de desarrollo
public static class ConfiguracionProduccion
{
    public static IReadOnlyList<string> Errores(IConfiguration c)
    {
        var errores = new List<string>();
        if (!UrlPublica(c["Frontend:BaseUrl"]))
            errores.Add("Configura Frontend:BaseUrl con la URL HTTPS pública del portal: los enlaces de los correos apuntarían a localhost.");
        if (!UrlPublica(c["Api:PublicBaseUrl"]))
            errores.Add("Configura Api:PublicBaseUrl con la URL HTTPS pública de la API: los enlaces de las simulaciones no deben depender de la cabecera Host.");
        if (string.IsNullOrWhiteSpace(c["DataProtection:KeysPath"]))
            errores.Add("Configura DataProtection:KeysPath en un volumen persistente: sin él los enlaces de restablecimiento caducan en cada reinicio.");
        return errores;
    }

    private static bool UrlPublica(string? valor) =>
        Uri.TryCreate(valor, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
        && !url.IsLoopback && !string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}
