using System.Security.Cryptography;
using System.Text;
using proyect.application.Services;

namespace proyecto.api.Configuration;

// seudonimiza IP y token con HMAC-SHA256 conservando la agrupacion por origen
public static class SeudonimosHttp
{
    private static byte[] Clave(IConfiguration configuration)
    {
        var secreto = configuration["Ml:SeudonimoClave"] is { Length: >= 32 } propia ? propia : configuration["Jwt:Key"]!;
        return SHA256.HashData(Encoding.UTF8.GetBytes("dismac-simulacion-http:" + secreto));
    }

    private static byte[] Hmac(IConfiguration configuration, string valor) =>
        HMACSHA256.HashData(Clave(configuration), Encoding.UTF8.GetBytes(valor));

    public static string Ip(IConfiguration configuration, string? ip)
    {
        var h = Hmac(configuration, "ip:" + (ip ?? "desconocida"));
        return $"10.{h[0]}.{h[1]}.{h[2]}";
    }

    // seudonimo con forma de GUID que conserva longitud y entropia de la ruta real
    public static string Token(IConfiguration configuration, Guid token) => new Guid(Hmac(configuration, "token:" + token)[..16]).ToString();

    public static InteraccionHttpMeta Meta(HttpContext context, IConfiguration configuration, string plantillaRuta, Guid token, int status, long bytes, double duracionMs) =>
        new(Ip(
            configuration,
            context.Connection.RemoteIpAddress?.ToString()
            ),
            context.Request.Method,
            plantillaRuta.Replace("{token}",
                Token(configuration, token)
                ),
            status,
            bytes,
            duracionMs,
            context.Request.Headers.UserAgent.ToString(),
            context.Request.Headers.Referer.ToString());
}
