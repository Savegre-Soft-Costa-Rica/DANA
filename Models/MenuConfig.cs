namespace Dana.Models;

/// <summary>Preferencia de un elemento del menú lateral (por usuario).</summary>
public class MenuItemPref
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }      // nombre personalizado (null = predeterminado)
    public string? Section { get; set; }    // sección personalizada (null = predeterminada)
    public bool Hidden { get; set; }
    public int Sort { get; set; }
    public string? Url { get; set; }        // solo enlaces personalizados
    public string? Icon { get; set; }       // solo enlaces personalizados (emoji o texto corto)
    public bool OpenInNewTab { get; set; }
}

public class MenuConfig
{
    public List<MenuItemPref> Items { get; set; } = new();
    public List<string> SectionOrder { get; set; } = new();
}
