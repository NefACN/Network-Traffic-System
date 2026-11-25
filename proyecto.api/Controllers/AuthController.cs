using proyect.application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using proyecto.api.Configuration;
using proyect.infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting(SecurityHardening.PoliticaAutenticacion)]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IApplicationDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AuthController> _logger;
        private readonly PoliticaMfa _politicaMfa;

        private readonly IAuditoriaService _auditoria;
        private readonly IRefreshTokenService _refreshTokenService;

        private const string MensajeSesionInvalida = "Sesión inválida o expirada, vuelve a iniciar sesión.";
        private const string MensajeVerificacionInvalida = "Sesión de verificación inválida o expirada, vuelve a iniciar sesión.";
        private const string MensajeCredencialesInvalidas = "Usuario o contraseña incorrectos.";
        private const string MensajeBloqueo = "Cuenta bloqueada temporalmente por demasiados intentos fallidos.";

        // uso legitimo simultaneo (dos pestanas renovando a la vez) frente a reutilizacion real
        private static readonly TimeSpan GraciaReutilizacionRefresh = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan EsperaReenvioCodigo = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan EsperaReenvioReset = TimeSpan.FromMinutes(2);

        // hash ficticio que iguala el tiempo de respuesta y evita enumerar cuentas 
        private static readonly Lazy<string> HashFicticio = new(() =>
            new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N")));

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IJwtTokenService jwtTokenService,
            IApplicationDbContext db,
            IEmailSender emailSender,
            IConfiguration configuration,
            IAuditoriaService auditoriaService,
            IRefreshTokenService refreshTokenService,
            IMemoryCache cache,
            ILogger<AuthController> logger,
            PoliticaMfa politicaMfa
        )
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtTokenService = jwtTokenService;
            _db = db;
            _emailSender = emailSender;
            _configuration = configuration;
            _auditoria = auditoriaService;
            _refreshTokenService = refreshTokenService;
            _cache = cache;
            _logger = logger;
            _politicaMfa = politicaMfa;
        }

        // valida usuario y contrasena
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
        {
            var identificador = request.NombreUsuario.Trim();

            // si correo y nombre de usuario apuntan a cuentas distintas se rechaza el acceso
            var porNombre = await _userManager.FindByNameAsync(identificador);
            var porCorreo = identificador.Contains('@') ? await _userManager.FindByEmailAsync(identificador) : null;
            var usuario = porNombre is not null && porCorreo is not null && porNombre.Id != porCorreo.Id
                ? null
                : porCorreo ?? porNombre;

            if (usuario is null)
            {
                _userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), HashFicticio.Value, request.Password);
                await _auditoria.RegistrarAsync(
                    "LoginFallido",
                    exitoso: false,
                    nombreUsuario: Recortar(identificador, 256),
                    detalles: new { razon = "UsuarioDesconocido" });

                // un identificador inexistente se bloquea igual que uno real para no revelar cuentas
                var claveDesconocido = $"login-desconocido:{identificador.ToUpperInvariant()}";
                var (estadoDesconocido, bloqueado) = BloqueoDesconocido.Fallo(
                    _cache.Get<BloqueoDesconocido>(claveDesconocido),
                    DateTimeOffset.UtcNow,
                    _userManager.Options.Lockout.MaxFailedAccessAttempts,
                    _userManager.Options.Lockout.DefaultLockoutTimeSpan);
                _cache.Set(claveDesconocido, estadoDesconocido, TimeSpan.FromDays(7));

                return bloqueado
                    ? StatusCode(423, new { mensaje = MensajeBloqueo })
                    : Unauthorized(new { mensaje = MensajeCredencialesInvalidas });
            }

            var resultado = await _signInManager.CheckPasswordSignInAsync(usuario, request.Password, lockoutOnFailure: true);

            if (resultado.IsLockedOut)
            {
                await _auditoria.RegistrarAsync(
                    "LoginFallido",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { razon = "Bloqueado" });
                return StatusCode(423, new { mensaje = MensajeBloqueo });
            }

            if (!resultado.Succeeded)
            {
                await _auditoria.RegistrarAsync(
                    "LoginFallido",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { razon = resultado.IsNotAllowed ? "NoPermitido" : "PasswordIncorrecta" });
                return Unauthorized(new { mensaje = MensajeCredencialesInvalidas });
            }

            if (!usuario.Activo)
            {
                await _auditoria.RegistrarAsync(
                    "LoginFallido",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { razon = "Inactivo" });
                return Unauthorized(new { mensaje = MensajeCredencialesInvalidas });
            }

            if (usuario.DebeCambiarPassword)
            {
                var cambioToken = _jwtTokenService.GenerarTokenCambioPasswordObligatorio(usuario);
                await _auditoria.RegistrarAsync(
                    "LoginCambioPasswordRequerido",
                    exitoso: true,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName);

                return Ok(new LoginResponse(
                    RequiereCambioPassword: true,
                    CambioPasswordToken: cambioToken,
                    RequiereMfa: false,
                    Token: null,
                    Refreshtoken: null,
                    MfaToken: null,
                    MetodosDisponibles: null));
            }

            // el evento Login se registra solo cuando la sesion se emite realmente
            return await ContinuarLoginTrasPasswordAsync(usuario);
        }

        [HttpPost("cambiar-password-obligatorio")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponse>> CambiarPasswordObligatorio(CambiarPasswordObligatorioRequest request)
        {
            var tokenValido = _jwtTokenService.ValidarTokenCambioPasswordObligatorio(request.CambioPasswordToken);
            if (tokenValido is null)
                return Unauthorized(new { mensaje = MensajeSesionInvalida });

            var usuario = await _userManager.FindByIdAsync(tokenValido.IdUsuario.ToString());

            // el token se invalida tras el primer cambio y solo sirve si el cambio es obligatorio
            if (usuario is null || !usuario.Activo || !usuario.DebeCambiarPassword || !tokenValido.CorrespondeA(usuario))
                return Unauthorized(new { mensaje = MensajeSesionInvalida });

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            if (await _userManager.CheckPasswordAsync(usuario, request.PasswordNueva))
                return BadRequest(new { mensaje = "La nueva contraseña debe ser diferente a la contraseña temporal." });

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            var resultado = await _userManager.ResetPasswordAsync(usuario, resetToken, request.PasswordNueva);

            if (!resultado.Succeeded)
                return BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });

            usuario.DebeCambiarPassword = false;
            usuario.FechaModificacion = DateTime.UtcNow;
            await _userManager.UpdateAsync(usuario);

            await _auditoria.RegistrarAsync(
                "CambioPassword",
                exitoso: true,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { motivo = "Obligatorio" });
            await RevocarTokenDeUsuarioAsync(usuario.Id);

            return await ContinuarLoginTrasPasswordAsync(usuario);
        }

        private async Task<ActionResult<LoginResponse>> ContinuarLoginTrasPasswordAsync(ApplicationUser usuario)
        {
            // ningun rol entra sin segundo factor , se activa el codigo por correo si falta
            var mfaPrevio = await _userManager.GetTwoFactorEnabledAsync(usuario);
            if (!await _politicaMfa.AplicarAsync(usuario))
            {
                await _auditoria.RegistrarAsync(
                    "LoginFallido",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { razon = "MfaObligatorioSinFactor" });
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { mensaje = "El sistema exige verificación en dos pasos, pero tu cuenta no tiene un correo confirmado. Contacta al administrador." });
            }

            if (!mfaPrevio && await _userManager.GetTwoFactorEnabledAsync(usuario))
                await _auditoria.RegistrarAsync(
                    "MfaActivadoPorPolitica",
                    exitoso: true,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { metodo = "Email" });

            if (await _userManager.GetTwoFactorEnabledAsync(usuario))
            {
                var metodos = await _politicaMfa.MetodosValidosAsync(usuario);
                var mfaToken = _jwtTokenService.GenerarTokenMfaPending(usuario);
                await _auditoria.RegistrarAsync(
                    "LoginMfaPendiente",
                    exitoso: true,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName);

                return Ok(new LoginResponse(
                    RequiereCambioPassword: false,
                    CambioPasswordToken: null,
                    RequiereMfa: true,
                    Token: null,
                    Refreshtoken: null,
                    MfaToken: mfaToken,
                    MetodosDisponibles: metodos.ToList()));
            }

            var (refreshTokenPlano, token) = await EmitirSesionAsync(usuario);
            await _auditoria.RegistrarAsync("Login", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);

            return Ok(new LoginResponse(
                RequiereCambioPassword: false,
                CambioPasswordToken: null,
                RequiereMfa: false,
                Token: token,
                Refreshtoken: refreshTokenPlano,
                MfaToken: null,
                MetodosDisponibles: null));
        }

        [HttpPost("enviar-codigo-mfa")]
        [AllowAnonymous]
        [EnableRateLimiting(SecurityHardening.PoliticaEnvioCodigos)]
        public async Task<IActionResult> EnviarCodigoMfa(EnviarCodigoMfaRequest request)
        {
            var usuario = await ObtenerUsuarioMfaPendienteAsync(request.MfaToken);
            if (usuario is null)
                return Unauthorized(new { mensaje = MensajeVerificacionInvalida });

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            if (request.Metodo == TokenOptions.DefaultAuthenticatorProvider)
                return BadRequest(new { mensaje = "El método autenticador no requiere envío de código." });

            if (!await MetodoPermitidoAsync(usuario, request.Metodo))
                return BadRequest(new { mensaje = "Método no reconocido." });

            // evita abuso del envio de correos (costos y acoso) aunque se cambie de IP
            var claveEspera = $"mfa-envio:{usuario.Id}";
            if (_cache.TryGetValue(claveEspera, out _))
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new { mensaje = "Espera unos segundos antes de solicitar otro código." });

            _cache.Set(claveEspera, true, EsperaReenvioCodigo);

            // la espera de reenvio solo aplica si el codigo salio
            var enviado = false;
            try
            {
                var codigo = await _userManager.GenerateTwoFactorTokenAsync(usuario, request.Metodo);

                if (request.Metodo == TokenOptions.DefaultEmailProvider)
                {
                    if (string.IsNullOrWhiteSpace(usuario.Email))
                        return BadRequest(new { mensaje = "El usuario no tiene un correo registrado." });

                    var cuerpoHtml = $"<p>Hola {WebUtility.HtmlEncode(usuario.Nombre)},</p>" +
                        $"<p>Tu código de verificación es:</p><h2>{WebUtility.HtmlEncode(codigo)}</h2>" +
                        "<p>Si no fuiste tú quien intentó iniciar sesión, cambia tu contraseña y avisa al administrador.</p>";

                    await _emailSender.EnviarAsync(
                        usuario.Email,
                        $"{usuario.Nombre} {usuario.Apellido}",
                        "Tu código de verificación",
                        cuerpoHtml);
                }
                else
                {
                    return BadRequest(new { mensaje = "Método no reconocido." });
                }

                enviado = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo enviar el código MFA por {Metodo} al usuario {IdUsuario}.",
                    request.Metodo,
                    usuario.Id);
                await _auditoria.RegistrarAsync(
                    "EnvioCodigoMfa",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { metodo = request.Metodo });

                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { mensaje = "No se pudo enviar el código en este momento. Intenta de nuevo o elige otro método de verificación." });
            }
            finally
            {
                if (!enviado)
                    _cache.Remove(claveEspera);
            }

            await _auditoria.RegistrarAsync(
                "EnvioCodigoMfa",
                exitoso: true,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { metodo = request.Metodo });

            return Ok(new { mensaje = $"Código enviado por {request.Metodo}." });
        }

        [HttpPost("verify-mfa")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponse>> VerifyMfa(VerifyMfaRequest request)
        {
            var usuario = await ObtenerUsuarioMfaPendienteAsync(request.MfaToken);
            if (usuario is null)
                return Unauthorized(new { mensaje = MensajeVerificacionInvalida });

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            // solo se aceptan los proveedores habilitados para este usuario
            var esValido = await MetodoPermitidoAsync(usuario, request.Metodo)
                && await _userManager.VerifyTwoFactorTokenAsync(usuario, request.Metodo, request.Codigo);
            if (!esValido)
            {
                await _userManager.AccessFailedAsync(usuario);
                await _auditoria.RegistrarAsync(
                    "VerificacionMfa",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { metodo = Recortar(request.Metodo, 40) });
                return Unauthorized(new { mensaje = "Código incorrecto o expirado." });
            }

            await _userManager.ResetAccessFailedCountAsync(usuario);
            var (refreshTokenPlano, token) = await EmitirSesionAsync(usuario);

            await _auditoria.RegistrarAsync(
                "VerificacionMfa",
                exitoso: true,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { metodo = request.Metodo });
            await _auditoria.RegistrarAsync("Login", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);

            return Ok(new TokenResponse(token, refreshTokenPlano));
        }

        [HttpPost("verify-mfa-recovery")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponse>> VerifyMfaRecoveryCode(VerifyMfaRecoveryRequest request)
        {
            var usuario = await ObtenerUsuarioMfaPendienteAsync(request.MfaToken);
            if (usuario is null)
                return Unauthorized(new { mensaje = MensajeVerificacionInvalida });

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            var resultado = await _userManager.RedeemTwoFactorRecoveryCodeAsync(usuario, request.CodigoRecuperacion.Trim());
            if (!resultado.Succeeded)
            {
                await _userManager.AccessFailedAsync(usuario);
                await _auditoria.RegistrarAsync(
                    "VerificacionMfa",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { metodo = "RecoveryCode" });
                return Unauthorized(new { mensaje = "Código de recuperación inválido." });
            }

            await _userManager.ResetAccessFailedCountAsync(usuario);
            var (refreshTokenPlano, token) = await EmitirSesionAsync(usuario);

            await _auditoria.RegistrarAsync(
                "VerificacionMfa",
                exitoso: true,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { metodo = "RecoveryCode", restantes = await _userManager.CountRecoveryCodesAsync(usuario) });
            await _auditoria.RegistrarAsync("Login", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);

            return Ok(new TokenResponse(token, refreshTokenPlano));
        }

        // restablecimiento de contrasena
        [HttpPost("olvide-password")]
        [AllowAnonymous]
        [EnableRateLimiting(SecurityHardening.PoliticaEnvioCodigos)]
        public async Task<IActionResult> OlvidePassword([FromBody] OlvidePasswordRequest request)
        {
            const string respuestaGenerica = "Si el correo existe en nuestro sistema, te enviamos un enlace para restablecer tu contraseña";
            var usuario = await _userManager.FindByEmailAsync(request.Email.Trim());

            // misma respuesta exista o no la cuenta, con limite de reenvio por cuenta
            if (usuario is { Activo: true, EmailConfirmed: true } && !_cache.TryGetValue($"reset:{usuario.Id}", out _))
            {
                _cache.Set($"reset:{usuario.Id}", true, EsperaReenvioReset);
                var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
                var frontendBaseUrl = (_configuration["Frontend:BaseUrl"] ?? "https://localhost:44320").TrimEnd('/');
                var emailCodificado = WebUtility.UrlEncode(usuario.Email);
                var tokenCodificado = WebUtility.UrlEncode(token);
                var linkReset = $"{frontendBaseUrl}/recuperar-password.html?email={emailCodificado}&token={tokenCodificado}";

                var cuerpoHtml = $"<p>Hola {WebUtility.HtmlEncode(usuario.Nombre)},</p>" +
                    "<p>Recibimos una solicitud para restablecer tu contraseña. " +
                    "Si fuiste tú, haz clic en el siguiente enlace (válido por tiempo limitado):</p>" +
                    $"<p><a href=\"{WebUtility.HtmlEncode(linkReset)}\">Restablecer mi contraseña</a></p>" +
                    "<p>Si no solicitaste esto, puedes ignorar este mensaje; tu contraseña actual seguirá funcionando.</p>";

                try
                {
                    await _emailSender.EnviarAsync(
                        usuario.Email!,
                        $"{usuario.Nombre} {usuario.Apellido}",
                        "Restablece tu contraseña",
                        cuerpoHtml);
                    await _auditoria.RegistrarAsync(
                        "SolicitudResetPassword",
                        exitoso: true,
                        idUsuario: usuario.Id,
                        nombreUsuario: usuario.UserName);
                }
                catch (Exception ex)
                {
                    // un error del proveedor no debe revelar que la cuenta existe
                    _logger.LogWarning(ex, "No se pudo enviar el enlace de restablecimiento al usuario {IdUsuario}.", usuario.Id);
                    await _auditoria.RegistrarAsync(
                        "SolicitudResetPassword",
                        exitoso: false,
                        idUsuario: usuario.Id,
                        nombreUsuario: usuario.UserName);
                }
            }

            return Ok(new { mensaje = respuestaGenerica });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordTokenRequest request)
        {
            var usuario = await _userManager.FindByEmailAsync(request.Email);

            const string mensajeGenerico = "El enlace de restablecimiento no es válido o ha expirado.";

            if (usuario is null || !usuario.Activo)
                return BadRequest(new { mensaje = mensajeGenerico });

            var resultado = await _userManager.ResetPasswordAsync(usuario, request.Token, request.PasswordNueva);
            if (!resultado.Succeeded)
            {
                // distingue el token invalido de una contrasena que no cumple la politica
                var tokenInvalido = resultado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken));
                await _auditoria.RegistrarAsync(
                    "CambioPassword",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { motivo = "Restablecimiento", tokenInvalido });

                return tokenInvalido
                    ? BadRequest(new { mensaje = mensajeGenerico })
                    : BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });
            }

            usuario.DebeCambiarPassword = false;
            usuario.FechaModificacion = DateTime.UtcNow;
            await _userManager.UpdateAsync(usuario);

            // quien demostro el control del correo recupera el acceso sin esperar el bloqueo
            await _userManager.ResetAccessFailedCountAsync(usuario);
            await _userManager.SetLockoutEndDateAsync(usuario, null);

            await _auditoria.RegistrarAsync(
                "CambioPassword",
                exitoso: true,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { motivo = "Restablecimiento" });
            await RevocarTokenDeUsuarioAsync(usuario.Id);

            return Ok(new { mensaje = "Contraseña restablecida correctamente." });
        }

        [HttpPost("cambiar-password")]
        [Authorize]
        public async Task<IActionResult> CambiarPassword(CambiarPasswordRequest request)
        {
            var idUsuario = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (idUsuario is null)
                return Unauthorized();

            var usuario = await _userManager.FindByIdAsync(idUsuario);
            if (usuario is null || !usuario.Activo)
                return Unauthorized();

            if (await _userManager.IsLockedOutAsync(usuario))
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            var passwordValido = await _signInManager.CheckPasswordSignInAsync(usuario, request.PasswordActual, lockoutOnFailure: true);
            if (passwordValido.IsLockedOut)
                return StatusCode(423, new { mensaje = MensajeBloqueo });

            if (!passwordValido.Succeeded)
            {
                await _auditoria.RegistrarAsync(
                    "CambioPassword",
                    exitoso: false,
                    idUsuario: usuario.Id,
                    nombreUsuario: usuario.UserName,
                    detalles: new { motivo = "PasswordActualIncorrecta" });
                return Unauthorized(new { mensaje = "Contraseña actual incorrecta." });
            }

            if (request.PasswordActual == request.PasswordNueva)
                return BadRequest(new { mensaje = "La nueva contraseña debe ser diferente a la actual." });

            var resultado = await _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.PasswordNueva);
            if (!resultado.Succeeded)
                return BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });

            usuario.DebeCambiarPassword = false;
            usuario.FechaModificacion = DateTime.UtcNow;
            await _userManager.UpdateAsync(usuario);

            await _auditoria.RegistrarAsync("CambioPassword", exitoso: true, idUsuario: usuario.Id, nombreUsuario: usuario.UserName);
            await RevocarTokenDeUsuarioAsync(usuario.Id);

            return NoContent();
        }

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        [EnableRateLimiting(SecurityHardening.PoliticaSesion)]
        public async Task<ActionResult<TokenResponse>> RefreshToken(RefreshTokenRequest request)
        {
            const string mensajeInvalido = "Refresh token inválido o expirado.";
            var hash = _refreshTokenService.Hash(request.RefreshToken);
            var guardado = await _db.RefreshTokens.Include(t => t.Usuario)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash);

            if (guardado is null)
                return Unauthorized(new { mensaje = mensajeInvalido });

            var ahora = DateTime.UtcNow;
            if (guardado.FechaRevocacion is not null)
            {
                // un refresh token rotado que se reutiliza indica robo, se revoca toda la sesion 
                if (guardado.ReemplazadoPorTokenHash is not null
                    && ahora - guardado.FechaRevocacion.Value > GraciaReutilizacionRefresh)
                    await ResponderReutilizacionAsync(guardado.Usuario);

                return Unauthorized(new { mensaje = mensajeInvalido });
            }

            if (!guardado.EstaActivo || !guardado.Usuario.Activo || await _userManager.IsLockedOutAsync(guardado.Usuario))
                return Unauthorized(new { mensaje = mensajeInvalido });

            // una sesion sin segundo factor no se renueva, obliga a pasar por el login con MFA
            if (!guardado.Usuario.TwoFactorEnabled && await _politicaMfa.RequiereMfaAsync(guardado.IdUsuario))
                return Unauthorized(new { mensaje = "El sistema exige verificación en dos pasos. Vuelve a iniciar sesión." });

            var (nuevoPlano, nuevaEntidad) = _refreshTokenService.Generar(
                guardado.IdUsuario,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // rotacion atomica: si dos peticiones usan el mismo token, solo una lo consume
            var consumido = await _db.RefreshTokens
                .Where(t => t.Id_RefreshToken == guardado.Id_RefreshToken && t.FechaRevocacion == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaRevocacion, ahora)
                    .SetProperty(t => t.ReemplazadoPorTokenHash, nuevaEntidad.TokenHash));
            if (consumido == 0)
                return Unauthorized(new { mensaje = mensajeInvalido });

            _db.RefreshTokens.Add(nuevaEntidad);
            await _db.SaveChangesAsync();

            var roles = await ObtenerRolesAsync(guardado.IdUsuario);
            var jwt = _jwtTokenService.GenerarToken(guardado.Usuario, roles);

            return Ok(new TokenResponse(jwt, nuevoPlano));
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        [EnableRateLimiting(SecurityHardening.PoliticaSesion)]
        public async Task<IActionResult> Logout(RefreshTokenRequest request)
        {
            var hash = _refreshTokenService.Hash(request.RefreshToken);
            var guardado = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (guardado is not null && guardado.FechaRevocacion is null)
            {
                guardado.FechaRevocacion = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _auditoria.RegistrarAsync("Logout", exitoso: true, idUsuario: guardado.IdUsuario);
            }

            return NoContent(); // idempotente, no revela si el token existia
        }

        private async Task<ApplicationUser?> ObtenerUsuarioMfaPendienteAsync(string mfaToken)
        {
            var tokenValido = _jwtTokenService.ValidarTokenMfaPending(mfaToken);
            if (tokenValido is null)
                return null;

            var usuario = await _userManager.FindByIdAsync(tokenValido.IdUsuario.ToString());
            return usuario is { Activo: true } && tokenValido.CorrespondeA(usuario) ? usuario : null;
        }

        private async Task<bool> MetodoPermitidoAsync(ApplicationUser usuario, string metodo) =>
            (await _politicaMfa.MetodosValidosAsync(usuario)).Contains(metodo, StringComparer.Ordinal);

        private async Task<(string RefreshToken, string AccessToken)> EmitirSesionAsync(ApplicationUser usuario)
        {
            var (refreshTokenPlano, refreshTokenEntidad) = _refreshTokenService.Generar(
                usuario.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());
            _db.RefreshTokens.Add(refreshTokenEntidad);
            await _db.SaveChangesAsync();

            var roles = await ObtenerRolesAsync(usuario.Id);
            return (refreshTokenPlano, _jwtTokenService.GenerarToken(usuario, roles));
        }

        private async Task ResponderReutilizacionAsync(ApplicationUser usuarioSinSeguimiento)
        {
            var usuario = await _userManager.FindByIdAsync(usuarioSinSeguimiento.Id.ToString());
            if (usuario is null)
                return;

            await RevocarTokenDeUsuarioAsync(usuario.Id);
            await _userManager.UpdateSecurityStampAsync(usuario); // invalida tambien los JWT vigentes
            await _auditoria.RegistrarAsync(
                "RefreshTokenReutilizado",
                exitoso: false,
                idUsuario: usuario.Id,
                nombreUsuario: usuario.UserName,
                detalles: new { accion = "Sesiones revocadas" });
            _logger.LogWarning(
                "Reutilización de refresh token detectada para el usuario {IdUsuario}; se revocaron sus sesiones.",
                usuario.Id);
        }

        private async Task<List<string>> ObtenerRolesAsync(int idUsuario) =>
            [await _db.RolUsuario.ActorEfectivoAsync(idUsuario, DateTime.UtcNow)];

        private async Task RevocarTokenDeUsuarioAsync(int idUsuario)
        {
            var ahora = DateTime.UtcNow;
            await _db.RefreshTokens
                .Where(t => t.IdUsuario == idUsuario && t.FechaRevocacion == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaRevocacion, ahora));
        }

        private static string Recortar(string? valor, int maximo) =>
            string.IsNullOrEmpty(valor) ? string.Empty : valor.Length <= maximo ? valor : valor[..maximo];
    }
}
