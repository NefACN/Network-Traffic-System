using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using proyect.application.Security;
using proyecto.api.Configuration;
using proyect.infrastructure.Services;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/mfa")]
    [Authorize]
    [EnableRateLimiting(SecurityHardening.PoliticaAutenticacion)]
    public class MfaController : ControllerBase
    {
        private const string FormatoUriAutenticator = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
        private const string MensajeBloqueo = "Cuenta bloqueada temporalmente por demasiados intentos fallidos.";
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly IAuditoriaService _auditoria;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<MfaController> _logger;
        private readonly PoliticaMfa _politica;

        public MfaController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration config,
            IAuditoriaService auditoria, IEmailSender emailSender, ILogger<MfaController> logger, PoliticaMfa politica)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _auditoria = auditoria;
            _emailSender = emailSender;
            _logger = logger;
            _politica = politica;
        }

        [HttpGet("estado")]
        public async Task<ActionResult> VerificarEstadoMfa()
        {
            var usuario = await ObtenerCurrentUserAsync();
            if (usuario is null)
                return Unauthorized();

            var habilitado = await _userManager.GetTwoFactorEnabledAsync(usuario);
            var autenticador = await _politica.AutenticadorConfirmadoAsync(usuario);
            var codigosRecuperacionRestantes = await _userManager.CountRecoveryCodesAsync(usuario);
            var obligatorio = await _politica.RequiereMfaAsync(usuario.Id);
            return Ok(new EstadoMfaResponse(habilitado, autenticador, codigosRecuperacionRestantes, obligatorio));
        }

        [HttpPost("autenticator/iniciar")]
        public async Task<ActionResult<IniciarAutenticatorResponse>> IniciarAutenticador()
        {
            var usuario = await ObtenerCurrentUserAsync();
            if (usuario is null)
                return Unauthorized();

            // solo se bloquea si ya hay una app confirmada, el MFA por correo puede sumar la app
            if (await _politica.AutenticadorConfirmadoAsync(usuario))
                return Conflict(new { mensaje = "Restablece la app autenticadora actual antes de configurar una nueva." });

            await _userManager.ResetAuthenticatorKeyAsync(usuario);
            await _politica.MarcarAutenticadorPendienteAsync(usuario);
            var clave = await _userManager.GetAuthenticatorKeyAsync(usuario);

            if (string.IsNullOrEmpty(clave))
            {
                // fallo genuino del almacen de Identity: se registra y se responde 500 sin detalles
                _logger.LogError("No se pudo generar la clave del autenticador para el usuario {IdUsuario}.", usuario.Id);
                return StatusCode(500, new { mensaje = "No se pudo generar la clave" });
            }

            var emisor = _config["Jwt:Issuer"] ?? "Proyecto";
            var uriQr = string.Format(
                FormatoUriAutenticator,
                UrlEncoder.Default.Encode(emisor),
                UrlEncoder.Default.Encode(usuario.Email ?? usuario.UserName ?? "usuario"),
                clave);

            await _auditoria.RegistrarAsync("IniciarConfiguracionMfa", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
            return Ok(new IniciarAutenticatorResponse(ClaveManual: AuthenticatorKeyFormatter.Format(clave), UriQr: uriQr));
        }

        [HttpPost("autenticator/confirmar")]
        public async Task<ActionResult<ConfirmarAutenticatorResponse>> ConfirmarAutenticador(ConfirmarAutenticatorRequest request)
        {
            var usuario = await ObtenerCurrentUserAsync();
            if (usuario is null)
                return Unauthorized();

            // con 2FA activo no se reconfirma, regenerar codigos exige el endpoint con contrasena
            if (await _politica.AutenticadorConfirmadoAsync(usuario))
                return Conflict(new { mensaje = "La app autenticadora ya está configurada." });

            if (string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(usuario)))
                return BadRequest(new { mensaje = "Primero inicia la configuración de la app autenticadora." });

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            var esValido = await _userManager.VerifyTwoFactorTokenAsync(
                usuario, TokenOptions.DefaultAuthenticatorProvider, request.Codigo);

            if (!esValido)
            {
                await _userManager.AccessFailedAsync(usuario);
                await _auditoria.RegistrarAsync("ConfigurarMfa", exitoso: false, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
                return BadRequest(new { mensaje = "Código incorrecto. Verifica que la hora de tu dispositivo esté sincronizada." });
            }

            await _userManager.ResetAccessFailedCountAsync(usuario);
            var mfaPrevio = await _userManager.GetTwoFactorEnabledAsync(usuario);
            await _userManager.SetTwoFactorEnabledAsync(usuario, true);
            await _politica.ConfirmarAutenticadorAsync(usuario);
            // si la cuenta no tenia segundo factor, sus sesiones anteriores quedan revocadas
            if (!mfaPrevio)
                await _politica.RevocarSesionesAsync(usuario.Id);

            var codigosRecuperacion = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
            await _auditoria.RegistrarAsync("ConfigurarMfa", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName, detalles: new { metodo = "Authenticator" });
            await NotificarCambioSeguridadAsync(usuario, "Se activó una app autenticadora para la verificación en dos pasos de tu cuenta.");

            return Ok(new ConfirmarAutenticatorResponse(codigosRecuperacion?.ToList() ?? new List<string>()));
        }

        [HttpPost("autenticator/quitar")]
        public async Task<IActionResult> QuitarAutenticador(DeshabilitarMfaRequest request)
        {
            var usuario = await ObtenerCurrentUserAsync();
            if (usuario is null)
                return Unauthorized();

            var passwordValido = await _signInManager.CheckPasswordSignInAsync(usuario, request.PasswordActual, lockoutOnFailure: true);
            if (passwordValido.IsLockedOut)
                return StatusCode(423, new { mensaje = MensajeBloqueo });
            if (!passwordValido.Succeeded)
            {
                await _auditoria.RegistrarAsync("DesactivarMfa", exitoso: false, idUsuario: usuario.Id, nombreUsuario: usuario.UserName, detalles: new { motivo = "PasswordIncorrecta" });
                return Unauthorized(new { mensaje = "Contraseña incorrecta." });
            }

            if (await _politica.RequiereMfaAsync(usuario.Id))
            {
                // al quitar la app el MFA sigue por correo, sin correo confirmado se rechaza
                if (!usuario.EmailConfirmed || string.IsNullOrWhiteSpace(usuario.Email))
                    return BadRequest(new { mensaje = "El sistema exige verificación en dos pasos y no tienes un correo confirmado como alternativa. Contacta al administrador." });

                await _politica.QuitarAutenticadorAsync(usuario);
                if (!await _userManager.GetTwoFactorEnabledAsync(usuario))
                    await _userManager.SetTwoFactorEnabledAsync(usuario, true);

                await _auditoria.RegistrarAsync("QuitarAutenticador", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName, detalles: new { mfaActivo = true, metodoRestante = "Email" });
                await NotificarCambioSeguridadAsync(usuario, "Se quitó la app autenticadora de tu cuenta. La verificación en dos pasos sigue activa con códigos enviados a tu correo.");
                return NoContent();
            }

            await _userManager.SetTwoFactorEnabledAsync(usuario, false);
            await _politica.QuitarAutenticadorAsync(usuario);

            await _auditoria.RegistrarAsync("DesactivarMfa", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
            await NotificarCambioSeguridadAsync(usuario, "Se desactivó la verificación en dos pasos de tu cuenta.");

            return NoContent();
        }

        [HttpPost("regenerar-codigos-recuperacion")]
        public async Task<ActionResult<ConfirmarAutenticatorResponse>> RegenerarCodigosRecuperacion(DeshabilitarMfaRequest request)
        {
            var usuario = await ObtenerCurrentUserAsync();
            if (usuario is null)
                return Unauthorized();

            if (!await _politica.AutenticadorConfirmadoAsync(usuario))
                return BadRequest(new { mensaje = "Primero debes configurar tu app autenticadora." });

            var passwordValido = await _signInManager.CheckPasswordSignInAsync(usuario, request.PasswordActual, lockoutOnFailure: true);
            if (passwordValido.IsLockedOut)
                return StatusCode(423, new { mensaje = MensajeBloqueo });
            if (!passwordValido.Succeeded)
            {
                await _auditoria.RegistrarAsync("RegenerarCodigosMfa", exitoso: false, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
                return Unauthorized(new { mensaje = "Contraseña incorrecta." });
            }

            var codigosRecuperacion = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(usuario, 10);
            await _auditoria.RegistrarAsync("RegenerarCodigosMfa", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
            await NotificarCambioSeguridadAsync(usuario, "Se generaron nuevos códigos de recuperación; los anteriores dejaron de funcionar.");
            return Ok(new ConfirmarAutenticatorResponse(codigosRecuperacion?.ToList() ?? new List<string>()));
        }

        // avisar al titular de cambios de seguridad que no hizo (ISO 27001 A.5.24 / A.8.16)
        private async Task NotificarCambioSeguridadAsync(ApplicationUser usuario, string mensaje)
        {
            if (string.IsNullOrWhiteSpace(usuario.Email))
                return;
            try
            {
                var cuerpo = $"<p>Hola {WebUtility.HtmlEncode(usuario.Nombre)},</p><p>{WebUtility.HtmlEncode(mensaje)}</p>" +
                             "<p>Si no realizaste este cambio, cambia tu contraseña de inmediato y avisa al administrador.</p>";
                await _emailSender.EnviarAsync(usuario.Email, $"{usuario.Nombre} {usuario.Apellido}", "Cambio de seguridad en tu cuenta", cuerpo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo notificar el cambio de seguridad al usuario {IdUsuario}.", usuario.Id);
            }
        }

        private async Task<ApplicationUser?> ObtenerCurrentUserAsync()
        {
            var idUsuario = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            // un identificador ausente o no numerico se trata como no autenticado (401), nunca como error 500
            return int.TryParse(idUsuario, out _) ? await _userManager.FindByIdAsync(idUsuario!) : null;
        }

    }
}
