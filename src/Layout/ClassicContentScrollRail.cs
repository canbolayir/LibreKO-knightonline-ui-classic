using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>Native pixel artwork bound to a ScrollContainer's live range and input model.</summary>
public partial class ClassicContentScrollRail : Control
{
    private readonly ScrollContainer _scroll;
    private readonly TextureButton _up,_down;
    private readonly ClassicScrollTrack _track;
    private Vector2 _arrangedSize;
    public VScrollBar Bar => _scroll.GetVScrollBar();
    public ClassicContentScrollRail(ScrollContainer scroll)
    {
        _scroll=scroll;MouseFilter=MouseFilterEnum.Stop;
        var source=Plugin.Kit.Layout("{nation}_chat_us").Find("scroll")!;
        var arrows=source.Children.Where(n=>n.IsButton).OrderBy(n=>n.Y).ToArray();
        _up=Arrow(arrows[0]);_down=Arrow(arrows[1]);
        _up.Pressed+=()=>Bar.Value-=32;_down.Pressed+=()=>Bar.Value+=32;
        AddChild(_up);AddChild(_down);
        _track=new ClassicScrollTrack(Bar,source.Children.First(n=>n.Type=="trackbar"),18,false,32);
        AddChild(_track);
        Resized+=Arrange;Arrange();Refresh();
    }
    private static TextureButton Arrow(LayoutNode source) => new()
    {
        TextureNormal=ClassicChatControls.Atlas(source.Images.First(n=>n.Tag==0)),
        TextureDisabled=ClassicChatControls.Atlas(source.Images.First(n=>n.Tag==0)),
        TexturePressed=ClassicChatControls.Atlas(source.Images.First(n=>n.Tag==1)),
        TextureHover=ClassicChatControls.Atlas(source.Images.First(n=>n.Tag==2)),
        FocusMode=FocusModeEnum.None,IgnoreTextureSize=true,StretchMode=TextureButton.StretchModeEnum.Scale,
    };
    private void Arrange()
    {
        _arrangedSize=Size;
        _up.Position=Vector2.Zero;_up.Size=new Vector2(18,18);
        _down.Position=new Vector2(0,Size.Y-18);_down.Size=new Vector2(18,18);
        _track.Position=new Vector2(0,18);_track.Size=new Vector2(18,Size.Y-36);
    }
    private void Refresh()
    {
        bool needed=Bar.MaxValue-Bar.MinValue>Bar.Page+.5;
        Visible=needed;
        _up.Disabled=Bar.Value<=Bar.MinValue;
        _down.Disabled=Bar.Value>=Math.Max(Bar.MinValue,Bar.MaxValue-Bar.Page);
        _up.SelfModulate=_up.Disabled?new Color(1,1,1,.7f):Colors.White;
        _down.SelfModulate=_down.Disabled?new Color(1,1,1,.7f):Colors.White;
        _track.QueueRedraw();
    }
    public override void _Ready() => Arrange();
    public override void _Process(double delta)
    {
        if(Size!=_arrangedSize) Arrange();
        Refresh();
    }
}
