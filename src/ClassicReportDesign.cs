using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic;

/// <summary>Original Various-window presentation, scoped to the four character tabs.</summary>
public static class ClassicReportDesign
{
    public static Color Accent => new(ClassicDesign.Karus ? "ff8080" : "34a7e4");
    public static Color Value => new(ClassicDesign.Karus ? "ebebeb" : "efd9b4");
    public static Color Caption => new(ClassicDesign.Karus ? "c0c0c0" : "d9be8c");
    public static Color SelectedRow => new(ClassicDesign.Karus ? "454b55" : "153a54");

    public static void StyleButton(Button button, bool tab, LayoutNode? node = null)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            int tag=state is "pressed" or "hover_pressed"?1:state=="hover"?2:state=="disabled"?3:0;
            var part=node?.Images.FirstOrDefault(n=>n.Tag==tag) ?? node?.Images.FirstOrDefault();
            var box = part?.Texture is { } texture ? new StyleBoxTexture {
                Texture=Plugin.Kit.Texture(texture),RegionRect=new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH)
            } : ClassicDesign.ButtonBox(state, tab);
            // Native height and fixed end caps; the gradient must never repeat vertically.
            foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) box.SetTextureMargin(side,3);
            box.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
            box.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
            box.SetContentMargin(Side.Top, 0); box.SetContentMargin(Side.Bottom, 0);
            box.SetContentMargin(Side.Left, 2); box.SetContentMargin(Side.Right, 2);
            if (state == "disabled") box.ModulateColor = new Color(.85f, .85f, .85f);
            button.AddThemeStyleboxOverride(state, box);
        }
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", Plugin.Kit.Bold);
        button.AddThemeFontSizeOverride("font_size", node?.Size > 0 ? UiKit.FontSize(node) : 12);
        button.AddThemeConstantOverride("outline_size", 0);
        button.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        var ink = new Color(ClassicDesign.Karus ? "201c18" : "efd9b4");
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, ink);
        button.AddThemeColorOverride("font_disabled_color", new Color(ClassicDesign.Karus ? "57544f" : "b0a68c"));
        if (!ClassicDesign.Karus) button.AddThemeColorOverride("font_pressed_color", new Color("fff5c5"));
    }

    public static void StyleLabel(Label label, LayoutNode node)
    {
        label.AddThemeFontOverride("font", node.Bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        label.AddThemeFontSizeOverride("font_size", node.Id=="grade_number"?23:UiKit.FontSize(node));
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeColorOverride("font_color", node.Color);
    }
}

public partial class ClassicReportButton : Button
{
}

/// <summary>Native frame rails bridge the close ornament, tabs, and body without stretching corners.</summary>
public partial class ClassicReportHeader : Control
{
    private readonly bool _separators;
    public ClassicReportHeader(bool separators=true) { _separators=separators; MouseFilter=MouseFilterEnum.Ignore; TextureFilter=TextureFilterEnum.Nearest; Resized+=QueueRedraw; }
    public override void _Draw()
    {
        var part=Plugin.Kit.Layout("{nation}_various_frame_us").Children.First(n=>n.IsImage);
        var texture=Plugin.Kit.Texture(part.Texture!);
        if(texture==null) return;
        int sourceTop=part.SrcY+(ClassicDesign.Karus?40:44)-part.Y;
        void Tile(Rect2 destination,Rect2 source)
        {
            for(float y=0;y<destination.Size.Y;y+=source.Size.Y)
                for(float x=0;x<destination.Size.X;x+=source.Size.X)
                {
                    var size=new Vector2(Math.Min(source.Size.X,destination.Size.X-x),Math.Min(source.Size.Y,destination.Size.Y-y));
                    DrawTextureRectRegion(texture,new Rect2(destination.Position+new Vector2(x,y),size),new Rect2(source.Position,size));
                }
        }
        Tile(new Rect2(4,0,Size.X-8,4),new Rect2(part.SrcX+56,sourceTop,16,4));
        Tile(new Rect2(4,40,Size.X-8,2),new Rect2(part.SrcX+56,part.SrcY+part.SrcH-2,16,2));
        Tile(new Rect2(0,0,6,42),new Rect2(part.SrcX,sourceTop,6,42));
        Tile(new Rect2(Size.X-4,0,4,42),new Rect2(part.SrcX+part.SrcW-4,sourceTop,4,42));
        for(int i=1;_separators && i<4;i++)
            Tile(new Rect2(4+i*87,4,2,36),new Rect2(part.SrcX+(ClassicDesign.Karus?72:71),sourceTop+4,2,36));
    }
}

/// <summary>Clean outer edges from the original Friend atlas, with untiled corner ornaments.</summary>
public partial class ClassicReportSurface : Control
{
    private readonly bool _rule;
    public ClassicReportSurface(bool rule = false)
    {
        _rule = rule; MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw;
    }
    public override void _Draw()
    {
        var part = Plugin.Kit.Layout(_rule ? "{nation}_page_state_us" : "{nation}_page_friends_us").Children.First(n => n.IsImage);
        var texture = Plugin.Kit.Texture(part.Texture!);
        if (texture == null || Size.X <= 0 || Size.Y <= 0) return;
        void Tile(Rect2 destination, Rect2 source)
        {
            for (float y = 0; y < destination.Size.Y; y += source.Size.Y)
                for (float x = 0; x < destination.Size.X; x += source.Size.X)
                {
                    var size = new Vector2(Math.Min(source.Size.X, destination.Size.X - x), Math.Min(source.Size.Y, destination.Size.Y - y));
                    DrawTextureRectRegion(texture, new Rect2(destination.Position + new Vector2(x, y), size), new Rect2(source.Position, size));
                }
        }
        if (_rule)
        {
            var source = Size.X <= 2 && Size.Y > 2 ? new Rect2(55, part.SrcY + 39, 2, 16) : new Rect2(part.SrcX + 20, part.SrcY + 31, 16, 2);
            Tile(new Rect2(Vector2.Zero, Size), source); return;
        }
        int sx = part.SrcX, sy = part.SrcY, sw = part.SrcW, sh = part.SrcH;
        DrawRect(new Rect2(4, 0, Size.X - 8, Size.Y - 15), new Color(.01f, .01f, .01f, .88f));
        // Straight side runs exclude every painted table crossing.
        Tile(new Rect2(0, 0, 6, Size.Y - 24), new Rect2(sx, 100, 6, 16));
        Tile(new Rect2(Size.X - 4, 0, 4, Size.Y - 24), new Rect2(sx + sw - 4, 100, 4, 16));
        // The entire sloped bottom-left ornament is copied once. Only the straight rail repeats.
        DrawTextureRectRegion(texture, new Rect2(0, Size.Y - 24, 64, 24), new Rect2(sx, sy + sh - 24, 64, 24));
        Tile(new Rect2(64, Size.Y - 24, Size.X - 80, 24), new Rect2(sx + 64, sy + sh - 24, 16, 24));
        DrawTextureRectRegion(texture, new Rect2(Size.X - 16, Size.Y - 24, 16, 24), new Rect2(sx + sw - 16, sy + sh - 24, 16, 24));
    }
}

/// <summary>Resizable inset borders sampled from the original list frame, with fixed native corners.</summary>
public partial class ClassicReportSection : Control
{
    private Texture2D? _texture;
    private Rect2 _source;
    public ClassicReportSection() { MouseFilter=MouseFilterEnum.Ignore; TextureFilter=TextureFilterEnum.Nearest; Resized+=QueueRedraw; }
    public override void _Draw()
    {
        if(_texture==null)
        {
            var part=Plugin.Kit.Layout("{nation}_page_clan_us").Children.First(n=>n.IsImage && n.Texture!=null);
            _texture=Plugin.Kit.Texture(part.Texture!);
            // Each nation's actual perimeter excludes the table-column feet and empty atlas padding.
            _source=ClassicDesign.Karus?new Rect2(13,100,266,168):new Rect2(13,101,264,166);
        }
        if(_texture==null || Size.X<8 || Size.Y<8) return;
        void Tile(Rect2 destination,Rect2 source)
        {
            for(float y=0;y<destination.Size.Y;y+=source.Size.Y)
                for(float x=0;x<destination.Size.X;x+=source.Size.X)
                {
                    var size=new Vector2(Math.Min(source.Size.X,destination.Size.X-x),Math.Min(source.Size.Y,destination.Size.Y-y));
                    DrawTextureRectRegion(_texture,new Rect2(destination.Position+new Vector2(x,y),size),new Rect2(source.Position,size));
                }
        }
        var s=_source.Position; var end=_source.End;
        DrawTextureRectRegion(_texture,new Rect2(0,0,4,4),new Rect2(s,new Vector2(4,4)));
        DrawTextureRectRegion(_texture,new Rect2(Size.X-4,0,4,4),new Rect2(end.X-4,s.Y,4,4));
        DrawTextureRectRegion(_texture,new Rect2(0,Size.Y-4,4,4),new Rect2(s.X,end.Y-4,4,4));
        DrawTextureRectRegion(_texture,new Rect2(Size.X-4,Size.Y-4,4,4),new Rect2(end-new Vector2(4,4),new Vector2(4,4)));
        // Only straight runs repeat: omit all original table-column intersections.
        Tile(new Rect2(4,0,Size.X-8,4),new Rect2(s.X+12,s.Y,16,4));
        Tile(new Rect2(4,Size.Y-4,Size.X-8,4),new Rect2(s.X+12,end.Y-4,16,4));
        Tile(new Rect2(0,4,4,Size.Y-8),new Rect2(s.X,s.Y+30,4,16));
        Tile(new Rect2(Size.X-4,4,4,Size.Y-8),new Rect2(end.X-4,s.Y+30,4,16));
    }
}
