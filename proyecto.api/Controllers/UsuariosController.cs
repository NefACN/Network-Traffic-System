using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using proyect.application.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using proyect.domain.Constants;
using proyect.application;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public class UsuariosController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IEmailSender _emailSender;
    private readonly IAuditoriaService _auditoria;
    private readonly ILogger<UsuariosController> _logger;

    private const string ProveedorInternoIdentity = "[AspNetUserStore]";
    private const string NombreTokenCodigosRecuperacion = "RecoveryCodes";

    public UsuariosController(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext db,
        IConfiguration configuration,
        IEmailSender emailSender,
        IAuditoriaService auditoria,
        ILogger<UsuariosController> logger)
    {
        _userManager = userManager;
        _db = db;
        _configuration = configuration;
        _emailSender = emailSender;
        _auditoria = auditoria;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ResultadoPaginado<UsuarioListItemDto>>> ListarUsuarios(
        [FromQuery] bool? activo,
        [FromQuery] string? busqueda,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanio,
        CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var query = _db.Users
            .Include(u => u.Sucursal)
            .Include(u => u.AreaOperativa)
            .AsQueryable();

        if (activo.HasValue)
        {
            query = query.Where(u => u.Activo == activo.Value);
        }

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var texto = busqueda.Trim().ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(texto)) ||
                (u.Email != null && u.Email.ToLower().Contains(texto)) ||
                u.Nombre.ToLower().Contains(texto) ||
                u.Apellido.ToLower().Contains(texto) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(texto))
            );
        }

        var paginaUsuarios = await query.AsNoTracking()
            .OrderBy(u => u.Nombre)
            .ThenBy(u => u.Apellido)
            .ThenBy(u => u.Id)
            .PaginarAsync(pagina, tamanio, ct);
        var usuarios = paginaUsuarios.Items;

        var idsUsuarios = usuarios.Select(u => u.Id).ToList();
        var rolesPorUsuario = await _db.RolUsuario
            .Where(ru => idsUsuarios.Contains(ru.IdUsuario)
                && ru.Activo
                && ru.FechaInicio <= ahora
                && (ru.FechaFin == null || ru.FechaFin > ahora))
            .Join(_db.Roles.Where(r => r.Activo), ru => ru.IdRol, r => r.IdRol, (ru, r) => new { ru.IdUsuario, r.Nombre })
            .GroupBy(x => x.IdUsuario)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Nombre).ToList());

        var idsConMfaConfigurado = (await _db.Set<IdentityUserToken<int>>()
            .Where(t => idsUsuarios.Contains(t.UserId)
                && t.LoginProvider == ProveedorInternoIdentity
                && t.Name == NombreTokenCodigosRecuperacion
                && t.Value != null && t.Value != "")
            .Select(t => t.UserId)
            .ToListAsync())
            .ToHashSet();

        var resultado = usuarios.Select(user => new UsuarioListItemDto(
                user.Id,
                user.UserName!,
                user.Email!,
                user.Nombre!,
                user.Apellido!,
                user.PhoneNumber,
                user.Id_Sucursal,
                user.Sucursal?.Nombre,
                user.Id_Area,
                user.AreaOperativa?.Nombre,
                user.ScoreRiesgo,
                user.Activo,
                user.DebeCambiarPassword,
                idsConMfaConfigurado.Contains(user.Id),
                new List<string> {
                    RolesSistemas.ActorEfectivo(rolesPorUsuario.TryGetValue(user.Id, out var roles) ? roles : new List<string>())
                }
            )).ToList();

        return Ok(new ResultadoPaginado<UsuarioListItemDto>(
            resultado,
            paginaUsuarios.Total,
            paginaUsuarios.Pagina,
            paginaUsuarios.Tamanio));
    }

    // usuarios activos sin paginar para los selectores (sucursales, exclusiones)
    [HttpGet("catalogo")]
    public async Task<IActionResult> ListarUsuariosCatalogo()
    {
        var usuarios = await _db.Users
            .Where(u => u.Activo)
            .Select(u => new {
                idUsuario = u.Id,
                nombre = u.Nombre,
                apellido = u.Apellido,
                userName = u.UserName,
                email = u.Email
            })
            .OrderBy(u => u.nombre)
            .ThenBy(u => u.apellido)
            .ThenBy(u => u.idUsuario)
            .ToListAsync();

        return Ok(usuarios);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UsuarioDetailDto>> ObtenerDetalleUsuario(int id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var roles = await _db.RolUsuario
            .Where(ru => ru.IdUsuario == user.Id)
            .OrderByDescending(ru => ru.FechaInicio)
            .Join(
                _db.Roles,
                ru => ru.IdRol,
                r => r.IdRol,
                (ru, r) => new RolAsignadoDto(r.IdRol, r.Nombre, ru.FechaInicio, ru.FechaFin, ru.Activo))
            .ToListAsync();

        var mfaConfigurado = await _userManager.CountRecoveryCodesAsync(user) > 0;

        return Ok(new UsuarioDetailDto(
            user.Id,
            user.UserName!,
            user.Email!,
            user.Nombre!,
            user.Apellido!,
            user.PhoneNumber,
            user.Id_Sucursal,
            user.Id_Area,
            user.Activo,
            user.DebeCambiarPassword,
            mfaConfigurado,
            user.FechaCreacion,
            roles
        ));
    }

    [HttpPost]
    public async Task<ActionResult<CrearUsuarioResponse>> CrearUsuario(CrearUsuarioRequest request)
    {
        if (request.IdSucursal.HasValue
            && !await _db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal && s.Activo))
            return BadRequest(new { mensaje = "La sucursal indicada no existe o está inactiva." });

        if (request.IdArea.HasValue
            && !await _db.AreasOperativas.AnyAsync(a => a.Id_Area == request.IdArea && a.Activo))
            return BadRequest(new { mensaje = "El área indicada no existe o está inactiva." });

        if (request.IdRolInicial.HasValue
            && !await _db.Roles.AnyAsync(r => r.IdRol == request.IdRolInicial && r.Activo))
            return BadRequest(new { mensaje = "El rol indicado no existe o está inactivo." });

        var rolInicial = await _db.Roles.FirstOrDefaultAsync(r => request.IdRolInicial.HasValue
            ? r.IdRol == request.IdRolInicial.Value
            : r.Nombre == RolesSistemas.RolColaborador);
        if (rolInicial is null || !rolInicial.Activo || !RolesSistemas.EsActor(rolInicial.Nombre))
            return BadRequest(new { mensaje = "Selecciona uno de los tres actores del sistema. Aplica la migración de roles si falta el catálogo." });

        if (rolInicial.Nombre == RolesSistemas.RolGerente && (!request.IdSucursal.HasValue ||
            !await _db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal && s.Activo)))
            return BadRequest(new { mensaje = "El gerente debe tener una sucursal activa asignada." });

        var nombreUsuarioNormalizado = _userManager.NormalizeName(request.NombreUsuario);
        var emailNormalizado = _userManager.NormalizeEmail(request.Email);

        var usuarioEnUso = await _db.Users.AnyAsync(u => u.NormalizedUserName == nombreUsuarioNormalizado);
        if (usuarioEnUso)
            return Conflict(new { mensaje = "Ya existe un usuario con ese nombre de usuario." });

        var emailEnUso = await _db.Users.AnyAsync(u => u.NormalizedEmail == emailNormalizado);
        if (emailEnUso)
            return Conflict(new { mensaje = "Ya existe un usuario registrado con ese correo." });

        var passwordTemporal = GeneradorPasswords.Generar();

        var nuevoUsuario = new ApplicationUser {
            UserName = request.NombreUsuario,
            Email = request.Email,
            Nombre = request.Nombre,
            Apellido = request.Apellido,
            PhoneNumber = request.Telefono,
            Id_Sucursal = request.IdSucursal,
            Id_Area = request.IdArea,
            // el correo queda verificado porque recibe la contrasena temporal
            EmailConfirmed = true,
            PhoneNumberConfirmed = false,
            DebeCambiarPassword = true,
            TwoFactorEnabled = true,
            Activo = true,
            FechaCreacion = DateTime.UtcNow,
        };

        var resultado = await _userManager.CreateAsync(nuevoUsuario, passwordTemporal);
        if (!resultado.Succeeded)
            return BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });

        try
        {
            var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "https://localhost:44320";
            var cuerpoHtml = $"<p>Hola {WebUtility.HtmlEncode(nuevoUsuario.Nombre)} {WebUtility.HtmlEncode(nuevoUsuario.Apellido)},</p>" +
                $"<p>Se ha creado una cuenta para usted en el sistema DISMAC. Su nombre de usuario es: <strong>{WebUtility.HtmlEncode(nuevoUsuario.UserName)}</strong></p>" +
                $"<p>Su contraseña temporal es: <strong style='color:#D96A62; font-size:16px;'>{WebUtility.HtmlEncode(passwordTemporal)}</strong></p>" +
                $"<p>Por favor, inicie sesión con su contraseña temporal. El sistema le pedirá crear una nueva contraseña segura inmediatamente.</p>" +
                $"<p><a href='{WebUtility.HtmlEncode(frontendBaseUrl)}/index.html'>Ir al portal de inicio de sesión</a></p>";

            await _emailSender.EnviarAsync(
                nuevoUsuario.Email!,
                $"{nuevoUsuario.Nombre} {nuevoUsuario.Apellido}",
                "Bienvenido al sistema DISMAC - Credenciales",
                cuerpoHtml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo entregar el correo de bienvenida al usuario {IdUsuario}.", nuevoUsuario.Id);
        }

        if (rolInicial is not null)
        {
            _db.RolUsuario.Add(new RolUsuario {
                IdUsuario = nuevoUsuario.Id,
                IdRol = rolInicial.IdRol,
                AsignadoPor = ObtenerIdAdminActual(),
                Activo = true,
                FechaInicio = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }

        await _auditoria.RegistrarAsync(
            "CrearUsuario",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: nuevoUsuario.Id.ToString());

        return Ok(new CrearUsuarioResponse(nuevoUsuario.Id, passwordTemporal));
    }

    // avisa al correo anterior: si el cambio no fue legitimo, la persona puede reportarlo
    private async Task AvisarCambioCorreoAsync(ApplicationUser usuario, string correoAnterior)
    {
        try
        {
            var cuerpoHtml = $"<p>Hola {WebUtility.HtmlEncode(usuario.Nombre)},</p>" +
                "<p>El correo de tu cuenta DISMAC fue cambiado por un administrador. Desde ahora los códigos de verificación y la recuperación de contraseña llegarán a la nueva dirección.</p>" +
                "<p>Si no reconoces este cambio, avisa de inmediato al equipo de seguridad.</p>";
            await _emailSender.EnviarAsync(
                correoAnterior,
                $"{usuario.Nombre} {usuario.Apellido}",
                "Se cambió el correo de tu cuenta",
                cuerpoHtml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo avisar del cambio de correo al usuario {IdUsuario}.", usuario.Id);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, ActualizarUsuarioRequest request)
    {
        var usuario = await _userManager.FindByIdAsync(id.ToString());
        if (usuario is null)
            return NotFound();

        var ahora = DateTime.UtcNow;
        if (await _db.RolUsuario.AnyAsync(r => r.IdUsuario == id && r.Activo && r.Rol.Activo &&
            r.Rol.Nombre == RolesSistemas.RolGerente && (r.FechaFin == null || r.FechaFin > ahora)) &&
            (!request.IdSucursal.HasValue
                || !await _db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal && s.Activo)))
            return BadRequest(new { mensaje = "El gerente debe conservar una sucursal activa asignada." });

        var cambioAlcance = usuario.Id_Sucursal != request.IdSucursal || usuario.Id_Area != request.IdArea;
        var correoAnterior = usuario.Email;
        var cambioCorreo = !string.Equals(usuario.Email, request.Email, StringComparison.OrdinalIgnoreCase);

        if (request.IdSucursal.HasValue && cambioAlcance &&
            !await _db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal && s.Activo))
            return BadRequest(new { mensaje = "La sucursal indicada no existe o está inactiva." });

        if (request.IdArea.HasValue && cambioAlcance &&
            !await _db.AreasOperativas.AnyAsync(a => a.Id_Area == request.IdArea && a.Activo))
            return BadRequest(new { mensaje = "El área indicada no existe o está inactiva." });

        if (cambioCorreo)
        {
            var emailNormalizado = request.Email.Trim().ToUpperInvariant();
            var emailEnUso = await _db.Users.AnyAsync(u => u.Id != id && u.NormalizedEmail == emailNormalizado);
            if (emailEnUso)
                return Conflict(new { mensaje = "Ese correo ya está en uso por otro usuario." });

            var resultadoEmail = await _userManager.SetEmailAsync(usuario, request.Email);
            if (!resultadoEmail.Succeeded)
                return BadRequest(new { errores = resultadoEmail.Errors.Select(e => e.Description) });

            usuario.EmailConfirmed = true;
        }

        usuario.Nombre = request.Nombre;
        usuario.Apellido = request.Apellido;
        if (!string.Equals(usuario.PhoneNumber, request.Telefono, StringComparison.Ordinal))
        {
            usuario.PhoneNumber = request.Telefono;
            usuario.PhoneNumberConfirmed = false;
        }

        usuario.Id_Sucursal = request.IdSucursal;
        usuario.Id_Area = request.IdArea;
        usuario.FechaModificacion = DateTime.UtcNow;

        var resultado = await _userManager.UpdateAsync(usuario);
        if (!resultado.Succeeded)
            return BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });

        // cambiar el correo cierra las sesiones, es factor de verificacion y de recuperacion
        if (cambioAlcance || cambioCorreo)
        {
            await _userManager.UpdateSecurityStampAsync(usuario);
            await RevocarTokenDeUsuarioAsync(id);
        }

        if (cambioCorreo && !string.IsNullOrWhiteSpace(correoAnterior))
            await AvisarCambioCorreoAsync(usuario, correoAnterior);

        await _auditoria.RegistrarAsync(
            "ActualizarUsuario",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: id.ToString(),
            detalles: request);

        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, CambiarEstadoUsuarioRequest request)
    {
        var usuario = await _userManager.FindByIdAsync(id.ToString());
        if (usuario is null)
            return NotFound();

        if (!request.Activo)
        {
            if (ObtenerIdAdminActual() == id)
                return BadRequest(new { mensaje = "No puedes desactivar tu propia cuenta." });

            if (await EsUsuarioAdminActivoAsync(id) && await ContarAdminsActivosAsync(excluirIdUsuario: id) == 0)
                return BadRequest(new { mensaje = "No puedes desactivar al único administrador activo del sistema." });
        }

        usuario.Activo = request.Activo;
        usuario.FechaModificacion = DateTime.UtcNow;
        await _userManager.UpdateAsync(usuario);
        await _userManager.UpdateSecurityStampAsync(usuario);

        await _auditoria.RegistrarAsync(
            "CambiarEstado",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: id.ToString(),
            detalles: new { Activo = request.Activo });

        if (!request.Activo)
        {
            await RevocarTokenDeUsuarioAsync(id);
        }

        return NoContent();
    }

    [HttpPost("{id:int}/resetear-password")]
    public async Task<ActionResult<ResetearPasswordResponse>> ResetearPassword(int id)
    {
        // sobre la propia cuenta se exige cambiar contrasena para no saltarse la actual
        if (ObtenerIdAdminActual() == id)
            return BadRequest(new { mensaje = "Para tu propia cuenta usa la opción «Cambiar contraseña»." });

        var usuario = await _userManager.FindByIdAsync(id.ToString());
        if (usuario is null)
            return NotFound();

        var passwordTemporal = GeneradorPasswords.Generar();
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(usuario);
        var resultado = await _userManager.ResetPasswordAsync(usuario, resetToken, passwordTemporal);

        if (!resultado.Succeeded)
            return BadRequest(new { errores = resultado.Errors.Select(e => e.Description) });

        usuario.DebeCambiarPassword = true;
        usuario.FechaModificacion = DateTime.UtcNow;
        await _userManager.UpdateAsync(usuario);

        try
        {
            var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "https://localhost:44320";
            var cuerpoHtml = $"<p>Hola {WebUtility.HtmlEncode(usuario.Nombre)},</p>" +
                $"<p>Un administrador de DISMAC ha restablecido su contraseña de acceso.</p>" +
                $"<p>Su nueva contraseña temporal es: <strong style='color:#D96A62; font-size:16px;'>{WebUtility.HtmlEncode(passwordTemporal)}</strong></p>" +
                $"<p>Por seguridad, deberá crear una nueva contraseña definitiva la próxima vez que inicie sesión.</p>" +
                $"<p>Si no solicitó este cambio, comuníquese con el área de seguridad.</p>" +
                $"<p><a href='{WebUtility.HtmlEncode(frontendBaseUrl)}/index.html'>Ir al portal de inicio de sesión</a></p>";

            await _emailSender.EnviarAsync(
                usuario.Email!,
                $"{usuario.Nombre} {usuario.Apellido}",
                "Restablecimiento de contraseña",
                cuerpoHtml);
        }
        catch
        {
            _logger.LogWarning("No se pudo entregar el correo de restablecimiento al usuario {IdUsuario}.", usuario.Id);
        }

        await _auditoria.RegistrarAsync(
            "ResetearPassword",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: id.ToString());
        await RevocarTokenDeUsuarioAsync(id);

        return Ok(new ResetearPasswordResponse(passwordTemporal));
    }

    [HttpPost("{id:int}/roles")]
    public async Task<IActionResult> AsignarRol(int id, AsignarRolRequest request)
    {
        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (usuario is null)
            return NotFound();

        var rol = await _db.Roles.FirstOrDefaultAsync(r => r.IdRol == request.IdRol);
        if (rol is null)
            return BadRequest(new { mensaje = "El rol indicado no existe." });

        if (!rol.Activo)
            return BadRequest(new { mensaje = "El rol indicado está inactivo." });

        if (!RolesSistemas.EsActor(rol.Nombre))
            return BadRequest(new { mensaje = "El rol no corresponde a un actor del sistema." });

        if (rol.Nombre == RolesSistemas.RolGerente && (!usuario.Id_Sucursal.HasValue ||
            !await _db.Sucursales.AnyAsync(s => s.Id_Sucursal == usuario.Id_Sucursal && s.Activo)))
            return BadRequest(new { mensaje = "Asigna primero una sucursal activa al gerente." });

        if (rol.Nombre != RolesSistemas.RolAdmin && await EsUsuarioAdminActivoAsync(id) &&
            await ContarAdminsActivosAsync(id) == 0)
            return BadRequest(new { mensaje = "No puedes reemplazar el rol del único administrador activo." });

        if (request.FechaFin.HasValue && request.FechaFin.Value <= DateTime.UtcNow)
            return BadRequest(new { mensaje = "La fecha de fin debe ser posterior a la fecha actual." });

        var ahora = DateTime.UtcNow;
        var yaTieneRolActivo = await _db.RolUsuario.AnyAsync(ru =>
            ru.IdUsuario == id && ru.IdRol == request.IdRol && ru.Activo &&
            (ru.FechaFin == null || ru.FechaFin > ahora));

        if (yaTieneRolActivo)
            return Conflict(new { mensaje = "El usuario ya tiene ese rol asignado." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var anteriores = await _db.RolUsuario.Where(r => r.IdUsuario == id && r.Activo).ToListAsync();
        foreach (var anterior in anteriores)
        {
            anterior.Activo = false;
            anterior.FechaFin = ahora;
            anterior.FechaModificacion = ahora;
        }

        _db.RolUsuario.Add(new RolUsuario {
            IdUsuario = id,
            IdRol = request.IdRol,
            FechaFin = request.FechaFin,
            AsignadoPor = ObtenerIdAdminActual()
        });

        // MFA obligatorio para todos los roles (HU-02): se asegura al asignar el rol (factor minimo: correo)
        if (!usuario.TwoFactorEnabled)
            usuario.TwoFactorEnabled = true;

        await _db.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(usuario);
        await _auditoria.RegistrarAsync(
            "AsignarRol",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: id.ToString(),
            detalles: new { request.IdRol });
        await RevocarTokenDeUsuarioAsync(id);
        await transaction.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}/roles/{idRol:int}")]
    public async Task<IActionResult> RevocarRol(int id, int idRol)
    {
        var ahora = DateTime.UtcNow;
        var asignacion = await _db.RolUsuario
            .Include(ru => ru.Rol)
            .Include(ru => ru.Usuario)
            .Where(ru => ru.IdUsuario == id && ru.IdRol == idRol && ru.Activo &&
                (ru.FechaFin == null || ru.FechaFin > ahora))
            .OrderByDescending(ru => ru.FechaInicio)
            .FirstOrDefaultAsync();

        if (asignacion is null)
            return NotFound(new { mensaje = "El usuario no tiene ese rol asignado actualmente" });

        if (asignacion.Rol.Nombre.Equals(RolesSistemas.RolAdmin, StringComparison.OrdinalIgnoreCase)
            && await ContarAdminsActivosAsync(excluirIdUsuario: id) == 0)
        {
            return BadRequest(new { mensaje = "No puedes quitar el rol Admin al único administrador activo del sistema." });
        }

        asignacion.Activo = false;
        asignacion.FechaFin = ahora;
        asignacion.FechaModificacion = ahora;
        await _db.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(asignacion.Usuario);

        await _auditoria.RegistrarAsync(
            "RevocarRol",
            exitoso: true,
            idUsuario: ObtenerIdAdminActual(),
            entidad: "Usuario",
            idEntidadAfectada: id.ToString(),
            detalles: new { IdRol = idRol });
        await RevocarTokenDeUsuarioAsync(id);

        return NoContent();
    }

    private int? ObtenerIdAdminActual()
    {
        var idStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return idStr is not null && int.TryParse(idStr, out var id) ? id : null;
    }

    private async Task<bool> EsUsuarioAdminActivoAsync(int idUsuario)
    {
        var ahora = DateTime.UtcNow;
        return await _db.RolUsuario.AnyAsync(ru =>
            ru.IdUsuario == idUsuario && ru.Activo && ru.FechaInicio <= ahora &&
            (ru.FechaFin == null || ru.FechaFin > ahora) &&
            ru.Rol.Activo &&
            ru.Rol.Nombre == RolesSistemas.RolAdmin);
    }

    private async Task<int> ContarAdminsActivosAsync(int excluirIdUsuario)
    {
        var ahora = DateTime.UtcNow;
        return await _db.RolUsuario
            .Where(ru => ru.IdUsuario != excluirIdUsuario
                && ru.Activo
                && ru.FechaInicio <= ahora
                && (ru.FechaFin == null || ru.FechaFin > ahora)
                && ru.Rol.Activo
                && ru.Rol.Nombre == RolesSistemas.RolAdmin
                && ru.Usuario.Activo)
            .Select(ru => ru.IdUsuario)
            .Distinct()
            .CountAsync();
    }

    private async Task RevocarTokenDeUsuarioAsync(int idUsuario)
    {
        var activos = await _db.RefreshTokens
            .Where(t => t.IdUsuario == idUsuario && t.FechaRevocacion == null)
            .ToListAsync();
        foreach (var t in activos)
            t.FechaRevocacion = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}
