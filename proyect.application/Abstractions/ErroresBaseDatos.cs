using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace proyect.application.Abstractions;

public static class ErroresBaseDatos
{
    public const string ViolacionUnica = "23505";

    public const string ViolacionForanea = "23503";

    public static bool EsDuplicado(this DbUpdateException error) => error.InnerException is DbException { SqlState: ViolacionUnica };

    public static bool EsReferenciaInvalida(this DbUpdateException error) => error.InnerException is DbException { SqlState: ViolacionForanea };
}
