using Dana.Services.Invoicing;
using Dana.Services.Messaging;
using Dana.Services.Telegram;

namespace Dana.Tests;

/// <summary>Reglas puras de Twilio y facturación electrónica (sin MongoDB ni red).</summary>
public class MessagingAndInvoicingTests
{
    [Theory]
    [InlineData("+50688887777", "+50688887777")]
    [InlineData("+506 8888-7777", "+50688887777")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    public void NormalizePhone_AcceptsE164WithSeparators(string input, string expected) =>
        Assert.Equal(expected, TwilioMessagingService.NormalizePhone(input));

    [Theory]
    [InlineData("88887777")]
    [InlineData("+506")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizePhone_RejectsNonInternationalNumbers(string? input) =>
        Assert.Null(TwilioMessagingService.NormalizePhone(input));

    [Fact]
    public void Chunk_SplitsLongRepliesWithinTelegramLimit()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('x', 999), 20));
        var chunks = TelegramService.Chunk(text).ToList();
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= TelegramService.MaxMessageLength));
        Assert.Equal(text.Replace("\n", ""), string.Concat(chunks).Replace("\n", ""));
    }

    [Fact]
    public void Chunk_KeepsShortRepliesWhole() =>
        Assert.Equal(["hola"], TelegramService.Chunk("hola").ToList());

    [Fact]
    public void Validate_AcceptsCompleteInvoice()
    {
        var invoice = ValidInvoice();
        Assert.Null(InvoiceService.Validate(invoice));
    }

    [Fact]
    public void Validate_RequiresReceiverIdentification()
    {
        var invoice = ValidInvoice();
        invoice.ReceiverId = "";
        Assert.NotNull(InvoiceService.Validate(invoice));
    }

    [Fact]
    public void Validate_RequiresThirteenDigitCabys()
    {
        var invoice = ValidInvoice();
        invoice.Lines[0].Cabys = "123";
        Assert.Contains("CABYS", InvoiceService.Validate(invoice));
    }

    [Fact]
    public void Validate_RequiresAtLeastOneLine()
    {
        var invoice = ValidInvoice();
        invoice.Lines.Clear();
        Assert.NotNull(InvoiceService.Validate(invoice));
    }

    [Fact]
    public void Invoice_ComputesTaxAt13Percent()
    {
        var invoice = ValidInvoice();
        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(13m, invoice.Tax);
        Assert.Equal(113m, invoice.Total);
    }

    [Fact]
    public async Task NotConfiguredProvider_RefusesToSubmit()
    {
        var provider = new NotConfiguredInvoiceProvider();
        Assert.False(provider.IsConfigured);
        var result = await provider.SubmitAsync(ValidInvoice());
        Assert.False(result.Ok);
        Assert.Null(result.Clave);
    }

    private static Dana.Models.ElectronicInvoice ValidInvoice() => new()
    {
        ReceiverName = "Cliente de prueba",
        ReceiverId = "3101000000",
        Lines =
        [
            new Dana.Models.InvoiceLine { Cabys = "8311100000100", Description = "Servicio", Quantity = 1, UnitPrice = 100m, TaxRate = 13m },
        ],
    };
}
