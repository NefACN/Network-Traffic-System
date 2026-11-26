using Microsoft.AspNetCore.Diagnostics;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace proyecto.api.Errors;

// convierte reglas de negocio y conflictos de BD en 4xx con {mensaje}
public sealed class BusinessExceptionHandler(ILogger<BusinessExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is BusinessRuleException error)
        {
            context.Response.StatusCode = error.Error switch {
                BusinessError.NotFound => StatusCodes.Status404NotFound,
                BusinessError.Conflict => StatusCodes.Status409Conflict,
                BusinessError.Unavailable => StatusCodes.Status503ServiceUnavailable,
                BusinessError.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            await context.Response.WriteAsJsonAsync(new { mensaje = error.Message }, ct);
            return true;
        }
        var mensaje = MensajeConflicto(exception);
        if (mensaje is null)
            return false;
        // conflicto esperado: se registra sin datos de la fila y se responde 409

        logger.LogWarning("Conflicto de base de datos en {Metodo} {Ruta}: {Tipo}.", context.Request.Method, context.Request.Path, exception.GetType().Name);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { mensaje }, ct);
        return true;
    }

    private static string? MensajeConflicto(Exception exception) => exception switch {
        DbUpdateConcurrencyException => "El registro fue modificado o eliminado por otra solicitud. Actualiza la página e intenta de nuevo.",
        // tambien cubre dos altas concurrentes que superaron la validacion previa
        DbUpdateException { InnerException: PostgresException { ConstraintName: "UQ_areas_nombre_normalizado" or "UQ_sucursales_nombre_ciudad" } } db when db.EsDuplicado()
            => "Ya existe una unidad con ese nombre e identificación. Revisa los registros activos e inactivos.",
        DbUpdateException db when db.EsDuplicado() => "El registro ya existe o fue creado por otra solicitud simultánea.",
        DbUpdateException db when db.EsReferenciaInvalida() => "No se puede completar: el registro está en uso o depende de otro que ya no existe.",
        _ => null
    };
}
