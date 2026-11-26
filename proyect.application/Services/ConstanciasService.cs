using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ConstanciasService(IApplicationDbContext db, IAuditoriaService auditoria)
{
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NuevoCodigo()
    {
        Span<char> c = stackalloc char[12];
        for (var i = 0; i < c.Length; i++)
            c[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return $"DSM-{new string(c[..4])}-{new string(c[4..8])}-{new string(c[8..])}";
    }

    public static string Normalizar(string codigo) => (codigo ?? "").Trim().ToUpperInvariant();

    public async Task<object> MisConstanciasAsync(int usuario, CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.IntentoCursoVoluntario.AsNoTracking()
            .Where(i => i.Id_Usuario == usuario && i.Exitoso == true && i.FechaCompletado != null && i.IdActividad != null)
            .OrderByDescending(i => i.FechaCompletado).ThenByDescending(i => i.Id_Intento)
            .Select(i => new {
                idIntento = i.Id_Intento,
                actividad = i.TituloRegistro ?? i.Categoria.Nombre,
                tema = i.Categoria.Nombre,
                i.Porcentaje,
                i.FechaCompletado,
                codigo = db.Set<Constancia>().Where(c => c.IdIntento == i.Id_Intento).Select(c => c.Codigo).FirstOrDefault(),
            }).PaginarAsync(pagina, tamanio, ct);

    public async Task<object> EmitirAsync(int usuario, int idIntento, CancellationToken ct)
    {
        var existente = await db.Set<Constancia>().AsNoTracking().FirstOrDefaultAsync(c => c.IdIntento == idIntento && c.IdUsuario == usuario, ct);

        if (existente is not null)
            return new { existente.Codigo };

        var i = await db.IntentoCursoVoluntario.AsNoTracking().Where(x => x.Id_Intento == idIntento && x.Id_Usuario == usuario)
            .Select(x => new {
                x.Exitoso,
                x.FechaCompletado,
                x.IdActividad,
                x.TituloRegistro,
                tema = x.Categoria.Nombre,
                x.Porcentaje,
                nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido
            }).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("No se encontró la actividad.", BusinessError.NotFound);

        if (i.Exitoso != true || i.FechaCompletado is null || i.IdActividad is null)
            throw new BusinessRuleException("Solo se emiten constancias de actividades aprobadas y finalizadas.");

        var c = new Constancia {
            Codigo = NuevoCodigo(),
            IdUsuario = usuario,
            IdIntento = idIntento,
            NombreCompleto = i.nombre,
            Actividad = i.TituloRegistro ?? i.tema,
            Tema = i.tema,
            Porcentaje = i.Porcentaje,
            FechaAprobacion = i.FechaCompletado.Value,
            FechaEmision = DateTime.UtcNow,
        };
        db.Set<Constancia>().Add(c);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.EsDuplicado())
        {
            db.Set<Constancia>().Entry(c).State = EntityState.Detached;
            return new {
                Codigo = await db.Set<Constancia>().Where(x => x.IdIntento == idIntento).Select(x => x.Codigo).SingleAsync(ct)
            };
        }
        await auditoria.RegistrarAsync("EmitirConstancia", true, usuario, entidad: "Constancia", idEntidadAfectada: c.Id.ToString(), detalles: new { c.Codigo, idIntento });

        return new { c.Codigo };
    }

    public async Task<object> ObtenerAsync(string codigo, int usuario, bool admin, CancellationToken ct)
    {
        var normal = Normalizar(codigo);
        var c = await db.Set<Constancia>().AsNoTracking().SingleOrDefaultAsync(x => x.Codigo == normal && (admin || x.IdUsuario == usuario), ct)
            ?? throw new BusinessRuleException("Constancia no encontrada.", BusinessError.NotFound);
        return new {
            c.Codigo,
            c.NombreCompleto,
            c.Actividad,
            c.Tema,
            c.Porcentaje,
            c.FechaAprobacion,
            c.FechaEmision
        };
    }

    public async Task<object> VerificarAsync(string codigo, CancellationToken ct)
    {
        var normal = Normalizar(codigo);
        if (normal.Length > 20)
            return new { valida = false };

        var c = await db.Set<Constancia>().AsNoTracking().Where(x => x.Codigo == normal)
            .Select(x => new {
                x.Codigo,
                x.NombreCompleto,
                x.Actividad,
                x.Tema,
                x.FechaAprobacion
            }).SingleOrDefaultAsync(ct);

        return c is null ? new { valida = false } : new {
            valida = true,
            c.Codigo,
            c.NombreCompleto,
            c.Actividad,
            c.Tema,
            c.FechaAprobacion
        };
    }
}
