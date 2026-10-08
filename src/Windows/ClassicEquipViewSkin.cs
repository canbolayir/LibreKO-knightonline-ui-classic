using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;
public static class ClassicEquipViewSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicEquipViewPanel? Apply(Control body)
    {
        Node? node = body; while (node != null && node is not HudWindow) node = node.GetParent();
        if (node is not HudWindow window || window.HasMeta("classic_equipview")) return null;
        if (window.GetMeta("classic_equipview_controls", 0).AsInt32() != 1) return null;
        window.SetMeta("classic_equipview", true);
        var panel = new ClassicEquipViewPanel(window, body); window.AddChild(panel); window.ResetSize(); return panel;
    }
}
public partial class ClassicEquipViewPanel : Control
{
    public HudWindow Window { get; }
    private readonly VBoxContainer _stats;
    private readonly Button _close;
    private readonly List<ClassicInspectSocket> _sockets = new();
    public ClassicEquipViewPanel(HudWindow window, Control body)
    {
        Window = window; Name = "classic_equipview"; Size = CustomMinimumSize = ClassicEquipViewLayout.Size;
        TextureFilter = TextureFilterEnum.Nearest;
        var native = CharacterDetailsSkin.Tree(body).OfType<Control>().ToArray();
        T Find<T>(string name) where T : Control => native.OfType<T>().Single(c => c.Name == name);
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        var title = bar.GetChildren().OfType<Label>().Last();
        _close = bar.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(new ClassicFrame { Size = Size, BackgroundAlpha = 1, BackgroundColor = Colors.Black });
        ClassicPartySkin.Drag(this, bar, new Rect2(14, 8, Size.X - 62, 32));
        Place(title, new Rect2(22, 14, Size.X - 76, 24)); title.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        ClassicMerchantSkin.Button(_close, Plugin.Kit.Layout("co_questtalk_us").Find("btn_close")!, this, new Rect2(Size.X - 38, 14, 24, 24), "");
        Place(Find<Label>("inspect_name"), ClassicEquipViewLayout.Name);
        Place(Find<Label>("inspect_summary"), ClassicEquipViewLayout.Summary);
        Place(Find<Label>("inspect_status"), ClassicEquipViewLayout.Status);
        var status = Find<Label>("inspect_status"); status.AutowrapMode = TextServer.AutowrapMode.Off;
        foreach (var (name, text, x, width) in new[] { ("inspect_equipment_heading", "Equipment", 16, 153), ("inspect_costume_heading", "Costume", 186, 153), ("inspect_stats_heading", "State", 356, 268) })
        {
            var label = new Label { Name = name, Text = text, MouseFilter = MouseFilterEnum.Ignore }; AddChild(label);
            Place(label, new Rect2(x, 104, width, 22)); label.AddThemeColorOverride("font_color", ClassicDesign.Heading);
        }
        Place(Find<GridContainer>("inspect_gear"), ClassicEquipViewLayout.Gear);
        Place(Find<GridContainer>("inspect_costume"), ClassicEquipViewLayout.Costume);
        _stats = Find<VBoxContainer>("inspect_stats"); Place(_stats, ClassicEquipViewLayout.Stats);
        _stats.AddThemeConstantOverride("separation", 0); _stats.ChildEnteredTree += StyleStat;
        foreach (var child in _stats.GetChildren()) StyleStat(child);
        foreach (var cell in native.OfType<ItemSlotView>())
        {
            var socket = new ClassicInspectSocket(cell); cell.AddChild(socket); _sockets.Add(socket);
            cell.SetMeta("inspect_socket_review", true);
        }
        foreach (var label in new[] { Find<Label>("inspect_name"), Find<Label>("inspect_summary"), status })
        {
            label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        SetProcessUnhandledKeyInput(true);
    }
    private void Place(Control c, Rect2 rect)
    {
        ClassicVendorSkin.Move(c, this, rect); Font(c); c.SetMeta("inspect_expected_rect", rect);
        if (c is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.MouseFilter = MouseFilterEnum.Ignore; }
    }
    private static void Font(Control c)
    {
        c.AddThemeFontOverride("font", Plugin.Kit.Bold); c.AddThemeFontSizeOverride("font_size", 13); c.AddThemeConstantOverride("outline_size", 0);
        c.AddThemeColorOverride("font_color", ClassicDesign.Text);
    }
    private void StyleStat(Node node)
    {
        if (node is HSeparator separator)
        {
            separator.CustomMinimumSize = new Vector2(0, 8);
            separator.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = ClassicDesign.Muted, Thickness = 1 }); return;
        }
        if (node is not HBoxContainer row) return;
        row.CustomMinimumSize = new Vector2(268, 23); row.AddThemeConstantOverride("separation", 0);
        var labels = row.GetChildren().OfType<Label>().ToArray();
        var semantic = labels[1].GetThemeColor("font_color");
        foreach (var label in labels) { Font(label); label.VerticalAlignment = VerticalAlignment.Center; label.ClipText = true; }
        labels[0].CustomMinimumSize = new Vector2(154, 23);
        labels[1].CustomMinimumSize = new Vector2(114, 23); labels[1].HorizontalAlignment = HorizontalAlignment.Right;
        if (labels[0].Text is "Max HP" or "Max MP") labels[1].AddThemeColorOverride("font_color", semantic);
    }
    public override void _Process(double delta)
    {
        foreach (var socket in _sockets) socket.UpdateFrame();
        foreach (var label in GetChildren().OfType<Label>().Where(l => l.Name.ToString() is "inspect_summary" or "inspect_status")) label.TooltipText = label.Text;
    }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (IsVisibleInTree() && ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); _close.EmitSignal(BaseButton.SignalName.Pressed); }
    }
}
public partial class ClassicInspectSocket : Control
{
    private readonly ItemSlotView _cell;
    private readonly TextureRect _empty;
    private readonly Control _count;
    private readonly StyleBoxEmpty _inside = new();
    private bool _hover;
    public ClassicInspectSocket(ItemSlotView cell)
    {
        _cell = cell; MouseFilter = MouseFilterEnum.Ignore; ShowBehindParent = true;
        foreach (var side in new[] { Side.Top, Side.Left, Side.Right, Side.Bottom }) _inside.SetContentMargin(side, 3);
        _empty = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        cell.AddChild(_empty); _empty.ShowBehindParent = true;
        if (cell.Index < InventoryConstants.SlotMax) _empty.Texture = InventoryLayout.EquipmentIcon(cell.Index);
        else
        {
            var costume = InventoryLayout.CostumeSlots.Single(s => s.Position + InventoryConstants.CospreStart == cell.Index);
            _empty.Texture = InventoryLayout.EmptyIcon(costume.IconX, costume.IconY);
        }
        _count = new Control { Name = "inspect_count_overlay", MouseFilter = MouseFilterEnum.Ignore }; cell.AddChild(_count);
        ItemCountStyle.Apply(cell.CountLabel, _count, Plugin.Kit.Bold);
        cell.MouseEntered += () => { _hover = true; QueueRedraw(); }; cell.MouseExited += () => { _hover = false; QueueRedraw(); };
        UpdateFrame();
    }
    public void UpdateFrame()
    {
        if (_cell.GetThemeStylebox("panel") != _inside) _cell.AddThemeStyleboxOverride("panel", _inside);
        Position = new Vector2(-4, -3); Size = new Vector2(51, 52);
        _empty.Position = new Vector2(5, 5); _empty.Size = new Vector2(35, 35); _empty.Visible = _cell.Item.IsEmpty;
        _count.Position = Vector2.Zero; _count.Size = _cell.Size;
    }
    public override void _Draw()
    {
        bool karus = ClassicDesign.Karus;
        DrawTextureRectRegion(Plugin.Kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png"), new Rect2(Vector2.Zero, Size), karus ? new Rect2(159, 326, 51, 52) : new Rect2(157, 333, 51, 52));
        if (_hover) DrawRect(new Rect2(4, 3, 45, 45), new Color(1, .85f, .45f, .12f));
    }
}
