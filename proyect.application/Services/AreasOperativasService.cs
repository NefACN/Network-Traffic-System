using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class AreasOperativasService(IApplicationDbContext db)
{
    public Task<List<AreaOperativaDto>> ListarAsync(CancellationToken ct, bool? activo = null) => db.AreasOperativas.AsNoTracking()
        .Where(a => !activo.HasValue || a.Activo == activo.Value)
        .OrderBy(a => a.Nombre).Select(a => new AreaOperativaDto(a.Id_Area, a.Nombre, a.Id_Criticidad, a.Criticidad.Nivel, a.Activo)).ToListAsync(ct);

    public Task<AreaOperativaDto?> ObtenerAsync(int id, CancellationToken ct) => db.AreasOperativas.AsNoTracking()
        .Where(a => a.Id_Area == id).Select(a => new AreaOperativaDto(a.Id_Area, a.Nombre, a.Id_Criticidad, a.Criticidad.Nivel, a.Activo)).FirstOrDefaultAsync(ct);

    public async Task<int> CrearAsync(CrearAreaOperativaRequest request, CancellationToken ct)
    {
        await ValidarAsync(request.Nombre, request.IdCriticidad, ct);
        var area = new AreaOperativa { Nombre = request.Nombre.Trim(), Id_Criticidad = request.IdCriticidad };
        db.AreasOperativas.Add(area);
        await db.SaveChangesAsync(ct);
        return area.Id_Area;
    }

    public async Task ActualizarAsync(int id, ActualizarAreaOperativaRequest request, CancellationToken ct)
    {
        var area = await db.AreasOperativas.FirstOrDefaultAsync(a => a.Id_Area == id, ct)
            ?? throw new BusinessRuleException("El área indicada no existe.", BusinessError.NotFound);
        await ValidarAsync(request.Nombre, request.IdCriticidad, ct, id);
        area.Nombre = request.Nombre.Trim();
        area.Id_Criticidad = request.IdCriticidad;
        await db.SaveChangesAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, CancellationToken ct)
    {
        var area = await db.AreasOperativas.FirstOrDefaultAsync(a => a.Id_Area == id, ct)
            ?? throw new BusinessRuleException("El área indicada no existe.", BusinessError.NotFound);
        area.Activo = activo;
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidarAsync(string nombre, int criticidad, CancellationToken ct, int? excluirId = null)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("El nombre del área es obligatorio.");

        var normalizado = nombre.Trim().ToUpperInvariant();

        if (await db.AreasOperativas.AnyAsync(a => a.Id_Area != excluirId && a.Nombre.Trim().ToUpper() == normalizado, ct))
            throw new BusinessRuleException("Ya existe un área con ese nombre, incluso si está inactiva.", BusinessError.Conflict);

        if (!await db.NivelesCriticidad.AnyAsync(c => c.Id_NivelCriticidad == criticidad, ct))
            throw new BusinessRuleException("El nivel de criticidad indicado no existe.");
    }
}
