using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicWhisperSkin
{
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach(var child in root.GetChildren())
        {
            yield return child;
            foreach(var nested in Descendants(child)) yield return nested;
        }
    }

    public static void Apply(HudWindow window)
    {
        var kit=Plugin.Kit;
        // The original client uses this common PM artwork for both nations.
        var open=kit.Layout("co_whisper_open_us");
        var closed=kit.Layout("co_whisper_close_us");
        // Every new Classic conversation opens at the same upper-right origin.
        // Existing conversations retain their manually dragged position.
        Vector2 StartPosition() => new(Mathf.Max(0, window.GetViewportRect().Size.X-open.W-12),96);
        window.DockTo(StartPosition);
        window.Position=StartPosition();
        var header=window.Header!;
        var nativeGrip=Descendants(header).OfType<HBoxContainer>().First();
        string name=Descendants(header).OfType<Label>().First(label=>label.Text!="◆").Text;
        var nativeClose=Descendants(header).OfType<Button>().Last();
        var scroll=Descendants(window.Body).OfType<ScrollContainer>().First();
        var input=Descendants(window.Body).OfType<LineEdit>().First();
        var nativeSend=Descendants(window.Body).OfType<Button>().First();

        var surface=new Control { Name="classic_whisper",MouseFilter=Control.MouseFilterEnum.Ignore };
        var expanded=new LayoutView(kit,open);
        var compact=new LayoutView(kit,closed) { Visible=false };
        surface.AddChild(expanded);
        surface.AddChild(compact);
        foreach(var native in window.GetChildren().OfType<MarginContainer>()) native.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        window.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;
        window.AddChild(surface);

        foreach(var view in new[]{expanded,compact})
        {
            var title=view.Get<Label>("exit_id")!;
            title.Text=name;
            title.AddThemeFontOverride("font",kit.Bold);
            title.AddThemeFontSizeOverride("font_size",UiKit.FontSize(open.Find("exit_id")!));
            title.HorizontalAlignment=HorizontalAlignment.Left;
            title.VerticalAlignment=VerticalAlignment.Center;
            title.AutowrapMode=TextServer.AutowrapMode.Off;
            title.ClipText=true;
            title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            title.Position=new Vector2(view==expanded ? 8:6,2);
            title.Size=new Vector2(view==expanded ? 216:94,20);
            title.AddThemeColorOverride("font_outline_color",Colors.Black);
            title.AddThemeConstantOverride("outline_size",1);
            var grip=view.Get("btn_bar")!;
            grip.MouseDefaultCursorShape=Control.CursorShape.Move;
            grip.GuiInput+=ev=> {
                if(ev is not InputEventMouseButton { ButtonIndex:MouseButton.Left } press) return;
                nativeGrip.EmitSignal(Control.SignalName.GuiInput,press);
                grip.AcceptEvent();
            };
        }
        expanded.OnPressed("btn_hide",()=>window.SetMinimized(true));
        compact.OnPressed("btn_open",()=>window.SetMinimized(false));
        expanded.OnPressed("btn_close",()=>nativeClose.EmitSignal(BaseButton.SignalName.Pressed));
        expanded.OnPressed("btn_chat",()=>nativeSend.EmitSignal(BaseButton.SignalName.Pressed));
        expanded.Get("btn_hide")!.TooltipText="Minimize";
        compact.Get("btn_open")!.TooltipText="Restore";
        expanded.Get("btn_close")!.TooltipText="Close";
        expanded.Get("btn_chat")!.TooltipText="Send (Enter)";

        // Reserve separate restore and close cells in the original compact frame.
        compact.Get("btn_open")!.Position=new Vector2(104,3);
        var compactBar=(TextureButton)compact.Get("btn_bar")!;
        compactBar.Size=new Vector2(101,15);
        AtlasTexture? HeaderCrop(Texture2D? texture) => texture is AtlasTexture atlas ? new AtlasTexture {
            Atlas=atlas.Atlas,Region=new Rect2(atlas.Region.Position,new Vector2(101,15)),FilterClip=true,
        }:null;
        compactBar.TextureNormal=HeaderCrop(compactBar.TextureNormal);
        compactBar.TextureHover=HeaderCrop(compactBar.TextureHover);
        compactBar.TexturePressed=HeaderCrop(compactBar.TexturePressed);
        compactBar.TextureDisabled=HeaderCrop(compactBar.TextureDisabled);
        var closeSource=(TextureButton)expanded.Get("btn_close")!;
        var compactClose=new TextureButton {
            Name="btn_close",Position=new Vector2(124,3),Size=new Vector2(19,15),
            IgnoreTextureSize=true,StretchMode=TextureButton.StretchModeEnum.Scale,
            TextureNormal=closeSource.TextureNormal,TextureHover=closeSource.TextureHover,
            TexturePressed=closeSource.TexturePressed,TextureDisabled=closeSource.TextureDisabled,
            FocusMode=Control.FocusModeEnum.None,TooltipText="Close",
        };
        compact.AddChild(compactClose);
        compactClose.Pressed+=()=>nativeClose.EmitSignal(BaseButton.SignalName.Pressed);

        var attention=new ClassicWhisperAttention(compactBar,compact.Get<Label>("exit_id")!);
        surface.AddChild(attention);
        window.AttentionStyler=attention.SetUnread;

        var logArea=open.Find("exit_chat")!;
        expanded.Get("exit_chat")!.Visible=false;
        scroll.Reparent(expanded,keepGlobalTransform:false);
        scroll.CustomMinimumSize=Vector2.Zero;
        scroll.Position=logArea.Position;
        scroll.Size=logArea.SizeVec;
        scroll.HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode=ScrollContainer.ScrollMode.ShowNever;
        scroll.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        foreach(var log in Descendants(scroll).OfType<VBoxContainer>()) log.AddThemeConstantOverride("separation",0);

        var edit=open.Find("edit_chat")!;
        var template=expanded.Get<LineEdit>("edit_chat")!;
        foreach(var background in template.GetChildren().OfType<TextureRect>().ToArray())
            background.Reparent(input,keepGlobalTransform:false);
        template.Visible=false;
        input.Reparent(expanded,keepGlobalTransform:false);
        input.CustomMinimumSize=Vector2.Zero;
        input.Position=edit.Position;
        input.PlaceholderText="";
        var textStyle=edit.Strings.First();
        foreach(var state in new[]{"normal","focus","read_only"})
            input.AddThemeStyleboxOverride(state,new StyleBoxEmpty { ContentMarginTop=textStyle.Y-edit.Y });
        input.AddThemeFontOverride("font",kit.ChatStrong);
        input.AddThemeFontSizeOverride("font_size",13);
        input.AddThemeColorOverride("font_color",textStyle.Color);
        input.AddThemeColorOverride("caret_color",textStyle.Color);
        input.AddThemeColorOverride("font_outline_color",Colors.Black);
        input.AddThemeConstantOverride("outline_size",1);
        input.Size=edit.SizeVec;

        BindScroll(expanded,open.Find("scroll")!,scroll.GetVScrollBar(),kit.ChatStrong.GetHeight(13));
        void Arrange(bool minimized)
        {
            if(minimized && input.HasFocus()) input.ReleaseFocus();
            expanded.Visible=!minimized;
            compact.Visible=minimized;
            var size=minimized ? closed.SizeVec:open.SizeVec;
            surface.CustomMinimumSize=size;
            surface.Size=size;
            window.CustomMinimumSize=size;
            window.Size=size;
            window.CallDeferred(Control.MethodName.ResetSize);
        }
        window.MinimizedChanged+=Arrange;
        Arrange(window.Minimized);
    }

    private static void BindScroll(LayoutView view,LayoutNode source,VScrollBar range,float lineHeight)
    {
        var arrows=source.Children.Where(n=>n.IsButton).OrderBy(n=>n.Y).ToArray();
        var up=(BaseButton)view.ControlOf(arrows[0])!;
        var down=(BaseButton)view.ControlOf(arrows[1])!;
        up.Pressed+=()=>range.Value-=lineHeight;
        down.Pressed+=()=>range.Value+=lineHeight;
        var trackSource=source.Children.First(n=>n.Type=="trackbar");
        view.ControlOf(trackSource)!.Visible=false;
        var track=new ClassicScrollTrack(range,trackSource,thumbHeight:trackSource.Images.First(n=>n.Tag==1).H,
            emptyAtEnd:false,scrollStep:lineHeight) {
            Position=trackSource.Position-source.Position,Size=trackSource.SizeVec,
        };
        view.ControlOf(source)!.AddChild(track);
        void Refresh()
        {
            up.Disabled=range.Value<=range.MinValue;
            down.Disabled=range.Value>=Math.Max(range.MinValue,range.MaxValue-range.Page);
            track.QueueRedraw();
        }
        range.Changed+=Refresh;
        range.ValueChanged+=_=>Refresh();
        Refresh();
    }

    public static Control Line(string name,bool mine,bool notice,string text)
    {
        var source=Plugin.Kit.Layout("co_whisper_open_us").Find("exit_chat")!;
        var label=new Label {
            Text=text,
            HorizontalAlignment=mine && !notice ? HorizontalAlignment.Right:HorizontalAlignment.Left,
            AutowrapMode=TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,
            CustomMinimumSize=new Vector2(source.W,0),MouseFilter=Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font",Plugin.Kit.ChatStrong);
        label.AddThemeFontSizeOverride("font_size",13);
        // Keep the original cyan private-channel color for incoming messages.
        label.AddThemeColorOverride("font_color",new Color(notice ? "ffff00":mine ? "ffffff":"80ffff"));
        label.AddThemeColorOverride("font_outline_color",Colors.Black);
        label.AddThemeConstantOverride("outline_size",1);
        label.AddThemeColorOverride("font_shadow_color",Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x",1);
        label.AddThemeConstantOverride("shadow_offset_y",1);
        return label;
    }
}

public partial class ClassicWhisperAttention : Node
{
    private readonly TextureButton _bar;
    private readonly Label _title;
    private readonly Texture2D? _normal;
    private readonly Texture2D? _highlight;
    private bool _unread;
    private bool _lit;
    private double _elapsed;

    public ClassicWhisperAttention(TextureButton bar,Label title)
    {
        _bar=bar;
        _title=title;
        _normal=bar.TextureNormal;
        _highlight=bar.TextureHover ?? _normal;
    }

    public override void _Ready() => SetProcess(_unread);

    public void SetUnread(bool unread)
    {
        if(_unread==unread) return;
        _unread=unread;
        _elapsed=0;
        _lit=unread;
        SetProcess(unread);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _elapsed+=delta;
        if(_elapsed<0.5) return;
        _elapsed%=0.5;
        _lit=!_lit;
        Refresh();
    }

    private void Refresh()
    {
        _bar.TextureNormal=_lit ? _highlight:_normal;
        _title.AddThemeColorOverride("font_color",new Color(_lit ? "ffff00":"00ffff"));
    }
}
