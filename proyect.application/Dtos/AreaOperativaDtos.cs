using System.ComponentModel.DataAnnotations;

namespace proyect.application.Dtos
{
    public record AreaOperativaDto(
        int IdArea,
        string Nombre,
        int IdCriticidad,
        string NivelCriticidad,
        bool Activo = true
    );

    public record CrearAreaOperativaRequest(
        [Required, StringLength(50)] string Nombre,
        [Range(1, int.MaxValue)] int IdCriticidad
    );

    public record ActualizarAreaOperativaRequest(
        [Required, StringLength(50)] string Nombre,
        [Range(1, int.MaxValue)] int IdCriticidad
    );

    public record CambiarEstadoAreaRequest(bool Activo);

}
