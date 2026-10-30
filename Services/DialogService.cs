namespace Dana.Services;

/// <summary>Diálogos propios (confirmar / pedir texto). Sustituyen a window.confirm/prompt, que algunos navegadores incrustados bloquean.</summary>
public class DialogService
{
    public record Request(string Kind, string Title, string? Message, string? DefaultValue, TaskCompletionSource<string?> Tcs);

    public Request? Current { get; private set; }
    public event Action? Changed;

    public async Task<bool> ConfirmAsync(string message, string title = "Confirmar") =>
        await ShowAsync("confirm", title, message, null) is not null;

    public Task<string?> PromptAsync(string title, string? defaultValue = null, string? message = null) =>
        ShowAsync("prompt", title, message, defaultValue);

    private Task<string?> ShowAsync(string kind, string title, string? message, string? defaultValue)
    {
        Current?.Tcs.TrySetResult(null);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current = new Request(kind, title, message, defaultValue, tcs);
        Changed?.Invoke();
        return tcs.Task;
    }

    public void Resolve(string? value)
    {
        var c = Current;
        Current = null;
        Changed?.Invoke();
        c?.Tcs.TrySetResult(value);
    }
}
