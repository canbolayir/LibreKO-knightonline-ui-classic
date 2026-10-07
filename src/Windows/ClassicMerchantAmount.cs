using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public static class ClassicMerchantAmount
{
    public static void Apply(CanvasLayer layer)
    {
        if(layer.HasMeta("classic_merchant_amount"))return;layer.SetMeta("classic_merchant_amount",true);
        var centre=layer.GetChildren().OfType<CenterContainer>().Single();
        var nodes=CharacterDetailsSkin.Tree(centre).ToArray();
        var spin=nodes.OfType<SpinBox>().Single();var price=nodes.OfType<MoneyEdit>().Single();
        var confirm=nodes.OfType<Button>().Single(b=>b.Text=="Yes");var cancel=nodes.OfType<Button>().Single(b=>b.Text=="Cancel");
        var labels=nodes.OfType<Label>().ToArray();var hint=labels.First(l=>l.GetThemeFontSize("font_size")==11);
        centre.Visible=false;layer.GetChildren().OfType<ColorRect>().Single().Color=Colors.Transparent;
        var marketHint=nodes.OfType<Label>().SingleOrDefault(l=>l.Name=="merchant_market_hint");
        var marketButton=nodes.OfType<Button>().SingleOrDefault(b=>b.Name=="merchant_market_history");
        var panel=new ClassicMerchantAmountPanel(layer,spin,price,confirm,cancel,hint,marketHint,marketButton);layer.AddChild(panel);
    }
}
public partial class ClassicMerchantAmountPanel : Control
{
    private readonly CanvasLayer _layer;
    private readonly HudWindow[] _owners;
    private readonly SpinBox _spin;
    private readonly LineEdit _count;
    private readonly MoneyEdit _price;
    private readonly Button _nativeConfirm,_nativeCancel,_ok=new(),_cancel=new();
    private readonly Label _nativeHint,_message=new(),_detail=new();
    private LayoutNode _art=null!;
    private int _stage;
    private bool _buying;
    private readonly Label? _marketHint;
    private readonly Button? _marketButton;
    private bool _marketVisible;
    public ClassicMerchantAmountPanel(CanvasLayer layer,SpinBox spin,MoneyEdit price,Button confirm,Button cancel,Label hint,Label? marketHint=null,Button? marketButton=null)
    {
        _owners=CharacterDetailsSkin.Tree(layer.GetParent()).OfType<HudWindow>().Where(w=>w.Id is "wishfind" or "sellstall" or "shop" or "wishlist" or "wantedstall").ToArray();
        _layer=layer;_spin=spin;_count=spin.GetLineEdit();_price=price;_nativeConfirm=confirm;_nativeCancel=cancel;_nativeHint=hint;
        Name="classic_merchant_amount";Size=CustomMinimumSize=new Vector2(255,106);TextureFilter=TextureFilterEnum.Nearest;
        ClassicVendorSkin.Move(_price,this,new Rect2(69,43,151,17));ClassicVendorSkin.Move(_count,this,new Rect2(54,41,151,17));
        foreach(var edit in new LineEdit[]{_price,_count}){ClassicMerchantSkin.Font(edit);foreach(var state in new[]{"normal","focus"})edit.AddThemeStyleboxOverride(state,new StyleBoxEmpty());edit.AddThemeColorOverride("font_color",new Color("ffff80"));}
        AddChild(_detail);ClassicMerchantSkin.Font(_detail);_detail.HorizontalAlignment=HorizontalAlignment.Center;_detail.Position=new Vector2(49,22);_detail.Size=new Vector2(189,16);_detail.Text="Price for each item";_detail.AddThemeColorOverride("font_color",new Color("efd9b4"));
        AddChild(_message);ClassicMerchantSkin.Font(_message);_message.HorizontalAlignment=HorizontalAlignment.Center;_message.VerticalAlignment=VerticalAlignment.Center;_message.AutowrapMode=TextServer.AutowrapMode.WordSmart;_message.ClipContents=true;
        AddChild(_ok);AddChild(_cancel);_ok.Pressed+=Advance;_cancel.Pressed+=()=>_nativeCancel.EmitSignal(BaseButton.SignalName.Pressed);
        layer.VisibilityChanged+=()=>{if(layer.Visible)Callable.From(Begin).CallDeferred();};
        _marketHint=marketHint;_marketButton=marketButton;
        if(_marketHint!=null){ClassicVendorSkin.Move(_marketHint,this,new Rect2(16,61,223,18));ClassicMerchantSkin.Font(_marketHint);_marketHint.HorizontalAlignment=HorizontalAlignment.Center;_marketHint.ClipText=true;}
        if(_marketButton!=null){ClassicVendorSkin.Move(_marketButton,this,new Rect2(69,82,117,25));_marketButton.FocusMode=FocusModeEnum.None;}
    }
    private bool Editable=>_layer.GetMeta("merchant_price_editable",false).AsBool();
    private bool Quantity=>_layer.GetMeta("merchant_quantity",false).AsBool();
    private void Begin(){var owner=_owners.Where(w=>IsInstanceValid(w) && w.IsVisibleInTree()).OrderByDescending(w=>w.Id=="wishfind").FirstOrDefault();_buying=owner?.Id is "wishlist" or "wishfind" or "wantedstall";_stage=Editable?0:Quantity?1:2;Build();}
    private void Build()
    {
        _art=Plugin.Kit.Layout(_stage==0?"co_tradeprice_us":_stage==1?"co_tradecount_us":"co_trademessagebox_us");
        _marketVisible=_stage==0 && _layer.GetMeta("merchant_market_available",false).AsBool() && _marketHint!=null && _marketButton!=null;
        Size=CustomMinimumSize=new Vector2(255,_marketVisible?158:106);
        if(_marketHint!=null)_marketHint.Visible=_marketVisible;
        if(_marketButton!=null){if(_marketVisible)ClassicMerchantSkin.Button(_marketButton,_art.Find("btn_ok")!,this,new Rect2(69,82,117,25),"Market Price",_buying);_marketButton.Visible=_marketVisible;}
        _price.Visible=_stage==0;_count.Visible=_stage==1;
        _detail.Visible=_stage==0;
        _message.AddThemeColorOverride("font_color",_buying?ClassicMerchantSkin.MerchantHeading(true):new Color("efd9b4"));
        _detail.AddThemeColorOverride("font_color",_buying?ClassicMerchantSkin.MerchantHeading(true):new Color("efd9b4"));
        _message.Text=_stage==0?"Please enter the item's price.":_stage==1?"Please enter the quantity of the item.":( _nativeHint.Text.StartsWith("Buy ")?"Buy this item?":"Sell these items?")+"\nQuantity: "+(Quantity?_count.Text:"1")+"\nTotal: "+(_price.Value*(Quantity && int.TryParse(_count.Text,out int qty)?qty:1)).ToString("n0")+" coins";
        _message.AutowrapMode=_stage==2?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off;
        _message.Position=_stage==2?new Vector2(22,16):_stage==0?new Vector2(21,4):new Vector2(10,20);
        _message.Size=_stage==2?new Vector2(209,48):_stage==0?new Vector2(224,16):new Vector2(240,18);
        ClassicMerchantSkin.Button(_ok,_art.Find("btn_ok")!,this,new Rect2(18,_marketVisible?124:72,71,25),_stage==2?"Yes":"OK",_buying);
        ClassicMerchantSkin.Button(_cancel,_art.Find("btn_cancel")!,this,new Rect2(171,_marketVisible?124:72,71,25),_stage==2?"No":"Cancel",_buying);
        if(_stage==0){_price.GrabFocus();_price.SelectAll();}else if(_stage==1){_count.GrabFocus();_count.SelectAll();}
        QueueRedraw();
    }
    private void Error(string text){_message.Text=text;_message.AddThemeColorOverride("font_color",new Color("ff6a6a"));}
    private void Advance()
    {
        if(_stage==0 && _price.Value<1){Error("Enter a valid price.");return;}
        if(_stage==1 && (!int.TryParse(_count.Text,out int amount) || amount<1 || amount>_spin.MaxValue)){Error("Enter a valid quantity.");return;}
        if(_stage==0 && Quantity){_stage=1;Build();return;}
        if(_stage==1 && !Editable){_stage=2;Build();return;}
        _nativeConfirm.EmitSignal(BaseButton.SignalName.Pressed);
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;
        if(_stage==0 && _marketVisible!=(_layer.GetMeta("merchant_market_available",false).AsBool() && _marketHint!=null && _marketButton!=null))Build();
        var owner=_owners.Where(w=>IsInstanceValid(w) && w.IsVisibleInTree()).OrderByDescending(w=>w.Id=="wishfind").FirstOrDefault();
        var center=owner?.GetGlobalRect().GetCenter() ?? GetViewportRect().Size/2;
        Position=(center-Size/2).Round().Clamp(Vector2.Zero,(GetViewportRect().Size-Size).Max(Vector2.Zero));
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);if(_art==null)return;
        foreach(var image in _art.Images)
        {
            var texture=Plugin.Kit.Texture(image.Texture!);var tint=_buying?ClassicMerchantSkin.MerchantAccent(true):Colors.White;
            if(!_marketVisible || image.W!=255 || image.H!=106){DrawTextureRectRegion(texture,new Rect2(image.Position,image.SizeVec),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH),tint);continue;}
            // Extend the straight frame rails between the price field and original footer.
            DrawTextureRectRegion(texture,new Rect2(0,0,255,60),new Rect2(image.SrcX,image.SrcY,255,60),tint);
            DrawTextureRectRegion(texture,new Rect2(0,60,255,52),new Rect2(image.SrcX,image.SrcY+59,255,1),tint);
            DrawTextureRectRegion(texture,new Rect2(0,112,255,46),new Rect2(image.SrcX,image.SrcY+60,255,46),tint);
        }
    }
    public override void _Input(InputEvent ev){if(!IsVisibleInTree() || _layer.GetMeta("merchant_market_open",false).AsBool() || ev is not InputEventKey {Pressed:true,Echo:false} key)return;if(key.Keycode==Key.Escape)_cancel.EmitSignal(BaseButton.SignalName.Pressed);else if(key.Keycode is Key.Enter or Key.KpEnter){if(_stage!=2)Advance();}else return;GetViewport().SetInputAsHandled();}
}
