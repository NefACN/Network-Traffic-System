using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Security;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

// canal SSE del centro de control, emite solo cuando el estado cambia
[ApiController]
[Route("api/ml/live")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class MlLiveController(MlLiveService live, IApplicationDbContext db, IConfiguration config, ILogger<MlLiveController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task Stream(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Ml:LivePollSeconds", 3), 1, 60));
        var heartbeat = TimeSpan.FromSeconds(15);
        var expires = long.TryParse(User.FindFirst("exp")?.Value, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime : DateTime.UtcNow.AddMinutes(15);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no"; // no almacenar el flujo en bufer
        await Response.WriteAsync("retry: 5000\n\n", ct);
        await Response.Body.FlushAsync(ct);

        string? last = null;
        var lastWrite = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested && DateTime.UtcNow < expires)
            {
                // se revalida la autorizacion en cada ciclo, como hace el middleware por peticion
                if (!await SigueAutorizadoAsync(ct))
                    break;
                var snapshot = await live.SnapshotAsync(ct);
                if (snapshot.Version != last)
                {
                    await Response.WriteAsync($"event: snapshot\ndata: {JsonSerializer.Serialize(snapshot, Json)}\n\n", ct);
                    last = snapshot.Version;
                    lastWrite = DateTime.UtcNow;
                    await Response.Body.FlushAsync(ct);
                }
                else if (DateTime.UtcNow - lastWrite >= heartbeat)
                {
                    await Response.WriteAsync(": ping\n\n", ct);
                    lastWrite = DateTime.UtcNow;
                    await Response.Body.FlushAsync(ct);
                }
                await Task.Delay(interval, ct);
            }
            if (!ct.IsCancellationRequested)
            {
                await Response.WriteAsync("event: expired\ndata: {}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /*  cliente cerro la conexion */ }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(error, "El canal en vivo del centro de control se interrumpió.");
        }
    }

    // misma regla que la validacion del JWT: cuenta activa, mismo security stamp y rol admin
    private async Task<bool> SigueAutorizadoAsync(CancellationToken ct)
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = User.FindFirstValue(JwtClaim.SecurityStamp);

        if (!int.TryParse(id, out var usuario) || string.IsNullOrEmpty(stamp))
            return false;

        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == usuario && u.Activo && u.SecurityStamp == stamp, ct))
            return false;

        var now = DateTime.UtcNow;
        var roles = await db.RolUsuario.AsNoTracking().Where(r => r.IdUsuario == usuario && r.Activo
            && r.FechaInicio <= now && (r.FechaFin == null || r.FechaFin > now) && r.Rol.Activo).Select(r => r.Rol.Nombre).ToListAsync(ct);

        return RolesSistemas.ActorEfectivo(roles) == RolesSistemas.RolAdmin;
    }
}
