using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;
/// <summary>Original common message artwork with native trade request and cancellation callbacks.</summary>
public static class ClassicExchangeNotice
{
    public static void Apply(CanvasLayer layer)
    {
        if(layer.HasMeta("classic_exchange_notice"))return;
        layer.SetMeta("classic_exchange_notice",true);
        var centre=layer.GetChildren().OfType<CenterContainer>().Single();
        var nodes=CharacterDetailsSkin.Tree(centre).ToArray();
        var message=nodes.OfType<Label>().Single();
        bool final=layer.HasMeta("exchange_final");
        bool request=layer.HasMeta("exchange_request") || final;
        var cancel=nodes.OfType<Button>().Single(b=>b.Text is "Cancel" or "Decline");
        var accept=request?nodes.OfType<Button>().Single(b=>b.Text=="Accept"):null;
        centre.Visible=false;
        if(!layer.GetChildren().OfType<ColorRect>().Any()) {
            var blocker=new ColorRect {Color=Colors.Transparent,MouseFilter=Control.MouseFilterEnum.Stop};
            blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);layer.AddChild(blocker);
        }
        var panel=new ClassicExchangeNoticePanel {Name=final?"classic_exchange_final":request?"classic_exchange_request":"classic_exchange_wait",Layer=layer,Cancel=cancel,Size=ClassicExchangeNoticeLayout.Size(request),Request=request};layer.AddChild(panel);
        ClassicVendorSkin.Move(message,panel,final?ClassicExchangeNoticeLayout.FinalPrompt:ClassicExchangeNoticeLayout.Message(request));
        ClassicVendorSkin.Font(message,12);message.ClipText=false;message.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        message.HorizontalAlignment=HorizontalAlignment.Center;message.AddThemeColorOverride("font_color",new Color("ffff80"));
        var art=Plugin.Kit.Layout("{nation}_messagebox_us");
        var title=ClassicPartySkin.Caption(panel,final?"Confirm trade":request?"Trade request":"Trade",ClassicExchangeNoticeLayout.Title,new Color("c0c0c0"),12);
        title.HorizontalAlignment=HorizontalAlignment.Center;
        if(final) {
            title.Visible=false;
            message.Text="Are you sure you want to trade?";
            message.Position=ClassicExchangeNoticeLayout.FinalPrompt.Position;
            message.Size=ClassicExchangeNoticeLayout.FinalPrompt.Size;
            message.Name="final_prompt";
            message.VerticalAlignment=VerticalAlignment.Center;
            message.AddThemeColorOverride("font_color",new Color("fbeec8"));
        }
        foreach(var label in new[]{title,message}) {
            label.AddThemeFontOverride("font",Plugin.Kit.Bold);
            label.AddThemeFontSizeOverride("font_size",12);
        }
        void Place(Button button,string source,string text,Rect2 rect) {
            ClassicVendorSkin.Move(button,panel,rect);ClassicVendorSkin.Button(button,art.Find(source));
            button.Text=text;button.FocusMode=Control.FocusModeEnum.None;button.Shortcut=null;
            button.AddThemeFontOverride("font",Plugin.Kit.Bold);button.AddThemeFontSizeOverride("font_size",12);
            foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})button.AddThemeColorOverride(state,new Color("fbeec8"));
        }
        if(accept!=null)Place(accept,"btn_yes","Yes",final?ClassicExchangeNoticeLayout.FinalAccept:ClassicExchangeNoticeLayout.Accept);
        Place(cancel,request?"btn_no":"btn_cancel",request?"No":"Cancel",final?ClassicExchangeNoticeLayout.FinalDecline:request?ClassicExchangeNoticeLayout.Decline:ClassicExchangeNoticeLayout.WaitCancel);
    }
}
public partial class ClassicExchangeNoticePanel : Control
{
    public Button Cancel=null!;
    public bool Request;
    public CanvasLayer Layer=null!;
    public ClassicExchangeNoticePanel(){MouseFilter=MouseFilterEnum.Stop;TextureFilter=TextureFilterEnum.Nearest;}
    public override void _Process(double delta) {
        if(!IsVisibleInTree())return;
        Position=((GetViewportRect().Size-Size)/2).Round();

    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);
        foreach(var image in Plugin.Kit.Layout("{nation}_messagebox_us").Images)
            DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(image.Position,image.SizeVec),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));
    }
    public override void _Input(InputEvent ev) {
        if(!IsVisibleInTree() || ev is not InputEventKey {Pressed:true,Echo:false} key)return;
        if(key.Keycode==Key.Escape)Cancel.EmitSignal(BaseButton.SignalName.Pressed);
        else if(key.Keycode is not (Key.Enter or Key.KpEnter))return;
        // Trade permission and final decisions require an explicit mouse click.
        GetViewport().SetInputAsHandled();
    }
}
