namespace Dana.Services;

/// <summary>Bus de cambios en memoria: los tableros abiertos en otros navegadores se refrescan al recibir un evento.</summary>
public class ChangeNotifier
{
    public event Action<string, string?>? Changed;
    public void Publish(string collection, string? id)
    {
        try { Changed?.Invoke(collection, id); } catch { }
    }
}
