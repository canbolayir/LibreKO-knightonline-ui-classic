using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using L = KnightOnlineUiClassic.Layout.ClassicStoreMykoLayout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Power-Up Store on the original Power Up Store art (bundled 2.x art, <c>assets/myko</c>). The native store keeps its
/// behaviour: its search, sort, categories, cards, details, cart, gift and checkout controls are moved onto the
/// original 1024×765 composition, and the few controls the original had but the native store lacks (card Buy and
/// Add to Basket buttons, the pager) drive the native store.
/// </summary>
public partial class ClassicStoreMykoPanel : Control
{
    public HudWindow Window { get; }
    private readonly LayoutNode _art;
    private readonly Control _stage, _overlay, _grip, _tabs, _basket;
    private readonly PanelContainer[] _modals;
    private readonly Button _close, _previous, _next;
    private readonly Label _page;
    private readonly VBoxContainer _categoryList;
    private readonly ScrollContainer _gridScroll;
    private readonly GridContainer _grid;
    private readonly MarginContainer _gridRoom;
    private readonly List<Button> _plates = new();
    private readonly Label[] _cartLabels;
    private bool _tabsQueued, _resetPage;
    private float _scale = 1;
    private static Texture2D? _basketRow;

    public ClassicStoreMykoPanel(HudWindow window, Control body)
    {
        Window = window; _art = L.Art!; Name = "classic_store"; TextureFilter = TextureFilterEnum.Nearest;
        var theme = ClassicStorePanel.StoreTheme();
        foreach (string featured in new[] { "", "featured_" }) foreach (bool active in new[] { false, true })
        {
            var card = Art(_art.Find("item1")!); card.ContentMarginLeft = card.ContentMarginRight = 0;
            theme.SetStylebox("pus_card_" + featured + (active ? "active" : "normal"), "PanelContainer", card);
        }
        Theme = theme; window.Theme = theme;
        _overlay = CharacterDetailsSkin.Tree(window).OfType<Control>().Single(c => c.Name == "pus_overlay");
        _modals = ClassicStorePanel.Modals(_overlay);
        var header = window.Header!.GetChildren().OfType<HBoxContainer>().Single();
        _close = header.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>().Where(c => c != _overlay)) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        // The native body stays (hidden) as the home of the category list the tabs mirror.
        body.Reparent(this); body.Visible = false;
        _stage = ClassicStorePanel.Owned(new ClassicStoreMykoStage(_art) { Name = "pus_stage", Size = L.Stage });
        AddChild(_stage);
        ClassicStorePanel.DescriptionScroll(_overlay);
        ClassicStorePanel.Watch(this, body, Escape); ClassicStorePanel.Watch(this, _overlay, Escape);
        ClassicStorePanel.FrameModals(_modals);
        T Part<T>(string name) where T : Node => CharacterDetailsSkin.Tree(this).Concat(CharacterDetailsSkin.Tree(_overlay)).OfType<T>().Single(c => c.Name == "pus_" + name);

        _close.Reparent(_stage); Place(_close, _art.Find(L.Close)!, "");
        ClassicPartySkin.Drag(this, header, L.Drag); _grip = GetChildren().OfType<Control>().Single(c => c.Name == "party_drag");

        // Header row: the native search field and sort list, and the catalogue title.
        var query = Part<LineEdit>("query"); var field = (PanelContainer)query.GetParent().GetParent();
        field.Reparent(_stage); Move(field, L.SearchField);
        var fieldArt = _art.Find(L.Search)!.Images.First();
        var fieldBox = Art(fieldArt); fieldBox.ContentMarginLeft = 14; fieldBox.ContentMarginRight = 6;
        field.AddThemeStyleboxOverride("panel", fieldBox);
        query.FocusEntered += () => field.AddThemeStyleboxOverride("panel", fieldBox); query.FocusExited += () => field.AddThemeStyleboxOverride("panel", fieldBox);
        foreach (var icon in query.GetParent().GetChildren().OfType<TextureRect>()) icon.Visible = false;
        foreach (string state in new[] { "normal", "focus", "read_only" }) query.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        query.AddThemeColorOverride("font_color", Colors.White); query.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.55f));
        var sort = Part<OptionButton>("sort"); sort.Reparent(_stage); Place(sort, _art.Find("btn_tab1")!, null, L.Sort); sort.Alignment = HorizontalAlignment.Left;
        var tools = Part<BoxContainer>("tools"); tools.Visible = false;
        var title = Part<Label>("grid_title"); title.Reparent(_stage); Move(title, L.GridTitle);
        title.HorizontalAlignment = HorizontalAlignment.Right; title.VerticalAlignment = VerticalAlignment.Center; Ink(title, L.CardNameInk, 13);

        // Cash: the native balance on the Knight Cash plate, the native recharge on the original top-right plate.
        var cash = Part<Label>("cash"); var wallet = (Control)cash.GetParent().GetParent().GetParent();
        var recharge = wallet.GetChildren().OfType<Button>().Single();
        cash.Reparent(_stage); Move(cash, L.Cash); cash.HorizontalAlignment = HorizontalAlignment.Center; cash.VerticalAlignment = VerticalAlignment.Center; Ink(cash, Colors.White, 13);
        recharge.Reparent(_stage); Place(recharge, _art.Find(L.Recharge)!, L.RechargeCaption); wallet.Visible = false;

        // Categories become the original tab strip; the native list stays the source of truth.
        _categoryList = Part<VBoxContainer>("category_list");
        _tabs = ClassicStorePanel.Owned(new Control { Name = "pus_tabs", MouseFilter = MouseFilterEnum.Ignore });
        _stage.AddChild(_tabs); Move(_tabs, L.TabRow);
        _categoryList.ChildEnteredTree += _ => QueueTabs(); _categoryList.ChildExitingTree += _ => QueueTabs(); QueueTabs();

        // Item well: whole 3×3 pages of original cards, turned by the pager (or the wheel).
        _gridScroll = Part<ScrollContainer>("grid_scroll"); _grid = Part<GridContainer>("grid"); _gridRoom = (MarginContainer)_grid.GetParent();
        _gridScroll.Reparent(_stage); Move(_gridScroll, L.Grid); _gridScroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        _gridRoom.AddThemeConstantOverride("margin_top", 0);
        _grid.AddThemeConstantOverride("h_separation", L.CardGapX); _grid.AddThemeConstantOverride("v_separation", L.CardGapY);
        _grid.PreSortChildren += () => { if (_grid.Columns != L.Columns) _grid.Columns = L.Columns; };
        _grid.SortChildren += UpdatePager; _gridScroll.VisibilityChanged += UpdatePager;
        _grid.ChildExitingTree += _ => _resetPage = true;
        _grid.ChildEnteredTree += node => { if (node is PanelContainer card) Callable.From(() => Card(card)).CallDeferred(); };
        foreach (var card in _grid.GetChildren().OfType<PanelContainer>()) Callable.From(() => Card(card)).CallDeferred();
        _gridScroll.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true } wheel || wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
            Turn(wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1); _gridScroll.AcceptEvent();
        };
        var empty = Part<Label>("grid_empty"); empty.Reparent(_stage); Move(empty, L.Grid);
        _previous = ClassicStorePanel.Owned(new Button { Name = "pus_page_previous", TooltipText = "Previous page", FocusMode = FocusModeEnum.None });
        _next = ClassicStorePanel.Owned(new Button { Name = "pus_page_next", TooltipText = "Next page", FocusMode = FocusModeEnum.None });
        _stage.AddChild(_previous); _stage.AddChild(_next);
        Place(_previous, _art.Find(L.Previous)!, ""); Place(_next, _art.Find(L.Next)!, "");
        _previous.Pressed += () => Turn(-1); _next.Pressed += () => Turn(1);
        _page = ClassicStorePanel.Owned(new Label { Name = "pus_page", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
        _stage.AddChild(_page); Move(_page, L.PageText); Ink(_page, Colors.White, 12);

        // My Basket: native lines and messages in the basket well, totals and actions on the original plates.
        var cartWell = Part<Control>("cart"); _basket = cartWell.GetChildren().OfType<VBoxContainer>().Single();
        _basket.Name = "pus_basket"; _basket.Reparent(_stage); Move(_basket, L.Basket); cartWell.Visible = false;
        var cartTitle = Part<Label>("cart_title"); cartTitle.Reparent(_stage); Move(cartTitle, L.BasketTitle);
        cartTitle.VerticalAlignment = VerticalAlignment.Center; cartTitle.ClipText = true; Ink(cartTitle, L.CardNameInk, 13);
        var clear = Part<Button>("clear_cart"); clear.Reparent(_stage); Place(clear, _art.Find(L.ClearCart)!, null, null, L.ClearInk);
        var checkout = Part<Button>("checkout"); checkout.Reparent(_stage); Place(checkout, _art.Find(L.Checkout)!, null, null, L.CheckoutInk);
        var gift = Part<Button>("gift"); gift.Reparent(_stage); Place(gift, _art.Find(L.Gift)!, null, null, _art.Find(L.Gift)!.Strings.First().Color);
        var total = Part<HBoxContainer>("total"); total.Reparent(_stage); Move(total, L.Total);
        string[] kept = { "pus_cart_scroll", "pus_cart_empty", "pus_gift_banner", "pus_shortfall", "pus_cart_status" };
        foreach (var child in _basket.GetChildren().OfType<Control>().Where(c => !kept.Contains(c.Name.ToString()))) child.Visible = false;
        _cartLabels = new[] { Part<Label>("cart_empty"), Part<Label>("shortfall"), Part<Label>("cart_status") };
        foreach (var label in _cartLabels) { label.MinimumSizeChanged += () => FitCartLabel(label); FitCartLabel(label); }
        var lines = Part<VBoxContainer>("cart_lines");
        lines.ChildEnteredTree += node => { if (node is PanelContainer row) Callable.From(() => CartLine(row)).CallDeferred(); };
        foreach (var row in lines.GetChildren().OfType<PanelContainer>()) Callable.From(() => CartLine(row)).CallDeferred();

        window.VisibilityChanged += () => { if (!window.Visible) foreach (var option in CharacterDetailsSkin.Tree(this).Concat(CharacterDetailsSkin.Tree(_overlay)).OfType<OptionButton>()) option.GetPopup().Hide(); };
        Resized += Arrange;
    }

    public override void _EnterTree() { GetViewport().SizeChanged += Refit; Callable.From(Refit).CallDeferred(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= Refit;
    public override Vector2 _GetMinimumSize() => (L.Stage * _scale).Ceil();

    /// <summary>The original stage at its own size, uniformly smaller only on screens that cannot hold it.</summary>
    private void Refit()
    {
        if (!IsInsideTree()) return;
        float scale = L.Scale(GetViewportRect().Size);
        if (scale == _scale && _stage.Scale == new Vector2(scale, scale)) return;
        _scale = scale; UpdateMinimumSize(); Window.ResetSize(); Arrange();
        if (Window.IsInsideTree()) Window.Position = ((GetViewportRect().Size - Window.Size) / 2).Max(Vector2.Zero).Floor();
    }
    private void Arrange()
    {
        _stage.Position = Vector2.Zero; _stage.Scale = new Vector2(_scale, _scale);
        // A stage shrunk for a small screen filters its art and text smoothly; at its own size it stays pixel-exact.
        _stage.TextureFilter = _scale < 1 ? TextureFilterEnum.Linear : TextureFilterEnum.ParentNode;
        _grip.Position = L.Drag.Position * _scale; _grip.Size = L.Drag.Size * _scale;
    }

    // Art -------------------------------------------------------------------------------------------------

    internal static StyleBoxTexture Art(LayoutNode image, float shade = 1)
    {
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Local.Texture(image.Texture!), RegionRect = new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH), ModulateColor = new Color(shade, shade, shade) };
        // Plates drawn larger or smaller than their art (touch targets, the search field) keep their borders.
        float edge = Mathf.Min(6, Mathf.Floor(Mathf.Min(image.SrcW, image.SrcH) / 4f));
        box.TextureMarginLeft = box.TextureMarginRight = box.TextureMarginTop = box.TextureMarginBottom = edge;
        box.SetContentMarginAll(0); box.ContentMarginLeft = box.ContentMarginRight = 4; return box;
    }
    /// <summary>Places a control at its stage rectangle and keeps it there when its content changes later.</summary>
    private static void Move(Control control, Rect2 rect)
    {
        control.CustomMinimumSize = Vector2.Zero; control.SetAnchorsPreset(LayoutPreset.TopLeft);
        bool watched = control.HasMeta("pus_stage_rect"); control.SetMeta("pus_stage_rect", rect);
        Settle(control);
        // Fonts and styles moved over from the native control settle their minimum sizes a frame later.
        if (!watched) control.MinimumSizeChanged += () => Callable.From(() => Settle(control)).CallDeferred();
        Callable.From(() => Settle(control)).CallDeferred();
    }
    private static void Settle(Control control)
    {
        if (!IsInstanceValid(control) || control.GetParent() is Container || control.GetMeta("pus_stage_rect").AsRect2() is var rect && control.GetRect() == rect) return;
        control.Position = rect.Position; control.Size = rect.Size;
    }
    private static void Ink(Control control, Color ink, int size)
    {
        ClassicStorePanel.Font(control, size); control.AddThemeColorOverride("font_color", ink);
    }
    /// <summary>Skins a button with an original UIF button (normal, pressed, hover, disabled images) at its rectangle.</summary>
    private static void Place(Button button, LayoutNode art, string? caption, Rect2? rect = null, Color? ink = null)
    {
        var images = art.Images.ToArray(); var label = art.Strings.FirstOrDefault();
        StyleBox State(int tag, float shade = 1) => Art(images.FirstOrDefault(i => i.Tag == tag) ?? images[0], shade);
        button.AddThemeStyleboxOverride("normal", State(ButtonState.Normal)); button.AddThemeStyleboxOverride("pressed", State(ButtonState.Pressed));
        button.AddThemeStyleboxOverride("hover", State(ButtonState.Hover)); button.AddThemeStyleboxOverride("hover_pressed", State(ButtonState.Pressed));
        button.AddThemeStyleboxOverride("disabled", State(ButtonState.Disabled, 0.55f)); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var color = ink ?? label?.Color ?? L.CardButtonInk;
        ClassicStorePanel.Font(button, 12);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, color);
        button.AddThemeColorOverride("font_disabled_color", color.Darkened(0.45f));
        if (caption != null) button.Text = caption;
        if (button is not OptionButton) button.Icon = null;
        button.ClipText = true; button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        if (button.TooltipText.Length == 0 && button.Text.Length > 0) button.TooltipText = button.Text;
        // Inside a native container (cart rows) the container keeps placing the button.
        if (button.GetParent() is null or Container) return;
        Move(button, rect ?? L.Plate(art));
    }

    // Tabs ------------------------------------------------------------------------------------------------

    private void QueueTabs()
    {
        if (_tabsQueued) return; _tabsQueued = true;
        Callable.From(BuildTabs).CallDeferred();
    }
    /// <summary>Mirrors the native category buttons on the original tab plates; a tab press is the native button's press.</summary>
    private void BuildTabs()
    {
        _tabsQueued = false; if (!IsInstanceValid(_tabs)) return;
        foreach (var old in _plates) { _tabs.RemoveChild(old); old.QueueFree(); }
        _plates.Clear();
        var natives = _categoryList.GetChildren().OfType<Button>().Where(b => !b.IsQueuedForDeletion()).ToArray();
        var art = _art.Find("btn_tab1")!;
        for (int i = 0; i < natives.Length; i++)
        {
            var native = natives[i];
            bool selected = native.GetMeta("pus_category_selected", false).AsBool();
            var tab = ClassicStorePanel.Owned(new Button
            {
                Name = native.Name.ToString().Replace("pus_category_", "pus_tab_"), Text = native.Text, FocusMode = FocusModeEnum.None,
                TooltipText = native.Text, Icon = native.Icon, IconAlignment = HorizontalAlignment.Right, Alignment = HorizontalAlignment.Center,
            });
            tab.SetMeta("pus_tab_selected", selected);
            _tabs.AddChild(tab);
            var rect = L.Tab(i, natives.Length);
            Place(tab, art, null, new Rect2(rect.Position - L.TabRow.Position, rect.Size), selected ? L.TabSelectedInk : L.TabInk);
            ClassicStorePanel.Font(tab, L.TabFont);
            if (selected) { tab.AddThemeStyleboxOverride("normal", tab.GetThemeStylebox("pressed")); tab.AddThemeStyleboxOverride("hover", tab.GetThemeStylebox("pressed")); }
            if (tab.Icon != null)
            {
                tab.AddThemeConstantOverride("icon_max_width", 14); tab.AddThemeConstantOverride("h_separation", 4);
                foreach (string state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) tab.AddThemeColorOverride(state, new Color("f2c94c"));
            }
            tab.Pressed += () => { if (IsInstanceValid(native)) native.EmitSignal(BaseButton.SignalName.Pressed); };
            _plates.Add(tab);
        }
    }

    // Cards and pager -------------------------------------------------------------------------------------

    /// <summary>Lays a native catalogue card out as the original card: name, icon cell, price, chips, Buy and Add to Basket.</summary>
    private void Card(PanelContainer card)
    {
        if (!IsInstanceValid(card) || card.HasMeta("pus_myko_card") || card.GetChildren().OfType<VBoxContainer>().FirstOrDefault() is not { } box) return;
        card.SetMeta("pus_myko_card", true);
        card.CustomMinimumSize = L.Card; card.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var face = ClassicStorePanel.Owned(new ClassicStoreMykoCard(card, _art.Find("item1")!) { Name = "pus_card_face", MouseFilter = MouseFilterEnum.Ignore });
        var overlay = card.GetChildren().OfType<Control>().LastOrDefault(c => c is not Container);
        card.AddChild(face); if (overlay != null) card.MoveChild(face, overlay.GetIndex());
        if (box.GetChildren().OfType<Label>().FirstOrDefault() is { } name)
        {
            name.Reparent(face); Move(name, L.CardName); name.AutowrapMode = TextServer.AutowrapMode.Off; name.MaxLinesVisible = -1;
            name.HorizontalAlignment = HorizontalAlignment.Center; name.VerticalAlignment = VerticalAlignment.Center;
            name.ClipText = true; name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; name.MouseFilter = MouseFilterEnum.Pass;
            Ink(name, L.CardNameInk, 13);
        }
        if (box.GetChildren().OfType<PanelContainer>().FirstOrDefault() is { } icon)
        {
            icon.Reparent(face); Move(icon, L.CardIcon); icon.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        }
        var priceCaption = ClassicStorePanel.Owned(new Label { Name = "pus_card_price_caption", Text = "Price :", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
        face.AddChild(priceCaption); Move(priceCaption, L.CardPriceCaption); Ink(priceCaption, Colors.White, L.CardCaptionFont);
        if (box.GetChildren().OfType<HBoxContainer>().FirstOrDefault() is { } price)
        {
            price.Reparent(face); price.Name = "pus_card_price"; price.Alignment = BoxContainer.AlignmentMode.Begin;
            foreach (var gem in price.GetChildren().OfType<TextureRect>()) gem.Visible = false;
            var labels = price.GetChildren().OfType<Label>().ToArray();
            foreach (var label in labels) { Ink(label, label == labels[^1] ? L.CardPriceInk : new Color("a0937c"), label == labels[^1] ? 13 : L.CardOldPriceFont); if (label.Text == "→") label.Visible = false; }
            if (labels.Length > 0 && !labels[^1].Text.EndsWith(" KC")) labels[^1].Text += " KC";
            // The price follows the "Price :" caption; a long sale price (old and new) takes the caption's place.
            var shown = labels.Where(l => l.Visible).ToArray();
            float width = shown.Sum(l => Plugin.Kit.Bold.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.GetThemeFontSize("font_size")).X) + Math.Max(0, shown.Length - 1) * price.GetThemeConstant("separation");
            float captionEnd = L.CardPriceCaption.Position.X + Plugin.Kit.Bold.GetStringSize(priceCaption.Text, HorizontalAlignment.Left, -1, L.CardCaptionFont).X + 3;
            priceCaption.Visible = captionEnd + width <= L.CardPriceEnd;
            float x = priceCaption.Visible ? captionEnd : L.CardPriceCaption.Position.X;
            Move(price, new Rect2(x, L.CardPrice.Position.Y, L.CardPriceEnd - x, L.CardPrice.Size.Y));
        }
        box.Visible = false;
        if (overlay != null)
        {
            foreach (var chip in overlay.GetChildren().OfType<Control>())
            {
                foreach (var label in CharacterDetailsSkin.Tree(chip).OfType<Label>()) ClassicStorePanel.Font(label, L.CardChipFont);
                chip.VisibilityChanged += () => QueueChips(overlay); chip.MinimumSizeChanged += () => QueueChips(overlay);
            }
            Chips(overlay);
        }
        foreach (var (id, rect, art, caption, add) in new[] { ("pus_card_buy", L.CardBuy, L.CardBuyArt, "Buy", false), ("pus_card_add", L.CardAdd, L.CardAddArt, "Add to Basket", true) })
        {
            var button = ClassicStorePanel.Owned(new Button { Name = id, FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop });
            face.AddChild(button); Place(button, _art.Find(art)!, caption, rect);
            button.TooltipText = add ? "Add this item to your basket" : "Look at this item and buy it";
            button.Pressed += () => NativeMarket.StoreCardAction(Window, card, add);
            face.Buttons.Add(button);
        }
        card.ThemeChanged += face.Refresh; face.Refresh();
    }
    internal bool Pending => NativeMarket.StorePending(Window);
    private static void QueueChips(Control overlay)
    {
        if (overlay.HasMeta("pus_chips_queued")) return; overlay.SetMeta("pus_chips_queued", true);
        Callable.From(() => { if (IsInstanceValid(overlay)) { overlay.RemoveMeta("pus_chips_queued"); Chips(overlay); } }).CallDeferred();
    }
    /// <summary>
    /// In-cart and sale-time chips in a row on the second value line; when both do not fit, the in-cart count moves onto
    /// the icon cell's lower corner like a stack count. The featured star sits on the icon cell's upper corner.
    /// </summary>
    private static void Chips(Control overlay)
    {
        var chips = overlay.GetChildren().OfType<Control>().ToArray();
        foreach (var chip in chips) { chip.SetAnchorsPreset(LayoutPreset.TopLeft); chip.GrowHorizontal = GrowDirection.End; chip.Size = chip.GetCombinedMinimumSize(); }
        var line = chips.Where(c => c.Visible && c.Name.ToString() is "pus_cart_chip" or "pus_timer_chip").ToList();
        if (line.Count > 1 && L.CardChips.X + line.Sum(c => c.Size.X) + (line.Count - 1) * L.CardChipGap > L.CardPriceEnd && line[0].Name == "pus_cart_chip")
        {
            line[0].Position = L.CardIconCell.End - line[0].Size + new Vector2(3, 3); line.RemoveAt(0);
        }
        float x = L.CardChips.X;
        foreach (var chip in line) { chip.Position = new Vector2(x, L.CardChips.Y); x += chip.Size.X + L.CardChipGap; }
        foreach (var badge in chips.Where(c => c.Name == "pus_featured_badge")) { badge.Scale = new Vector2(L.CardBadgeScale, L.CardBadgeScale); badge.Position = L.CardBadge; }
    }

    private int Pages => Math.Max(1, (int)Math.Ceiling(Rows / (double)L.Rows));
    private int Rows => _gridScroll.Visible ? (int)Math.Ceiling(_grid.GetChildren().OfType<Control>().Count(c => c.Visible) / (double)L.Columns) : 0;
    private int Page => Math.Clamp((int)Math.Round(_gridScroll.ScrollVertical / L.PageHeight), 0, Pages - 1);
    /// <summary>Pads the grid to whole pages, so every page starts at a multiple of three rows.</summary>
    private void UpdatePager()
    {
        int pad = Rows == 0 ? 0 : (int)((Pages * L.Rows - Rows) * (L.Card.Y + L.CardGapY));
        if (_gridRoom.GetThemeConstant("margin_bottom") != pad) _gridRoom.AddThemeConstantOverride("margin_bottom", pad);
        if (_resetPage) { _resetPage = false; _gridScroll.ScrollVertical = 0; }
        _page.Text = $"{Page + 1} / {Pages}";
        _previous.Disabled = Page == 0; _next.Disabled = Page >= Pages - 1;
    }
    private void Turn(int delta)
    {
        int page = Math.Clamp(Page + delta, 0, Pages - 1);
        _gridScroll.ScrollVertical = (int)(page * L.PageHeight);
        Callable.From(UpdatePager).CallDeferred(); UpdatePager();
    }

    // Basket ----------------------------------------------------------------------------------------------

    private void FitCartLabel(Label label)
    {
        var size = new Vector2(L.Basket.Size.X - 16, 0);
        if (label.CustomMinimumSize != size) label.CustomMinimumSize = size;
    }
    /// <summary>
    /// A native cart line on the original basket row: icon cell (pointer), name, steppers on the original quantity
    /// buttons, price and the X plate.
    /// </summary>
    private void CartLine(PanelContainer row)
    {
        if (!IsInstanceValid(row) || row.HasMeta("pus_myko_line")) return;
        row.SetMeta("pus_myko_line", true);
        var box = new StyleBoxTexture { Texture = BasketRow(), TextureMarginTop = 24, TextureMarginBottom = 22, TextureMarginLeft = 6, TextureMarginRight = 6 };
        box.ContentMarginLeft = L.BasketIcons ? 13 : 10; box.ContentMarginRight = 6; box.ContentMarginTop = 5; box.ContentMarginBottom = 5;
        row.AddThemeStyleboxOverride("panel", box); row.CustomMinimumSize = new Vector2(0, L.BasketRowHeight);
        var line = row.GetChildren().OfType<HBoxContainer>().FirstOrDefault();
        if (L.BasketIcons)
        {
            var cell = _art.Find(L.BasketRow)!.Images.First(i => i.SrcX == (int)L.BasketIconCell.Position.X);
            row.Draw += () => row.DrawTextureRectRegion(Plugin.Kit.Local.Texture(cell.Texture!), new Rect2(8, (row.Size.Y - 46) / 2, 46, 46), new Rect2(cell.SrcX, cell.SrcY, cell.SrcW, cell.SrcH));
        }
        else if (line?.GetChildren().OfType<TextureRect>().FirstOrDefault() is { } icon) icon.Visible = false;
        foreach (var label in CharacterDetailsSkin.Tree(row).OfType<Label>())
        {
            bool price = label.GetParent() is HBoxContainer parent && parent.GetChildren().OfType<TextureRect>().Any();
            Ink(label, label.GetParent() is VBoxContainer ? L.CardNameInk : price ? new Color("ff8040") : Colors.White, 12);
            if (!price && label.GetParent() is HBoxContainer) label.CustomMinimumSize = new Vector2(L.StepperCount, 0);
        }
        var confirm = _art.Find(L.Confirm)!;
        foreach (var button in CharacterDetailsSkin.Tree(row).OfType<Button>())
        {
            var (art, side) = button.TooltipText switch
            {
                "One less" => (confirm.Find(L.StepDown), L.StepperSide),
                "One more" => (confirm.Find(L.StepUp), L.StepperSide),
                "Remove from the cart" => (_art.Find(L.BasketRow)!.Find(L.BasketRemove), L.RemoveSide),
                _ => (null, 0f),
            };
            if (art == null) continue;
            Place(button, art, ""); button.CustomMinimumSize = new Vector2(side, side); button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        }
    }
    private Texture2D? BasketRow()
    {
        if (_basketRow != null && IsInstanceValid(_basketRow)) return _basketRow;
        var assets = Plugin.Kit.Local.Image(_art.Find(L.BasketRow)!.Images.First().Texture!); if (assets == null) return null;
        using var image = Image.CreateEmpty(238, 54, false, Image.Format.Rgba8); int y = 0;
        foreach (var part in new[] { L.BasketRowArtTop, L.BasketRowArtMiddle, L.BasketRowArtBottom })
        {
            using var piece = assets.GetRegion((Rect2I)part); piece.Convert(Image.Format.Rgba8);
            image.BlitRect(piece, new Rect2I(Vector2I.Zero, piece.GetSize()), new Vector2I(0, y));
            y += (int)part.Size.Y;
        }
        return _basketRow = ImageTexture.CreateFromImage(image);
    }

    // Keys ------------------------------------------------------------------------------------------------

    private void Escape() { if (!ClassicStorePanel.EscapeOverlay(this, _overlay, _modals)) _close.EmitSignal(BaseButton.SignalName.Pressed); }
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (!Window.IsVisibleInTree() || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        Escape(); GetViewport().SetInputAsHandled();
    }
}

/// <summary>The window art of the original store: background, strip, logo, basket header and value plates.</summary>
public partial class ClassicStoreMykoStage : Control
{
    private readonly LayoutNode _art;
    public ClassicStoreMykoStage(LayoutNode art) { _art = art; MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Nearest; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
        foreach (var image in _art.Images)
        {
            var rect = new Rect2(image.Position, image.SizeVec);
            if (ClassicStoreMykoLayout.OmittedArt.Contains(rect) || Plugin.Kit.Local.Texture(image.Texture!) is not { } texture) continue;
            DrawTextureRectRegion(texture, rect, new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH));
        }
    }
}

/// <summary>The original card's icon cell under a native card's moved parts; outlines the native active state.</summary>
public partial class ClassicStoreMykoCard : Control
{
    private readonly PanelContainer _card;
    private readonly LayoutNode _cell;
    public readonly List<Button> Buttons = new();
    public ClassicStoreMykoCard(PanelContainer card, LayoutNode item)
    {
        _card = card; _cell = item.Images.First(i => i.W == 53);
    }
    public void Refresh()
    {
        if (!IsInstanceValid(_card)) return;
        bool pending = GetParent()?.GetParent() is { } && FindStore() is { } store && store.Pending;
        foreach (var button in Buttons) button.Disabled = pending;
        QueueRedraw();
    }
    private ClassicStoreMykoPanel? FindStore()
    {
        Node? node = this; while (node != null && node is not ClassicStoreMykoPanel) node = node.GetParent();
        return node as ClassicStoreMykoPanel;
    }
    public override void _Draw()
    {
        var cell = ClassicStoreMykoLayout.CardIconCell;
        if (Plugin.Kit.Local.Texture(_cell.Texture!) is { } texture) DrawTextureRectRegion(texture, cell, new Rect2(_cell.SrcX, _cell.SrcY, _cell.SrcW, _cell.SrcH));
        if (_card.GetMeta(HotNames.PusCardActive, false).AsBool()) DrawRect(new Rect2(Vector2.One, Size - 2 * Vector2.One), new Color("cfb575"), false, 1);
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName PusCardActive = "pus_card_active";
}
