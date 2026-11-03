using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Dana.Services;

/// <summary>Usuario de la sesión actual (ámbito: circuito de Blazor).</summary>
public class CurrentUser
{
    private readonly AuthenticationStateProvider _provider;

    public CurrentUser(AuthenticationStateProvider provider)
    {
        _provider = provider;
    }

    public async Task<ClaimsPrincipal> PrincipalAsync() => (await _provider.GetAuthenticationStateAsync()).User;

    public async Task<string> NameAsync()
    {
        var user = await PrincipalAsync();
        return user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Email) ?? "Sistema";
    }

    public async Task<bool> IsAdminAsync() => (await PrincipalAsync()).IsInRole(Models.Roles.Admin);

    /// <summary>true si la sesión es un invitado del modo demo (sin cuenta real).</summary>
    public async Task<bool> IsDemoAsync() => (await PrincipalAsync()).HasClaim("demo", "1");

    /// <summary>false para cuentas de "Solo lectura" (no pueden crear/editar/eliminar).</summary>
    public async Task<bool> CanWriteAsync()
    {
        var p = await PrincipalAsync();
        return !(p.IsInRole(Models.Roles.Viewer) && !p.IsInRole(Models.Roles.Admin));
    }
}
