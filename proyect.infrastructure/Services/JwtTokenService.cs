using proyect.application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using proyect.domain.Entidades;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using proyect.application.Security;

namespace proyect.infrastructure.Services
{

    public class JwtTokenService : IJwtTokenService
    {
        private const string PropositoMfaPendiente = "mfa_pendiente";
        private const string PropositoCambioPassword = "cambio_password_obligatorio";
        private readonly IConfiguration _config;

        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }

        // audiencia distinta para que un token de proposito nunca se acepte como token de acceso
        public static string AudienciaProposito(string audiencia, string proposito) => $"{audiencia}#{proposito}";

        public string GenerarToken(ApplicationUser usuario, IEnumerable<string> roles)
        {
            var jwtSection = _config.GetSection("Jwt");
            var credenciales = new SigningCredentials(ClaveFirma(), SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, usuario.Email ?? string.Empty),
                new(ClaimTypes.Name, usuario.UserName ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtClaim.SecurityStamp, usuario.SecurityStamp ?? string.Empty),
            };

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var expiryMins = int.TryParse(jwtSection["ExpiryMinutes"], out var minutos) && minutos is > 0 and <= 60 ? minutos : 15;
            var token = new JwtSecurityToken(
                    issuer: jwtSection["Issuer"],
                    audience: jwtSection["Audience"],
                    claims: claims,
                    notBefore: DateTime.UtcNow,
                    expires: DateTime.UtcNow.AddMinutes(expiryMins),
                    signingCredentials: credenciales
                );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerarTokenMfaPending(ApplicationUser usuario) =>
            GenerarTokenProposito(usuario, PropositoMfaPendiente, minutosValidez: 5);

        public TokenPropositoValido? ValidarTokenMfaPending(string token) =>
            ValidarTokenProposito(token, PropositoMfaPendiente);

        public string GenerarTokenCambioPasswordObligatorio(ApplicationUser usuario) =>
            GenerarTokenProposito(usuario, PropositoCambioPassword, minutosValidez: 10);

        public TokenPropositoValido? ValidarTokenCambioPasswordObligatorio(string token) =>
            ValidarTokenProposito(token, PropositoCambioPassword);

        private SymmetricSecurityKey ClaveFirma() =>
            new(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        private string GenerarTokenProposito(ApplicationUser usuario, string proposito, int minutosValidez)
        {
            var jwtSection = _config.GetSection("Jwt");
            var credenciales = new SigningCredentials(ClaveFirma(), SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtClaim.SecurityStamp, usuario.SecurityStamp ?? string.Empty),
                new("purpose", proposito)
            };

            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: AudienciaProposito(jwtSection["Audience"] ?? string.Empty, proposito),
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(minutosValidez),
                signingCredentials: credenciales);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private TokenPropositoValido? ValidarTokenProposito(string token, string propositoEsperado)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
                return null;
            var jwtSection = _config.GetSection("Jwt");
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

            try
            {
                var principal = handler.ValidateToken(token, new TokenValidationParameters {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = AudienciaProposito(jwtSection["Audience"] ?? string.Empty, propositoEsperado),
                    IssuerSigningKey = ClaveFirma(),
                    ClockSkew = TimeSpan.FromSeconds(30)
                }, out _);

                if (principal.FindFirstValue("purpose") != propositoEsperado)
                    return null;

                var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var stamp = principal.FindFirstValue(JwtClaim.SecurityStamp);
                return int.TryParse(sub, out var id) && !string.IsNullOrEmpty(stamp) ? new TokenPropositoValido(id, stamp) : null;
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                return null; // token expirado, manipulado o mal formado
            }
        }
    }
}
