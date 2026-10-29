using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Restyles live shop controls without replacing trade validation or network callbacks.</summary>
public static class ClassicVendorSkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static ClassicVendorPanel? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_vendor")) return null;
        window.SetMeta("classic_vendor", true);
        var bar = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var title = bar.GetChildren().OfType<Label>().Last();
        var close = bar.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var panel = new ClassicVendorPanel(window, body, window.Header!, title, close);
        window.AddChild(panel); window.ResetSize();
        var footer = CharacterDetailsSkin.Tree(body).OfType<FooterBand>().Single();
        window.VisibilityChanged += () => { if (window.Visible) footer.ResetStatus(); };
        Node? root = window;
        while (root != null && root is not WorldRoot) root = root.GetParent();
        if (root != null)
            foreach (var prompt in root.GetChildren().OfType<QuantityPrompt>().Where(p => p.Layer == 76))
            { ClassicTradeQuantity.Apply(prompt, window); panel.BindQuantity(prompt); }
        return panel;
    }
    internal static void Move(Control node, Node parent, Rect2 rect)
    {
        node.Reparent(parent); node.Visible = true; node.CustomMinimumSize = Vector2.Zero;
        if (node is Label label) label.ClipText = true;
        if (node is Button button) button.ClipText = true;
        node.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        node.Position = rect.Position; node.Size = rect.Size;
        // Text and style minimum sizes settle after reparenting out of native containers.
        Callable.From(() => node.Size = rect.Size).CallDeferred();
    }
    internal static void Font(Control node, int size = 12)
    {
        node.AddThemeFontOverride("font", ClassicNpcLayout.BodyFont); node.AddThemeFontSizeOverride("font_size", size);
        node.AddThemeConstantOverride("outline_size", 0);
        if (node is Label label) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipText = true; }
    }
    internal static void Button(Button button, LayoutNode? art = null)
    {
        ClassicReportDesign.StyleButton(button, false, art); Font(button);
        button.Icon = null; button.ClipText = true; button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }
    internal static void Input(LineEdit input)
    {
        Font(input); input.AddThemeColorOverride("font_color", ClassicDesign.Text);
        input.AddThemeColorOverride("font_placeholder_color", ClassicDesign.Muted);
        input.AddThemeStyleboxOverride("normal", ClassicDesign.InputBox());
        input.AddThemeStyleboxOverride("focus", ClassicDesign.InputBox());
    }
}

public partial class ClassicVendorFrame : Control
{
    private static readonly string[] Signs = { "img_blacksmith", "img_store", "img_inn" };
    public ClassicVendorFrame() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; }
    public override void _Draw()
    {
        bool karus = ClassicDesign.Karus;
        var art = Plugin.Kit.Layout("{nation}_transaction_us");
        // The original transaction composition: catalogue artwork above the Inventory lower part.
        DrawRect(new Rect2(4, 41, 357, 515), Colors.Black);
        foreach (var image in art.Images.Where(n => !Signs.Contains(n.Id)))
            DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!), new Rect2(image.Position, image.SizeVec), new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH));
        // Clear the baked sockets and compose the same cell frames as Inventory and Warehouse.
        DrawRect(new Rect2(8, 92, 306, 202), Colors.Black);
        DrawRect(new Rect2(9, 345, 348, 199), Colors.Black);
        var inventory = Plugin.Kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png");
        void Frame(Rect2 rect)
        {
            DrawTextureRectRegion(inventory, new Rect2(rect.Position - new Vector2(4, 3), new Vector2(51, 52)),
                new Rect2(karus ? 159 : 157, karus ? 326 : 333, 51, 52));
        }
        for (int i = 0; i < 24; i++) Frame(ClassicVendorLayout.CatalogueCell(i));
        for (int i = 0; i < 28; i++) Frame(ClassicVendorLayout.BagCell(i));
    }
}

public partial class ClassicVendorSlot : Control
{
    private readonly ItemSlotView _owner;
    private readonly StyleBoxTexture _box;
    private SlotLook _look;
    private bool _hover;
    public ItemSlotView OwnerCell => _owner;
    private readonly StyleBox _panel;
    public ClassicVendorSlot(ItemSlotView owner, bool socket = true)
    {
        _owner = owner; MouseFilter = MouseFilterEnum.Ignore; ShowBehindParent = true;
        _box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(ClassicDesign.Karus ? "ui_ka_transaction_person trade_us.png" : "ui_el_transaction_person trade_us.png"),
            RegionRect = ClassicDesign.Karus ? new Rect2(169, 57, 50, 50) : new Rect2(170, 58, 50, 50), DrawCenter = false };
        foreach (var side in new[] { Side.Top, Side.Left, Side.Right, Side.Bottom }) { _box.SetTextureMargin(side, 1); _box.SetContentMargin(side, 3); }
        // Without a socket the frame is painted by the window; the icon keeps the Inventory 2 px inset.
        var plain = new StyleBoxEmpty();
        foreach (var side in new[] { Side.Top, Side.Left, Side.Right, Side.Bottom }) plain.SetContentMargin(side, ItemCountStyle.Inset);
        _panel = socket ? _box : plain;
        Resized += QueueRedraw;
        owner.MouseEntered += () => { _hover = true; UpdateFrame(); QueueRedraw(); }; owner.MouseExited += () => { _hover = false; UpdateFrame(); QueueRedraw(); };
        UpdateFrame();
    }
    public void UpdateFrame()
    {
        if (_owner.GetThemeStylebox(HotNames.Panel) != _panel) _owner.AddThemeStyleboxOverride(HotNames.Panel, _panel);
        Position = Vector2.Zero; Size = _owner.Size;
        if (_look != _owner.Look) { _look = _owner.Look; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_look == SlotLook.Selected) DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), ClassicDesign.Heading, false, 2);
        if (_hover) DrawRect(new Rect2(2, 2, Size.X - 4, Size.Y - 4), new Color(1, .85f, .45f, .16f));
    }
}

public static class ClassicTradeQuantity
{
    public static void Apply(QuantityPrompt prompt, Control? shop = null)
    {
        if (prompt.HasMeta("classic_trade_quantity")) return;
        prompt.SetMeta("classic_trade_quantity", true);
        var centre = prompt.GetChildren().OfType<CenterContainer>().Single();
        var nodes = CharacterDetailsSkin.Tree(centre).ToArray();
        var labels = nodes.OfType<Label>().ToArray();
        var amount = nodes.OfType<MoneyEdit>().Single();
        amount.GroupDigits = false;
        var buttons = nodes.OfType<Button>().ToArray();
        var all = buttons.Single(b => b.Text == "All");
        var cancel = buttons.Single(b => b.Text == "Cancel");
        var ok = buttons.Single(b => b != all && b != cancel);
        centre.Visible = false;
        // OpenKO locks the underlying window without adding a dark screen overlay.
        prompt.GetChildren().OfType<ColorRect>().Single().Color = Colors.Transparent;
        var panel = new ClassicQuantityPanel { Name = "classic_trade_quantity", Size = ClassicQuantityLayout.Size, Shop = shop }; prompt.AddChild(panel);
        panel.AddChild(new ClassicQuantityFrame { Size = panel.Size });
        var message = ClassicNpcLayout.Text("Please enter the quantity of the item.", 13, true, new Color("c0c0c0"));
        message.AddThemeFontOverride("font", Plugin.Kit.Bold);
        panel.AddChild(message); ClassicNpcLayout.Place(message, ClassicQuantityLayout.Message); message.HorizontalAlignment = HorizontalAlignment.Center;
        var icon = nodes.OfType<TextureRect>().Single(); ClassicVendorSkin.Move(icon, panel, ClassicQuantityLayout.Icon);
        ClassicVendorSkin.Move(amount, panel, ClassicQuantityLayout.Amount); ClassicVendorSkin.Font(amount, 13);
        amount.AddThemeFontOverride("font", Plugin.Kit.Bold);
        amount.AddThemeColorOverride("font_color", new Color("ffff00"));
        amount.AddThemeColorOverride("caret_color", new Color("ffff00"));
        foreach (var state in new[] { "normal", "focus", "read_only" }) amount.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        void OriginalButton(Button button, string name, Rect2 rect)
        {
            button.Name = "classic_trade_" + name;
            ClassicVendorSkin.Move(button, panel, rect); StyleButton(button, name);
        }
        OriginalButton(ok, "ok", ClassicQuantityLayout.Confirm); OriginalButton(cancel, "cancel", ClassicQuantityLayout.Cancel);
        prompt.VisibilityChanged += () =>
        {
            if (!prompt.Visible) return;
            bool gold = labels[0].Text is "Deposit gold" or "Withdraw gold";
            message.Text = gold ? "Please enter the amount of coins." : "Please enter the quantity of the item.";
            if (gold)
            {
                var coin = Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
                icon.Texture = new AtlasTexture { Atlas = Plugin.Kit.Texture(coin.Texture!), Region = new Rect2(coin.SrcX, coin.SrcY, coin.SrcW, coin.SrcH) };
                icon.Visible = true;
            }
            string action = ok.Text;
            prompt.SetMeta("trade_action", action); ok.Text = ClassicDesign.Karus ? "O K" : "O  K";
            if (action == "Buy") amount.Text = "";
            amount.EmitSignal(LineEdit.SignalName.TextChanged, amount.Text);
            amount.TooltipText = labels[1].Text; icon.TooltipText = labels[0].Text;
        };
    }
    internal static void StyleButton(Button button, string name)
    {
        ClassicVendorSkin.Button(button, Plugin.Kit.Layout("{nation}_personaltradeedit_us").Find("btn_" + name));
        button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 16);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, new Color("fbeec8"));
        button.AddThemeColorOverride("font_disabled_color", new Color("81796c"));
        button.AddThemeColorOverride("font_shadow_color", Colors.Black);
        button.AddThemeConstantOverride("shadow_offset_x", 1); button.AddThemeConstantOverride("shadow_offset_y", 1);
    }
}

public partial class ClassicQuantityFrame : Control
{
    private readonly LayoutNode[] _images = Plugin.Kit.Layout("{nation}_personaltradeedit_us").Images.ToArray();
    public bool DrawAmountField { get; set; } = true;
    public ClassicQuantityFrame() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw; }
    public override void _Draw()
    {
        float extraHeight = Size.Y - ClassicQuantityLayout.Size.Y;
        DrawRect(new Rect2(ClassicQuantityLayout.Origin, new Vector2(308, 128 + extraHeight)), Colors.Black);
        for (int i = 0; i < 6; i++) DrawTextureRectRegion(Plugin.Kit.Texture(_images[i].Texture!), ClassicQuantityLayout.FramePart(i, Size),
            new Rect2(_images[i].SrcX, _images[i].SrcY, _images[i].SrcW, _images[i].SrcH));
        if (extraHeight > 0)
        {
            // Extend only the straight rails; preserve the original corners and button artwork.
            var left = _images[0]; var right = _images[1];
            DrawTextureRectRegion(Plugin.Kit.Texture(left.Texture!), new Rect2(ClassicQuantityLayout.Origin + new Vector2(0, 64), new Vector2(8, extraHeight)),
                new Rect2(left.SrcX, left.SrcY + 50, 8, 1));
            DrawTextureRectRegion(Plugin.Kit.Texture(right.Texture!), new Rect2(ClassicQuantityLayout.Origin + new Vector2(300, 64), new Vector2(8, extraHeight)),
                new Rect2(right.SrcX + 56, right.SrcY + 50, 8, 1));
        }
        if (!DrawAmountField) return;
        var field = _images[6];
        DrawTextureRectRegion(Plugin.Kit.Texture(field.Texture!), ClassicQuantityLayout.InputFrame, new Rect2(field.SrcX, field.SrcY, field.SrcW, field.SrcH));
    }
}

public partial class ClassicQuantityPanel : Control
{
    public Control? Shop { get; set; }
    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            var center = Shop?.GetGlobalRect().GetCenter() ?? GetViewportRect().Size / 2;
            Position = (center - Size / 2).Round().Clamp(Vector2.Zero, (GetViewportRect().Size - Size).Max(Vector2.Zero));
        }
    }
    public override void _Input(InputEvent ev)
    {
        if (!IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo:false } key) return;
        if(key.Keycode==Key.Escape) ((QuantityPrompt)GetParent()).Close();
        else if(key.Keycode is Key.Enter or Key.KpEnter) ((QuantityPrompt)GetParent()).Confirm();
        else return;
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName Panel = "panel";
}
