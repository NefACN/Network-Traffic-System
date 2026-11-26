using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using proyecto.web.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddOptions<ApiOptions>().BindConfiguration("Api")
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http"), "Api:BaseUrl debe ser una URL HTTP o HTTPS absoluta.")
    .ValidateOnStart();

builder.Services.AddRazorPages();

// detras de nginx local: solo se confia en el proxy local por defecto
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // en Docker se confia en la red de Proxy:RedConfiable para X-Forwarded-For
    foreach (var red in (builder.Configuration["Proxy:RedConfiable"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(red));
});

var app = builder.Build();
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // HSTS por defecto dura 30 dias
    app.UseHsts();
}

app.UseHttpsRedirection();

// cabeceras de seguridad, la CSP bloquea scripts inyectados que robarian tokens
var apiOrigin = new Uri(app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl).GetLeftPart(UriPartial.Authority);
var contentSecurityPolicy = string.Join("; ",
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'", // atributos style generados por las vistas
    "img-src 'self' data:",              // codigo QR del autenticador
    "font-src 'self'",
    $"connect-src 'self' {apiOrigin}",
    "frame-src 'self'",                  // vista previa aislada (iframe sandbox) de correos
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'");
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = contentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // los enlaces de restablecimiento llevan el token en la URL, no se envia como Referer
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        return Task.CompletedTask;
    });
    await next();
});

// se revalidan los archivos de rutas estables para no mezclar modulos de distintas publicaciones
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path == "/" || new[] { ".html", ".js", ".css" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.CacheControl.ToString().Contains("no-store", StringComparison.OrdinalIgnoreCase))
                context.Response.Headers.CacheControl = "no-cache";
            return Task.CompletedTask;
        });
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

// solo configuracion publica del cliente, nunca secretos ni credenciales
app.MapGet("/app-config.js", (IOptions<ApiOptions> options, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Text("window.APP_CONFIG = Object.freeze(" +
        JsonSerializer.Serialize(new { apiUrl = options.Value.BaseUrl.TrimEnd('/') }) + ");",
        "application/javascript");
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
