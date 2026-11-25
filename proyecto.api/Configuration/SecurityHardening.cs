using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace proyecto.api.Configuration;

// controles de hardening (OWASP API4/A05), limites configurables en Seguridad:RateLimit
public static class SecurityHardening
{
    public const string PoliticaAutenticacion = "autenticacion";
    public const string PoliticaEnvioCodigos = "envio-codigos";
    public const string PoliticaSesion = "sesion";

    public static IServiceCollection AddSecurityHardening(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var limites = configuration.GetSection("Seguridad:RateLimit");
        int Limite(string clave, int porDefecto) => limites.GetValue<int?>(clave) is int valor && valor > 0 ? valor : porDefecto;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { mensaje = "Demasiadas solicitudes. Espera un momento e intenta nuevamente." }, ct);
            };

            // login, MFA y cambio de contrasena: frena fuerza bruta y password spraying por IP
            options.AddPolicy(PoliticaAutenticacion, context => RateLimitPartition.GetSlidingWindowLimiter(
                ClaveCliente(context), _ => new SlidingWindowRateLimiterOptions {
                    PermitLimit = Limite("AutenticacionPorMinuto", 60),
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));

            // correos y SMS: evita costes de proveedor (SMS pumping) y spam a los usuarios
            options.AddPolicy(PoliticaEnvioCodigos, context => RateLimitPartition.GetFixedWindowLimiter(
                ClaveCliente(context), _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = Limite("EnvioCodigosPor5Minutos", 20),
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0
                }));

            options.AddPolicy(PoliticaSesion, context => RateLimitPartition.GetSlidingWindowLimiter(
                ClaveCliente(context), _ => new SlidingWindowRateLimiterOptions {
                    PermitLimit = Limite("SesionPorMinuto", 240),
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));
        });

        if (!environment.IsDevelopment())
        {
            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
                options.IncludeSubDomains = true;
            });
        }

        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-site";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

                // la API no ejecuta nada en el navegador, solo Swagger UI en desarrollo necesita scripts
                var esSwagger = environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger");
                if (!esSwagger)
                    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

                // las respuestas de autenticacion contienen tokens y nunca deben almacenarse en cache
                var path = context.Request.Path;
                if (path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/mfa") || path.StartsWithSegments("/api/usuarios"))
                {
                    headers.CacheControl = "no-store";
                    headers.Pragma = "no-cache";
                }
                return Task.CompletedTask;
            });
            await next();
        });
    }

    private static string ClaveCliente(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } ip
            ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
            : IPAddress.None.ToString();
}
