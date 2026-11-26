using System.Net;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// reporte de mensajes reales con seguimiento ISO/IEC 27002 6.8 y 5.24-5.27
public sealed class IncidentesService(
    IApplicationDbContext db,
    IAuditoriaService auditoria,
    ReportesSospechososService simulaciones,
    IEmailSender correo,
    SugerenciasService sugerencias)
{
    public static readonly IReadOnlyList<string> Canales = ["correo", "sms", "whatsapp", "llamada", "otro"];
    public static readonly IReadOnlyList<string> Interacciones = ["ninguna", "clic", "adjunto", "datos"];
    public static readonly IReadOnlyList<string> Estados = ["recibido", "en_analisis", "resuelto"];
    public static readonly IReadOnlyList<string> Clasificaciones = ["malicioso", "spam", "legitimo", "simulacion"];
    public const int LimiteDiario = 20;

    // prioridad inicial segun la interaccion reportada
    public static string PrioridadInicial(string interaccion) => interaccion switch {
        "datos" or "adjunto" => "alta",
        "clic" => "media",
        _ => "baja",
    };

    public static string Recomendacion(string interaccion) => interaccion switch {
        "datos" => "Cambia ahora la contraseña que hayas escrito y avisa a tu jefatura. El equipo de seguridad revisará tu cuenta.",
        "adjunto" => "Desconecta el equipo de la red si notas comportamientos extraños y no vuelvas a abrir el archivo.",
        "clic" => "No escribas datos en la página que se abrió y cierra la pestaña. Te informaremos el resultado del análisis.",
        _ => "No respondas ni reenvíes el mensaje. Puedes eliminarlo después de reportarlo.",
    };

    public async Task<object> ReportarAsync(int usuario, ReporteIncidenteRequest r, CancellationToken ct)
    {
        if (!Canales.Contains(r.Canal))
            throw new BusinessRuleException("Selecciona el canal por el que llegó el mensaje.");

        if (!Interacciones.Contains(r.Interaccion))
            throw new BusinessRuleException("Indica qué hiciste con el mensaje.");

        if (string.IsNullOrWhiteSpace(r.Asunto) || string.IsNullOrWhiteSpace(r.Descripcion))
            throw new BusinessRuleException("Indica el asunto o tema del mensaje y describe qué te pareció sospechoso.");

        var desde = DateTime.UtcNow.AddDays(-1);
        if (await db.Set<IncidenteReportado>().CountAsync(i => i.IdUsuario == usuario && i.FechaCreacion >= desde, ct) >= LimiteDiario)
            throw new BusinessRuleException(
                $"Alcanzaste el máximo de {LimiteDiario} reportes en 24 horas. Si es urgente, comunícate con el equipo de seguridad.",
                BusinessError.Conflict);

        // si hay simulacion activa se compara el asunto normalizado con el de la campania
        var asunto = GobernanzaService.Normalizar(r.Asunto).Trim();
        var now = DateTime.UtcNow;
        var candidatas = await db.EnviosSimulacion.AsNoTracking()
            .Where(e => e.Id_Usuario == usuario && e.EstadoEntrega == "accepted"
                && e.FechaReporte == null && e.Campania.Id_Estado == 2 && e.Campania.FechaFinalizacion == null
                && (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now))
            .Select(e => new { e.TokenUuid, asunto = e.Campania.AsuntoRegistro ?? e.Campania.Plantilla.AsuntoCorreo })
            .ToListAsync(ct);

        var simulacion = candidatas.FirstOrDefault(c => GobernanzaService.Normalizar(c.asunto).Trim() == asunto);

        if (simulacion is not null)
        {
            await simulaciones.ReportarAsync(simulacion.TokenUuid, usuario, true, ct);

            return new {
                tipo = "simulacion",
                mensaje = "¡Bien hecho! Este mensaje era una simulación de entrenamiento. Tu reporte quedó registrado en tu progreso preventivo."
            };
        }

        var persona = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuario)
            .Select(u => new { u.Id_Sucursal, u.Id_Area })
            .SingleAsync(ct);

        var incidente = new IncidenteReportado {
            IdUsuario = usuario,
            IdSucursalRegistro = persona.Id_Sucursal,
            IdAreaRegistro = persona.Id_Area,
            Canal = r.Canal,
            Remitente = string.IsNullOrWhiteSpace(r.Remitente) ? null : r.Remitente.Trim(),
            Asunto = r.Asunto.Trim(),
            Descripcion = r.Descripcion.Trim(),
            Interaccion = r.Interaccion,
            Prioridad = PrioridadInicial(r.Interaccion),
            Estado = "recibido",
            FechaCreacion = now,
            Codigo = "INC-" + Guid.NewGuid().ToString("N")[..8],
        };

        incidente.Seguimientos.Add(new IncidenteSeguimiento {
            IdUsuario = usuario,
            Fecha = now,
            EstadoNuevo = "recibido",
            Publica = true,
            Nota = "Recibimos tu reporte. " + Recomendacion(r.Interaccion)
        });

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.BloquearAsync(Bloqueos.Incidentes, usuario, ct);
        if (await db.Set<IncidenteReportado>().CountAsync(i => i.IdUsuario == usuario && i.FechaCreacion >= desde, ct) >= LimiteDiario)
            throw new BusinessRuleException(
                $"Alcanzaste el máximo de {LimiteDiario} reportes en 24 horas. Si es urgente, comunícate con el equipo de seguridad.",
                BusinessError.Conflict);

        db.Set<IncidenteReportado>().Add(incidente);
        await db.SaveChangesAsync(ct);

        incidente.Codigo = $"INC-{incidente.Id:000000}";
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(
            "ReportarIncidente",
            true,
            usuario,
            entidad: "IncidenteReportado",
            idEntidadAfectada: incidente.Id.ToString(),
            detalles: new {
                incidente.Codigo,
                incidente.Canal,
                incidente.Interaccion,
                incidente.Prioridad
            });

        await tx.CommitAsync(ct);

        return new {
            tipo = "incidente",
            incidente.Codigo,
            incidente.Prioridad,
            recomendacion = Recomendacion(r.Interaccion),
            mensaje = $"Reporte {incidente.Codigo} recibido. El equipo de seguridad lo analizará y te responderá aquí."
        };
    }

    // reportes paginados
    public async Task<object> MisReportesAsync(int usuario, CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.Set<IncidenteReportado>().AsNoTracking()
            .Where(i => i.IdUsuario == usuario)
            .OrderByDescending(i => i.FechaCreacion)
            .ThenByDescending(i => i.Id)
            .Select(i => new {
                i.Codigo,
                i.Canal,
                i.Asunto,
                i.Estado,
                i.Clasificacion,
                i.Respuesta,
                i.FechaCreacion,
                i.FechaResolucion,
                seguimiento = i.Seguimientos
                    .Where(s => s.Publica)
                    .OrderBy(s => s.Fecha)
                    .Select(s => new { s.Fecha, s.EstadoNuevo, s.Nota })
                    .ToList(),
            }).PaginarAsync(pagina, tamanio, ct);

    public async Task<object> ListarAsync(string? estado, string? prioridad, int pagina, int tamanio, CancellationToken ct)
    {
        if (tamanio is < 1 or > 100 || pagina < 1 || pagina > 100000)
            throw new BusinessRuleException("La paginación solicitada está fuera del rango permitido.");

        var q = db.Set<IncidenteReportado>().AsNoTracking();

        if (!string.IsNullOrEmpty(estado))
            q = q.Where(i => i.Estado == estado);

        if (!string.IsNullOrEmpty(prioridad))
            q = q.Where(i => i.Prioridad == prioridad);

        var todos = db.Set<IncidenteReportado>().AsNoTracking();
        var tiempos = await todos.Select(i => new {
            i.FechaCreacion,
            i.FechaAtencion,
            i.FechaResolucion,
            i.Clasificacion,
            i.Estado
        }).ToListAsync(ct);

        static double? Mediana(IEnumerable<double> v)
        {
            var l = v.Order().ToList();
            return l.Count == 0
                ? null
                : Math.Round(l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2, 1);
        }

        return new {
            pagina,
            tamanio,
            total = await q.CountAsync(ct),
            items = await q
                .OrderBy(i => i.Estado == "resuelto")
                .ThenByDescending(i => i.Prioridad == "alta")
                .ThenByDescending(i => i.Prioridad == "media")
                .ThenBy(i => i.FechaCreacion)
                .Skip((pagina - 1) * tamanio)
                .Take(tamanio)
                .Select(i => new {
                    i.Id,
                    i.Codigo,
                    i.Canal,
                    i.Asunto,
                    i.Interaccion,
                    i.Prioridad,
                    i.Estado,
                    i.Clasificacion,
                    i.FechaCreacion,
                    i.FechaResolucion,
                    reportadoPor = i.Usuario.Nombre + " " + i.Usuario.Apellido,
                    sucursal = db.Sucursales.Where(s => s.Id_Sucursal == i.IdSucursalRegistro).Select(s => s.Nombre).FirstOrDefault(),
                    area = db.AreasOperativas.Where(a => a.Id_Area == i.IdAreaRegistro).Select(a => a.Nombre).FirstOrDefault()
                }).ToListAsync(ct),

            indicadores = new {
                porEstado = Estados.ToDictionary(e => e, e => tiempos.Count(t => t.Estado == e)),
                porClasificacion = Clasificaciones.ToDictionary(c => c, c => tiempos.Count(t => t.Clasificacion == c)),
                medianaHorasHastaAtencion = Mediana(tiempos
                    .Where(t => t.FechaAtencion != null)
                    .Select(t => (t.FechaAtencion!.Value - t.FechaCreacion).TotalHours)),
                medianaHorasHastaResolucion = Mediana(tiempos
                    .Where(t => t.FechaResolucion != null)
                    .Select(t => (t.FechaResolucion!.Value - t.FechaCreacion).TotalHours)),
            },
        };
    }

    public async Task<object> DetalleAsync(int id, CancellationToken ct) =>
        await db.Set<IncidenteReportado>().AsNoTracking().Where(i => i.Id == id).Select(i => new {
            i.Id,
            i.Codigo,
            i.Canal,
            i.Remitente,
            i.Asunto,
            i.Descripcion,
            i.Interaccion,
            i.Prioridad,
            i.Estado,
            i.Clasificacion,
            i.Respuesta,
            i.FechaCreacion,
            i.FechaAtencion,
            i.FechaResolucion,
            reportadoPor = i.Usuario.Nombre + " " + i.Usuario.Apellido,
            correo = i.Usuario.Email,
            sucursal = db.Sucursales.Where(s => s.Id_Sucursal == i.IdSucursalRegistro).Select(s => s.Nombre).FirstOrDefault(),
            area = db.AreasOperativas.Where(a => a.Id_Area == i.IdAreaRegistro).Select(a => a.Nombre).FirstOrDefault(),
            analista = db.Users.Where(u => u.Id == i.IdAnalista).Select(u => u.Nombre + " " + u.Apellido).FirstOrDefault(),
            seguimiento = i.Seguimientos.OrderBy(s => s.Fecha).Select(s => new {
                s.Fecha,
                s.EstadoAnterior,
                s.EstadoNuevo,
                s.Nota,
                s.Publica,
                autor = db.Users.Where(u => u.Id == s.IdUsuario).Select(u => u.Nombre + " " + u.Apellido).FirstOrDefault()
            }).ToList(),
        }).SingleOrDefaultAsync(ct) ?? throw new BusinessRuleException("El incidente no existe.", BusinessError.NotFound);

    public async Task ActualizarAsync(int id, int actor, ActualizarIncidenteRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Set<IncidenteReportado>()
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, i => i.Estado), ct);

        var i = await db.Set<IncidenteReportado>().Include(x => x.Usuario).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new BusinessRuleException("El incidente no existe.", BusinessError.NotFound);
        var nuevo = string.IsNullOrEmpty(r.Estado) ? i.Estado : r.Estado;

        if (!Estados.Contains(nuevo))
            throw new BusinessRuleException("Selecciona un estado válido.");

        if (r.Prioridad is not null && !new[] { "baja", "media", "alta" }.Contains(r.Prioridad))
            throw new BusinessRuleException("Selecciona una prioridad válida.");

        var nota = string.IsNullOrWhiteSpace(r.Nota) ? null : r.Nota.Trim();
        var anterior = i.Estado;

        if (anterior == "resuelto" && nuevo != "resuelto" && nota is null)
            throw new BusinessRuleException("Para reabrir un incidente resuelto, registra el motivo en la nota.");

        if (nuevo == "resuelto" && anterior != "resuelto")
        {
            if (r.Clasificacion is null || !Clasificaciones.Contains(r.Clasificacion))
                throw new BusinessRuleException("Clasifica el mensaje antes de resolver el incidente.");

            if (string.IsNullOrWhiteSpace(r.Respuesta))
                throw new BusinessRuleException("Escribe la respuesta que recibirá quien reportó.");
        }

        if (nuevo == anterior && nota is null && r.Prioridad is null)
            throw new BusinessRuleException("No hay cambios para registrar.");

        var now = DateTime.UtcNow;

        if (anterior == "recibido" && nuevo != "recibido")
        {
            i.FechaAtencion ??= now;
            i.IdAnalista ??= actor;
        }

        if (nuevo == "resuelto" && anterior != "resuelto")
        {
            i.Clasificacion = r.Clasificacion;
            i.Respuesta = r.Respuesta!.Trim();
            i.FechaResolucion = now;
            i.IdAnalista ??= actor;
        }

        // incidente reabierto tras haber sido resuelto
        if (anterior == "resuelto" && nuevo != "resuelto")
        {
            i.FechaResolucion = null;
            i.Clasificacion = null;
        }

        if (r.Prioridad is not null)
            i.Prioridad = r.Prioridad;

        i.Estado = nuevo;
        i.Seguimientos.Add(new IncidenteSeguimiento {
            IdIncidente = i.Id,
            IdUsuario = actor,
            Fecha = now,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            Nota = nuevo == "resuelto" && anterior != "resuelto" ? i.Respuesta : nota,
            Publica = (nuevo == "resuelto" && anterior != "resuelto") || r.NotaPublica
        });

        await db.SaveChangesAsync(ct);

        // ciclo cerrado
        if (nuevo == "resuelto" && anterior != "resuelto" && i.Clasificacion == "malicioso")
            await sugerencias.SugerirAsync("incidente", i.IdSucursalRegistro, i.IdAreaRegistro, null, i.Codigo,
                $"Incidente real confirmado ({i.Canal}): «{i.Asunto}»", ct);

        await auditoria.RegistrarAsync("ActualizarIncidente", true, actor, entidad: "IncidenteReportado",
            idEntidadAfectada: id.ToString(), detalles: new { i.Codigo, anterior, nuevo, i.Clasificacion, i.Prioridad });

        await tx.CommitAsync(ct);

        if (nuevo == "resuelto" && anterior != "resuelto" && !string.IsNullOrWhiteSpace(i.Usuario.Email))
        {
            // retroalimentacion al colaborador que reporto el incidente
            try
            {
                await correo.EnviarAsync(i.Usuario.Email!, $"{i.Usuario.Nombre} {i.Usuario.Apellido}", $"Resultado de tu reporte {i.Codigo}",
                    $"<p>Hola {WebUtility.HtmlEncode(i.Usuario.Nombre)},</p><p>Gracias por reportar «{WebUtility.HtmlEncode(i.Asunto)}». Resultado: <strong>{WebUtility.HtmlEncode(Etiqueta(i.Clasificacion))}</strong>.</p><p>{WebUtility.HtmlEncode(i.Respuesta)}</p><p>Reportar a tiempo protege a toda la organización.</p>");
            }
            catch (Exception)
            {
                /* queda visible para el colaborador */
            }
        }
    }

    public static string Etiqueta(string? clasificacion) => clasificacion switch {
        "malicioso" => "mensaje malicioso confirmado",
        "spam" => "correo no deseado",
        "legitimo" => "mensaje legítimo",
        "simulacion" => "simulación de entrenamiento",
        _ => "sin clasificar",
    };
}
