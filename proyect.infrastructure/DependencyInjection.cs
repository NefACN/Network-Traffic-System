using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using proyect.application.Security;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.infrastructure.Persistence;
using proyect.infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace proyect.infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("PostgreSQL");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Configura ConnectionStrings:PostgreSQL mediante secretos de usuario o variables de entorno.");

        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            throw new InvalidOperationException("Configura Jwt:Key con una clave de al menos 32 bytes.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection));

        // persistir las claves de Data Protection evita invalidar tokens de Identity al reiniciar (ISO 22301)
        var dataProtection = services.AddDataProtection().SetApplicationName("DISMAC");
        var rutaClaves = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(rutaClaves))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(rutaClaves));

        services.AddMemoryCache();
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            // OWASP ASVS 2.1.1: minimo 12 caracteres
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders()
        .AddSignInManager();

        var jwtSection = configuration.GetSection("Jwt");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    // evita confusion de algoritmos / solo se acepta lo que emite el servidor
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = jwtSection["Audience"],

                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSection["Key"]!)),

                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // el token solo se acepta en la cabecera Authorization, nunca por query string
                options.SaveToken = false;

                options.Events = new JwtBearerEvents {
                    OnTokenValidated = async context =>
                    {
                        var userManager = context.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();

                        var idUsuario = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                            ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (idUsuario is null)
                        {
                            context.Fail("Token sin identidad de usuario.");
                            return;
                        }

                        if (context.Principal!.HasClaim(c => c.Type == "purpose"))
                        {
                            context.Fail("Un token de verificación no autoriza el acceso a la API.");
                            return;
                        }

                        var usuario = await userManager.FindByIdAsync(idUsuario);
                        if (usuario is null || !usuario.Activo)
                        {
                            context.Fail("Usuario inactivo o inexistente.");
                            return;
                        }

                        var stampEnToken = context.Principal!.FindFirstValue(JwtClaim.SecurityStamp);
                        if (string.IsNullOrEmpty(stampEnToken) || stampEnToken != usuario.SecurityStamp)
                        {
                            context.Fail("La sesión ya no es válida, vuelve a iniciar sesión.");
                            return;
                        }

                        // verificar vigencia en cada peticion, un rol temporal no dura hasta que expire el JWT
                        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();

                        var actor = await db.RolUsuario.ActorEfectivoAsync(
                            usuario.Id,
                            DateTime.UtcNow,
                            context.HttpContext.RequestAborted);

                        var identity = (ClaimsIdentity)context.Principal!.Identity!;
                        foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
                            identity.RemoveClaim(claim);

                        identity.AddClaim(new Claim(identity.RoleClaimType, actor));
                    }
                };
            });

        // denegar por defecto (OWASP A01): un endpoint sin [Authorize] ni [AllowAnonymous] exige autenticacion
        services.AddAuthorization(options =>
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddHttpClient<IEmailSender, BrevoEmailSender>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddScoped<IUserSessionInvalidator, UserSessionInvalidator>();
        services.AddScoped<PoliticaMfa>();
        services.AddScoped<IHttpDatasetStorage, HttpDatasetStorage>();
        services.AddScoped<IModelRegistry, ModelRegistry>();
        services.AddHttpClient<IModelAnalysisClient, ModelAnalysisClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<IModelTrainingClient, ModelTrainingClient>(client => client.Timeout = TimeSpan.FromMinutes(10));

        return services;
    }
}
