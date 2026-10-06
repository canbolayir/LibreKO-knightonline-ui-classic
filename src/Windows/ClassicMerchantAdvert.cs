using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;
public partial class ClassicMerchantAdvert : Control
{
    private readonly LayoutNode _art=Plugin.Kit.Layout("co_trademessage_us");
    private Button _cancel=null!;
    private LibreKO.HudWindow? _owner;
    public static void Apply(CanvasLayer layer)
    {
        if(layer.HasMeta("classic_merchant_advert"))return;layer.SetMeta("classic_merchant_advert",true);
        var centre=layer.GetChildren().OfType<CenterContainer>().Single();var nodes=CharacterDetailsSkin.Tree(centre).ToArray();centre.Visible=false;
        var panel=new ClassicMerchantAdvert {Size=new Vector2(364,148),TextureFilter=TextureFilterEnum.Nearest};layer.AddChild(panel);panel._owner=CharacterDetailsSkin.Tree(layer.GetParent()).OfType<LibreKO.HudWindow>().FirstOrDefault(w=>w.Id=="sellstall");
        var label=ClassicPartySkin.Caption(panel,"Merchant mode message",new Rect2(20,4,324,18),new Color("efd9b4"),12);label.HorizontalAlignment=HorizontalAlignment.Center;ClassicMerchantSkin.Font(label);
        var edit=(LineEdit)layer.GetMeta("merchant_advert_edit").AsGodotObject();ClassicVendorSkin.Move(edit,panel,new Rect2(22,31,320,51));ClassicMerchantSkin.Font(edit);edit.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());edit.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
        foreach(var button in nodes.OfType<Button>()){var art=panel._art.Find(button.Text=="OK"?"btn_ok":"btn_cancel")!;ClassicMerchantSkin.Button(button,art,panel,new Rect2(art.Position,art.SizeVec));if(button.Text=="Cancel")panel._cancel=button;}
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;
        var owner=_owner!=null && IsInstanceValid(_owner) && _owner.IsVisibleInTree()?_owner:null;
        var center=owner?.GetGlobalRect().GetCenter() ?? GetViewportRect().Size/2;
        Position=(center-Size/2).Round().Clamp(Vector2.Zero,(GetViewportRect().Size-Size).Max(Vector2.Zero));
    }
    public override void _Draw(){DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);foreach(var part in _art.Images)DrawTextureRectRegion(Plugin.Kit.Texture(part.Texture!),new Rect2(part.Position,part.SizeVec),new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH));}
    public override void _Input(InputEvent ev){if(!IsVisibleInTree() || ev is not InputEventKey {Pressed:true,Echo:false} key)return;if(key.Keycode==Key.Escape)_cancel.EmitSignal(BaseButton.SignalName.Pressed);else if(key.Keycode is not (Key.Enter or Key.KpEnter))return;GetViewport().SetInputAsHandled();}
}
