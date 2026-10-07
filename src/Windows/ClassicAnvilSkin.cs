using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicAnvilSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(()=>Apply(body)).CallDeferred();
    public static ClassicAnvilPanel? Apply(Control body)
    {
        Node? owner=body;
        while(owner!=null && owner is not HudWindow)owner=owner.GetParent();
        if(owner is not HudWindow window || window.HasMeta("classic_anvil"))return null;
        window.SetMeta("classic_anvil",true);window.SetMeta("embedded_inventory",true);
        var panel=new ClassicAnvilPanel(window,body);
        window.AddChild(panel);window.ResetSize();return panel;
    }
}

public partial class ClassicAnvilPanel : Control
{
    public HudWindow Window { get; }
    private readonly Control _items=new(),_accessories=new();
    private readonly Label _wallet,_weight,_status;
    private readonly Button _cancel,_back;
    private readonly ItemSlotView[] _cells;
    private bool _compound,_busy;
    private double _scanTime;
    private bool _scan;
    private readonly Control _scanOverlay=new(){MouseFilter=MouseFilterEnum.Ignore,ClipContents=true,Position=ClassicAnvilLayout.Scan.Position,Size=ClassicAnvilLayout.Scan.Size};
    public ClassicAnvilPanel(HudWindow window,Control body)
    {
        Window=window;Name="classic_anvil";Size=CustomMinimumSize=ClassicAnvilLayout.Size;
        TextureFilter=TextureFilterEnum.Nearest;
        var nodes=CharacterDetailsSkin.Tree(body).ToArray();
        var grip=window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close=grip.GetChildren().OfType<Button>().Single(b=>b.TooltipText=="Close");
        foreach(var child in window.GetChildren().OfType<Control>())child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        ClassicPartySkin.Drag(this,grip,ClassicAnvilLayout.Drag);
        _status=nodes.OfType<DetailStrip>().Single().Sub;
        ClassicVendorSkin.Move(_status,this,ClassicAnvilLayout.Status);ClassicMerchantSkin.Font(_status);
        _status.HorizontalAlignment=HorizontalAlignment.Center;_status.AutowrapMode=TextServer.AutowrapMode.Off;
        _status.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        _wallet=ClassicPartySkin.Caption(this,"0",ClassicAnvilLayout.Wallet,new Color("c8b68e"));
        _wallet.HorizontalAlignment=HorizontalAlignment.Right;ClassicMerchantSkin.Font(_wallet);
        _weight=ClassicPartySkin.Caption(this,"",ClassicAnvilLayout.Weight,new Color("c0c0c0"));
        ClassicMerchantSkin.Font(_weight);_weight.VerticalAlignment=VerticalAlignment.Center;
        _weight.ClipText=true;
        _weight.SetMeta("anvil_expected_rect",ClassicAnvilLayout.Weight);
        _wallet.SetMeta("anvil_expected_rect",ClassicAnvilLayout.Wallet);
        AddChild(_items);AddChild(_accessories);
        _cells=nodes.OfType<ItemSlotView>().ToArray();
        foreach(var cell in _cells)
        {
            string name=cell.Name.ToString();Rect2 rect;Control parent=this;
            if(name.StartsWith("anvil_bag_"))rect=ClassicAnvilLayout.Bag(int.Parse(name[10..]));
            else if(name.StartsWith("anvil_item_")) {int i=cell.Index;parent=_items;rect=i==0?ClassicAnvilLayout.Item:ClassicAnvilLayout.Material(i-1);}
            else if(name.StartsWith("anvil_accessory_")) {int i=cell.Index;parent=_accessories;rect=i<3?ClassicAnvilLayout.Accessory(i):ClassicAnvilLayout.CompoundMaterial(i-3);}
            else {parent=name=="anvil_result_item"?_items:_accessories;rect=ClassicAnvilLayout.Result;}
            ClassicVendorSkin.Move(cell,parent,rect);
            // Bench outlines already exist in the artwork; only bag cells need live frames.
            StyleBox frame=new StyleBoxEmpty();
            if(name.StartsWith("anvil_bag_"))
            {
                frame=new StyleBoxEmpty();
            }
            cell.AddThemeStyleboxOverride("panel",frame);
            cell.AddChild(new ClassicAnvilCellSkin(cell,frame));
            var countOverlay=new Control{MouseFilter=MouseFilterEnum.Ignore};cell.AddChild(countOverlay);
            ItemCountStyle.Apply(cell.CountLabel,countOverlay);cell.CountLabel.OffsetRight=cell.CountLabel.OffsetBottom=0;
            ClassicVendorSkin.Font(cell.CountLabel,11);
            cell.CountLabel.VerticalAlignment=VerticalAlignment.Bottom;
            cell.SetMeta("anvil_expected_rect",rect);
        }
        var art=Plugin.Kit.Layout("co_itemupgrade_us");
        ClassicPartySkin.Place(this,close,art.Find("btn_close")!,"");
        Button Find(string name)=>nodes.OfType<Button>().Single(b=>b.Name.ToString()==name);
        var ok=nodes.OfType<Button>().Single(b=>b.Text=="Upgrade");
        _cancel=Find("anvil_cancel");_back=Find("anvil_back");
        Button Place(Button button,string id,Rect2 rect,string? caption=null)
        {
            ClassicMerchantSkin.Button(button,art.Find(id)!,this,rect,caption);
            button.FocusMode=FocusModeEnum.None;button.Shortcut=null;
            foreach(string state in new[]{"normal","hover","pressed","disabled","focus"})
                if(button.GetThemeStylebox(state) is StyleBoxTexture box)foreach(var side in new[]{Side.Top,Side.Bottom})box.SetContentMargin(side,0);
            button.SetMeta("anvil_expected_rect",rect);return button;
        }
        Place(ok,"btn_ok",ClassicAnvilLayout.Ok,"OK");
        Place(_cancel,"btn_cancel",ClassicAnvilLayout.Cancel,"Cancel");
        Place(_back,"btn_conversation",ClassicAnvilLayout.Back,"Back");
        _status.AddThemeConstantOverride("outline_size",2);
        _status.AddThemeColorOverride("font_outline_color",new Color("151416"));
        _status.AddThemeColorOverride("font_color",new Color("e4e0d7"));
        AddChild(_scanOverlay);_scanOverlay.Draw+=DrawScan;
        Resized+=()=>Size=CustomMinimumSize;
    }
    public override void _Process(double delta)
    {
        bool compound=Window.GetMeta("anvil_compound",false).AsBool();
        if(compound!=_compound){_compound=compound;QueueRedraw();}
        _items.Visible=!compound;_accessories.Visible=compound;
        bool busy=Window.GetMeta("anvil_busy",false).AsBool();
        _cancel.Disabled=_back.Disabled=busy;
        if(busy!=_busy){_busy=busy;QueueRedraw();}
        _wallet.Text=Plugin.Kit.Game.Character.Gold.ToString("n0");
        var character=Plugin.Kit.Game.Character;
        _weight.Text=character.MaxWeight>0?$"Weight : {character.Weight/10f:0.0}/{character.MaxWeight/10f:0.0}":"";
        if(Window.HasMeta("anvil_error"))_status.Text=Window.GetMeta("anvil_error").AsString();
        _status.TooltipText=_status.Text;
        _scan=Window.HasMeta("anvil_scan_success") && busy;
        _scanOverlay.Visible=_scan;
        if(_scan){_scanTime+=delta;_scanOverlay.QueueRedraw();}else _scanTime=0;
        foreach(var cell in _cells)cell.MouseFilter=busy?MouseFilterEnum.Ignore:MouseFilterEnum.Stop;
    }
    public override void _Draw()
    {
        var inventory=Plugin.Kit.Texture("ui_ka_inven_us.png");
        DrawTextureRectRegion(inventory,new Rect2(306,-1,57,41),new Rect2(3,6,57,41));
        var atlas=Plugin.Kit.Texture(_compound?"ui_upgrade_ring_us.png":"ui_upgrade_us.png");
        // Original Karus inventory pieces and the same editable grid composition as inventory.
        DrawRect(new Rect2(7,314,351,236),Colors.Black);
        DrawTextureRectRegion(inventory,new Rect2(1,314,363,134),new Rect2(149,294,362,134));
        DrawTextureRectRegion(inventory,new Rect2(1,448,363,42),new Rect2(149,378,362,42));
        // Crop the inventory footer at its actual baseline, excluding the dangling tail.
        DrawTextureRectRegion(inventory,new Rect2(1,490,363,62),new Rect2(149,433,362,62));
        DrawRect(new Rect2(9,345,345,199),Colors.Black);
        for(int i=0;i<28;i++)
        {
            var rect=ClassicAnvilLayout.Bag(i);
            DrawTextureRectRegion(inventory,new Rect2(rect.Position-new Vector2(4,3),new Vector2(51,52)),new Rect2(159,326,51,52));
        }
        DrawTextureRectRegion(atlas,new Rect2(1,39,363,275),new Rect2(146,41,363,275));
        var coin=Plugin.Kit.Texture("ui_ka_transaction_person trade_us.png");
        DrawTextureRectRegion(coin,new Rect2(204,316,22,24),new Rect2(20,145,22,24));
    }
    private void DrawScan()
    {
            if(!Window.HasMeta("anvil_scan_success"))return;
            var atlas=Plugin.Kit.Texture(_compound?"ui_upgrade_ring_us.png":"ui_upgrade_us.png");
            float open=Math.Clamp((float)(_scanTime-2.15)/.8f,0,1);open=1-(1-open)*(1-open);
            float gap=1-Math.Clamp((float)(_scanTime/.25),0,1)+open;
            _scanOverlay.DrawTextureRectRegion(atlas,new Rect2(0,-76*gap,325,76),new Rect2(159,327,325,76));
            _scanOverlay.DrawTextureRectRegion(atlas,new Rect2(0,76+74*gap,325,74),new Rect2(159,403,325,74));
            int frame=Math.Clamp((int)((_scanTime-.25)/.1),0,18);int mapped=frame<=9?frame:18-frame;
            bool success=Window.GetMeta("anvil_scan_success").AsBool();
            var animation=Plugin.Kit.Texture($"item_{(success?"load":"fail")}bar{mapped:0000}{(_compound?"_us":"")}.png");
            if(animation!=null && gap==0)_scanOverlay.DrawTextureRect(animation,new Rect2(1,43,323,64),false);
    }
    public override void _Ready()
    {
        var title=ClassicPartySkin.Caption(this,"Magic Anvil",ClassicAnvilLayout.Title,new Color("e4e0d7"));
        title.HorizontalAlignment=HorizontalAlignment.Center;
        ClassicMerchantSkin.Font(title);title.AddThemeConstantOverride("outline_size",2);
        title.AddThemeColorOverride("font_outline_color",new Color("151416"));
    }
}

public partial class ClassicAnvilCellSkin : Control
{
    private readonly ItemSlotView _cell;private readonly StyleBox _frame;
    public ClassicAnvilCellSkin(ItemSlotView cell,StyleBox frame){_cell=cell;_frame=frame;MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Process(double delta){if(_cell.GetThemeStylebox("panel")!=_frame)_cell.AddThemeStyleboxOverride("panel",_frame);}
}
