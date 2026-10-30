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
}
