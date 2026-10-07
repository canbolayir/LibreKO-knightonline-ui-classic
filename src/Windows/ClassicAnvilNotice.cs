using Godot;
using LibreKO.Plugins;
namespace KnightOnlineUiClassic.Windows;

// Original upgrade warning artwork, composed with live modal callbacks.
public partial class ClassicAnvilNotice : Control
{
    private readonly DialogRequest _request;
    private readonly Control _panel=new(){Size=new Vector2(271,105)};
    private readonly Label _message;
    public ClassicAnvilNotice(DialogRequest request)
    {
        _request=request;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var blocker=new ColorRect{Color=new Color(0,0,0,.35f),MouseFilter=MouseFilterEnum.Stop};blocker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(blocker);
        AddChild(_panel);var art=Plugin.Kit.Layout("co_msgboxokcancel_us");
        _panel.Draw+=()=>{_panel.DrawRect(new Rect2(2,2,267,101),Colors.Black);foreach(var image in art.Images)_panel.DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(image.Position,image.SizeVec),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));};
        _message=ClassicPartySkin.Caption(_panel,request.Message,new Rect2(12,13,246,52),new Color("c0c0c0"));
        _message.AutowrapMode=TextServer.AutowrapMode.WordSmart;_message.HorizontalAlignment=HorizontalAlignment.Center;ClassicMerchantSkin.Font(_message);
        foreach(string id in new[]{"btn_ok","btn_cancel"})
        {
            var button=new Button();_panel.AddChild(button);var source=art.Find(id)!;
            ClassicMerchantSkin.Button(button,source,_panel,new Rect2(source.Position,source.SizeVec),id=="btn_ok"?"OK":"Cancel");
            button.FocusMode=FocusModeEnum.None;button.Shortcut=null;
            foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_focus_color"})
                button.AddThemeColorOverride(state,new Color("292621"));
            button.AddThemeColorOverride("font_disabled_color",new Color("68635b"));
            button.AddThemeConstantOverride("outline_size",0);
            foreach(string state in new[]{"normal","hover","pressed","disabled","focus"})if(button.GetThemeStylebox(state) is StyleBoxTexture box)foreach(var side in new[]{Side.Top,Side.Bottom})box.SetContentMargin(side,0);
            if(id=="btn_ok")button.Pressed+=request.Confirm;else button.Pressed+=request.Cancel;
        }
        request.MessageChanged+=UpdateMessage;
    }
    private void UpdateMessage(string text)=>_message.Text=text;
    public override void _ExitTree()=>_request.MessageChanged-=UpdateMessage;
    public override void _Process(double delta)=>_panel.Position=((GetViewportRect().Size-_panel.Size)/2).Round();
    public override void _Input(InputEvent ev)
    {
        if(ev is not InputEventKey{Pressed:true,Echo:false} key)return;
        if(key.Keycode==Key.Escape)_request.Cancel();
        else if(key.Keycode is Key.Enter or Key.KpEnter)_request.Confirm();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
