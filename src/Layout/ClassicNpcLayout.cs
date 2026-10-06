using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Editable geometry for the selected ornate NPC composition.</summary>
public static class ClassicNpcLayout
{
    private static FontVariation? _headingFont;
    public static Font BodyFont => Plugin.Kit.ChatStrong;
    public static Font HeadingFont => _headingFont??=new FontVariation { BaseFont=Plugin.Kit.ChatFont,VariationEmbolden=UiKit.BoldStrength };
    public const int Width=363,Inset=18,InnerWidth=327,BodyY=208,RowHeight=32,RowGap=5,CardHeight=42,CardGap=6;
    public static readonly Rect2 TitlePlate=new(18,28,305,32);
    public static readonly Rect2 TitleText=new(36,28,281,32);
    public static readonly Rect2 Close=new(TitlePlate.End.X+7,TitlePlate.GetCenter().Y-10,20,20);
    public static readonly Rect2 Speech=new(117,80,228,120);
    public static readonly Rect2 Portrait=new(22,86,84,110);
    public static StyleBoxTexture InsetBox(int padding=0)
    {
        var box=new StyleBoxTexture { Texture=Plugin.Kit.Texture("ui_quest_us.png"),RegionRect=new Rect2(18,47,328,186),DrawCenter=false };
        foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) { box.SetTextureMargin(side,3);box.SetContentMargin(side,padding); }
        return box;
    }
    public static Label Text(string text,int size=12,bool bold=false,Color? ink=null)
    {
        var label=new Label { Text=text,MouseFilter=Control.MouseFilterEnum.Ignore,VerticalAlignment=VerticalAlignment.Center };
        label.AddThemeFontOverride("font",bold?HeadingFont:BodyFont);label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",ink??ClassicReportDesign.Value);label.AddThemeConstantOverride("outline_size",0);
        label.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
        return label;
    }
    public static void Place(Control control,Rect2 rect) { control.Position=rect.Position;control.Size=rect.Size; }
}
