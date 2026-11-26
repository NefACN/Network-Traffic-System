using proyect.application.Services;

namespace proyecto.api.Configuration;

// revisa cada IntervaloMinutos si toca el envio diario de recordatorios, maximo una vez por dia
public sealed class RecordatoriosAprendizajeWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RecordatoriosAprendizajeWorker> logger) : BackgroundService
{
    public static ConfiguracionRecordatorios Configuracion(IConfiguration configuration)
    {
        var s = configuration.GetSection("Programa:Recordatorios");
        return new ConfiguracionRecordatorios(
            s.GetValue("Activo", true),
            Math.Clamp(s.GetValue("HoraLocal", 9), 0, 23),
            s.GetValue("SoloDiasHabiles", true),
            Math.Clamp(s.GetValue("IntervaloMinutos", 30), 5, 720));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // margen para que la API termine de iniciar y las migraciones esten aplicadas
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = Configuracion(configuration);
            if (config.Activo)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var resultado = await scope.ServiceProvider.GetRequiredService<RecordatoriosProgramadosService>()
                        .EjecutarSiCorrespondeAsync(DateTime.UtcNow, config, stoppingToken);
                    if (resultado is not null)
                        logger.LogInformation("Recordatorios automáticos de aprendizaje enviados: {Resultado}", System.Text.Json.JsonSerializer.Serialize(resultado));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "No se pudieron enviar los recordatorios automáticos de aprendizaje; se reintentará.");
                }
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(config.IntervaloMinutos), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
