using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using proyect.application;
using proyect.infrastructure;
using proyect.infrastructure.Configuration;
using proyecto.api.Configuration;

var builder = WebApplication.CreateBuilder(args);

// no anunciar la tecnologia del servidor (hardening, OWASP A05)
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddProjectEnvironmentFile(builder.Environment.ContentRootPath);
    builder.Configuration.AddUserSecrets<Program>(optional: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSecurityHardening(builder.Configuration, builder.Environment);

builder.Services.AddControllers();

// Idempotency-Key: reenvios devuelven la respuesta original sin repetir el efecto
builder.Services.AddIdempotencia();

// recordatorios diarios de las rutas de aprendizaje (seccion Programa:Recordatorios)
builder.Services.AddHostedService<proyecto.api.Configuration.RecordatoriosAprendizajeWorker>();

builder.Services.AddScoped<proyecto.api.Errors.MlAgentAuthorizationFilter>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<proyecto.api.Errors.BusinessExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();

if (!builder.Environment.IsDevelopment()
    && ConfiguracionProduccion.Errores(builder.Configuration) is { Count: > 0 } erroresConfiguracion)
    throw new InvalidOperationException(string.Join(" ", erroresConfiguracion));

var originesPermitidos = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

if (!builder.Environment.IsDevelopment()
    && originesPermitidos.Any(o => o == "*" || !o.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("En producción Cors:AllowedOrigins solo admite orígenes HTTPS explícitos.");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // lista blanca explicita, los tokens viajan en Authorization y no en cookies
        policy.WithOrigins(originesPermitidos)
            .WithHeaders("Authorization", "Content-Type", "Accept", Idempotencia.Cabecera)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Disposition", "Retry-After")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Introduce tu token JWT."
        });

        options.AddSecurityDefinition("MlAgent", new OpenApiSecurityScheme {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-ML-Agent-Key",
            Description = "Credencial privada del trabajador ML. Solo se utiliza en los endpoints internos del agente."
        });

        options.DocumentFilter<proyecto.api.Configuration.SwaggerSecurityFilter>();
    });
}

// detras de nginx: solo se confia en el proxy local para obtener la IP real del cliente
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    // Proxy:RedConfiable agrega la red Docker del nginx del host como proxy confiable
    foreach (var red in (builder.Configuration["Proxy:RedConfiable"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(red));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSecurityHeaders(app.Environment);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseIdempotencia();

app.MapControllers();

// monitoreo de disponibilidad sin exponer detalles internos (ISO 22301)
app.MapGet("/health", async (proyect.application.Abstractions.IApplicationDbContext db, CancellationToken ct) =>
    {
        bool disponible;
        try
        {
            disponible = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception)
        {
            disponible = false;
        }

        return disponible
            ? Results.Ok(new { estado = "disponible" })
            : Results.Json(new { estado = "degradado" }, statusCode: 503);
    })
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();
