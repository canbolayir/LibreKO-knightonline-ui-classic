using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class ClassicStatusShortcut : BaseButton
{
    private readonly PluginGame _game;
    private readonly LayoutNode _source;
    private readonly UiKit _kit;
    private readonly string _letter,_windowId,_title;
    private readonly Font _letterFont=new SystemFont { FontNames=new[]{"Times New Roman"},FontWeight=700 };
    private readonly Font _countFont;
    private readonly Color _ink;
    private readonly Texture2D? _mailTexture;
    private static readonly Vector2I[] MailFrames = {
        new(7,115),new(40,115),new(7,138),new(41,138),
        new(41,138),new(7,138),new(40,115),new(7,115),
    };
    private int _pending=-1;
    private int _mailFrame;
    private bool _hovered,_lit;
    private double _refreshClock,_flashClock;
    public string Letter => _letter;
    public bool HasBackground => _windowId!="mail";
    public Rect2 MailIconRect => new(0,1,28,20);

    public ClassicStatusShortcut(UiKit kit,LayoutNode source,string letter,string windowId,string title)
    {
        _kit=kit;
        _game=kit.Game;
        _source=source;
        _letter=letter;
        _windowId=windowId;
        _title=title;
        if(windowId=="mail") _mailTexture=kit.Texture("classic_mail_chaos.png");
        _countFont=kit.Bold;
        _ink=new Color(kit.Nation==1 ? "f0e3e2":"d7e9ee");
        Name="btn_"+windowId;
        Size=new Vector2(source.W,source.H);
        CustomMinimumSize=Size;
        FocusMode=FocusModeEnum.None;
        MouseFilter=MouseFilterEnum.Stop;
        TextureFilter=TextureFilterEnum.Nearest;
        TooltipText=title;
        Pressed+=()=>_game.Windows.Toggle(_windowId);
        MouseEntered+=()=> { _hovered=true; QueueRedraw(); };
        MouseExited+=()=> { _hovered=false; QueueRedraw(); };
        ButtonDown+=QueueRedraw;
        ButtonUp+=QueueRedraw;
    }

    public override void _Ready() => SetPending(_game.Windows.NotificationCount(_windowId));

    public override void _Process(double delta)
    {
        _refreshClock+=delta;
        if(_refreshClock>=0.25)
        {
            _refreshClock%=0.25;
            SetPending(_game.Windows.NotificationCount(_windowId));
        }
        if(_pending<=0) return;
        _flashClock+=delta;
        if(_windowId=="mail")
        {
            const double frameTime=0.1;
            if(_flashClock<frameTime) return;
            int steps=(int)(_flashClock/frameTime);
            _flashClock%=frameTime;
            _mailFrame=(_mailFrame+steps)%MailFrames.Length;
            QueueRedraw();
            return;
        }
        if(_flashClock<0.6) return;
        _flashClock%=0.6;
        _lit=!_lit;
        QueueRedraw();
    }

    private void SetPending(int count)
    {
        count=Math.Max(0,count);
        if(_pending==count) return;
        _pending=count;
        _flashClock=0;
        _mailFrame=0;
        _lit=count>0;
        TooltipText=count==0 ? _title:_windowId=="mail"
            ? $"Mail — {count} unread"
            : $"{_title} — {count} reward{(count==1 ? "":"s")} to claim";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if(_windowId=="mail")
        {
            DrawMailIcon();
            if(_pending>0) DrawBadge();
            return;
        }
        DrawBackground();
        const int fontSize=22;
        float width=_letterFont.GetStringSize(_letter,fontSize:fontSize).X;
        var pen=new Vector2(Mathf.Round((Size.X-width)/2),
            Mathf.Round((28-_letterFont.GetHeight(fontSize))/2+_letterFont.GetAscent(fontSize)));
        if(IsPressed()) pen+=Vector2.One;
        DrawStringOutline(_letterFont,pen,_letter,fontSize:fontSize,size:1,modulate:new Color(0,0,0,0.7f));
        var ink=Disabled ? new Color("8c8c8c"):_lit ? new Color("fff1a8"):_hovered || IsPressed() ? Colors.White:_ink;
        DrawString(_letterFont,pen,_letter,fontSize:fontSize,modulate:ink);
        if(_pending<=0) return;
        DrawBadge();
    }
    private void DrawBackground()
    {
        int state=Disabled?ButtonState.Disabled:IsPressed()?ButtonState.Pressed:_hovered || _lit?ButtonState.Hover:ButtonState.Normal;
        var part=_source.Images.First(n=>n.Tag==state);
        var texture=_kit.Texture(part.Texture!);if(texture==null) return;
        var origin=new Vector2(part.SrcX,part.SrcY);
        // Original nation border and vertical fill, without the baked-in Q glyph.
        void Slice(Rect2 target,Rect2 crop) => DrawTextureRectRegion(texture,target,new Rect2(origin+crop.Position,crop.Size));
        Slice(new Rect2(0,0,28,3),new Rect2(0,0,28,3));
        Slice(new Rect2(0,25,28,3),new Rect2(0,25,28,3));
        Slice(new Rect2(0,3,4,22),new Rect2(0,3,4,22));
        Slice(new Rect2(24,3,4,22),new Rect2(24,3,4,22));
        Slice(new Rect2(4,3,20,22),new Rect2(4,3,1,22));
    }

    private void DrawMailIcon()
    {
        if(_mailTexture==null) return;
        // Draw the complete native envelope at its original pixel size.
        var iconSize=MailIconRect.Size;
        var position=MailIconRect.Position;
        if(IsPressed()) position+=Vector2.One;
        var frame=MailFrames[_pending>0 ? _mailFrame : 0];
        var tint=Disabled ? new Color(0.55f,0.55f,0.55f) : Colors.White;
        DrawTextureRectRegion(_mailTexture,new Rect2(position,iconSize),
            new Rect2(frame.X,frame.Y,28,20),tint);
    }

    private void DrawBadge()
    {
        string text=_pending>9 ? "9+":_pending.ToString();
        var box=new Rect2(14,_windowId=="mail"?16:29,14,12);
        var edge=new Color(_kit.Nation==1 ? "c2b8b0":"b9a36f");
        DrawRect(box,new Color(0.04f,0.045f,0.05f,0.95f));
        DrawRect(box.Grow(-0.5f),edge,false,1);
        const int fontSize=9;
        float width=_countFont.GetStringSize(text,fontSize:fontSize).X;
        var pen=new Vector2(Mathf.Round(box.Position.X+(box.Size.X-width)/2),box.Position.Y+9);
        DrawString(_countFont,pen,text,fontSize:fontSize,modulate:new Color("fff1c8"));
    }
}
