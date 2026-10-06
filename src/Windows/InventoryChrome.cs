using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Native inventory rails, preserving each nation's painted baseline.</summary>
public partial class InventoryDrawerFrame : Control
{
    public InventoryDrawerFrame()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        TextureFilter=TextureFilterEnum.Nearest;
        Resized+=QueueRedraw;
    }

    public override void _Draw()
    {
        var source=Plugin.Kit.Layout("{nation}_inventory_us");
        var top=source.Children.First(n=>n.IsImage && n.W==362 && n.H==275);
        var body=source.Children.First(n=>n.IsImage && n.W==362 && n.H==134);
        var bottom=source.Children.First(n=>n.IsImage && n.W==362 && n.H>=75 && n.H<100);
        var texture=Plugin.Kit.Texture(top.Texture!);
        if(texture==null) return;
        void Tile(Rect2 destination,Rect2 sample)
        {
            for(int y=0;y<destination.Size.Y;y+=(int)sample.Size.Y)
                for(int x=0;x<destination.Size.X;x+=(int)sample.Size.X)
                {
                    var size=new Vector2(Math.Min(sample.Size.X,destination.Size.X-x),Math.Min(sample.Size.Y,destination.Size.Y-y));
                    DrawTextureRectRegion(texture,new Rect2(destination.Position+new Vector2(x,y),size),new Rect2(sample.Position,size));
                }
        }
        Tile(new Rect2(0,0,Size.X,6),new Rect2(top.SrcX+97,top.SrcY,16,6));
        Tile(new Rect2(0,5,6,Size.Y-10),new Rect2(body.SrcX,body.SrcY+16,6,16));
        Tile(new Rect2(Size.X-4,6,4,Size.Y-11),new Rect2(body.SrcX+body.SrcW-4,body.SrcY+16,4,16));
        int bottomY=bottom.SrcY+InventoryLayout.DrawerBottom-5-(bottom.Y-source.Y);
        DrawSetTransform(new Vector2(0,5),0,new Vector2(1,-1));
        DrawTextureRectRegion(texture,new Rect2(0,0,6,5),new Rect2(bottom.SrcX,bottomY,6,5));
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
        Tile(new Rect2(0,Size.Y-5,Size.X,5),new Rect2(bottom.SrcX+97,bottomY,16,5));
        DrawTextureRectRegion(texture,new Rect2(0,Size.Y-5,6,5),new Rect2(bottom.SrcX,bottomY,6,5));
        DrawSetTransform(new Vector2(Size.X,Size.Y-5),0,new Vector2(-1,1));
        DrawTextureRectRegion(texture,new Rect2(0,0,4,5),new Rect2(bottom.SrcX,bottomY,4,5));
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
}

/// <summary>A textured fold control fitted to the transparent side of the native portrait radius.</summary>
public partial class InventoryCornerButton : BaseButton
{
    private readonly int[] _ends=new int[64];
    private bool _expanded;
    public bool Expanded
    {
        get=>_expanded;
        set { _expanded=value;TooltipText=value?"Close costume and magic bags":"Open costume and magic bags";QueueRedraw(); }
    }
    public int ArrowDirection=>Expanded?1:-1;

    public InventoryCornerButton()
    {
        FocusMode=FocusModeEnum.None;
        MouseFilter=MouseFilterEnum.Stop;
        TextureFilter=TextureFilterEnum.Nearest;
        var part=Plugin.Kit.Layout("{nation}_inventory_us").Children.First(n=>n.IsImage && n.W==362 && n.H==275);
        using var image=Plugin.Kit.Texture(part.Texture!)!.GetImage();
        // Sample the original radius once; drawing and hit testing share its exact silhouette.
        for(int y=1;y<_ends.Length;y++)
        {
            int edge=80;
            for(int x=0;x<80;x++)
                if(image.GetPixel(part.SrcX+x,part.SrcY+y).A>0.35f) {edge=x;break;}
            _ends[y]=Math.Max(1,edge-1);
        }
        MouseEntered+=QueueRedraw;MouseExited+=QueueRedraw;ButtonDown+=QueueRedraw;ButtonUp+=QueueRedraw;
    }

    public override bool _HasPoint(Vector2 point)
    {
        int y=(int)Math.Floor(point.Y);
        return y>0 && y<_ends.Length && point.X>=1 && point.X<_ends[y];
    }

    public override void _Draw()
    {
        string state=GetDrawMode()==DrawMode.Pressed?"pressed":IsHovered()?"hover":"normal";
        var box=ClassicDesign.ButtonBox(state);
        var region=box.RegionRect.Grow(-3);
        for(int y=1;y<_ends.Length;y++)
        {
            if(_ends[y]<=1) continue;
            float sy=region.Position.Y+(y-1)*region.Size.Y/62f;
            DrawTextureRectRegion(box.Texture,new Rect2(1,y,_ends[y]-1,1),
                new Rect2(region.Position.X,sy,region.Size.X,Math.Max(1,region.Size.Y/62f)));
        }
        var rim=ClassicReportDesign.Caption;
        DrawLine(new Vector2(1.5f,1.5f),new Vector2(_ends[1]-.5f,1.5f),rim,1);
        int last=Enumerable.Range(1,63).Last(y=>_ends[y]>1);
        DrawLine(new Vector2(1.5f,1.5f),new Vector2(1.5f,last+.5f),rim,1);
        var edge=Enumerable.Range(1,last).Select(y=>new Vector2(_ends[y]-.5f,y+.5f)).ToArray();
        DrawPolyline(edge,rim,1);
        Vector2[] arrow={new(11,14),new(23,9),new(23,19)};
        if(Expanded) arrow=arrow.Select(p=>new Vector2(36-p.X,p.Y)).ToArray();
        arrow=arrow.Select(p=>p+new Vector2(-2,0)).ToArray();
        if(GetDrawMode()==DrawMode.Pressed) arrow=arrow.Select(p=>p+Vector2.One).ToArray();
        DrawColoredPolygon(arrow.Select(p=>p+Vector2.One).ToArray(),new Color(0,0,0,.85f));
        DrawColoredPolygon(arrow,new Color(ClassicDesign.Karus?"30271c":"f2ce75"));
        DrawLine(arrow[0],arrow[1],new Color(ClassicDesign.Karus?"796d59":"fff0bc"),1);
    }
}
