using System.ComponentModel.DataAnnotations;

namespace proyect.application.Dtos
{
    public record LoginRequest
    {
        [Required(ErrorMessage = "El correo electrónico o usuario es obligatorio"), StringLength(256)]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria"), StringLength(128)]
        public string Password { get; set; } = string.Empty;
    }

    public record LoginResponse(
        bool RequiereCambioPassword,
        string? CambioPasswordToken,
        bool RequiereMfa,
        string? Token,
        string? Refreshtoken,
        string? MfaToken,
        List<string>? MetodosDisponibles);

    public class CambiarPasswordObligatorioRequest
    {
        [Required, StringLength(4096)]
        public string CambioPasswordToken { get; set; } = string.Empty;

        [Required, StringLength(128, MinimumLength = 12, ErrorMessage = "La contraseña debe tener entre 12 y 128 caracteres.")]
        public string PasswordNueva { get; set; } = string.Empty;
    }

    public class CambiarPasswordRequest
    {
        [Required, StringLength(128)]
        public string PasswordActual { get; set; } = string.Empty;

        [Required, StringLength(128, MinimumLength = 12, ErrorMessage = "La contraseña debe tener entre 12 y 128 caracteres.")]
        public string PasswordNueva { get; set; } = string.Empty;
    }
    public class EnviarCodigoMfaRequest
    {
        [Required, StringLength(4096)]
        public string MfaToken { get; set; } = string.Empty;

        [Required, StringLength(40)]
        public string Metodo { get; set; } = string.Empty;
    }

    public class VerifyMfaRequest
    {
        [Required, StringLength(4096)]
        public string MfaToken { get; set; } = string.Empty;

        [Required, StringLength(40)]
        public string Metodo { get; set; } = string.Empty;

        [Required, StringLength(10, MinimumLength = 6)]
        public string Codigo { get; set; } = string.Empty;
    }
    public class VerifyMfaRecoveryRequest
    {
        [Required, StringLength(4096)]
        public string MfaToken { get; set; } = string.Empty;

        [Required, StringLength(64)]
        public string CodigoRecuperacion { get; set; } = string.Empty;
    }

    public record TokenResponse(string Token, string RefreshToken);
    public record RefreshTokenRequest
    {
        [Required, StringLength(512)]
        public string RefreshToken { get; set; } = string.Empty;
    }
    public record OlvidePasswordRequest
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;
    }
    public class ResetPasswordTokenRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(2048)]
        public string Token { get; set; } = string.Empty;

        [Required, StringLength(128, MinimumLength = 12, ErrorMessage = "La contraseña debe tener entre 12 y 128 caracteres.")]
        public string PasswordNueva { get; set; } = string.Empty;
    }
}
