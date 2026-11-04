using Dana.Data;
using Dana.Models;

namespace Dana.Services.Accounting;

/// <summary>Contabilidad: plan de cuentas, asientos de diario (partida doble) y reportes
/// (libro mayor, balance de comprobación y estados financieros).</summary>
public class AccountingService
{
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<JournalEntry> _entries;
    private readonly IRepository<PaymentPlan> _plans;
    private readonly IRepository<Company> _companies;
    private readonly IRepository<Contact> _contacts;
    private readonly SettingsService _settings;

    public AccountingService(IRepository<Account> accounts, IRepository<JournalEntry> entries,
        IRepository<PaymentPlan> plans, IRepository<Company> companies, IRepository<Contact> contacts, SettingsService settings)
    {
        _accounts = accounts;
        _entries = entries;
        _plans = plans;
        _companies = companies;
        _contacts = contacts;
        _settings = settings;
    }

    // ---------- Plan de cuentas ----------

    public Task<List<Account>> GetAccountsAsync() => _accounts.GetAllAsync();
    public Task<Account?> GetAccountAsync(string? id) => _accounts.GetByIdAsync(id);

    /// <summary>Cuentas de detalle activas, ordenadas por código (para los selectores de asiento).</summary>
    public async Task<List<Account>> GetPostableAsync() =>
        (await _accounts.GetAllAsync()).Where(a => a.IsPostable && a.IsActive).OrderBy(a => a.Code).ToList();

    public async Task<(bool Ok, string? Error)> SaveAccountAsync(Account account)
    {
        account.Code = account.Code.Trim();
        account.Name = account.Name.Trim();
        if (string.IsNullOrWhiteSpace(account.Code)) return (false, "El código de la cuenta es obligatorio.");
        if (string.IsNullOrWhiteSpace(account.Name)) return (false, "El nombre de la cuenta es obligatorio.");

        var all = await _accounts.GetAllAsync();
        if (all.Any(a => a.Code == account.Code && a.Id != account.Id))
            return (false, $"Ya existe una cuenta con el código {account.Code}.");

        if (account.Id is null) await _accounts.InsertAsync(account);
        else await _accounts.UpdateAsync(account);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteAccountAsync(string id)
    {
        var acc = await _accounts.GetByIdAsync(id);
        if (acc is null) return (false, "Cuenta no encontrada.");
        var entries = await _entries.GetAllAsync();
        if (entries.Any(e => e.Status != "void" && e.Lines.Any(l => l.AccountCode == acc.Code)))
            return (false, "No se puede eliminar: la cuenta tiene movimientos. Desactívela en su lugar.");
        await _accounts.DeleteAsync(id);
        return (true, null);
    }

    /// <summary>Carga el catálogo base de Costa Rica si el plan de cuentas está vacío. Idempotente.</summary>
    public async Task<int> SeedChartOfAccountsAsync()
    {
        if ((await _accounts.CountAsync()) > 0) return 0;
        var n = 0;
        foreach (var (code, name, type, postable) in BaseChart)
        {
            await _accounts.InsertAsync(new Account { Code = code, Name = name, Type = type, IsPostable = postable });
            n++;
        }
        return n;
    }

    // ---------- Asientos de diario ----------

    public Task<List<JournalEntry>> GetEntriesAsync() => _entries.GetAllAsync();
    public Task<JournalEntry?> GetEntryAsync(string? id) => _entries.GetByIdAsync(id);

    /// <summary>Requisitos para registrar un asiento. Devuelve null si es válido.</summary>
    public static string? Validate(JournalEntry entry)
    {
        var lines = entry.Lines.Where(l => l.HasMovement).ToList();
        if (string.IsNullOrWhiteSpace(entry.Description)) return "El asiento necesita una descripción.";
        if (lines.Count < 2) return "El asiento debe tener al menos dos líneas con movimiento.";
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.AccountCode))) return "Toda línea con movimiento debe tener una cuenta.";
        if (lines.Any(l => l.Debit < 0 || l.Credit < 0)) return "Los montos no pueden ser negativos.";
        if (lines.Any(l => l.Debit != 0 && l.Credit != 0)) return "Cada línea es débito o crédito, no ambos a la vez.";
        var debit = Math.Round(lines.Sum(l => l.Debit), 2);
        var credit = Math.Round(lines.Sum(l => l.Credit), 2);
        if (debit == 0) return "El asiento no tiene montos.";
        if (debit != credit) return $"El asiento no cuadra: débitos {debit:N2} ≠ créditos {credit:N2}.";
        return null;
    }

    public async Task<(bool Ok, string? Error)> SaveDraftAsync(JournalEntry entry)
    {
        if (entry.Id is not null)
        {
            var current = await _entries.GetByIdAsync(entry.Id);
            if (current is not null && current.Status != "draft")
                return (false, "Solo se pueden editar asientos en borrador.");
        }
        await FillNamesAsync(entry);
        if (entry.Id is null) await _entries.InsertAsync(entry);
        else await _entries.UpdateAsync(entry);
        return (true, null);
    }

    /// <summary>Registra el asiento (lo hace definitivo): valida la partida doble y asigna consecutivo.</summary>
    public async Task<(bool Ok, string? Error)> PostAsync(string id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return (false, "Asiento no encontrado.");
        if (entry.Status != "draft") return (false, "Solo se registran asientos en borrador.");

        var val = Validate(entry);
        if (val is not null) return (false, val);

        await FillNamesAsync(entry);
        entry.Lines = entry.Lines.Where(l => l.HasMovement).ToList();
        entry.Status = "posted";
        if (entry.Number == 0) entry.Number = await NextNumberAsync();
        await _entries.UpdateAsync(entry);
        return (true, null);
    }

    /// <summary>Anula un asiento registrado (no lo borra; deja de contar en los saldos).</summary>
    public async Task<(bool Ok, string? Error)> VoidAsync(string id)
    {
        var entry = await _entries.GetByIdAsync(id);
        if (entry is null) return (false, "Asiento no encontrado.");
        if (entry.Status == "void") return (false, "El asiento ya está anulado.");
        entry.Status = "void";
        await _entries.UpdateAsync(entry);
        return (true, null);
    }

    public Task DeleteDraftAsync(string id) => _entries.DeleteAsync(id);

    private async Task<int> NextNumberAsync()
    {
        var all = await _entries.GetAllAsync();
        return all.Select(e => e.Number).DefaultIfEmpty(0).Max() + 1;
    }

    private async Task FillNamesAsync(JournalEntry entry)
    {
        var byCode = (await _accounts.GetAllAsync()).ToDictionary(a => a.Code, a => a.Name);
        foreach (var l in entry.Lines)
            if (!string.IsNullOrWhiteSpace(l.AccountCode) && byCode.TryGetValue(l.AccountCode, out var name))
                l.AccountName = name;
    }

    // ---------- Reportes ----------

    /// <summary>Asientos registrados dentro del rango de fechas indicado.</summary>
    private async Task<List<JournalEntry>> PostedAsync(DateTime? from, DateTime? to)
    {
        var q = (await _entries.GetAllAsync()).Where(e => e.Status == "posted");
        if (from is not null) q = q.Where(e => e.Date.Date >= from.Value.Date);
        if (to is not null) q = q.Where(e => e.Date.Date <= to.Value.Date);
        return q.ToList();
    }

    /// <summary>Balance de comprobación: débitos, créditos y saldo por cuenta con movimiento.</summary>
    public async Task<List<AccountBalance>> TrialBalanceAsync(DateTime? from = null, DateTime? to = null)
    {
        var accounts = (await _accounts.GetAllAsync()).Where(a => a.IsPostable).ToDictionary(a => a.Code);
        var posted = await PostedAsync(from, to);

        var totals = new Dictionary<string, (decimal Debit, decimal Credit)>();
        foreach (var line in posted.SelectMany(e => e.Lines))
        {
            var t = totals.GetValueOrDefault(line.AccountCode);
            totals[line.AccountCode] = (t.Debit + line.Debit, t.Credit + line.Credit);
        }

        return totals
            .Where(kv => accounts.ContainsKey(kv.Key) && (kv.Value.Debit != 0 || kv.Value.Credit != 0))
            .Select(kv => new AccountBalance(accounts[kv.Key], kv.Value.Debit, kv.Value.Credit))
            .OrderBy(b => b.Account.Code)
            .ToList();
    }

    /// <summary>Libro mayor de una cuenta: sus movimientos con saldo acumulado.</summary>
    public async Task<List<LedgerRow>> LedgerAsync(string accountCode, DateTime? from = null, DateTime? to = null)
    {
        var account = (await _accounts.GetAllAsync()).FirstOrDefault(a => a.Code == accountCode);
        var debitNature = account is null || AccountTypes.IsDebit(account.Type);
        var posted = await PostedAsync(from, to);

        var movements = posted
            .OrderBy(e => e.Date).ThenBy(e => e.Number)
            .SelectMany(e => e.Lines.Where(l => l.AccountCode == accountCode).Select(l => (Entry: e, Line: l)));

        var rows = new List<LedgerRow>();
        decimal running = 0;
        foreach (var m in movements)
        {
            running += debitNature ? m.Line.Debit - m.Line.Credit : m.Line.Credit - m.Line.Debit;
            rows.Add(new LedgerRow(m.Entry, m.Line, running));
        }
        return rows;
    }

    // ---------- Posteo automático (facturas y pagos) ----------

    public async Task<bool> IsPostedAsync(string source, string sourceId) =>
        (await _entries.FindAsync(e => e.Source == source && e.SourceId == sourceId)).Any(e => e.Status != "void");

    /// <summary>Ids de origen ya contabilizados para un tipo de documento (p. ej. "invoice").</summary>
    public async Task<HashSet<string>> PostedSourceIdsAsync(string source) =>
        (await _entries.FindAsync(e => e.Source == source)).Where(e => e.Status != "void" && e.SourceId is not null).Select(e => e.SourceId!).ToHashSet();

    /// <summary>Genera el asiento de una factura aceptada: CxC ↔ Ventas + IVA por pagar (una nota de crédito invierte los signos).
    /// Idempotente: no duplica si ya existe el asiento de esa factura.</summary>
    public async Task<(bool Ok, string? Error)> PostInvoiceAsync(ElectronicInvoice inv, string? by)
    {
        if (inv.Id is null) return (false, "La factura no está guardada.");
        if (await IsPostedAsync("invoice", inv.Id)) return (false, "La factura ya está contabilizada.");

        var m = (await _settings.GetAsync()).Accounting;
        var missing = await MissingAccountsAsync(m.ReceivableAccount, m.SalesAccount, m.VatPayableAccount);
        if (missing is not null) return (false, missing);

        var isCredit = inv.Kind == "credit";
        var rec = new JournalLine { AccountCode = m.ReceivableAccount };
        var sales = new JournalLine { AccountCode = m.SalesAccount };
        var vat = new JournalLine { AccountCode = m.VatPayableAccount };

        if (!isCredit) { rec.Debit = inv.Total; sales.Credit = inv.Subtotal; vat.Credit = inv.Tax; }
        else { rec.Credit = inv.Total; sales.Debit = inv.Subtotal; vat.Debit = inv.Tax; }

        var lines = new List<JournalLine> { rec, sales };
        if (inv.Tax != 0) lines.Add(vat);

        var desc = $"{(isCredit ? "Nota de crédito" : "Factura")} {inv.Clave ?? inv.Id} · {inv.ReceiverName}";
        await PostEntryAsync("invoice", inv.Id, inv.Date, desc, inv.Currency, lines, by);
        return (true, null);
    }

    /// <summary>Genera el asiento de un pago recibido: Banco ↔ Cuentas por cobrar. Idempotente.</summary>
    public async Task<(bool Ok, string? Error)> PostPaymentAsync(PaymentLink link, string? by)
    {
        if (link.Id is null) return (false, "El pago no está guardado.");
        if (await IsPostedAsync("payment", link.Id)) return (false, "El pago ya está contabilizado.");

        var m = (await _settings.GetAsync()).Accounting;
        var missing = await MissingAccountsAsync(m.BankAccount, m.ReceivableAccount);
        if (missing is not null) return (false, missing);

        var lines = new List<JournalLine>
        {
            new() { AccountCode = m.BankAccount, Debit = link.Amount },
            new() { AccountCode = m.ReceivableAccount, Credit = link.Amount },
        };
        await PostEntryAsync("payment", link.Id, link.PaidAt ?? DateTime.UtcNow, $"Pago recibido · {link.Description}", link.Currency, lines, by);
        return (true, null);
    }

    /// <summary>Intenta contabilizar sin lanzar excepciones (para los ganchos automáticos). Nunca rompe la operación origen.</summary>
    public async Task TryPostInvoiceAsync(ElectronicInvoice inv, string? by)
    {
        try { if ((await _settings.GetAsync()).Accounting.AutoPost) await PostInvoiceAsync(inv, by); } catch { }
    }

    public async Task TryPostPaymentAsync(PaymentLink link, string? by)
    {
        try { if ((await _settings.GetAsync()).Accounting.AutoPost) await PostPaymentAsync(link, by); } catch { }
    }

    private async Task<string?> MissingAccountsAsync(params string[] codes)
    {
        var existing = (await _accounts.GetAllAsync()).Select(a => a.Code).ToHashSet();
        var missing = codes.Where(c => !existing.Contains(c)).ToList();
        return missing.Count == 0 ? null : $"Faltan cuentas en el plan: {string.Join(", ", missing)}. Revise la configuración contable.";
    }

    private async Task PostEntryAsync(string source, string sourceId, DateTime date, string desc, string currency, List<JournalLine> lines, string? by)
    {
        var entry = new JournalEntry
        {
            Source = source, SourceId = sourceId, Date = date, Description = desc, Currency = currency,
            Status = "posted", Number = await NextNumberAsync(), Lines = lines, CreatedBy = by, UpdatedBy = by,
        };
        await FillNamesAsync(entry);
        await _entries.InsertAsync(entry);
    }

    // ---------- Antigüedad de cuentas por cobrar (aging) ----------

    /// <summary>Antigüedad de saldos por cobrar, a partir de las cuotas no pagadas de los planes de pago.</summary>
    public async Task<List<AgingRow>> AgingAsync()
    {
        var plans = await _plans.GetAllAsync();
        var companies = (await _companies.GetAllAsync()).ToDictionary(c => c.Id!, c => c.Title);
        var contacts = (await _contacts.GetAllAsync()).ToDictionary(c => c.Id!, c => c.FullName);
        var today = DateTime.UtcNow.Date;

        var buckets = new Dictionary<string, (string Label, string? CompanyId, decimal[] B)>();
        foreach (var p in plans)
        {
            var key = p.CompanyId ?? p.ContactId ?? p.Id!;
            var label = (p.CompanyId is not null ? companies.GetValueOrDefault(p.CompanyId) : null)
                        ?? (p.ContactId is not null ? contacts.GetValueOrDefault(p.ContactId) : null)
                        ?? p.Title;
            if (!buckets.TryGetValue(key, out var row)) row = (label, p.CompanyId, new decimal[5]);

            foreach (var inst in p.Installments.Where(i => i.Status != InstallmentStatuses.Paid))
            {
                var daysOverdue = (today - inst.DueDate.Date).Days;
                var b = daysOverdue <= 0 ? 0 : daysOverdue <= 30 ? 1 : daysOverdue <= 60 ? 2 : daysOverdue <= 90 ? 3 : 4;
                row.B[b] += inst.Amount;
            }
            buckets[key] = row;
        }

        return buckets.Values
            .Select(r => new AgingRow(r.Label, r.CompanyId, r.B[0], r.B[1], r.B[2], r.B[3], r.B[4]))
            .Where(r => r.Total != 0)
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    /// <summary>Saldo por cobrar vigente de una compañía (cuotas no pagadas de sus planes).</summary>
    public async Task<decimal> CompanyOutstandingAsync(string companyId)
    {
        var plans = await _plans.FindAsync(p => p.CompanyId == companyId);
        return plans.SelectMany(p => p.Installments).Where(i => i.Status != InstallmentStatuses.Paid).Sum(i => i.Amount);
    }

    // ---------- Catálogo base de Costa Rica ----------

    /// <summary>Plan de cuentas inicial. Los títulos de agrupación tienen IsPostable = false.</summary>
    private static readonly (string Code, string Name, AccountType Type, bool Postable)[] BaseChart =
    [
        // 1. Activo
        ("1", "ACTIVO", AccountType.Activo, false),
        ("1-01", "Activo circulante", AccountType.Activo, false),
        ("1-01-01", "Caja", AccountType.Activo, true),
        ("1-01-02", "Bancos", AccountType.Activo, true),
        ("1-01-03", "Cuentas por cobrar", AccountType.Activo, true),
        ("1-01-04", "Inventarios", AccountType.Activo, true),
        ("1-01-05", "IVA acreditable (crédito fiscal)", AccountType.Activo, true),
        ("1-02", "Activo fijo", AccountType.Activo, false),
        ("1-02-01", "Mobiliario y equipo", AccountType.Activo, true),
        ("1-02-02", "Equipo de cómputo", AccountType.Activo, true),
        ("1-02-03", "Vehículos", AccountType.Activo, true),
        ("1-02-04", "Depreciación acumulada", AccountType.Activo, true),

        // 2. Pasivo
        ("2", "PASIVO", AccountType.Pasivo, false),
        ("2-01", "Pasivo circulante", AccountType.Pasivo, false),
        ("2-01-01", "Cuentas por pagar", AccountType.Pasivo, true),
        ("2-01-02", "IVA por pagar (débito fiscal)", AccountType.Pasivo, true),
        ("2-01-03", "Impuesto sobre la renta por pagar", AccountType.Pasivo, true),
        ("2-01-04", "Sueldos por pagar", AccountType.Pasivo, true),
        ("2-01-05", "Cargas sociales por pagar (CCSS)", AccountType.Pasivo, true),
        ("2-02", "Pasivo a largo plazo", AccountType.Pasivo, false),
        ("2-02-01", "Préstamos por pagar", AccountType.Pasivo, true),

        // 3. Patrimonio
        ("3", "PATRIMONIO", AccountType.Patrimonio, false),
        ("3-01-01", "Capital social", AccountType.Patrimonio, true),
        ("3-01-02", "Utilidades acumuladas", AccountType.Patrimonio, true),
        ("3-01-03", "Utilidad o pérdida del periodo", AccountType.Patrimonio, true),

        // 4. Ingresos
        ("4", "INGRESOS", AccountType.Ingreso, false),
        ("4-01-01", "Ventas", AccountType.Ingreso, true),
        ("4-01-02", "Ingresos por servicios", AccountType.Ingreso, true),
        ("4-01-03", "Otros ingresos", AccountType.Ingreso, true),

        // 5. Gastos y costos
        ("5", "GASTOS Y COSTOS", AccountType.Gasto, false),
        ("5-01-01", "Costo de ventas", AccountType.Gasto, true),
        ("5-02", "Gastos de operación", AccountType.Gasto, false),
        ("5-02-01", "Sueldos y salarios", AccountType.Gasto, true),
        ("5-02-02", "Cargas sociales", AccountType.Gasto, true),
        ("5-02-03", "Alquileres", AccountType.Gasto, true),
        ("5-02-04", "Servicios públicos", AccountType.Gasto, true),
        ("5-02-05", "Gastos administrativos", AccountType.Gasto, true),
        ("5-02-06", "Gastos de ventas", AccountType.Gasto, true),
        ("5-02-07", "Depreciación", AccountType.Gasto, true),
        ("5-02-08", "Gastos financieros", AccountType.Gasto, true),
    ];
}
