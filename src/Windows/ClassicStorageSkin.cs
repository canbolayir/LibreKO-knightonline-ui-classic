using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

public static class ClassicStorageSkin
{
    public static readonly string[] WindowIds = { "warehouse", "vipwarehouse", "clanwarehouse" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicStoragePanel? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_storage") || NativeStorage.Of(window) == null) return null;
        window.SetMeta("classic_storage", true); window.SetMeta("embedded_inventory", true);
        if (window.Id == "warehouse") window.SetMeta("warehouse_page_size", 24);
        var panel = new ClassicStoragePanel(window, body); window.AddChild(panel); window.ResetSize();
        return panel;
    }
}

public partial class ClassicStoragePanel : Control
{
    public HudWindow Window { get; }
    private readonly Label _wallet, _weight, _stored, _status;
    private readonly Button _prev, _next;
    private readonly bool _vip;
    private readonly ItemSlotView[] _cells;

    public ClassicStoragePanel(HudWindow window, Control body)
    {
        Window = window; _vip = window.Id == "vipwarehouse"; Name = "classic_storage";
        Size = CustomMinimumSize = ClassicStorageLayout.Size; TextureFilter = TextureFilterEnum.Nearest;
        var nodes = CharacterDetailsSkin.Tree(body).ToArray();
        var grip = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close = grip.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        ClassicPartySkin.Drag(this, grip, new Rect2(8, 8, 320, 32));
        var art = Plugin.Kit.Layout("{nation}_warehouse_us");
        ClassicPartySkin.Place(this, close, art.Find("btn_close")!, "");
        var title = Caption(window.Id == "warehouse" ? "Warehouse" : _vip ? "VIP Warehouse" : "Clan Warehouse", ClassicStorageLayout.Title);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color("efd9b4"));
        title.SetMeta("storage_expected_rect", ClassicStorageLayout.Title);
        Button Find(string name) => nodes.OfType<Button>().Single(b => b.Name.ToString() == name);
        Button Place(Button button, string id, Rect2 rect, string text)
        {
            ClassicMerchantSkin.Button(button, art.Find(id)!, this, rect, text);
            button.SetMeta("storage_expected_rect", rect); return button;
        }
        _prev = Place(Find("storage_prev"), "btn_page_up", ClassicStorageLayout.PageUp(_vip), "");
        _next = Place(Find("storage_next"), "btn_page_down", ClassicStorageLayout.PageDown(_vip), "");
        _prev.TooltipText = "Previous storage page"; _next.TooltipText = "Next storage page";
        var page = nodes.OfType<Label>().Single(l => l.Name == "storage_page");
        Move(page, ClassicStorageLayout.Page(_vip)); page.MouseFilter = MouseFilterEnum.Pass; page.HorizontalAlignment = HorizontalAlignment.Center;
        _cells = nodes.OfType<ItemSlotView>().ToArray();
        foreach (var cell in _cells)
        {
            string name = cell.Name.ToString();
            bool bag = name.StartsWith("storage_bag_");
            int index = int.Parse(name[(bag ? 12 : 13)..]);
            if (!bag && index >= (_vip ? 12 : 24)) { cell.Visible = false; continue; }
            var rect = bag ? ClassicStorageLayout.Bag(index) : ClassicStorageLayout.Stored(index);
            ClassicVendorSkin.Move(cell, this, rect);
            cell.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            cell.AddChild(new ClassicStorageCell(cell));
            var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore }; cell.AddChild(overlay);
            var icon = cell.GetChildren().OfType<TextureRect>().Single();
            icon.Reparent(overlay, false); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = icon.OffsetTop = ItemCountStyle.Inset;
            icon.OffsetRight = icon.OffsetBottom = -ItemCountStyle.Inset;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
            ItemCountStyle.Apply(cell.CountLabel, overlay);
            cell.MoveChild(overlay, 0);
            cell.SetMeta("storage_expected_rect", rect);
        }
        _status = nodes.OfType<Label>().Single(l => l.Name == "storage_status");
        Move(_status, ClassicStorageLayout.Status);
        if (_status is StatusLabel timedStatus) timedStatus.Hint = "";
        _wallet = Caption("0", ClassicStorageLayout.Gold); _wallet.HorizontalAlignment = HorizontalAlignment.Right;
        _weight = Caption("", ClassicStorageLayout.Weight);
        _stored = Caption("0", ClassicStorageLayout.StoredGold); _stored.HorizontalAlignment = HorizontalAlignment.Right;
        if (_vip)
        {
            _stored.Visible = false;
            Caption("Expires in", new Rect2(14, 178, 120, 20));
            var expiry = nodes.OfType<Label>().Single(l => l.Name == "storage_expiry"); Move(expiry, new Rect2(149, 178, 155, 20));
            expiry.HorizontalAlignment = HorizontalAlignment.Right; expiry.AddThemeColorOverride("font_color", new Color("efd9b4"));
            var set = nodes.OfType<Button>().Single(b => b.Text == "Set / change PIN");
            var clear = nodes.OfType<Button>().Single(b => b.Text == "Clear PIN");
            foreach (var (button, rect) in new[] { (set, new Rect2(13, 207, 141, 27)), (clear, new Rect2(162, 207, 141, 27)) })
            {
                if (button == set) button.Text = "Change PIN";
                ClassicVendorSkin.Move(button, this, rect); ClassicVendorSkin.Button(button); ClassicMerchantSkin.Font(button);
                button.SetMeta("storage_expected_rect", rect);
            }
        }
        else
        {
            var deposit = window.Id == "warehouse" ? nodes.OfType<Button>().Single(b => b.Text == "Deposit") : Find("storage_deposit");
            var withdraw = window.Id == "warehouse" ? nodes.OfType<Button>().Single(b => b.Text == "Withdraw") : Find("storage_withdraw");
            Place(deposit, "btn_gold", new Rect2(206, 316, 22, 25), "");
            Place(withdraw, "btn_gold_warehouse", new Rect2(206, 276, 22, 25), "");
            deposit.TooltipText = "Deposit coins"; withdraw.TooltipText = "Withdraw coins";
            Caption("Stored coins", ClassicStorageLayout.StoredCaption);
            if (window.Id == "warehouse")
            {
                var search = nodes.OfType<LineEdit>().Single(); search.Text = ""; search.Visible = false;
            }
        }
        if (window.HasMeta("storage_amount")) ClassicTradeQuantity.Apply((QuantityPrompt)window.GetMeta("storage_amount").AsGodotObject(), this);
        Resized += () => Size = CustomMinimumSize;
    }

    private void Move(Control control, Rect2 rect)
    {
        ClassicVendorSkin.Move(control, this, rect); ClassicMerchantSkin.Font(control);
        control.SetMeta("storage_expected_rect", rect);
        if (control is Label label) { label.ClipText = true; label.AutowrapMode = TextServer.AutowrapMode.Off; label.VerticalAlignment = VerticalAlignment.Center; }
    }
    private Label Caption(string text, Rect2 rect)
    {
        var label = ClassicPartySkin.Caption(this, text, rect, new Color("c0c0c0"));
        ClassicMerchantSkin.Font(label); label.SetMeta("storage_expected_rect", rect); return label;
    }
    public override void _Process(double delta)
    {
        var character = Plugin.Kit.Game.Character;
        _wallet.Text = character.Gold.ToString("n0");
        _weight.Text = character.MaxWeight > 0 ? $"Weight : {character.Weight / 10f:0.0}/{character.MaxWeight / 10f:0.0}" : "";
        _stored.Text = Window.GetMeta("storage_money", 0).AsInt32().ToString("n0");
        _status.Visible = _status.Text.Length > 0 && !_status.Text.StartsWith("Right-click");
        _status.TooltipText = _status.Text;
        if (_status is not StatusLabel) _status.AddThemeColorOverride("font_color", new Color("e9b56a"));
        int page = Window.GetMeta("storage_page", 0).AsInt32(), pages = Window.GetMeta("storage_pages", _vip ? 4 : 8).AsInt32();
        _prev.Disabled = page == 0; _next.Disabled = page >= pages - 1;
    }
    public override void _Draw()
    {
        bool karus = Plugin.Kit.Nation == 1;
        DrawRect(new Rect2(4, 41, 358, 509), Colors.Black);
        var warehouse = Plugin.Kit.Texture(karus ? "ui_ka_warehouse_us.png" : "ui_el_warehouse_us.png");
        var reference = Plugin.Kit.Layout("{nation}_warehouse_us").Children.First(n => n.IsImage);
        DrawTextureRectRegion(warehouse, new Rect2(1, 1, 363, 313), new Rect2(reference.SrcX, reference.SrcY, reference.SrcW, reference.SrcH));
        // Clear baked sockets, then compose the same cell frames as the inventory drawer.
        DrawRect(new Rect2(8, 44, 349, 227), Colors.Black);
        var inventory = Plugin.Kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png");
        var pieces = Plugin.Kit.Layout("{nation}_warehouse_us").Children.Where(n => n.IsImage).Skip(1).ToArray();
        foreach (var piece in pieces)
        {
            int y = piece.Y == 497 ? 490 : piece.Y;
            int height = piece.Y == 497 ? 63 : piece.Y == 448 ? 42 : 134;
            DrawTextureRectRegion(inventory, new Rect2(1, y, 363, height),
                new Rect2(piece.SrcX, piece.SrcY, piece.SrcW, height));
        }
        if (_vip) DrawRect(new Rect2(195, 274, 162, 31), Colors.Black);
        DrawRect(new Rect2(9, 345, 348, 199), Colors.Black);
        void Frame(Rect2 rect)
        {
            DrawRect(rect, Colors.Black);
            DrawTextureRectRegion(inventory, new Rect2(rect.Position - new Vector2(4, 3), new Vector2(51, 52)),
                new Rect2(karus ? 159 : 157, karus ? 326 : 333, 51, 52));
        }
        for (int i = 0; i < (_vip ? 12 : 24); i++) Frame(ClassicStorageLayout.Stored(i));
        for (int i = 0; i < 28; i++) Frame(ClassicStorageLayout.Bag(i));
        var coin = Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
        DrawTextureRectRegion(Plugin.Kit.Texture(coin.Texture!), new Rect2(206, 316, 22, 25), new Rect2(coin.SrcX, coin.SrcY, coin.SrcW, coin.SrcH));
    }
}

public partial class ClassicStorageCell : Control
{
    private readonly ItemSlotView _cell;
    private readonly StyleBoxEmpty _empty = new();
    private SlotLook _look;
    public ClassicStorageCell(ItemSlotView cell) { _cell = cell; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta)
    {
        if (_cell.GetThemeStylebox("panel") != _empty) _cell.AddThemeStyleboxOverride("panel", _empty);
        Size = _cell.Size;
        if (_look != _cell.Look) { _look = _cell.Look; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_cell.Look == SlotLook.Selected) DrawRect(new Rect2(Vector2.Zero, Size), new Color("e5c16b"), false, 1);
    }
}
