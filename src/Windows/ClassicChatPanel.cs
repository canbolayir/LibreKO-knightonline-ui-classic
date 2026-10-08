using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public partial class ClassicChatPanel : Control
{
    public readonly Control Body=new();
    public ClassicChatPanel(string title,int height)
    {
        ZIndex=300;Size=new Vector2(363,height);CustomMinimumSize=Size;MouseFilter=MouseFilterEnum.Stop;
        var chrome=new ClassicNpcQuestChrome {Size=Size};AddChild(chrome);
        Resized+=()=> {chrome.Size=Size;Body.Size=new Vector2(319,Size.Y-108);};
        var caption=ClassicNpcLayout.Text(title,13,true);caption.Position=new Vector2(36,28);caption.Size=new Vector2(281,32);AddChild(caption);
        var close=new Button {TooltipText="Close",FocusMode=FocusModeEnum.None};
        ClassicReportDesign.StyleButton(close,false,Plugin.Kit.Layout("co_questmenu_us").Find("btn_close")!);
        close.Position=new Vector2(330,34);close.Size=new Vector2(20,20);close.Pressed+=()=>Visible=false;AddChild(close);
        Body.Position=new Vector2(22,78);Body.Size=new Vector2(319,height-108);AddChild(Body);
        NativeLayout.Attach(this,"classic_"+title.Replace(' ','_').ToLowerInvariant(),caption,
            ()=>new Vector2(Mathf.Round((GetViewportRect().Size.X-Size.X)/2),Mathf.Round((GetViewportRect().Size.Y-Size.Y)/2)),legacyResizeGrip:true);
    }
    public static Button Button(string text)
    {
        var button=new Button {Text=text,FocusMode=FocusModeEnum.None,ClipText=true};
        foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled"}) button.AddThemeStyleboxOverride(state,ClassicNpcQuestChrome.Plate(state));
        button.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color"}) button.AddThemeColorOverride(state,new Color("dfc184"));
        button.AddThemeFontOverride("font",Plugin.Kit.ChatStrong);button.AddThemeFontSizeOverride("font_size",12);
        button.CustomMinimumSize=new Vector2(0,24);
        return button;
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if(!IsVisibleInTree() || ev is not InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape}) return;
        Visible=false;GetViewport().SetInputAsHandled();
    }
}
