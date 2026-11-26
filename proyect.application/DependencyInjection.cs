using Microsoft.Extensions.DependencyInjection;
using proyect.application.Abstractions;
using proyect.application.Services;

namespace proyect.application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuditoriaService, AuditoriaService>();

        services.AddScoped<AreasOperativasService>();

        services.AddScoped<SucursalesService>();

        services.AddScoped<CiudadesService>();

        services.AddScoped<AuditoriaConsultaService>();

        services.AddScoped<RolesService>();

        services.AddScoped<SeguimientoPreventivoService>();

        services.AddScoped<CuentaService>();

        services.AddScoped<MlLiveService>();

        services.AddScoped<PhishingResultadoService>();

        services.AddScoped<CampaniasService>();

        services.AddScoped<PlantillasService>();

        services.AddScoped<ReportesSospechososService>();

        services.AddScoped<ResultadosPreventivosService>();

        services.AddScoped<ReportesService>();

        services.AddScoped<GamificacionService>();

        services.AddScoped<CyberScoreService>();

        services.AddScoped<MicroleccionesService>();

        services.AddScoped<PlanRefuerzoService>();

        services.AddScoped<CyberScoreColaboradoresService>();

        services.AddScoped<GobernanzaService>();

        services.AddScoped<IncidentesService>();

        services.AddScoped<SugerenciasService>();

        services.AddScoped<SimulacionHttpService>();

        services.AddScoped<RutasAprendizajeService>();

        services.AddScoped<ConstanciasService>();

        services.AddScoped<PlanificadorService>();

        services.AddScoped<EncuestasCulturaService>();

        services.AddScoped<ProgramaService>();

        services.AddScoped<RecordatoriosProgramadosService>();

        services.AddScoped<ActividadesEducativasService>();

        services.AddScoped<MlService>();

        services.AddScoped<ModelTrainingService>();

        services.AddScoped<MlReportingService>();

        services.AddScoped<MlAgentService>();

        return services;
    }
}
