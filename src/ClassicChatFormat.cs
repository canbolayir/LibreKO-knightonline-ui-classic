namespace KnightOnlineUiClassic;

/// <summary>Original channel tags, channel colours and nation name colours of the Classic chat log.</summary>
public static class ClassicChatFormat
{
    public static (string Tag, string Color) Channel(byte type) => type switch
    {
        2 => ("whisper", "ff7ad9"), 3 => ("party", "5fd95f"),
        5 => ("shout", "ff9a3c"), 6 => ("clan", "46d3c0"),
        7 or 8 or 12 => ("GM", "ffe24a"), 13 => ("commander", "c8e66b"),
        14 => ("trade", "d2b48c"), 15 => ("alliance", "6fb7ff"),
        19 => ("zone", "9aa0a6"), 23 => ("officer", "b48cff"),
        _ => ("", "e8e8e8"),
    };

    /// <summary>The channel slot a custom colour overrides (General, Shout, Party, Clan, Alliance), or -1.</summary>
    public static int ColourSlot(byte type) => type switch { 1 => 0, 5 => 1, 3 => 2, 6 => 3, 15 => 4, _ => -1 };

    public static string Line(byte type, string name, int nation, bool gm, string text, string? ink = null, string? body = null, bool links = false)
    {
        var (tag, color) = Channel(type);
        color = ink ?? color;
        body ??= $"[color=#{color}]{Esc(text)}[/color]";
        if (name.Length == 0)
            return (type == 6 ? $"[color=#{color}][lb]{tag}[rb] [/color]" : "") + body;
        string nameColor = gm ? "ffd24a" : nation switch { 1 => "e06666", 2 => "6fa8ff", _ => "dddddd" };
        string nameText = $"[color=#{nameColor}]{Esc(name)}[/color]";
        if (links && name.IndexOfAny(new[] { '[', ']' }) < 0) nameText = $"[url=p:{name}]{nameText}[/url]";
        return (tag.Length > 0 ? $"[color=#{color}][lb]{tag}[rb] [/color]" : "")
            + nameText + ": " + body;
    }

    /// <summary>Escapes player text so it cannot open BBCode tags.</summary>
    public static string Esc(string text) => text.Replace("[", "[lb]");
}
