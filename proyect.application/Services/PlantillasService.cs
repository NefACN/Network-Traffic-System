using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// plantillas de simulacion con senales de alerta y alineacion NIST Phish Scale
public sealed class PlantillasService(IApplicationDbContext db, IAuditoriaService auditoria)
{
    public const int MaxSenales = 40;

    // catalogo completo (selectores del formulario de campania)
    public async Task<List<PlantillaDto>> ListarAsync(bool? habilitada, CancellationToken ct)
        => Proyectar(await Consulta(habilitada).ToListAsync(ct));

    // tabla de plantillas paginada en el servidor
    public async Task<ResultadoPaginado<PlantillaDto>> ListarPaginaAsync(bool? habilitada, int? pagina, int? tamanio, CancellationToken ct)
    {
        var pag = await Consulta(habilitada).PaginarAsync(pagina, tamanio, ct);
        return new ResultadoPaginado<PlantillaDto>(Proyectar(pag.Items), pag.Total, pag.Pagina, pag.Tamanio);
    }

    private sealed record FilaPlantilla(int Id, string Nombre, string AsuntoCorreo, string Nivel, bool Habilitada, int Senales, string AlineacionGeneral, string PaginaSenuelo);

    private IQueryable<FilaPlantilla> Consulta(bool? habilitada) => db.PlantillasPhishing.AsNoTracking()
        .Where(p => !habilitada.HasValue || p.Habilitada == habilitada.Value)
        .OrderBy(p => p.Id_Plantilla)
        .Select(p => new FilaPlantilla(p.Id_Plantilla, p.Nombre, p.AsuntoCorreo, p.Criticidad.Nivel, p.Habilitada,
            p.Senales.Count, p.AlineacionGeneral, p.PaginaSenuelo));

    private static List<PlantillaDto> Proyectar(IEnumerable<FilaPlantilla> rows) => rows.Select(r => new PlantillaDto(r.Id, r.Nombre, r.AsuntoCorreo, r.Nivel, r.Habilitada,
        r.Senales, r.AlineacionGeneral, r.PaginaSenuelo, PhishScale.Dificultad(r.Senales, r.AlineacionGeneral))).ToList();

    public async Task<PlantillaDetalleDto> ObtenerAsync(int id, CancellationToken ct)
    {
        var p = await db.PlantillasPhishing.AsNoTracking().Include(x => x.Senales).Include(x => x.Alineaciones)
            .SingleOrDefaultAsync(x => x.Id_Plantilla == id, ct)
            ?? throw new BusinessRuleException("La plantilla no existe.", BusinessError.NotFound);
        var senales = p.Senales.OrderBy(s => s.Orden).ThenBy(s => s.Id).ToList();
        return new PlantillaDetalleDto(p.Id_Plantilla, p.Nombre, p.AsuntoCorreo, p.CuerpoHtml, p.Id_Criticidad,
            p.Id_CategoriaPegunta, p.Habilitada, p.AlineacionGeneral, p.PaginaSenuelo,
            senales.Select(s => new SenalAlertaDto(s.Tipo, s.Descripcion, s.Fragmento)).ToList(),
            p.Alineaciones.OrderBy(a => a.IdArea).Select(a => new AlineacionAreaRequest(a.IdArea, a.Nivel)).ToList(),
            await EvaluarAsync(senales, p.AlineacionGeneral, p.Alineaciones, ct), p.RemitenteNombre, p.RemitenteCorreo, p.EnlaceMostrado);
    }

    // calcula la dificultad por area activa y por las inactivas con alineacion propia
    public async Task<PhishScaleDto> EvaluarAsync(IReadOnlyCollection<SenalAlertaPlantilla> senales, string general,
        IEnumerable<AlineacionPlantillaArea> especificas, CancellationToken ct)
    {
        var mapa = especificas.ToDictionary(a => a.IdArea, a => a.Nivel);
        var areas = await db.AreasOperativas.AsNoTracking().OrderBy(a => a.Nombre)
            .Select(a => new { a.Id_Area, a.Nombre, a.Activo }).ToListAsync(ct);
        var porArea = areas.Where(a => a.Activo || mapa.ContainsKey(a.Id_Area)).Select(a =>
        {
            var especifica = mapa.TryGetValue(a.Id_Area, out var nivel);
            var alineacion = especifica ? nivel! : general;
            var dificultad = PhishScale.Dificultad(senales.Count, alineacion);
            return new DificultadAreaDto(a.Id_Area, a.Nombre, a.Activo, alineacion, especifica, dificultad, PhishScale.Etiqueta(dificultad));
        }).ToList();
        var porTipo = PhishScale.Tipos.Keys.ToDictionary(t => t, t => senales.Count(s => s.Tipo == t));
        return new PhishScaleDto(senales.Count, PhishScale.CategoriaSenales(senales.Count), general,
            PhishScale.Dificultad(senales.Count, general), porArea, porTipo);
    }

    public async Task<int> GuardarAsync(int? id, CrearPlantillaRequest r, int actor, CancellationToken ct)
    {
        await ValidarAsync(r, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        PlantillaPhishing p;
        if (id.HasValue)
        {
            p = await db.PlantillasPhishing.Include(x => x.Senales).Include(x => x.Alineaciones)
                .SingleOrDefaultAsync(x => x.Id_Plantilla == id, ct)
                ?? throw new BusinessRuleException("Plantilla no encontrada.", BusinessError.NotFound);
        }
        else
        {
            p = new PlantillaPhishing();
            db.PlantillasPhishing.Add(p);
        }
        p.Nombre = r.Nombre.Trim();
        p.AsuntoCorreo = r.AsuntoCorreo.Trim();
        p.CuerpoHtml = r.CuerpoHtml;
        p.Id_Criticidad = r.IdCriticidad;
        p.Id_CategoriaPegunta = r.IdCategoriaPregunta;
        p.Habilitada = r.Habilitada;
        p.AlineacionGeneral = r.AlineacionGeneral;
        p.PaginaSenuelo = r.PaginaSenuelo;
        p.RemitenteNombre = Limpio(r.RemitenteNombre);
        p.RemitenteCorreo = Limpio(r.RemitenteCorreo)?.ToLowerInvariant();
        p.EnlaceMostrado = Limpio(r.EnlaceMostrado);
        if (r.Senales is not null)
        {
            db.Set<SenalAlertaPlantilla>().RemoveRange(p.Senales);
            p.Senales = r.Senales.Select((s, i) => new SenalAlertaPlantilla {
                Tipo = s.Tipo,
                Descripcion = s.Descripcion.Trim(),
                Fragmento = string.IsNullOrWhiteSpace(s.Fragmento) ? null : s.Fragmento.Trim(),
                Orden = i + 1,
            }).ToList();
        }
        if (r.Alineaciones is not null)
        {
            db.Set<AlineacionPlantillaArea>().RemoveRange(p.Alineaciones);
            p.Alineaciones = r.Alineaciones.Select(a => new AlineacionPlantillaArea { IdArea = a.IdArea, Nivel = a.Nivel }).ToList();
        }
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(id.HasValue ? "EditarPlantilla" : "CrearPlantilla", true, actor, entidad: "PlantillaPhishing",
            idEntidadAfectada: p.Id_Plantilla.ToString(), detalles: new {
                senales = p.Senales.Count,
                p.AlineacionGeneral,
                alineacionesPorArea = p.Alineaciones.Count,
                p.PaginaSenuelo,
                dificultadGeneral = PhishScale.Dificultad(p.Senales.Count, p.AlineacionGeneral),
            });
        await tx.CommitAsync(ct);
        return p.Id_Plantilla;
    }

    private async Task ValidarAsync(CrearPlantillaRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre) || string.IsNullOrWhiteSpace(r.AsuntoCorreo) || string.IsNullOrWhiteSpace(r.CuerpoHtml))
            throw new BusinessRuleException("Completa el nombre, el asunto y el cuerpo de la plantilla.");
        if (!PhishScale.Alineaciones.Contains(r.AlineacionGeneral))
            throw new BusinessRuleException("La alineación general debe ser débil, media o fuerte.");
        if (r.PaginaSenuelo is not ("ninguna" or "credenciales"))
            throw new BusinessRuleException("Selecciona una página señuelo válida.");
        if (Limpio(r.RemitenteCorreo) is { } correo && !System.Text.RegularExpressions.Regex.IsMatch(correo, @"^[^@\s<>""]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$"))
            throw new BusinessRuleException("La dirección del remitente simulado no es válida (ejemplo: soporte@banco-verificacion.com).");
        if (Limpio(r.EnlaceMostrado) is { } enlace && !(Uri.TryCreate(enlace, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.')))
            throw new BusinessRuleException("El enlace mostrado debe ser una URL completa que empiece por http:// o https://.");
        if (r.PaginaSenuelo == "credenciales" && !r.CuerpoHtml.Contains("{{LinkTrampa}}"))
            throw new BusinessRuleException("La página señuelo de credenciales requiere que el correo incluya {{LinkTrampa}}.");
        if (r.Senales is { } senales)
        {
            if (senales.Count > MaxSenales)
                throw new BusinessRuleException($"Registra como máximo {MaxSenales} señales de alerta.");
            foreach (var s in senales)
            {
                if (s is null || !PhishScale.Tipos.ContainsKey(s.Tipo ?? ""))
                    throw new BusinessRuleException("Cada señal debe tener un tipo NIST válido.");
                if (string.IsNullOrWhiteSpace(s.Descripcion) || s.Descripcion.Trim().Length > 300)
                    throw new BusinessRuleException("Describe cada señal de alerta (máximo 300 caracteres).");
                if (s.Fragmento?.Trim().Length > 500)
                    throw new BusinessRuleException("El fragmento de una señal admite como máximo 500 caracteres.");
            }
        }
        if (r.Alineaciones is { } alineaciones)
        {
            if (alineaciones.Any(a => a is null || !PhishScale.Alineaciones.Contains(a.Nivel ?? "")))
                throw new BusinessRuleException("Cada alineación por área debe ser débil, media o fuerte.");
            var ids = alineaciones.Select(a => a.IdArea).ToList();
            if (ids.Distinct().Count() != ids.Count)
                throw new BusinessRuleException("Cada área puede tener una sola alineación.");
            if (ids.Count > 0 && await db.AreasOperativas.CountAsync(a => ids.Contains(a.Id_Area), ct) != ids.Count)
                throw new BusinessRuleException("Una de las áreas de la alineación no existe.");
        }
        if (!await db.NivelesCriticidad.AnyAsync(c => c.Id_NivelCriticidad == r.IdCriticidad, ct))
            throw new BusinessRuleException("El nivel de criticidad seleccionado no existe.");
        if (!await db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == r.IdCategoriaPregunta, ct))
            throw new BusinessRuleException("La categoría de concientización seleccionada no existe.");
    }
    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
