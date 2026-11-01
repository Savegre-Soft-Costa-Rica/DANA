using System.Security.Claims;
using Dana.Data;
using Dana.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace Dana.Services;

public class UserService
{
    private readonly IRepository<User> _users;
    private readonly PasswordHasher<User> _hasher = new();

    public UserService(IRepository<User> users)
    {
        _users = users;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public Task<List<User>> GetAllAsync() => _users.GetAllAsync();
    public Task<User?> GetAsync(string? id) => _users.GetByIdAsync(id);
    public Task<long> CountAsync() => _users.CountAsync();
    public async Task<User?> FindByNameAsync(string name) => await _users.Collection.Find(u => u.Name == name && u.DeletedAt == null).FirstOrDefaultAsync();
    public async Task SavePermissionsAsync(User u) => await _users.UpdateAsync(u);

    public async Task<User?> FindByEmailAsync(string email)
    {
        var e = Normalize(email);
        return await _users.Collection.Find(u => u.Email == e && u.DeletedAt == null).FirstOrDefaultAsync();
    }

    /// <summary>Registro con correo y contraseña. El primer usuario registrado es Admin.</summary>
    public async Task<(User? User, string? Error)> RegisterAsync(string email, string name, string password)
    {
        var e = Normalize(email);
        if (await FindByEmailAsync(e) is not null)
            return (null, "Ya existe una cuenta con ese correo.");

        var user = new User
        {
            Email = e,
            Name = string.IsNullOrWhiteSpace(name) ? e : name.Trim(),
            Role = await _users.CountAsync() == 0 ? Roles.Admin : Roles.User,
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        try
        {
            await _users.InsertAsync(user);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return (null, "Ya existe una cuenta con ese correo.");
        }
        return (user, null);
    }

    /// <summary>Alta de usuario hecha por un administrador: define rol y permisos, no inicia sesión
    /// y no depende de <see cref="AuthOptions.AllowRegistration"/>.</summary>
    public async Task<(User? User, string? Error)> CreateByAdminAsync(
        string email, string name, string password, string role, Dictionary<string, string>? permissions)
    {
        var e = Normalize(email);
        if (string.IsNullOrWhiteSpace(e)) return (null, "Ingrese un correo.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return (null, "La contraseña debe tener al menos 8 caracteres.");
        if (!Roles.All.Contains(role)) role = Roles.User;
        if (await FindByEmailAsync(e) is not null)
            return (null, "Ya existe una cuenta con ese correo.");

        var user = new User
        {
            Email = e,
            Name = string.IsNullOrWhiteSpace(name) ? e : name.Trim(),
            Role = role,
            Permissions = role == Roles.Admin ? new() : permissions ?? new(),
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        try
        {
            await _users.InsertAsync(user);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return (null, "Ya existe una cuenta con ese correo.");
        }
        return (user, null);
    }

    public async Task<User?> ValidateCredentialsAsync(string email, string password)
    {
        var user = await FindByEmailAsync(email);
        if (user is null || !user.IsActive || user.PasswordHash is null) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        return user;
    }

    /// <summary>Busca o crea el usuario a partir de una cuenta Google. Devuelve null si el usuario está inactivo.</summary>
    public async Task<User?> SignInWithGoogleAsync(string email, string? name, string? picture)
    {
        var user = await FindByEmailAsync(email);
        if (user is null)
        {
            user = new User
            {
                Email = Normalize(email),
                Name = string.IsNullOrWhiteSpace(name) ? email : name!,
                Picture = picture,
                HasGoogle = true,
                Role = await _users.CountAsync() == 0 ? Roles.Admin : Roles.User,
                LastLoginAt = DateTime.UtcNow,
            };
            await _users.InsertAsync(user);
            return user;
        }

        if (!user.IsActive) return null;
        user.HasGoogle = true;
        if (!string.IsNullOrWhiteSpace(picture)) user.Picture = picture;
        if (string.IsNullOrWhiteSpace(user.Name) && !string.IsNullOrWhiteSpace(name)) user.Name = name!;
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        return user;
    }

    public async Task<string?> ChangePasswordAsync(User user, string? currentPassword, string newPassword)
    {
        if (user.PasswordHash is not null)
        {
            if (currentPassword is null ||
                _hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
                return "La contraseña actual no es correcta.";
        }
        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        await _users.UpdateAsync(user);
        return null;
    }

    public async Task<string?> SetRoleAsync(string userId, string role)
    {
        if (!Roles.All.Contains(role)) return "Rol inválido.";
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return "Usuario no encontrado.";
        if (user.Role == Roles.Admin && role != Roles.Admin && await _users.CountAsync(u => u.Role == Roles.Admin && u.IsActive) <= 1)
            return "Debe quedar al menos un administrador activo.";
        user.Role = role;
        await _users.UpdateAsync(user);
        return null;
    }

    public async Task<string?> SetActiveAsync(string userId, bool active)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return "Usuario no encontrado.";
        if (!active && user.Role == Roles.Admin && await _users.CountAsync(u => u.Role == Roles.Admin && u.IsActive) <= 1)
            return "Debe quedar al menos un administrador activo.";
        user.IsActive = active;
        await _users.UpdateAsync(user);
        return null;
    }

    public async Task<string?> DeleteAsync(string userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return null;
        if (user.Role == Roles.Admin && await _users.CountAsync(u => u.Role == Roles.Admin && u.IsActive) <= 1)
            return "Debe quedar al menos un administrador activo.";
        await _users.PurgeAsync(userId);
        return null;
    }

    public async Task UpdateProfileAsync(User user)
    {
        await _users.UpdateAsync(user);
    }

    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id!));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Name));
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        if (!string.IsNullOrEmpty(user.Picture)) identity.AddClaim(new Claim("picture", user.Picture));
        return new ClaimsPrincipal(identity);
    }
}
