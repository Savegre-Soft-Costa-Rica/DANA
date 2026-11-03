using Microsoft.AspNetCore.DataProtection;

namespace Dana.Services;

/// <summary>Cifra/descifra secretos en reposo (p. ej. contraseñas SMTP personales) con Data Protection.</summary>
public class SecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Dana.Secrets.v1");

    public string? Protect(string? plain)
        => string.IsNullOrEmpty(plain) ? plain : _protector.Protect(plain);

    /// <summary>Devuelve el texto plano, o null si no se puede descifrar (p. ej. rotaron las llaves).</summary>
    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return cipher;
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }
}
