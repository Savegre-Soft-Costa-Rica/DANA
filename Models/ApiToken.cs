using System.Security.Cryptography;

namespace Dana.Models;

/// <summary>Token personal para la API REST (/api/v1). Actúa en nombre del usuario que lo creó.</summary>
public class ApiToken : Entity
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "dtk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    public string UserName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }
}
