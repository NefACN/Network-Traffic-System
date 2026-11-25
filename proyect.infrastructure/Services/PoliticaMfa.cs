using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Services;

// MFA obligatorio para todos los roles, como minimo con codigo por correo 
public sealed class PoliticaMfa(UserManager<ApplicationUser> users, IApplicationDbContext db)
{
    // proveedor y nombre que usa el UserStore de Identity para la clave del autenticador
    private const string ProveedorInterno = "[AspNetUserStore]";
    private const string ClaveAutenticador = "AuthenticatorKey";
    // marca de una configuracion iniciada pero no confirmada con un codigo valido
    private const string ProveedorPropio = "DISMAC";
    private const string MarcaPendiente = "AuthenticatorPendiente";

    // todos los roles exigen MFA, se mantiene como metodo por si la politica cambia por cuenta
    public Task<bool> RequiereMfaAsync(int idUsuario, CancellationToken ct = default) => Task.FromResult(true);

    public async Task<bool> AutenticadorConfirmadoAsync(ApplicationUser usuario) =>
        await users.GetTwoFactorEnabledAsync(usuario)
        && !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(usuario))
        && await users.GetAuthenticationTokenAsync(usuario, ProveedorPropio, MarcaPendiente) is null;

    public Task MarcarAutenticadorPendienteAsync(ApplicationUser usuario) =>
        users.SetAuthenticationTokenAsync(usuario, ProveedorPropio, MarcaPendiente, "1");

    public Task ConfirmarAutenticadorAsync(ApplicationUser usuario) =>
        users.RemoveAuthenticationTokenAsync(usuario, ProveedorPropio, MarcaPendiente);

    // elimina la clave del autenticador para que el usuario quede solo con el codigo por correo
    public async Task QuitarAutenticadorAsync(ApplicationUser usuario)
    {
        await users.RemoveAuthenticationTokenAsync(usuario, ProveedorInterno, ClaveAutenticador);
        await users.RemoveAuthenticationTokenAsync(usuario, ProveedorPropio, MarcaPendiente);
    }

    // metodos validos en el login, excluye autenticador sin confirmar y SMS
    public async Task<IList<string>> MetodosValidosAsync(ApplicationUser usuario)
    {
        var metodos = (await users.GetValidTwoFactorProvidersAsync(usuario)).Where(m => m != TokenOptions.DefaultPhoneProvider).ToList();
        if (metodos.Contains(TokenOptions.DefaultAuthenticatorProvider) && !await AutenticadorConfirmadoAsync(usuario))
            metodos.Remove(TokenOptions.DefaultAuthenticatorProvider);
        return metodos;
    }

    // true si la cuenta cumple (o se pudo hacer cumplir) la politica
    public async Task<bool> AplicarAsync(ApplicationUser usuario, CancellationToken ct = default)
    {
        if (!await RequiereMfaAsync(usuario.Id, ct))
            return true;

        if (await users.GetTwoFactorEnabledAsync(usuario) && (await MetodosValidosAsync(usuario)).Count > 0)
            return true;

        // activacion automatica con el correo confirmado como factor minimo
        if (!usuario.EmailConfirmed || string.IsNullOrWhiteSpace(usuario.Email))
            return false;

        if (!(await users.SetTwoFactorEnabledAsync(usuario, true)).Succeeded)
            return false;

        await RevocarSesionesAsync(usuario.Id, ct);
        return true;
    }

    // revoca los refresh tokens emitidos antes de activar el segundo factor
    public Task RevocarSesionesAsync(int idUsuario, CancellationToken ct = default) =>
        db.RefreshTokens.Where(t => t.IdUsuario == idUsuario && t.FechaRevocacion == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaRevocacion, DateTime.UtcNow), ct);
}
