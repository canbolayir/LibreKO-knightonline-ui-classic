using Godot;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

public static class ClassicExchangeAmount
{
    public static void Apply(CanvasLayer layer,Control shop)
    {
        if(layer.HasMeta("classic_exchange_amount"))return;
        layer.SetMeta("classic_exchange_amount",true);
        var centre=layer.GetChildren().OfType<CenterContainer>().Single();
        var nodes=CharacterDetailsSkin.Tree(centre).ToArray();
        var spin=nodes.OfType<SpinBox>().Single();
        var edit=spin.GetLineEdit();
        var icon=nodes.OfType<TextureRect>().Single();
        var labels=nodes.OfType<Label>().ToArray();
        var ok=nodes.OfType<Button>().Single(b=>b.Text=="Offer");
        var cancel=nodes.OfType<Button>().Single(b=>b.Text=="Cancel");
        centre.Visible=false;
        layer.GetChildren().OfType<ColorRect>().Single().Color=Colors.Transparent;
        var panel=new ClassicExchangeAmountPanel {Name="classic_exchange_quantity",Size=ClassicQuantityLayout.Size,Shop=shop,Confirm=ok,Cancel=cancel};layer.AddChild(panel);
        panel.AddChild(new ClassicQuantityFrame {Size=panel.Size});
        var message=ClassicNpcLayout.Text("Please enter the quantity of the item.",13,true,new Color("c0c0c0"));
        panel.Message=message;panel.Layer=layer;panel.AddChild(message);ClassicNpcLayout.Place(message,ClassicQuantityLayout.Message);message.HorizontalAlignment=HorizontalAlignment.Center;
        ClassicVendorSkin.Move(icon,panel,ClassicQuantityLayout.Icon);
        ClassicVendorSkin.Move(edit,panel,ClassicQuantityLayout.Amount);
        edit.AddThemeFontOverride("font",Plugin.Kit.Bold);edit.AddThemeFontSizeOverride("font_size",13);
        edit.AddThemeColorOverride("font_color",new Color("ffff00"));edit.AddThemeColorOverride("caret_color",new Color("ffff00"));
        foreach(var state in new[]{"normal","focus","read_only"})edit.AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        ClassicVendorSkin.Move(ok,panel,ClassicQuantityLayout.Confirm);ClassicTradeQuantity.StyleButton(ok,"ok");ok.Text=ClassicDesign.Karus?"O K":"O  K";
        ClassicVendorSkin.Move(cancel,panel,ClassicQuantityLayout.Cancel);ClassicTradeQuantity.StyleButton(cancel,"cancel");
        void UpdateMessage() {
            if(!layer.Visible)return;
            icon.TooltipText=labels.First(l=>!string.IsNullOrWhiteSpace(l.Text) && l.Text!="Quantity").Text;
            if(layer.HasMeta("ex_gold") && layer.GetMeta("ex_gold").AsBool()) {
                var coin=Plugin.Kit.Layout("el_inventory_us").Find("img_gold") ?? Plugin.Kit.Layout("el_personaltrade_us").Images.First(n=>n.Id.Contains("gold"));
                icon.Texture=new AtlasTexture {Atlas=Plugin.Kit.Texture(coin.Texture!),Region=new Rect2(coin.SrcX,coin.SrcY,coin.SrcW,coin.SrcH),FilterClip=true};
                message.Text="Please enter the amount of coins.";
            } else message.Text="Please enter the quantity of the item.";
        }
        layer.VisibilityChanged+=()=>Callable.From(UpdateMessage).CallDeferred();
    }
}
public partial class ClassicExchangeAmountPanel : Control
{
    public Control Shop=null!;
    public Label Message=null!;
    public CanvasLayer Layer=null!;
    public Button Confirm=null!,Cancel=null!;
    public override void _Process(double delta) {
        if(IsVisibleInTree()) {
            string error=Layer.HasMeta("ex_amount_error")?Layer.GetMeta("ex_amount_error").AsString():"";
            Message.Text=error.Length>0?error:Layer.HasMeta("ex_gold") && Layer.GetMeta("ex_gold").AsBool()?"Please enter the amount of coins.":"Please enter the quantity of the item.";
            Message.AddThemeColorOverride("font_color",error.Length>0?new Color("ff6a6a"):new Color("c0c0c0"));
            Position=(Shop.GetGlobalRect().GetCenter()-Size/2).Round().Clamp(Vector2.Zero,(GetViewportRect().Size-Size).Max(Vector2.Zero));
        }
    }
    public override void _Input(InputEvent ev) {
        if(!IsVisibleInTree() || ev is not InputEventKey {Pressed:true,Echo:false} key)return;
        if(key.Keycode==Key.Escape)Cancel.EmitSignal(BaseButton.SignalName.Pressed);
        else if(key.Keycode is Key.Enter or Key.KpEnter)Confirm.EmitSignal(BaseButton.SignalName.Pressed);
        else return;
        GetViewport().SetInputAsHandled();
    }
}
