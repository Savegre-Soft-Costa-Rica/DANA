namespace Dana.Components.Shared;

public static class Avatar
{
    private static readonly string[] Palette =
        ["#0073ea", "#00c875", "#a25ddc", "#fdab3d", "#e2445c", "#579bfc", "#ff642e", "#00d2d2", "#7e3b8a", "#bb3354"];

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    public static string ColorFor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "#c3c6d4";
        var h = 0;
        foreach (var ch in name) h = (h * 31 + ch) & 0x7fffffff;
        return Palette[h % Palette.Length];
    }
}
