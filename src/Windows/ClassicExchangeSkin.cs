using Godot;

using LibreKO;

using LibreKO.Domain;

using LibreKO.Plugins;

using KnightOnlineUiClassic.Layout;

using ClassicSlot = KnightOnlineUiClassic.Layout.ItemSlot;



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

        Node root=window; while(root.GetParent()!=null && root is not World) root=root.GetParent();

        foreach(var layer in CharacterDetailsSkin.Tree(root).OfType<CanvasLayer>().Where(l=>l.HasMeta("exchange_amount"))) ClassicExchangeAmount.Apply(layer,window);

        foreach(var layer in CharacterDetailsSkin.Tree(root).OfType<CanvasLayer>().Where(l=>l.HasMeta("exchange_request") || l.HasMeta("exchange_wait") || l.HasMeta("exchange_final"))) ClassicExchangeNotice.Apply(layer);

        return panel;

    }

}



public partial class ClassicExchangePanel : Control

{

    public readonly HudWindow Window;

    private readonly LayoutNode _art;

    private readonly VBoxContainer _mine, _theirs, _inventory;

    private readonly Label _myName, _theirName, _myGold, _theirGold, _wallet;

    private readonly Button _confirm, _otherConfirm, _gold;

    private readonly Control _myGrid=new(), _otherGrid=new();

    private readonly ClassicExchangeCell[] _bagCells;

    private bool _refreshQueued;

    public bool Locked => Window.HasMeta("ex_locked") && Window.GetMeta("ex_locked").AsBool();

    public bool PartnerLocked => Window.HasMeta("ex_partner_locked") && Window.GetMeta("ex_partner_locked").AsBool();
    public bool OfferBlocked => Blocked || PartnerLocked;
    public bool Blocked => Locked || Window.HasMeta("ex_pending") && Window.GetMeta("ex_pending").AsBool() || Window.HasMeta("ex_modal") && Window.GetMeta("ex_modal").AsBool();

    public ClassicExchangePanel(HudWindow window,Control body,Control grip,Button close)

    {

        Window=window; Name="classic_exchange"; CustomMinimumSize=ClassicExchangeLayout.Size; Size=CustomMinimumSize;

        TextureFilter=TextureFilterEnum.Nearest; MouseFilter=MouseFilterEnum.Stop;

        _art=Plugin.Kit.Layout("{nation}_personaltrade_us");

        var nodes=CharacterDetailsSkin.Tree(body).ToArray();

        _mine=nodes.OfType<VBoxContainer>().Single(n=>n.Name=="exchange_mine");

        _theirs=nodes.OfType<VBoxContainer>().Single(n=>n.Name=="exchange_theirs");

        _inventory=nodes.OfType<VBoxContainer>().Single(n=>n.Name=="exchange_inventory");

        ClassicPartySkin.Drag(this,grip,ClassicExchangeLayout.Drag);

        Text("Trade",ClassicExchangeLayout.Title,13).HorizontalAlignment=HorizontalAlignment.Center;

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
        }
        var status=nodes.OfType<Label>().Last();

        // The native status label remains the source of rejection and completion messages.

        status.AddThemeFontOverride("font",Plugin.Kit.Bold); status.AddThemeFontSizeOverride("font_size",12);

        ClassicVendorSkin.Move(status,this,ClassicExchangeLayout.Status); status.HorizontalAlignment=HorizontalAlignment.Center;

        status.ClipText=true; status.AutowrapMode=TextServer.AutowrapMode.Off;

        status.TextOverrunBehavior=TextServer.OverrunBehavior.NoTrimming; status.ClipContents=true;

        MakeOfferViewport(_myGrid,ClassicExchangeLayout.Mine,true); MakeOfferViewport(_otherGrid,ClassicExchangeLayout.Theirs,false);

        _bagCells=new ClassicExchangeCell[28];

        for(int i=0;i<28;i++) {var cell=new ClassicExchangeCell(this,true,i);_bagCells[i]=cell;AddChild(cell);cell.Position=ClassicExchangeLayout.BagCell(i).Position;cell.Size=ClassicExchangeLayout.BagCell(i).Size;}

        foreach(var list in new[]{_mine,_theirs,_inventory}) {list.ChildEnteredTree+=_=>QueueRefresh();list.ChildExitingTree+=_=>QueueRefresh();}

        Resized+=()=>Size=CustomMinimumSize;

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

    }

    public override void _Ready()=>Refresh();

    private void QueueRefresh() {if(_refreshQueued)return;_refreshQueued=true;Callable.From(Refresh).CallDeferred();}

    private void Refresh()

    {

        _refreshQueued=false;if(!IsInsideTree())return;

        var rows=_inventory.GetChildren().OfType<Control>().Where(n=>n.HasMeta("trade_item_id")).ToArray();

        int first=Plugin.Kit.Game.Inventory.GridStart;

        for(int i=0;i<_bagCells.Length;i++) _bagCells[i].Bind(rows.FirstOrDefault(r=>r.GetMeta("trade_source").AsInt32()==first+i));

        FillOffers(_myGrid,_mine);FillOffers(_otherGrid,_theirs);

    }

    private void FillOffers(Control grid,VBoxContainer list)

    {

        foreach(var cell in grid.GetChildren().OfType<ClassicExchangeCell>()) {grid.RemoveChild(cell);cell.QueueFree();}

        var rows=list.GetChildren().OfType<Control>().Where(n=>n.HasMeta("trade_item_id")).ToArray();

        var groups=rows.GroupBy(r=>(ItemData.Get(r.GetMeta("trade_item_id").AsInt32())?.Countable ?? 0)!=0?r.GetMeta("trade_item_id").AsInt64():-(long)r.GetInstanceId()).ToArray();

        int count=12;grid.CustomMinimumSize=new Vector2(149,198);

        grid.Size=grid.CustomMinimumSize;

        for(int i=0;i<count;i++) {

            var cell=new ClassicExchangeCell(this,false,i,grid==_myGrid);grid.AddChild(cell);var rect=ClassicExchangeLayout.OfferCell(i);cell.Position=rect.Position;cell.Size=rect.Size;

            cell.Bind(i<groups.Length?groups[i].First():null,i<groups.Length?groups[i].Sum(r=>r.GetMeta("trade_count").AsInt32()):0);

        }



    }

    public Control? InventoryRow(int index)=>_inventory.GetChildren().OfType<Control>().FirstOrDefault(r=>r.HasMeta("trade_source") && r.GetMeta("trade_source").AsInt32()==index);

    public void Offer(int index) {if(OfferBlocked)return;var row=InventoryRow(index);if(row==null)return;CharacterDetailsSkin.Tree(row).OfType<Button>().Single(b=>b.Text=="Offer").EmitSignal(BaseButton.SignalName.Pressed);}

    public bool ValidDrop(Variant data)

    {

        if(OfferBlocked || data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();

        return d.ContainsKey("invFrom") && d.ContainsKey("id") && InventoryRow(d["invFrom"].AsInt32()) is { } row && row.GetMeta("trade_item_id").AsInt32()==d["id"].AsInt32();

    }

    public override void _Input(InputEvent ev)

    {

        if(IsVisibleInTree() && ev is InputEventKey {Pressed:true} key && key.Keycode is Key.Enter or Key.KpEnter

            && !(Window.HasMeta("ex_modal") && Window.GetMeta("ex_modal").AsBool()))

            GetViewport().SetInputAsHandled();

    }

    public override void _Process(double delta)

    {

        string Meta(string name,string fallback)=>Window.HasMeta(name)?Window.GetMeta(name).AsString():fallback;

        string Money(string key)=>Window.HasMeta(key)?Window.GetMeta(key).AsInt64().ToString("n0"):"0";

        _myName.Text=Meta("ex_self","You");_theirName.Text=Meta("ex_partner","Player");

        _myGold.Text=Money("ex_my_gold");_theirGold.Text=Money("ex_other_gold");_wallet.Text=Money("ex_wallet");

        _confirm.Disabled=Blocked;_confirm.Text=Locked?"Confirmed":"Trade";

        bool other=Window.HasMeta("ex_partner_locked") && Window.GetMeta("ex_partner_locked").AsBool();_otherConfirm.Disabled=other;_otherConfirm.Text=other?"Confirmed":"Trade";

        _gold.Disabled=OfferBlocked;

        foreach(var cell in _bagCells) {if(cell.InputEnabled==!OfferBlocked)continue;cell.InputEnabled=!OfferBlocked;cell.Refresh();}

    }

    internal StyleBoxTexture OfferSocket()

    {

        var source=_art.Images.First();return new StyleBoxTexture {Texture=Plugin.Kit.Texture(source.Texture!),RegionRect=new Rect2(source.SrcX+20,source.SrcY+60,50,50),DrawCenter=true};

    }

    public override void _Draw()

    {

        DrawRect(new Rect2(1,39,362,584),Colors.Black);

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

    private Control? _native;

    private readonly StyleBoxTexture? _socket;

    public ClassicExchangeCell(ClassicExchangePanel owner,bool bag,int index,bool acceptsOffer=false):base(index)

    {

        _owner=owner;_bag=bag;_acceptsOffer=acceptsOffer;Name=(bag?"exchange_bag_":"exchange_offer_")+index;

        if(!bag)_socket=owner.OfferSocket();

        OnActivate=bag?slot=>owner.Offer(slot):null;OnDoubleClick=OnActivate;

        OnHover=(_,over)=> {if(_native!=null && GodotObject.IsInstanceValid(_native))_native.EmitSignal(over?Control.SignalName.MouseEntered:Control.SignalName.MouseExited);};

    }

    public void Bind(Control? row,int total=0)

    {

        _native=row;Slot=row?.GetMeta("trade_source").AsInt32() ?? -1;

        Source=_=>row==null || !GodotObject.IsInstanceValid(row)?GameItem.Empty(Slot):new GameItem(Slot,row.GetMeta("trade_item_id").AsInt32(),ItemData.DisplayName(row.GetMeta("trade_item_id").AsInt32()),total>0?total:row.GetMeta("trade_count").AsInt32(),row.GetMeta("trade_durability").AsInt32(),0,0,ItemData.Icon(row.GetMeta("trade_item_id").AsInt32()));

        Refresh();

    }

    public override Variant _GetDragData(Vector2 atPosition)=>_bag && !_owner.OfferBlocked?base._GetDragData(atPosition):default;

    public override bool _CanDropData(Vector2 atPosition,Variant data)=>_acceptsOffer && _owner.ValidDrop(data);

    public override void _DropData(Vector2 atPosition,Variant data){if(_CanDropData(atPosition,data))_owner.Offer(data.AsGodotDictionary()["invFrom"].AsInt32());}

    public override void _Draw(){if(_socket!=null)DrawStyleBox(_socket,new Rect2(Vector2.Zero,Size));}

}

public partial class ClassicExchangeDrop : Control

{

    private readonly ClassicExchangePanel _owner;

    public ClassicExchangeDrop(ClassicExchangePanel owner){_owner=owner;MouseFilter=MouseFilterEnum.Stop;}

    public override bool _CanDropData(Vector2 p,Variant data)=>_owner.ValidDrop(data);

    public override void _DropData(Vector2 p,Variant data){if(_CanDropData(p,data))_owner.Offer(data.AsGodotDictionary()["invFrom"].AsInt32());}

}

