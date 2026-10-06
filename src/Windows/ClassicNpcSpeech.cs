using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>A fixed speech frame whose paragraph scrolls without resizing the NPC page.</summary>
public partial class ClassicNpcSpeech : Control
{
    public Label Identity { get; }
    public RichTextLabel Text { get; }
    public int LineHeight { get; }=(int)Mathf.Ceil(ClassicNpcLayout.BodyFont.GetHeight(13));
    public int ViewportHeight => 75/LineHeight*LineHeight;
    public ClassicNpcSpeech()
    {
        Name="npc_speech";MouseFilter=MouseFilterEnum.Ignore;
        var frame=new Panel { MouseFilter=MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel",ClassicNpcLayout.InsetBox());AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Identity=ClassicNpcLayout.Text("",12,true,ClassicReportDesign.Caption);Identity.ClipText=true;
        AddChild(Identity);ClassicNpcLayout.Place(Identity,new Rect2(8,7,212,19));
        Text=new RichTextLabel { Name="speech_text",BbcodeEnabled=true,FitContent=false,ScrollActive=true,
            AutowrapMode=TextServer.AutowrapMode.WordSmart,ClipContents=true,MouseFilter=MouseFilterEnum.Stop };
        foreach(string face in new[]{"normal","bold","italics","bold_italics","mono"})
            Text.AddThemeFontOverride(face+"_font",face.StartsWith("bold")?ClassicNpcLayout.HeadingFont:ClassicNpcLayout.BodyFont);
        Text.AddThemeFontSizeOverride("normal_font_size",13);Text.AddThemeFontSizeOverride("bold_font_size",13);
        Text.AddThemeColorOverride("default_color",ClassicReportDesign.Value);
        Text.AddThemeConstantOverride("line_separation",0);Text.AddThemeConstantOverride("outline_size",0);
        Text.AddThemeConstantOverride("scrollbar_separation",4);
        Text.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());Text.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        AddChild(Text);ClassicNpcLayout.Place(Text,new Rect2(8,29,212,ViewportHeight));
        var bar=Text.GetVScrollBar();
        var track=new StyleBoxFlat { BgColor=new Color("171715"),BorderColor=ClassicReportDesign.Caption };
        track.SetBorderWidthAll(1);track.SetContentMarginAll(0);bar.AddThemeStyleboxOverride("scroll",track);
        foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})
        {
            var grab=ClassicDesign.ButtonBox(state=="grabber_highlight"?"hover":state=="grabber_pressed"?"pressed":"normal");
            grab.SetContentMarginAll(0);bar.AddThemeStyleboxOverride(state,grab);
        }
        bar.CustomMinimumSize=new Vector2(12,0);bar.Step=LineHeight;bar.FocusMode=FocusModeEnum.None;
        Text.GuiInput+=input=>
        {
            if(input is not InputEventMouseButton { Pressed:true } wheel || wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
            bar.Value+=(wheel.ButtonIndex==MouseButton.WheelUp?-3:3)*LineHeight;Text.AcceptEvent();
        };
    }
    public void ShowContent(string name,string text)
    {
        Identity.Text=name;Identity.TooltipText=name;Text.Text=text;Text.GetVScrollBar().Value=0;
    }
}
