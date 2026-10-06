using Godot;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;
public partial class ClassicMerchantNotice : Control
{
    private readonly DialogRequest _request;
    private readonly Control _panel=new();
    private readonly Label _message=new();
    private readonly LayoutNode _art=Plugin.Kit.Layout("co_trademessagebox_us");
    public ClassicMerchantNotice(DialogRequest request){
        _request=request;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);MouseFilter=MouseFilterEnum.Stop;
        var blocker=new ColorRect {Color=Colors.Transparent,MouseFilter=MouseFilterEnum.Stop};blocker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(blocker);
        AddChild(_panel);_panel.Size=new Vector2(255,106);_panel.Draw+=()=>{_panel.DrawRect(new Rect2(Vector2.Zero,_panel.Size),Colors.Black);foreach(var image in _art.Images)_panel.DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(image.Position,image.SizeVec),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));};
        _panel.AddChild(_message);_message.Position=new Vector2(22,16);_message.Size=new Vector2(209,48);_message.Text=request.Message;_message.AutowrapMode=TextServer.AutowrapMode.WordSmart;_message.HorizontalAlignment=HorizontalAlignment.Center;_message.VerticalAlignment=VerticalAlignment.Center;ClassicMerchantSkin.Font(_message);
        var yes=new Button();_panel.AddChild(yes);ClassicMerchantSkin.Button(yes,_art.Find("btn_ok")!,_panel,new Rect2(18,72,71,25),request.ConfirmText);yes.Pressed+=()=>{if(request.HasCancel)request.Confirm();else request.Dismiss();};
        var no=new Button();_panel.AddChild(no);ClassicMerchantSkin.Button(no,_art.Find("btn_cancel")!,_panel,new Rect2(171,72,71,25),request.CancelText??"Cancel");no.Pressed+=request.Cancel;no.Visible=request.HasCancel;
        request.MessageChanged+=UpdateMessage;
    }
    private void UpdateMessage(string text)=>_message.Text=text;
    public override void _ExitTree()=>_request.MessageChanged-=UpdateMessage;
    public override void _Process(double delta)=>_panel.Position=((GetViewportRect().Size-_panel.Size)/2).Round();
    public override void _Input(InputEvent ev){if(ev is not InputEventKey {Pressed:true,Echo:false} key)return;if(key.Keycode==Key.Escape){if(_request.HasCancel)_request.Cancel();else _request.Dismiss();}else if(key.Keycode is not (Key.Enter or Key.KpEnter))return;GetViewport().SetInputAsHandled();}
}
