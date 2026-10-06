using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

// A single vertical layout. The group rotates as a whole, including its artwork.
public sealed class HotbarGeometry
{
    public const int Pitch=36, PageBand=28, KeyWidth=22, Tools=20, PageTools=22;
    public int Count { get; }
    public bool SharedKeys { get; }
    public Vector2 Size { get; }
    public int Column => SharedKeys ? KeyWidth : 0;
    public int End => PageBand+Count*Pitch;
    public HotbarGeometry(int count,bool sharedKeys)
    {
        Count=count;SharedKeys=sharedKeys;
        Size=new Vector2(Column+Pitch,End+PageTools+Tools);
    }
    public Vector2 Slot(int slot) => new(Column+2,PageBand+slot*Pitch+2);
    public Rect2 Key(int slot) => new(2,PageBand+slot*Pitch,KeyWidth-4,Pitch);
    public Rect2 TopOrnament => new(0,0,30,28);
    public Rect2 PageGrip => SharedKeys ? TopOrnament : new(Column+3,3,30,22);
    // Two equal footer cells share a baseline and integer centers in both orientations.
    public int FooterY => End+PageTools;
    public Rect2 PageRect => new(Column+18,FooterY,18,Tools);
    public Rect2 PageArrow(int direction) => new(Column+(direction<0 ? 2:18),End+2,16,18);
    public Rect2 Rotate => new(2,Size.Y-Tools+1,19,18);
    public Rect2 Lock => new(2,End+2,18,18);
    public Rect2 Add => new(Column,FooterY,18,Tools);
    public Rect2 Remove => new(Column,FooterY,18,Tools);
}

public static class HotbarVisuals
{
    private static Font? _font;
    public static Font Font => _font ??=new SystemFont { FontNames=new[]{"Tahoma","Verdana","Arial"},FontWeight=700 };
    public static Color Ink => Plugin.Kit.Nation==1 ? new Color(0.92f,0.90f,0.83f):new Color(0.98f,0.87f,0.61f);
    public static Label Label(string text,int size)
    {
        var label=new Label { Text=text,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            MouseFilter=Control.MouseFilterEnum.Ignore,AutowrapMode=TextServer.AutowrapMode.Off,ClipText=true };
        label.AddThemeFontOverride("font",Font);label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",Ink);label.AddThemeColorOverride("font_outline_color",Colors.Black);
        label.AddThemeConstantOverride("outline_size",1);
        return label;
    }
    public static Label KeyLabel(string text)
    {
        var label=Label(text,12);
        // Light lettering with a dark outline stays readable on either native metal rail.
        label.AddThemeColorOverride("font_color",Plugin.Kit.Nation==1 ? new Color(0.96f,0.94f,0.85f):new Color(0.98f,0.85f,0.17f));
        return label;
    }
    public static StyleBoxTexture Cell(LayoutNode layout)
    {
        var image=layout.Images.First();
        var area=layout.Children.First(n=>n.IsArea && n.Id=="1");
        var style=new StyleBoxTexture { Texture=Plugin.Kit.Texture(image.Texture!),
            RegionRect=new Rect2(image.SrcX+area.X-image.X,image.SrcY+area.Y-image.Y-2,image.W-area.X+image.X,36) };
        foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) style.SetTextureMargin(side,2);
        return style;
    }
    public static StyleBoxTexture Rail(LayoutNode layout)
    {
        var image=layout.Images.First();var last=layout.Children.First(n=>n.IsArea && n.Id=="7");
        var row=layout.Children.First(n=>n.IsArea && n.Id=="1");
        // Below the native '8' is a clean piece of the entire metal rail. Tile it
        // continuously through all ten keys and the footer, without baked digits
        // or a stretched two-pixel join at the bottom.
        var style=new StyleBoxTexture { Texture=Plugin.Kit.Texture(image.Texture!),
            RegionRect=new Rect2(image.SrcX,image.SrcY+last.Y-image.Y+27,row.X-image.X,4),
            AxisStretchVertical=StyleBoxTexture.AxisStretchMode.Tile };
        style.SetTextureMargin(Side.Left,3);style.SetTextureMargin(Side.Right,2);
        return style;
    }
}

// Native cells, caps and rail keep the same coordinates in both orientations.
public partial class OriginalHotbarArtwork : Control
{
    private readonly LayoutNode _layout;
    private readonly HotbarGeometry _grid;
    private readonly StyleBoxTexture _cell,_rail;
    public OriginalHotbarArtwork(LayoutNode layout,HotbarGeometry grid)
    {
        _layout=layout;_grid=grid;_cell=HotbarVisuals.Cell(layout);_rail=HotbarVisuals.Rail(layout);
        MouseFilter=MouseFilterEnum.Ignore;
    }
    public override void _Draw()
    {
        var canvas=GetCanvasItem();var image=_layout.Images.First();var texture=Plugin.Kit.Texture(image.Texture!);
        if(texture==null) return;
        if(_grid.SharedKeys)
            _rail.Draw(canvas,new Rect2(0,HotbarGeometry.PageBand,HotbarGeometry.KeyWidth,_grid.Size.Y-HotbarGeometry.PageBand));
        for(int slot=0;slot<_grid.Count;slot++) {
            _cell.Draw(canvas,new Rect2(_grid.Slot(slot)-new Vector2(2,2),new Vector2(36,36)));
        }
        _cell.Draw(canvas,new Rect2(_grid.Column,_grid.End,36,HotbarGeometry.PageTools));
        _cell.Draw(canvas,_grid.PageRect);
        if(_grid.SharedKeys) {
            DrawTextureRectRegion(texture,_grid.TopOrnament,new Rect2(image.SrcX+12,image.SrcY,29,30));
        }
    }
}

public partial class NativePageButton : BaseButton
{
    private readonly Dictionary<int,AtlasTexture> _faces=new();
    private bool _hovering;
    public NativePageButton(LayoutNode node)
    {
        FocusMode=FocusModeEnum.None;
        foreach(var image in node.Images) {
            var atlas=Plugin.Kit.Texture(image.Texture!);
            if(atlas!=null) _faces[image.Tag]=new AtlasTexture { Atlas=atlas,Region=new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH),FilterClip=true };
        }
        MouseEntered+=()=> { _hovering=true;QueueRedraw(); };
        MouseExited+=()=> { _hovering=false;QueueRedraw(); };
        ButtonDown+=QueueRedraw;ButtonUp+=QueueRedraw;
    }
    public override void _Draw()
    {
        int state=Disabled ? 3:ButtonPressed ? 1:_hovering ? 2:0;
        if(!_faces.TryGetValue(state,out var face) && !_faces.TryGetValue(0,out face)) return;
        DrawSetTransform(Size/2);
        DrawTextureRect(face,new Rect2(-Size.X/2,-6,Size.X,12),false);
    }
}

public partial class HotbarButton : Button
{
    private int _icon;
    public float GlyphRotation { get; set; }
    public int Glyph
    {
        get => _icon;
        set { if(_icon==value) return; _icon=value; QueueRedraw(); }
    }
    public HotbarButton(int icon=0)
    {
        _icon=icon;FocusMode=FocusModeEnum.None;
        foreach(var state in new[]{"normal","hover","pressed","disabled","focus"}) {
            var style=HotbarVisuals.Cell(Plugin.Kit.Layout("{nation}_hotkey_us"));
            if(state=="hover") style.ModulateColor=new Color(1.3f,1.2f,1.05f);
            if(state=="pressed") style.ModulateColor=new Color(0.7f,0.7f,0.7f);
            AddThemeStyleboxOverride(state,style);
        }
    }
    public override void _Draw()
    {
        var ink=HotbarVisuals.Ink;
        if(_icon>=3) {
            var center=new Vector2(Mathf.Floor(Size.X/2),Mathf.Floor(Size.Y/2));
            DrawSetTransform(center,GlyphRotation);
            bool locked=_icon==3;
            DrawRect(new Rect2(-5,-1,10,7),ink);
            DrawRect(new Rect2(-1,1,2,3),Colors.Black);
            DrawRect(new Rect2(-3,-6,6,2),ink);
            DrawRect(new Rect2(-4,-4,2,locked ? 4:2),ink);
            DrawRect(new Rect2(2,-4,2,4),ink);
            DrawSetTransform(Vector2.Zero);
        } else if(_icon>0) {
            var center=new Vector2(Mathf.Floor(Size.X/2),Mathf.Floor(Size.Y/2));
            // Filled two-pixel strokes avoid half-pixel/antialiased offsets between + and -.
            DrawRect(new Rect2(center-new Vector2(5,1),new Vector2(10,2)),ink);
            if(_icon==1) DrawRect(new Rect2(center-new Vector2(1,5),new Vector2(2,10)),ink);
        } else {
            DrawLine(new Vector2(5,13),new Vector2(5,5),ink,1.5f,true);
            DrawLine(new Vector2(5,5),new Vector2(13,5),ink,1.5f,true);
            DrawColoredPolygon(new[]{new Vector2(12,2),new Vector2(17,5),new Vector2(12,8)},ink);
            DrawColoredPolygon(new[]{new Vector2(2,12),new Vector2(5,17),new Vector2(8,12)},ink);
        }
    }
}
