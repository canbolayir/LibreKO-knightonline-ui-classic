using Godot;
namespace KnightOnlineUiClassic;

/// <summary>Original 1298 quest-frame corners and edges, resized without stretching ornaments.</summary>
public partial class ClassicFrame : Control
{
    public float BackgroundAlpha { get; set; } = 0.86f;
    public Color BackgroundColor { get; set; } = new(0.025f,0.022f,0.018f);
    public ClassicFrame() { MouseFilter=MouseFilterEnum.Ignore; Resized+=QueueRedraw; }
    public override void _Draw()
    {
        var texture=Plugin.Kit.Texture("ui_quest_us.png");
        if(texture==null) return;
        const float edge=42;
        float w=Size.X,h=Size.Y;
        DrawRect(new Rect2(4,4,w-8,h-8),new Color(BackgroundColor,BackgroundAlpha));
        void Slice(Rect2 dst,Rect2 src)=>DrawTextureRectRegion(texture,dst,src);
        Slice(new Rect2(0,0,edge,edge),new Rect2(0,0,edge,edge));
        Slice(new Rect2(w-edge,0,edge,edge),new Rect2(320,0,edge,edge));
        Slice(new Rect2(0,h-edge,edge,edge),new Rect2(0,269,edge,edge));
        Slice(new Rect2(w-edge,h-edge,edge,edge),new Rect2(320,269,edge,edge));
        Slice(new Rect2(edge,0,w-edge*2,6),new Rect2(48,0,260,6));
        Slice(new Rect2(edge,h-8,w-edge*2,8),new Rect2(48,303,260,8));
        Slice(new Rect2(0,edge,7,h-edge*2),new Rect2(0,60,7,160));
        Slice(new Rect2(w-7,edge,7,h-edge*2),new Rect2(355,60,7,160));
    }
}
