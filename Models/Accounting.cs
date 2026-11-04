namespace Dana.Models;

/// <summary>Clasificación de una cuenta del plan contable.</summary>
public enum AccountType { Activo, Pasivo, Patrimonio, Ingreso, Gasto }

public static class AccountTypes
{
    /// <summary>Tipo, etiqueta, color y si su saldo normal es deudor (aumenta con débitos).</summary>
    public static readonly (AccountType Type, string Label, string Color, bool Debit)[] All =
    [
        (AccountType.Activo, "Activo", "#0073ea", true),
        (AccountType.Pasivo, "Pasivo", "#e2445c", false),
        (AccountType.Patrimonio, "Patrimonio", "#a25ddc", false),
        (AccountType.Ingreso, "Ingreso", "#00c875", false),
        (AccountType.Gasto, "Gasto", "#fdab3d", true),
    ];

    public static (AccountType Type, string Label, string Color, bool Debit) Get(AccountType t) =>
        All.FirstOrDefault(x => x.Type == t, All[0]);

    public static string Label(AccountType t) => Get(t).Label;
    public static string Color(AccountType t) => Get(t).Color;

    /// <summary>true si el saldo normal de la cuenta es deudor.</summary>
    public static bool IsDebit(AccountType t) => Get(t).Debit;
}

/// <summary>Cuenta del plan de cuentas (catálogo contable).</summary>
public class Account : Entity
{
    /// <summary>Código jerárquico, p. ej. "1-01-01".</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; } = AccountType.Activo;

    /// <summary>Cuenta de detalle que acepta movimientos. Si es false es un título de agrupación.</summary>
    public bool IsPostable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string Currency { get; set; } = "CRC";
    public string? Notes { get; set; }
}

public static class JournalStatuses
{
    public static readonly (string Id, string Label, string Color)[] All =
    [
        ("draft", "Borrador", "#9ca3af"),
        ("posted", "Registrado", "#00c875"),
        ("void", "Anulado", "#e2445c"),
    ];

    public static (string Id, string Label, string Color) Get(string id) => All.FirstOrDefault(s => s.Id == id, All[0]);
}

/// <summary>Movimiento de una cuenta dentro de un asiento (una línea débito o crédito).</summary>
public class JournalLine
{
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public bool HasMovement => Debit != 0 || Credit != 0;
}

/// <summary>Asiento de diario (partida doble). El total de débitos debe igualar al de créditos.</summary>
public class JournalEntry : Entity
{
    /// <summary>Consecutivo del asiento; se asigna al registrar (0 mientras es borrador).</summary>
    public int Number { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public string Description { get; set; } = "";
    /// <summary>Referencia libre: n.º de factura, cheque, documento, etc.</summary>
    public string? Reference { get; set; }
    public string Status { get; set; } = "draft";
    public string Currency { get; set; } = "CRC";

    /// <summary>Origen del asiento: "manual", "invoice", etc.</summary>
    public string Source { get; set; } = "manual";
    public string? SourceId { get; set; }

    public List<JournalLine> Lines { get; set; } = new();

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public decimal Difference => Math.Round(TotalDebit - TotalCredit, 2);
    public bool IsBalanced => Difference == 0 && TotalDebit > 0;
}

/// <summary>Saldo acumulado de una cuenta (para balance de comprobación y estados financieros).</summary>
public record AccountBalance(Account Account, decimal Debit, decimal Credit)
{
    /// <summary>Saldo final según la naturaleza de la cuenta (positivo = saldo normal).</summary>
    public decimal Balance => AccountTypes.IsDebit(Account.Type) ? Debit - Credit : Credit - Debit;
}

/// <summary>Fila del libro mayor: un movimiento de la cuenta con su saldo acumulado.</summary>
public record LedgerRow(JournalEntry Entry, JournalLine Line, decimal Running);

/// <summary>Mapeo de cuentas para el posteo automático y bandera de activación.</summary>
public class AccountingSettings
{
    /// <summary>Si está activo, las facturas aceptadas y los pagos generan asientos automáticamente.</summary>
    public bool AutoPost { get; set; }
    public string CashAccount { get; set; } = "1-01-01";        // Caja
    public string BankAccount { get; set; } = "1-01-02";        // Bancos
    public string ReceivableAccount { get; set; } = "1-01-03";  // Cuentas por cobrar
    public string VatPayableAccount { get; set; } = "2-01-02";  // IVA por pagar (débito fiscal)
    public string SalesAccount { get; set; } = "4-01-01";       // Ventas / ingresos
}

/// <summary>Fila de antigüedad de cuentas por cobrar (aging), por cliente.</summary>
public record AgingRow(string Party, string? CompanyId, decimal Current, decimal D1_30, decimal D31_60, decimal D61_90, decimal Over90)
{
    public decimal Total => Current + D1_30 + D31_60 + D61_90 + Over90;
}
