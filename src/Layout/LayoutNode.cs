using System.Text.Json;
using Godot;

namespace KnightOnlineUiClassic.Layout;

public static class TextStyle
{
    public const uint SingleLine = 0x00100000;
    public const uint AlignLeft = 0x00200000;
    public const uint AlignRight = 0x00400000;
    public const uint AlignCenter = 0x00800000;
    public const uint AlignTop = 0x01000000;
    public const uint AlignBottom = 0x02000000;
    public const uint AlignVCenter = 0x04000000;
}

public static class ProgressStyle
{
    public const uint RightToLeft = 0x20000000;
    public const uint TopToBottom = 0x40000000;
    public const uint BottomToTop = 0x80000000;
}

public static class ImageStyle
{
    public const uint Animated = 0x00010000;
}

public static class ButtonState
{
    public const int Normal = 0;
    public const int Pressed = 1;
    public const int Hover = 2;
    public const int Disabled = 3;
}

public static class ProgressPart
{
    public const int Background = 0;
    public const int Foreground = 1;
}

public sealed class LayoutNode
{
    public string Type = "";
    public string Id = "";
    public int X, Y, W, H;
    public bool HasDrag;
    public int DragX, DragY, DragW, DragH;
    public uint Style;
    public int Tag;
    public string Tooltip = "";
    public string? Texture;
    public int SrcX, SrcY, SrcW, SrcH;
    public bool FlipH, FlipV;
    public float Fps = 30f;
    public string Font = "";
    public int Size;
    public bool Bold, Italic;
    public Color Color = Colors.White;
    public string Text = "";
    public int LineSpacing;
    public bool HasClick;
    public int ClickX, ClickY, ClickW, ClickH;
    public int AreaType;
    public readonly List<LayoutNode> Children = new();

    public Rect2I Rect => new(X, Y, W, H);
    public Vector2 Position => new(X, Y);
    public Vector2 SizeVec => new(W, H);
    public Rect2I DragRect => new(DragX, DragY, DragW, DragH);

    public bool IsImage => Type == "image";
    public bool IsString => Type == "string";
    public bool IsButton => Type == "button";
    public bool IsArea => Type == "area";
    public bool IsProgress => Type is "progress" or "gradualprogress";

    public LayoutNode? Find(string id)
    {
        foreach (var n in All())
            if (n.Id == id) return n;
        return null;
    }

    public IEnumerable<LayoutNode> All()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var n in c.All())
                yield return n;
    }

    public IEnumerable<LayoutNode> Images => Children.Where(c => c.IsImage);
    public IEnumerable<LayoutNode> Strings => Children.Where(c => c.IsString);

    public static LayoutNode Parse(JsonElement e)
    {
        var n = new LayoutNode
        {
            Type = Str(e, "type"),
            Id = Str(e, "id"),
            Style = e.TryGetProperty("style", out var st) ? st.GetUInt32() : 0,
            Tag = Int(e, "tag"),
            Tooltip = Str(e, "tooltip"),
            Texture = e.TryGetProperty("texture", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            Font = Str(e, "font"),
            Size = Int(e, "size"),
            Bold = Bool(e, "bold"),
            Italic = Bool(e, "italic"),
            Text = Str(e, "text"),
            LineSpacing = Int(e, "lineSpacing"),
            AreaType = Int(e, "areaType"),
        };
        if (e.TryGetProperty("rect", out var r)) (n.X, n.Y, n.W, n.H) = Rect4(r);
        if (e.TryGetProperty("drag", out var d)) { n.HasDrag = true; (n.DragX, n.DragY, n.DragW, n.DragH) = Rect4(d); }
        if (e.TryGetProperty("src", out var s)) (n.SrcX, n.SrcY, n.SrcW, n.SrcH) = Rect4(s);
        if (e.TryGetProperty("flip", out var f) && f.ValueKind == JsonValueKind.Array)
        {
            n.FlipH = f[0].GetBoolean();
            n.FlipV = f[1].GetBoolean();
        }
        if (e.TryGetProperty("fps", out var fps) && fps.ValueKind == JsonValueKind.Number) n.Fps = (float)fps.GetDouble();
        if (e.TryGetProperty("click", out var c)) { n.HasClick = true; (n.ClickX, n.ClickY, n.ClickW, n.ClickH) = Rect4(c); }
        if (e.TryGetProperty("color", out var col) && col.ValueKind == JsonValueKind.String)
            n.Color = ParseColor(col.GetString() ?? "");
        if (e.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
            foreach (var k in kids.EnumerateArray())
                n.Children.Add(Parse(k));
        return n;
    }

    public static Color ParseColor(string hex)
    {
        if (hex.Length == 9 && hex[0] == '#')
        {
            uint v = Convert.ToUInt32(hex.Substring(1), 16);
            return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        }
        return Colors.White;
    }

    private static (int, int, int, int) Rect4(JsonElement a) =>
        (a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32());

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
