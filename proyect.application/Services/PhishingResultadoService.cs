using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class PhishingResultadoService(IApplicationDbContext db)
{
    public async Task RegistrarClickAsync(Guid token, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var candidatos = db.EnviosSimulacion.Where(e => e.TokenUuid == token && !e.HizoClick &&
            e.EstadoEntrega != "failed" && e.Campania.Id_Estado == 2 &&
            (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now));

        await candidatos.ExecuteUpdateAsync(s => s.SetProperty(e => e.HizoClick, true)
            .SetProperty(e => e.FueAbierto, true).SetProperty(e => e.FechaVulneracion, now)
            .SetProperty(e => e.FechaApertura, e => e.FechaApertura ?? now), ct);

        await transaction.CommitAsync(ct);
    }

    // clic registrado de una campania activa que muestra la pagina senuelo de credenciales
    private IQueryable<EnvioSimulacion> ConSenuelo(Guid token)
    {
        var now = DateTime.UtcNow;

        return db.EnviosSimulacion.Where(e => e.TokenUuid == token && e.HizoClick && e.EstadoEntrega != "failed" &&
            e.Campania.Id_Estado == 2 && (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now) &&
            e.Campania.PaginaSenueloRegistro == "credenciales");
    }

    public Task<bool> UsaSenueloAsync(Guid token, CancellationToken ct = default) => ConSenuelo(token).AnyAsync(ct);


    // registra una sola vez el envio del formulario senuelo, no se reciben ni guardan los valores
    public async Task<bool> RegistrarCredencialesAsync(Guid token, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await ConSenuelo(token).Where(e => !e.IngresoCredenciales)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IngresoCredenciales, true)
            .SetProperty(e => e.FechaIngresoCredenciales, now), ct) > 0;
    }
}
