using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Caching.Memory;

namespace proyecto.api.Configuration;

// deduplica POST/PUT/PATCH/DELETE autenticados mediante la cabecera Idempotency-Key
public static class Idempotencia
{
    public const string Cabecera = "Idempotency-Key";

    public static IServiceCollection AddIdempotencia(this IServiceCollection services)
    {
        services.AddMemoryCache();
        return services;
    }

    // va despues de autenticacion y autorizacion para asociar la clave al usuario
    public static IApplicationBuilder UseIdempotencia(this IApplicationBuilder app) => app.UseMiddleware<IdempotenciaMiddleware>();
}

internal sealed class IdempotenciaMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);
    private const int LongitudMaximaClave = 100;
    // solo se guardan respuestas JSON o vacias de hasta 1 MB
    private const int TamanioMaximoRespuesta = 1024 * 1024;
    // el cerrojo solo protege la consulta y el alta del registro, no la ejecucion
    private readonly object _cerrojo = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!EsMutacion(request.Method) || !request.Headers.TryGetValue(Idempotencia.Cabecera, out var valores))
        {
            await next(context);
            return;
        }
        var usuario = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;
        if (usuario is null)
        {
            // sin usuario no hay ambito seguro para la clave: se procesa sin deduplicar
            await next(context);
            return;
        }
        if (valores.Count != 1 || !ClaveValida(valores[0]))
        {
            await Responder(context, StatusCodes.Status400BadRequest,
                "La cabecera Idempotency-Key debe tener entre 1 y 100 caracteres imprimibles, sin espacios.");
            return;
        }

        var huella = await HuellaAsync(context);
        var llave = string.Join('\n', "idempotencia", usuario, request.Method.ToUpperInvariant(), request.Path.Value, request.QueryString.Value, valores[0]);
        Registro registro;
        var propio = false;
        lock (_cerrojo)
        {
            if (!cache.TryGetValue(llave, out Registro? existente) || existente is null)
            {
                existente = new Registro(huella);
                cache.Set(llave, existente, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Vigencia, Size = 1 });
                propio = true;
            }
            registro = existente;
        }

        if (!propio)
        {
            if (registro.Huella != huella)
                await Responder(context, StatusCodes.Status422UnprocessableEntity,
                    "Esta clave de idempotencia ya se usó con datos distintos. Genera una nueva clave para esta solicitud.");
            else if (registro.Respuesta is not { } guardada)
                await Responder(context, StatusCodes.Status409Conflict, "La solicitud anterior con esta clave aún se está procesando.");
            else
                await Repetir(context, guardada);
            return;
        }

        var original = context.Response.Body;
        var captura = new CapturaRespuesta(original, TamanioMaximoRespuesta);
        context.Response.Body = captura;
        var guardado = false;
        try
        {
            await next(context);
            var respuesta = context.Response;
            var cuerpo = captura.Capturado;
            // un 5xx no se guarda para permitir el reintento con la misma clave
            if (respuesta.StatusCode < 500 && cuerpo is not null && (cuerpo.Length == 0 || EsJson(respuesta.ContentType)))
            {
                lock (_cerrojo)
                {
                    registro.Respuesta = new RespuestaGuardada(respuesta.StatusCode, respuesta.ContentType, respuesta.Headers.Location.ToString(), cuerpo);
                    cache.Set(llave, registro, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Vigencia, Size = cuerpo.Length + 1 });
                }
                guardado = true;
            }
        }
        finally
        {
            context.Response.Body = original;
            if (!guardado)
                lock (_cerrojo)
                    cache.Remove(llave);
        }
    }

    private static bool EsMutacion(string metodo) =>
        HttpMethods.IsPost(metodo) || HttpMethods.IsPut(metodo) || HttpMethods.IsPatch(metodo) || HttpMethods.IsDelete(metodo);

    private static bool ClaveValida(string? clave) =>
        clave is { Length: > 0 and <= LongitudMaximaClave } && clave.All(c => c is >= '!' and <= '~');

    private static bool EsJson(string? tipo) =>
        tipo is not null && (tipo.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || tipo.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase));

    // SHA-256 del cuerpo, en multipart se usa el tamano declarado porque el separador cambia
    private static async Task<string> HuellaAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true)
            return $"multipart:{request.ContentLength}";

        // leer el cuerpo antes de MVC exige aplicar ya el limite propio del endpoint ([RequestSizeLimit])
        if (context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is { } limite
            && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } tamanio)
            tamanio.MaxRequestBodySize = limite.MaxRequestBodySize;

        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bufer = new byte[16 * 1024];
        int leidos;

        while ((leidos = await request.Body.ReadAsync(bufer, context.RequestAborted)) > 0)
            hash.AppendData(bufer, 0, leidos);
        request.Body.Position = 0;

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Task Responder(HttpContext context, int estado, string mensaje)
    {
        context.Response.StatusCode = estado;
        return context.Response.WriteAsJsonAsync(new { mensaje });
    }

    private static async Task Repetir(HttpContext context, RespuestaGuardada guardada)
    {
        var respuesta = context.Response;
        respuesta.StatusCode = guardada.Estado;

        if (guardada.TipoContenido is not null)
            respuesta.ContentType = guardada.TipoContenido;

        if (!string.IsNullOrEmpty(guardada.Ubicacion))
            respuesta.Headers.Location = guardada.Ubicacion;

        respuesta.Headers["Idempotency-Replayed"] = "true";

        if (guardada.Cuerpo.Length > 0)
            await respuesta.Body.WriteAsync(guardada.Cuerpo, context.RequestAborted);
    }

    private sealed class Registro(string huella)
    {
        public string Huella { get; } = huella;
        // null mientras la solicitud original se esta procesando
        public volatile RespuestaGuardada? Respuesta;
    }

    private sealed record RespuestaGuardada(int Estado, string? TipoContenido, string? Ubicacion, byte[] Cuerpo);

    // escribe directo al cliente y copia hasta el maximo indicado
    private sealed class CapturaRespuesta(Stream destino, int maximo) : Stream
    {
        private readonly MemoryStream _copia = new();
        private bool _excedido;

        // el cuerpo completo, o null si supero el maximo
        public byte[]? Capturado => _excedido ? null : _copia.ToArray();

        private void Copiar(ReadOnlySpan<byte> datos)
        {
            if (_excedido)
                return;
            if (_copia.Length + datos.Length > maximo)
            { _excedido = true; _copia.SetLength(0); return; }
            _copia.Write(datos);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { Copiar(buffer); destino.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Copiar(buffer.Span);
            return destino.WriteAsync(buffer, cancellationToken);
        }
        public override void Flush() => destino.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => destino.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
