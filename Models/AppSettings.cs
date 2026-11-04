namespace Dana.Models;

/// <summary>Configuración de la empresa (documento único): datos para cotizaciones, moneda base y tipos de cambio.</summary>
public class AppSettings : Entity
{
    public string CompanyName { get; set; } = "Mi empresa";
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Web { get; set; }
    public string? LogoText { get; set; }

    public string BaseCurrency { get; set; } = "USD";
    /// <summary>Unidades de cada moneda por 1 unidad de la moneda base (p. ej. CRC: 520 → 1 USD = 520 CRC).</summary>
    public Dictionary<string, decimal> Rates { get; set; } = new() { ["USD"] = 1m, ["CRC"] = 520m, ["EUR"] = 0.92m, ["MXN"] = 17.5m };

    public string QuotePrefix { get; set; } = "COT-";
    public int NextQuoteNumber { get; set; } = 1;
    public int QuoteValidDays { get; set; } = 15;
    public decimal DefaultTaxRate { get; set; } = 13m;
    public AiSettings Ai { get; set; } = new();
    public IntegrationSettings Integrations { get; set; } = new();
    /// <summary>Datos del emisor para la factura electrónica de Hacienda (Costa Rica), versión 4.4.</summary>
    public HaciendaSettings Hacienda { get; set; } = new();
    /// <summary>Mapeo de cuentas y activación del posteo contable automático.</summary>
    public AccountingSettings Accounting { get; set; } = new();

    public string? QuoteTerms { get; set; } = "Precios sujetos a cambio sin previo aviso. Validez de la oferta según fecha indicada.";

    public static readonly (string Id, string Label)[] TiposIdentificacion =
    [
        ("01", "Cédula física"), ("02", "Cédula jurídica"), ("03", "DIMEX"), ("04", "NITE"), ("05", "Extranjero (no domiciliado)"),
    ];

    public decimal ToBase(decimal amount, string? currency)
    {
        if (string.IsNullOrEmpty(currency) || currency == BaseCurrency) return amount;
        return Rates.TryGetValue(currency, out var r) && r > 0 ? Math.Round(amount / r * (Rates.GetValueOrDefault(BaseCurrency, 1m) == 0 ? 1 : Rates.GetValueOrDefault(BaseCurrency, 1m)), 2) : amount;
    }
}
