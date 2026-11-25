using proyect.domain.Entidades;

namespace proyect.application.Abstractions
{
    public interface IJwtTokenService
    {
        string GenerarToken(ApplicationUser usuario, IEnumerable<string> roles);

        // token ligado al security stamp: un cambio de contrasena, rol o estado lo invalida
        string GenerarTokenMfaPending(ApplicationUser usuario);
        TokenPropositoValido? ValidarTokenMfaPending(string token);

        string GenerarTokenCambioPasswordObligatorio(ApplicationUser usuario);
        TokenPropositoValido? ValidarTokenCambioPasswordObligatorio(string token);
    }

    public sealed record TokenPropositoValido(int IdUsuario, string SecurityStamp)
    {
        public bool CorrespondeA(ApplicationUser usuario) =>
            usuario.Id == IdUsuario && !string.IsNullOrEmpty(SecurityStamp) && SecurityStamp == usuario.SecurityStamp;
    }
}
