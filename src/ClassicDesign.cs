using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic;

/// <summary>Shared metrics and original atlas fragments for composable classic screens.</summary>
public static class ClassicDesign
{
    public const int FontSize = 13;
    public const int RowHeight = 22;
    public const int ActionHeight = 26;
    public static bool Karus => Plugin.Kit.Nation == 1;
    public static Color Text => new("eee9dd");
    public static Color Heading => new(Karus ? "dedbd4" : "f2ce75");
    public static Color Muted => new("b4aea1");
    public static Color Accent => new(Karus ? "ffaaa0" : "9cd8f0");
    public static Color Selection => Karus ? new Color(.34f, .31f, .28f, .8f) : new Color(.13f, .23f, .31f, .8f);

    public static StyleBoxTexture ButtonBox(string state, bool tab = false)
    {
        var source = Plugin.Kit.Layout(tab ? "{nation}_various_frame_us" : "{nation}_page_friends_us")
            .All().First(n => n.IsButton && n.Id.Equals(tab ? "btn_state" : "btn_refresh", StringComparison.OrdinalIgnoreCase));
        int tag = state is "pressed" or "hover_pressed" ? 1 : state == "hover" ? 2 : 0;
        var part = source.Images.FirstOrDefault(n => n.Tag == tag) ?? source.Images.First();
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(part.Texture!),
            RegionRect = new Rect2(part.SrcX, part.SrcY, part.SrcW, part.SrcH) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        {
            box.SetTextureMargin(side, 3);
            box.SetContentMargin(side, side is Side.Left or Side.Right ? 6 : 2);
        }
        if (state == "disabled") box.ModulateColor = new Color(.55f, .55f, .55f);
        return box;
    }

    public static void StyleButton(Button button, bool tab = false)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            button.AddThemeStyleboxOverride(state, ButtonBox(state, tab));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", Plugin.Kit.Bold);
        button.AddThemeFontSizeOverride("font_size", FontSize);
        button.AddThemeConstantOverride("outline_size", Karus ? 1 : 0);
        button.AddThemeColorOverride("font_outline_color", new Color("171513"));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, Heading);
        button.AddThemeColorOverride("font_disabled_color", Muted);
    }

    public static StyleBoxFlat InputBox()
    {
        var box = new StyleBoxFlat { BgColor = new Color(.025f, .025f, .025f, .94f),
            BorderColor = new Color(Karus ? "77746e" : "8e7545") };
        box.SetBorderWidthAll(1);
        box.SetContentMargin(Side.Left, 6); box.SetContentMargin(Side.Right, 6);
        box.SetContentMargin(Side.Top, 3); box.SetContentMargin(Side.Bottom, 3);
        return box;
    }

    public static StyleBoxTexture SectionBox(int padding = 8)
    {
        var box = new StyleBoxTexture
        {
            Texture = Plugin.Kit.Texture(Karus ? "ui_ka_myinfo_us.png" : "ui_el_myinfo_us.png"),
            RegionRect = Karus ? new Rect2(6, 40, 281, 34) : new Rect2(7, 80, 281, 34),
            DrawCenter = false,
        };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        {
            box.SetTextureMargin(side, 1);
            box.SetContentMargin(side, padding);
        }
        return box;
    }

    public static void StyleLabel(Label label, bool heading = false)
    {
        label.AddThemeFontOverride("font", heading ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeColorOverride("font_color", heading ? Heading : Text);
    }
}

/// <summary>Original character border, tiled at native resolution; no painted table is stretched.</summary>
public partial class ClassicSurface : Control
{
    private readonly bool _rule;
    public ClassicSurface(bool rule = false)
    {
        _rule = rule; MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var part = Plugin.Kit.Layout("{nation}_page_state_us").Children.First(n => n.IsImage);
        var texture = Plugin.Kit.Texture(part.Texture!);
        if (texture == null || Size.X < 1 || Size.Y < 1) return;
        int sx = part.SrcX, sy = part.SrcY, sw = part.SrcW, sh = part.SrcH;
        void Tile(Rect2 destination, Rect2 source)
        {
            for (float y = 0; y < destination.Size.Y; y += source.Size.Y)
                for (float x = 0; x < destination.Size.X; x += source.Size.X)
                {
                    var size = new Vector2(Math.Min(source.Size.X, destination.Size.X - x), Math.Min(source.Size.Y, destination.Size.Y - y));
                    DrawTextureRectRegion(texture, new Rect2(destination.Position + new Vector2(x, y), size), new Rect2(source.Position, size));
                }
        }
        // The first horizontal rule and unbroken outer edge are sampled independently.
        if (_rule)
        {
            var source = Size.X == 1 && Size.Y > 1
                ? new Rect2(55, sy + 39, 1, 16)
                : new Rect2(sx + 20, sy + 31, 16, 1);
            Tile(new Rect2(Vector2.Zero, Size), source);
            return;
        }
        DrawRect(new Rect2(4, 2, Math.Max(0, Size.X - 8), Math.Max(0, Size.Y - 8)), new Color(.018f, .018f, .016f, .94f));
        Tile(new Rect2(6, 0, Size.X - 12, 2), new Rect2(sx + 20, sy + 31, 16, 2));
        Tile(new Rect2(0, 2, 6, Size.Y - 24), new Rect2(sx, sy + 12, 6, 16));
        Tile(new Rect2(Size.X - 6, 2, 6, Size.Y - 24), new Rect2(sx + sw - 6, sy + 12, 6, 16));
        Tile(new Rect2(24, Size.Y - 15, Size.X - 48, 15), new Rect2(sx + 40, sy + sh - 15, 16, 15));
        DrawTextureRectRegion(texture, new Rect2(0, Size.Y - 24, 24, 24), new Rect2(sx, sy + sh - 24, 24, 24));
        DrawTextureRectRegion(texture, new Rect2(Size.X - 24, Size.Y - 24, 24, 24), new Rect2(sx + sw - 24, sy + sh - 24, 24, 24));
    }
}
