using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using ClassicSlot = KnightOnlineUiClassic.Layout.ItemSlot;
using TradeItem = KnightOnlineUiClassic.Native.NativeTrade.TradeItem;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicExchangeSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(()=>Apply(body)).CallDeferred();
    public static ClassicExchangePanel? Apply(Control body)
    {
        Node? node=body; while(node!=null && node is not HudWindow) node=node.GetParent();
        if(node is not HudWindow window || window.HasMeta("classic_exchange")) return null;
        window.SetMeta("classic_exchange",true);
        var grip=window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close=grip.GetChildren().OfType<Button>().Single(b=>b.TooltipText=="Close");
        foreach(var child in window.GetChildren().OfType<Control>()) child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        var panel=new ClassicExchangePanel(window,body,grip,close); window.AddChild(panel); window.ResetSize();
        Node root=window; while(root.GetParent()!=null && root is not WorldRoot) root=root.GetParent();
        foreach(var layer in CharacterDetailsSkin.Tree(root).OfType<CanvasLayer>().Where(l=>l.HasMeta("exchange_amount"))) ClassicExchangeAmount.Apply(layer,window);
        foreach(var layer in CharacterDetailsSkin.Tree(root).OfType<CanvasLayer>().Where(l=>l.HasMeta("exchange_request") || l.HasMeta("exchange_wait") || l.HasMeta("exchange_final"))) ClassicExchangeNotice.Apply(layer);
        return panel;
    }
}

/// <summary>
/// The original personal trade: both offer grids, the carried inventory grid and the coin plaques on the
/// nation artwork. The offers and the carried items come from the client's exchange state; offering, the
/// quantity prompt, refusals and the pending state stay native.
/// </summary>
public partial class ClassicExchangePanel : Control
{
    public readonly HudWindow Window;
    private WorldRoot? _world => KnightOnlineUiClassic.Native.NativeTrade.WorldOf(Window);
    private readonly LayoutNode _art;
    private readonly Label _myName, _theirName, _myGold, _theirGold, _wallet;
    private readonly Button _confirm, _otherConfirm, _gold;
    private readonly Control _myGrid=new(), _otherGrid=new();
    private readonly ClassicExchangeCell[] _bagCells;
    private string _shown="";
    private static readonly StringName LockedMeta="ex_locked",PartnerLockedMeta="ex_partner_locked",PendingMeta="ex_pending",ModalMeta="ex_modal";
    private static readonly StringName SelfMeta="ex_self",PartnerMeta="ex_partner",MyGoldMeta="ex_my_gold",OtherGoldMeta="ex_other_gold",WalletMeta="ex_wallet";
    private bool Flag(StringName meta)=>Window.HasMeta(meta) && Window.GetMeta(meta).AsBool();
    public bool Locked => Flag(LockedMeta);
    public bool PartnerLocked => Flag(PartnerLockedMeta);
    public bool OfferBlocked => Blocked || PartnerLocked;
    public bool Blocked => Locked || Flag(PendingMeta) || Flag(ModalMeta);
    public ClassicExchangePanel(HudWindow window,Control body,Control grip,Button close)
    {
        Window=window; Name="classic_exchange"; CustomMinimumSize=ClassicExchangeLayout.Size; Size=CustomMinimumSize;
        TextureFilter=TextureFilterEnum.Nearest; MouseFilter=MouseFilterEnum.Stop;
        _art=Plugin.Kit.Layout("{nation}_personaltrade_us");
        var nodes=CharacterDetailsSkin.Tree(body).ToArray();
        ClassicPartySkin.Drag(this,grip,ClassicExchangeLayout.Drag);
        var title=Text("Trade",ClassicExchangeLayout.Title,13); title.HorizontalAlignment=HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color",new Color("efd9b4"));
        _myName=Text("You",ClassicExchangeLayout.MyName,13); _theirName=Text("Player",ClassicExchangeLayout.TheirName,13);
        foreach(var label in new[]{_myName,_theirName}) { label.HorizontalAlignment=HorizontalAlignment.Center; label.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis; }
        _myGold=Text("0",ClassicExchangeLayout.MyGold); _theirGold=Text("0",ClassicExchangeLayout.TheirGold); _wallet=Text("0",ClassicExchangeLayout.Wallet);
        foreach(var label in new[]{_myGold,_theirGold,_wallet}) label.HorizontalAlignment=HorizontalAlignment.Right;
        Button Native(string text)=>nodes.OfType<Button>().Single(b=>b.Text==text);
        ClassicPartySkin.Place(this,close,_art.Find("btn_close")!,"");
        _confirm=Native("Confirm"); _confirm.FocusMode=FocusModeEnum.None; _confirm.Shortcut=null; ClassicPartySkin.Place(this,_confirm,_art.Find("btn_trade_my")!,"Trade"); ShiftButton(_confirm);
        _otherConfirm=new Button { Text="Trade",FocusMode=FocusModeEnum.None,MouseFilter=MouseFilterEnum.Ignore }; AddChild(_otherConfirm);
        ClassicPartySkin.Place(this,_otherConfirm,_art.Find("btn_trade_other")!); ShiftButton(_otherConfirm);
        _otherConfirm.TooltipText="Partner confirmation";
        _gold=Native("Add gold"); ClassicPartySkin.Place(this,_gold,_art.Find("btn_gold")!,""); ShiftButton(_gold); _gold.TooltipText="Offer coins";
        foreach(var button in new[]{_confirm,_otherConfirm,_gold}) {
            button.AddThemeFontOverride("font",Plugin.Kit.Bold);button.AddThemeFontSizeOverride("font_size",12);
            // A confirmed side stays legible on the Human blue artwork; Karus keeps its dark ink.
            if(!ClassicDesign.Karus) button.AddThemeColorOverride("font_disabled_color",new Color("d8c58c"));
        }
        // The native status label remains the source of rejection and completion messages.
        var status=nodes.OfType<Label>().Single(l=>l.Name=="exchange_status");
        status.AddThemeFontOverride("font",Plugin.Kit.Bold); status.AddThemeFontSizeOverride("font_size",12);
        ClassicVendorSkin.Move(status,this,ClassicExchangeLayout.Status); status.HorizontalAlignment=HorizontalAlignment.Center;
        status.ClipText=true; status.AutowrapMode=TextServer.AutowrapMode.Off;
        status.TextOverrunBehavior=TextServer.OverrunBehavior.NoTrimming; status.ClipContents=true;
        MakeOfferViewport(_myGrid,ClassicExchangeLayout.Mine,true); MakeOfferViewport(_otherGrid,ClassicExchangeLayout.Theirs,false);
        _bagCells=new ClassicExchangeCell[28];
        for(int i=0;i<28;i++) {var cell=new ClassicExchangeCell(this,true,i);_bagCells[i]=cell;AddChild(cell);cell.Position=ClassicExchangeLayout.BagCell(i).Position;cell.Size=ClassicExchangeLayout.BagCell(i).Size;}
        Resized+=()=>Size=CustomMinimumSize;
        ShownOnly.Gate(this,Sync);
    }
    private Label Text(string text,Rect2 rect,int size=12) {
        var label=ClassicPartySkin.Caption(this,text,rect,ClassicDesign.Karus?new Color("c0c0c0"):new Color("f8d987"),12);
        label.AddThemeFontOverride("font",Plugin.Kit.Bold);return label;
    }
    private static void ShiftButton(Button button)
    {
        button.Position += new Vector2(0,49);
        var rect=button.GetMeta("party_expected_rect").AsRect2(); rect.Position+=new Vector2(0,49);
        button.SetMeta("party_expected_rect",rect);
    }
    private void MakeOfferViewport(Control grid,Rect2 rect,bool mine)
    {
        grid.Name=mine?"trade_my_offer":"trade_other_offer";
        grid.Position=rect.Position; grid.Size=rect.Size; grid.CustomMinimumSize=rect.Size; grid.ClipContents=true;
        AddChild(grid);
        if(mine) {var target=new ClassicExchangeDrop(this) {Size=rect.Size};grid.AddChild(target);}
        for(int i=0;i<12;i++) {
            var cell=new ClassicExchangeCell(this,false,i,mine);grid.AddChild(cell);var cellRect=ClassicExchangeLayout.OfferCell(i);cell.Position=cellRect.Position;cell.Size=cellRect.Size;
        }
    }
    public override void _Ready()=>Refresh();
    /// <summary>A key of everything the grids show; the grids rebind only when it changes.</summary>
    private string Shown()
    {
        if(_world==null) return "";
        var parts=new System.Text.StringBuilder();
        foreach(var list in new[]{KnightOnlineUiClassic.Native.NativeTrade.Offers(_world,true),KnightOnlineUiClassic.Native.NativeTrade.Offers(_world,false),KnightOnlineUiClassic.Native.NativeTrade.Bag(_world)}) {
            foreach(var item in list) parts.Append(item.Slot).Append(':').Append(item.ItemId).Append('x').Append(item.Count).Append(',');
            parts.Append('|');
        }
        return parts.ToString();
    }
    private void Refresh()
    {
        if(!IsInsideTree() || _world==null) return;
        _shown=Shown();
        var bag=KnightOnlineUiClassic.Native.NativeTrade.Bag(_world);
        int first=Inventory.GridStart;
        for(int i=0;i<_bagCells.Length;i++) {
            var held=bag.Where(b=>b.Slot==first+i).ToArray();
            _bagCells[i].Bind(held.Length>0?held[0]:null,first+i);
        }
        FillOffers(_myGrid,KnightOnlineUiClassic.Native.NativeTrade.Offers(_world,true));
        FillOffers(_otherGrid,KnightOnlineUiClassic.Native.NativeTrade.Offers(_world,false));
    }
    /// <summary>Repeated countable items share one cell with their total, as the server counts the offer.</summary>
    private static void FillOffers(Control grid,IReadOnlyList<TradeItem> offers)
    {
        var groups=offers.Select((o,i)=>(o,i)).GroupBy(e=>(ItemData.Get(e.o.ItemId)?.Countable ?? 0)!=0?e.o.ItemId:-1-e.i).ToArray();
        var cells=grid.GetChildren().OfType<ClassicExchangeCell>().ToArray();
        for(int i=0;i<cells.Length;i++)
            cells[i].Bind(i<groups.Length?groups[i].First().o:null,-1,i<groups.Length?groups[i].Sum(e=>e.o.Count):0);
    }
    public TradeItem? BagItem(int abs)=>_world==null?null:KnightOnlineUiClassic.Native.NativeTrade.Bag(_world).Where(b=>b.Slot==abs).Select(b=>(TradeItem?)b).FirstOrDefault();
    /// <summary>Offers a bag slot; a part picked with the mouse wheel is offered at once, the whole stack asks the count.</summary>
    public void Offer(int abs,int? picked=null)
    {
        if(OfferBlocked || _world==null || BagItem(abs) is not { } item)return;
        KnightOnlineUiClassic.Native.NativeTrade.Offer(_world,abs,ClassicStackDial.Part(picked,item.Count));
    }
    /// <summary>Offers the bag slot a drop carries, with the amount picked on the drag.</summary>
    public void OfferDrop(Variant data)=>Offer(data.AsGodotDictionary()["invFrom"].AsInt32(),ClassicStackDial.Picked(data));
    public void Tooltip(TradeItem? item,bool bag,bool over)
    {
        if(_world==null) return;
        if(over && item is { } shown) KnightOnlineUiClassic.Native.NativeTrade.Tooltip(_world,shown,bag);
        else KnightOnlineUiClassic.Native.NativeTrade.HideTooltip(_world);
    }
    public bool ValidDrop(Variant data)
    {
        if(OfferBlocked || data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();
        return d.ContainsKey("invFrom") && d.ContainsKey("id") && BagItem(d["invFrom"].AsInt32()) is { } item && item.ItemId==d["id"].AsInt32();
    }
    public override void _Input(InputEvent ev)
    {
        if(IsVisibleInTree() && ev is InputEventKey {Pressed:true} key && key.Keycode is Key.Enter or Key.KpEnter
            && !Flag(ModalMeta))
            GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta)=>Sync();
    /// <summary>Grids, names, coins and confirmations from the trade state; runs every frame while shown.</summary>
    private void Sync()
    {
        if(Shown()!=_shown) Refresh();
        string Meta(StringName name,string fallback)=>Window.HasMeta(name)?Window.GetMeta(name).AsString():fallback;
        string Money(StringName key)=>Window.HasMeta(key)?Window.GetMeta(key).AsInt64().ToString("n0"):"0";
        _myName.Text=Meta(SelfMeta,"You");_theirName.Text=Meta(PartnerMeta,"Player");
        _myGold.Text=Money(MyGoldMeta);_theirGold.Text=Money(OtherGoldMeta);_wallet.Text=Money(WalletMeta);
        _confirm.Disabled=Blocked;_confirm.Text=Locked?"Confirmed":"Trade";
        bool other=PartnerLocked;_otherConfirm.Disabled=other;_otherConfirm.Text=other?"Confirmed":"Trade";
        _gold.Disabled=OfferBlocked;
        foreach(var cell in _bagCells) {if(cell.InputEnabled==!OfferBlocked)continue;cell.InputEnabled=!OfferBlocked;cell.Refresh();}
    }
    internal StyleBoxTexture OfferSocket()
    {
        var source=_art.Images.First();return new StyleBoxTexture {Texture=Plugin.Kit.Texture(source.Texture!),RegionRect=new Rect2(source.SrcX+20,source.SrcY+60,50,50),DrawCenter=true};
    }
    public override void _Draw()
    {
        // Fill only behind the opaque artwork; the lower corners of the Inventory part are transparent.
        DrawRect(new Rect2(1,39,362,(ClassicDesign.Karus?559:555)+49-39),Colors.Black);
        foreach(var image in _art.Images.Where(n=>!n.Id.Contains("gold")))
        {
            var rect=new Rect2(image.Position,image.SizeVec);
            if(image.Y>=(ClassicDesign.Karus?200:199))rect.Position+=new Vector2(0,49);
            DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),rect,new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));
            if(image.Y==(ClassicDesign.Karus?151:150)) DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(image.X,image.Y+49,image.W,49),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));
        }
        DrawRect(new Rect2(21,99,149,198),Colors.Black);DrawRect(new Rect2(206,99,149,198),Colors.Black);
        var coin=Plugin.Kit.Layout("el_inventory_us").Find("img_gold");
        coin ??= Plugin.Kit.Layout("el_personaltrade_us").Images.First(n=>n.Id.Contains("gold"));
        foreach(var rect in new[]{ClassicExchangeLayout.MyCoin,ClassicExchangeLayout.TheirCoin,ClassicExchangeLayout.WalletCoin}) DrawTextureRectRegion(Plugin.Kit.Texture(coin.Texture!),rect,new Rect2(coin.SrcX,coin.SrcY,coin.SrcW,coin.SrcH));
    }
}

public partial class ClassicExchangeCell : ClassicSlot
{
    private readonly ClassicExchangePanel _owner;
    private readonly bool _bag, _acceptsOffer;
    private TradeItem? _item;
    private readonly StyleBoxTexture? _socket;
    public ClassicExchangeCell(ClassicExchangePanel owner,bool bag,int index,bool acceptsOffer=false):base(index)
    {
        _owner=owner;_bag=bag;_acceptsOffer=acceptsOffer;Name=(bag?"exchange_bag_":"exchange_offer_")+index;
        if(!bag)_socket=owner.OfferSocket();
        OnActivate=bag?slot=>owner.Offer(slot):null;OnDoubleClick=OnActivate;
        OnHover=(_,over)=>_owner.Tooltip(_item,_bag,over);
        PicksAmount=bag;
    }
    public TradeItem? Item => _item;
    /// <summary>Shows an item (or an empty cell); bag cells keep their bag slot, offer cells the item's source slot.</summary>
    public void Bind(TradeItem? item,int slot,int total=0)
    {
        _item=item;Slot=slot>=0?slot:item?.Slot ?? -1;
        Source=_=>item is not { } shown?GameItem.Empty(Slot):new GameItem(Slot,shown.ItemId,ItemData.DisplayName(shown.ItemId),total>0?total:shown.Count,shown.Durability,0,0,ItemIcons.Get(shown.ItemId));
        Refresh();
    }
    public override Variant _GetDragData(Vector2 atPosition)=>_bag && !_owner.OfferBlocked?base._GetDragData(atPosition):default;
    public override bool _CanDropData(Vector2 atPosition,Variant data)=>_acceptsOffer && _owner.ValidDrop(data);
    public override void _DropData(Vector2 atPosition,Variant data){if(_CanDropData(atPosition,data))_owner.OfferDrop(data);}
    public override void _Draw(){if(_socket!=null)DrawStyleBox(_socket,new Rect2(Vector2.Zero,Size));}
}

public partial class ClassicExchangeDrop : Control
{
    private readonly ClassicExchangePanel _owner;
    public ClassicExchangeDrop(ClassicExchangePanel owner){_owner=owner;MouseFilter=MouseFilterEnum.Stop;}
    public override bool _CanDropData(Vector2 p,Variant data)=>_owner.ValidDrop(data);
    public override void _DropData(Vector2 p,Variant data){if(_CanDropData(p,data))_owner.OfferDrop(data);}
}
