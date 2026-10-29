using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureStoreAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1200, 850); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1600, 1000), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var checks = new List<string>(); var screens = new List<object>(); string design = "classic";
        void Require(bool value, string text) { if (!value) throw new Exception("STORE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control c)
        {
            var p = c.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        async Task KeyInput(Key key, uint unicode = 0)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false }); await Frames();
        }
        foreach (string stateVariant in new[] { "pus-loading", "pus-empty", "pus-category", "pus-details", "pus-gift", "pus-gift-search", "pus-gift-card", "pus-gift-set", "pus-short", "pus-touch-category", "pus-touch-details", "pus-touch-gift", "pus-touch-gift-card" })
        {
            bool touch = stateVariant.StartsWith("pus-touch-"); Platform.Override = touch ? Platform.UiMode.Touch : Platform.UiMode.Pointer;
            string variant = touch ? stateVariant.Replace("pus-touch-", "pus-") : stateVariant;
            var world = PreviewFixtures.Prepared(new WorldRoot()); var layer = (CanvasLayer)DetailCall(world, "BuildPowerUpStoreClassicUiPreview", variant)!; AddChild(layer);
            var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicStoreSkin.Apply(window.Body)!;
            // The original Power Up Store art is bundled (assets/myko) and the default; store-fallback-audit turns it off.
            bool myko = panel is ClassicStoreMykoPanel; design = myko ? "myko-art" : "classic";
            if (myko == ClassicFallback) throw new Exception("STORE_AUDIT_FAIL: expected the " + (ClassicFallback ? "Classic fallback" : "bundled 2.x art") + " store, found " + design + " (art root " + Plugin.Kit.Local.Root + ")");
            T Find<T>(string name) where T : Control => Descendants(window).OfType<T>().Single(c => c.Name == "pus_" + name);
            var cart = (PowerUpStoreCart)DetailField(world, "ShoppingMall.Model.Cart")!;
            async Task Capture(string state)
            {
                await Frames(); Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " window fits viewport " + window.GetGlobalRect());
                if (myko) MykoStoreGeometry(state, window, (ClassicStoreMykoPanel)panel, Require);
                else
                {
                    Require(window.Body.GetRect() == ClassicStoreLayout.Body(panel.Size), state + " native body matches declarative rectangle " + window.Body.GetRect());
                    var close = Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close");
                    Require(close.GetRect() == ClassicStoreLayout.Close(panel.Size.X), state + " original CLOSE actual bounds");
                    foreach (var c in new[] { Find<Control>("tabs"), Find<Control>("well"), Find<Control>("cart"), Find<Control>("grid_scroll"), Find<Control>("tools"), Find<Control>("wallet") }.Where(c => c.IsVisibleInTree()))
                        Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " column fits frame " + c.Name);
                    StoreGeometry(state, window, (ClassicStorePanel)panel, touch, Require);
                }
                foreach (var modal in new[] { Find<PanelContainer>("details"), Find<PanelContainer>("gift_box") }.Where(m => m.IsVisibleInTree()))
                    Require(window.GetGlobalRect().Encloses(modal.GetGlobalRect()), state + " active modal stays inside native window " + modal.GetGlobalRect());
                var giftScroll = Descendants(Find<PanelContainer>("gift_box")).OfType<ScrollContainer>().FirstOrDefault(s => s.IsVisibleInTree());
                if (giftScroll != null && !touch)
                {
                    var giftRows = Descendants(giftScroll).OfType<VBoxContainer>().First(); var giftBar = giftScroll.GetVScrollBar();
                    Require(giftScroll.Size.Y == 188, state + " gift list shows four whole rows " + giftScroll.Size);
                    if (giftBar.Visible)
                        foreach (var row in giftRows.GetChildren().OfType<Control>().Where(c => c.Visible))
                            Require(row.GetGlobalRect().End.X + 6 <= giftBar.GetGlobalRect().Position.X + .5f, state + " gift row keeps the gap before the scroll bar " + row.GetGlobalRect());
                }
                foreach (var c in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("classic_store_control") && c is Label or Button or LineEdit))
                {
                    Require(c.GetThemeFont("font") == Plugin.Kit.Bold, state + " shared bold font " + c.Name);
                    if (c is Button b && b is not OptionButton && b.Text.Length > 0)
                    {
                        float room = b.Size.X - 2 * ClassicStoreLayout.TabPadding - (b.Icon != null ? ClassicStoreLayout.TabIcon + 4 : 0);
                        if (b.HasMeta("pus_tab_selected") && b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X > room)
                            Require(b.ClipText && b.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && b.TooltipText.StartsWith(b.Text), state + " long category tab keeps ellipsis and full caption tooltip " + b.Text);
                        else if (b.HasMeta("pus_tab_selected")) Require(b.ClipText && b.TooltipText.StartsWith(b.Text), state + " category tab caption fits its plate " + b.Text + " " + b.Size);
                        else if (b.HasMeta("pus_category_selected")) Require(b.ClipText && b.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && b.TooltipText.StartsWith(b.Text), state + " category retains native ellipsis and full caption tooltip " + b.Text);
                        else if (myko && b.ClipText && b.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && b.TooltipText.Length > 0 && b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X + 4 > b.Size.X)
                            Require(true, state + " long caption on an original plate keeps ellipsis and tooltip " + b.Text + " " + b.Size);
                        else Require(b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X + 4 <= b.Size.X, state + " button caption fits " + b.Text + " " + b.Size);
                    }
                }
                foreach (var c in Find<GridContainer>("grid").GetChildren().OfType<PanelContainer>())
                {
                    Require(c.HasMeta("pus_card_id"), state + " actual native catalogue card metadata");
                    if (myko) Require(c.GetThemeStylebox("panel") is StyleBoxTexture art && art.RegionRect.Size == ClassicStoreMykoLayout.Card, state + " dynamic card retains the original card art after native refresh");
                    else Require(c.GetThemeStylebox("panel") is StyleBoxFlat box && box.CornerRadiusTopLeft == 0, state + " dynamic card retains Classic material after native refresh");
                }
                string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
            }
            try
            {
                await Frames(16); DetailCall(world, "ShoppingMall.FitPowerUpStore"); await Frames(); await Capture(stateVariant);
                if (touch) { Require(Find<Button>("checkout").Size.Y >= 40, "Touch checkout retains native minimum hit target"); continue; }
                if (variant == "pus-details")
                {
                    var description = Find<Label>("details_description"); description.Text = string.Join(" ", Enumerable.Repeat("This item can be used during your adventures in Moradon and other zones.", 20)); description.Visible = true;
                    await Frames(); var scroll = Find<ScrollContainer>("description_scroll"); Require(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page && scroll.Size.Y == ClassicStoreLayout.DescriptionHeight, "Long details scroll inside five whole description lines");
                    Require(description.GetGlobalRect().End.X + 6 <= scroll.GetVScrollBar().GetGlobalRect().Position.X + .5f && Math.Abs(description.GetGlobalRect().GetCenter().X - scroll.GetGlobalRect().GetCenter().X) < 1, "Description keeps the gap before the scroll bar and stays centred " + description.GetGlobalRect()); await Capture("pus-long-description");
                    int oldUnits = cart.Units; await Click(Find<Button>("details_buy"));
                    Require(DetailField(world, "ShoppingMall.Model.Pending")!.ToString() == "BuyNow" && Find<Button>("details_buy").Disabled && Find<Button>("details_add").Disabled, "Actual Buy now enters native pending state and disables duplicate actions");
                    await KeyInput(Key.Escape); Require(Find<Control>("overlay").Visible && window.Visible, "Pending Buy now Escape retains protected native details"); await Capture("pus-buy-now-pending");
                    DetailCall(world, "OnStorePurchase", PowerUpStoreResult.Unavailable, 12_500); await Frames();
                    Require(Find<PanelContainer>("details").IsVisibleInTree() && Find<Label>("details_status").Visible && cart.Units == oldUnits, "Native Buy-now refusal preserves details and existing cart"); await Capture("pus-buy-now-failed");
                    await Click(Find<Button>("details_buy")); DetailCall(world, "OnStorePurchase", PowerUpStoreResult.Succeeded, 12_000); await Frames();
                    Require(!Find<Control>("overlay").Visible && cart.Units == oldUnits && Find<Label>("cart_status").Text.Contains("mailbox"), "Native Buy-now success preserves existing cart and mailbox delivery"); await Capture("pus-buy-now-success");
                }
                if (variant == "pus-short") Require(Find<Button>("checkout").Disabled && Find<Label>("shortfall").Visible, "Insufficient cash keeps native disabled checkout and shortfall");
                if (variant != "pus-category") continue;
                var query = Find<LineEdit>("query");
                var timers = (List<(Label Label, DateTime EndsAt)>)DetailField(world, "ShoppingMall.CatalogView.pusTimerLabels")!;
                Require(timers.Count > 0, "Native sale timer remains attached to catalogue");
                timers[0] = (timers[0].Label, DateTime.UtcNow.AddSeconds(-1)); DetailCall(world, "ShoppingMall.TickTimers");
                Require((bool)DetailField(world, "ShoppingMall.refreshing")! && timers[0].Label.Text == "", "Native expired sale timer requests catalogue refresh");
                var catalogue = ((List<PowerUpStoreEntry>)DetailField(world, "ShoppingMall.Model.Catalog")!).ToList(); DetailCall(world, "ShoppingMall.OnCatalog", catalogue); await Frames();
                Require(!(bool)DetailField(world, "ShoppingMall.refreshing")!, "Native catalogue reply clears refresh state and reprices cart");
                var start = window.Position; var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var at = grip.GetGlobalRect().Position + new Vector2(100, 12);
                GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true }, true);
                GetViewport().PushInput(new InputEventMouseMotion { Position = at + new Vector2(12, 8), GlobalPosition = at + new Vector2(12, 8), Relative = new Vector2(12, 8), ButtonMask = MouseButtonMask.Left }, true);
                GetViewport().PushInput(new InputEventMouseButton { Position = at + new Vector2(12, 8), GlobalPosition = at + new Vector2(12, 8), ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
                Require(window.Position == start + new Vector2(12, 8), "Actual store header drag forwards native HudLayout"); window.Position = start;
                await Click(query); foreach (char c in "zzmissing") await KeyInput(Key.None, c);
                Require(query.Text == "zzmissing" && Find<GridContainer>("grid").GetChildCount() == 0 && Find<Label>("grid_empty").Visible, "Actual typed query updates native empty catalogue"); await Capture("pus-no-match");
                await KeyInput(Key.Enter); Require(DetailField(world, "ShoppingMall.Model.Pending")!.ToString() == "None", "Query Enter never buys");
                await Click(Find<Button>("tab_1")); Require(query.Text == "" && Find<GridContainer>("grid").GetChildCount() > 0, "Actual category tab click clears query and restores native catalogue");
                Require(Find<Button>("tab_1").GetMeta("pus_tab_selected").AsBool() && (int)DetailField(world, "ShoppingMall.CatalogView.Category")! == 1, "Category tab press selects the native category and the tab shows it");
                var other = Descendants(Find<Control>("tabs")).OfType<Button>().First(b => b.Name != "pus_tab_1"); string otherId = other.Name.ToString()["pus_tab_".Length..];
                await Click(other); Require(DetailField(world, "ShoppingMall.CatalogView.Category")!.ToString() == otherId && Find<Button>("tab_" + otherId).GetMeta("pus_tab_selected").AsBool() && !Find<Button>("tab_1").GetMeta("pus_tab_selected").AsBool(), "Second category tab moves the native selection " + otherId);
                await Capture("pus-tab-selected");
                await Click(Find<Button>("tab_1"));
                var sort = Find<OptionButton>("sort"); await Click(sort); Require(sort.GetPopup().Visible, "Native sort popup opens"); await Capture("pus-sort-menu");
                await KeyInput(Key.Escape); Require(!sort.GetPopup().Visible && window.Visible, "Sort popup Escape closes only popup"); await Click(sort);
                await KeyInput(Key.Down); if (sort.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down); await KeyInput(Key.Enter);
                Require(sort.Selected == 1 && !sort.GetPopup().Visible, "Sort keyboard retains native selection callback");
                var card = Find<GridContainer>("grid").GetChildren().OfType<PanelContainer>().First();
                var icon = Descendants(card).OfType<PanelContainer>().Single(c => c.Name == "pus_card_icon"); var hover = icon.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                Require(world.ItemTooltipVisible, "Actual catalogue icon hover opens complete native item tooltip"); await Capture("pus-item-tooltip"); DetailCall(world, "ItemTooltip.Hide");
                if (myko) { await MykoActions(world, window, cart, Find<GridContainer>("grid"), Click, Frames, Capture, Require); card = Find<GridContainer>("grid").GetChildren().OfType<PanelContainer>().First(); }
                var savedLines = cart.Lines.ToArray();
                var cartIcon = Descendants(Find<VBoxContainer>("cart_lines")).OfType<TextureRect>().First(); hover = cartIcon.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                Require(world.ItemTooltipVisible, "Actual cart icon hover opens native item tooltip"); DetailCall(world, "ItemTooltip.Hide");
                int lineCount = savedLines.Length; await Click(Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "Remove from the cart"));
                Require(cart.Lines.Count() == lineCount - 1, "Actual cart remove updates native line count");
                await Click(Find<Button>("clear_cart")); Require(cart.IsEmpty && Find<Label>("cart_empty").Visible && Find<Button>("clear_cart").Disabled, "Actual Clear all restores empty cart and disabled action");
                foreach (var saved in savedLines) cart.Add(saved.Entry, saved.Count); DetailCall(world, "ShoppingMall.CartView.RenderCart"); await Frames();
                int before = cart.Units; await Click(card); Require(Find<PanelContainer>("details").IsVisibleInTree(), "Actual catalogue card opens native details");
                await KeyInput(Key.Escape); Require(!Find<Control>("overlay").Visible && window.Visible, "Details Escape returns to open store"); await Click(card);
                await Click(Find<Button>("details_add")); Require(cart.Units == before + 1 && !Find<Control>("overlay").Visible, "Actual Add to cart retains native cart and closes details");
                var plus = Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "One more"); await Click(plus); Require(cart.Units == before + 2, "Actual cart stepper increases one unit");
                var minus = Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "One less"); await Click(minus); Require(cart.Units == before + 1, "Actual cart stepper decreases one unit");
                await Click(Find<Button>("gift")); Require(Find<PanelContainer>("gift_box").IsVisibleInTree(), "Actual gift button opens native recipient picker");
                var giftQuery = Find<LineEdit>("gift_query"); await Click(giftQuery); await KeyInput(Key.Escape); Require(!Find<Control>("overlay").Visible && window.Visible, "Focused gift query Escape closes modal before store");
                await Click(Find<Button>("gift")); await Click(giftQuery); foreach (char c in "Rikka") await KeyInput(Key.None, c); await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout); await Frames();
                Require(Find<VBoxContainer>("gift_list").GetChildCount() > 0, "Actual gift typing preserves debounced contact matching");
                await KeyInput(Key.Enter); Require((string)DetailField(world, "PowerUpStoreGift.GiftChecking")! == "Rikka", "Gift query Enter checks native recipient without buying");
                DetailCall(world, "PowerUpStoreGift.OnRecipient", PowerUpStoreResult.RecipientNotAllowed, "Rikka", 72, 211); Require(Find<Label>("gift_status").Visible && !Find<PanelContainer>("gift_card").Visible, "Native membership refusal retains picker and warning"); await Capture("pus-recipient-refused");
                await KeyInput(Key.Enter); DetailCall(world, "PowerUpStoreGift.OnRecipient", PowerUpStoreResult.Succeeded, "Rikka", 72, 211); await Frames();
                Require(Find<PanelContainer>("gift_card").Visible && Find<Label>("recipient_name").Text == "Rikka", "Repeated Enter retains native verified recipient card");
                var confirm = Descendants(Find<PanelContainer>("gift_card")).OfType<Button>().Single(b => b.Text == "Confirm recipient"); await Click(confirm);
                Require(Find<PanelContainer>("gift_banner").Visible && !Find<Control>("overlay").Visible, "Actual recipient confirmation updates native cart banner"); await Capture("pus-recipient-confirmed");
                await Click(Find<Button>("checkout")); Require(DetailField(world, "ShoppingMall.Model.Pending")!.ToString() == "Cart" && Find<Button>("checkout").Disabled, "Actual checkout enters native pending state and prevents duplicate buy"); await Capture("pus-pending");
                DetailCall(world, "OnStorePurchase", PowerUpStoreResult.PriceChanged, 12_500); await Frames(); Require(cart.Units > 0 && (bool)DetailField(world, "ShoppingMall.refreshing")!, "Native price-change refusal keeps cart and requests refresh"); await Capture("pus-price-changed");
                await Click(Find<Button>("checkout")); DetailCall(world, "OnStorePurchase", PowerUpStoreResult.Succeeded, 10_000); await Frames();
                Require(cart.IsEmpty && !Find<PanelContainer>("gift_banner").Visible && Find<Label>("cart_status").Text.Contains("Gift sent"), "Native gift success clears cart and recipient while retaining mailed-delivery status"); await Capture("pus-purchase-success");
                DetailCall(world, "ShoppingMall.OnOpen", (short)-5, (short)0); await Frames();
                Require(Find<Label>("grid_empty").Visible && Find<Label>("grid_empty").Text.Contains("zone"), "Native zone refusal retains store restriction message"); await Capture("pus-zone-refused");
                DetailCall(world, "ShoppingMall.OnOpen", (short)1, (short)0); await Frames();
                GetWindow().Size = new Vector2I(1000, 740); DetailCall(world, "ShoppingMall.FitPowerUpStore"); await Frames(); await Capture("pus-compact");
                GetWindow().Size = new Vector2I(1600, 1000); DetailCall(world, "ShoppingMall.FitPowerUpStore"); await Frames(); await Capture("pus-wide");
                GetWindow().Size = new Vector2I(1200, 850); DetailCall(world, "ShoppingMall.FitPowerUpStore"); await Frames();
                query.GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && !(bool)DetailField(world, "ShoppingMall.shown")!, "Focused store query Escape closes native store");
            }
            finally { layer.Free(); world.Free(); Platform.Override = null; }
        }
        net.Free();
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-store.json", JsonSerializer.Serialize(new { nation, design, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic store audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }

    /// <summary>Actual rendered bounds of the store composition against <see cref="ClassicStoreLayout"/>.</summary>
    private static void StoreGeometry(string state, HudWindow window, ClassicStorePanel panel, bool touch, Action<bool, string> require)
    {
        T Part<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == "pus_" + name);
        const int inset = ClassicStoreLayout.Inset;
        var size = panel.Size; var body = window.Body.GetRect();
        require(panel.GetGlobalRect() == window.GetGlobalRect(), state + " plain frame covers the whole window " + panel.GetGlobalRect());
        require(body.Position.X == inset && size.X - body.End.X == inset && size.Y - body.End.Y == inset, state + " equal side and bottom insets inside the frame " + body);
        var title = Descendants(panel).OfType<Label>().Single(l => l.Text == "Power-Up Store");
        require(title.GetRect() == ClassicStoreLayout.Title(size.X) && Math.Abs(title.GetRect().GetCenter().X - size.X / 2) < .5f, state + " title centred in the title bar " + title.GetRect());
        var close = Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close");
        require(size.X - close.GetRect().End.X == inset && close.Position.Y == ClassicStoreLayout.TitleTop && !close.GetRect().Intersects(title.GetRect()), state + " CLOSE plate keeps the frame inset " + close.GetRect());
        var tools = Part<BoxContainer>("tools"); var wallet = Part<Control>("wallet");
        require(tools.Position == ClassicStoreLayout.Tools && tools.Size.Y == ClassicStoreLayout.ToolsHeight && !tools.Vertical, state + " search row at the top of the window " + tools.GetRect());
        foreach (var c in tools.GetChildren().OfType<Control>().Where(c => c.Visible))
            require(c.Size.Y == ClassicStoreLayout.ToolsHeight && tools.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " search row control at full height " + c.GetType().Name + " " + c.GetRect());
        require(wallet.GetRect() == ClassicStoreLayout.Wallet(size.X, wallet.GetCombinedMinimumSize()) && wallet.GetRect().End.X == body.End.X, state + " cash display right-aligned with the sidebar " + wallet.GetRect());
        require(tools.GetRect().End.X + 16 <= wallet.Position.X && tools.GetRect().End.Y <= ClassicStoreLayout.BodyTop - 8 && wallet.GetRect().End.Y <= ClassicStoreLayout.BodyTop, state + " search row and cash display never overlap " + tools.GetRect() + " " + wallet.GetRect());
        var tabs = Part<Control>("tabs"); var well = Part<PanelContainer>("well"); var cart = Part<Control>("cart");
        require(tabs.GetGlobalRect().Position.X == well.GetGlobalRect().Position.X && tabs.GetGlobalRect().End.Y == well.GetGlobalRect().Position.Y, state + " category tabs stand on the item well " + tabs.GetGlobalRect() + " " + well.GetGlobalRect());
        require(cart.GetGlobalRect().Position.Y == tabs.GetGlobalRect().Position.Y && cart.GetGlobalRect().End.Y == well.GetGlobalRect().End.Y, state + " sidebar spans the tab row and the item well " + cart.GetGlobalRect());
        require(well.GetGlobalRect().End.X + 10 == cart.GetGlobalRect().Position.X, state + " native column gap between well and sidebar " + well.GetGlobalRect());
        var plates = tabs.GetChildren().OfType<Button>().ToArray();
        var native = Part<VBoxContainer>("category_list").GetChildren().OfType<Button>().ToArray();
        require(plates.Length == native.Length && !Part<Control>("categories").Visible, state + " one tab per native category " + plates.Length);
        float height = touch ? ClassicStoreLayout.TouchTabHeight : ClassicStoreLayout.TabHeight;
        for (int i = 0; i < plates.Length; i++)
        {
            var plate = plates[i];
            require(plate.Size.Y == height && plate.Size.X >= plate.GetMeta("pus_tab_width").AsInt32() - 1 && tabs.GetGlobalRect().Encloses(plate.GetGlobalRect()), state + " tab plate keeps its caption width " + plate.Text + " " + plate.GetRect());
            bool rowEnd = i == plates.Length - 1 || plates[i + 1].Position.Y != plate.Position.Y;
            if (rowEnd && plates.Select(p => p.Position.Y).Distinct().Count() > 1) require(plate.GetRect().End.X == tabs.Size.X, state + " wrapped tab rows are justified to the well " + plate.Text + " " + plate.GetRect());
            if (plates.All(p => p.Position.Y == 0)) require(plate.Size.X == plate.GetMeta("pus_tab_width").AsInt32(), state + " single tab row keeps natural plates " + plate.Text);
            require(plate.Text == native[i].Text && plate.GetMeta("pus_tab_selected").AsBool() == native[i].GetMeta("pus_category_selected", false).AsBool(), state + " tab mirrors native category state " + plate.Text);
            for (int j = i + 1; j < plates.Length; j++) require(!plate.GetGlobalRect().Intersects(plates[j].GetGlobalRect()), state + " tabs never overlap " + plate.Text + " " + plates[j].Text);
        }
        int rows = plates.Select(p => p.Position.Y).Distinct().Count();
        require(rows <= 2 && (touch || window.Size.X < 960 || rows <= 1) && tabs.Size.Y == rows * height + Math.Max(0, rows - 1) * ClassicStoreLayout.TabGap, state + " tab rows " + rows + " at " + window.Size);
        require(plates.Count(p => p.GetMeta("pus_tab_selected").AsBool()) <= 1, state + " at most one selected tab");
        foreach (var plate in plates)
            require(plate.GetThemeColor("font_color") == ClassicStoreLayout.TabInk(plate.GetMeta("pus_tab_selected").AsBool()) && plate.GetThemeColor("font_hover_color") == ClassicStoreLayout.TabInk(true), state + " tab ink marks the selection " + plate.Text);
        var open = plates.FirstOrDefault(p => p.GetMeta("pus_tab_selected").AsBool());
        if (open != null && open.GetRect().End.Y == tabs.Size.Y && !Descendants(window).OfType<Control>().Single(c => c.Name == "pus_overlay").Visible && !Descendants(panel).OfType<OptionButton>().Any(o => o.GetPopup().Visible))
        {
            var image = panel.GetViewport().GetTexture().GetImage(); int edge = (int)well.GetGlobalRect().Position.Y;
            var under = image.GetPixel((int)open.GetGlobalRect().GetCenter().X, edge);
            var closed = plates.Where(p => p != open && p.GetRect().End.Y == tabs.Size.Y).Select(p => image.GetPixel((int)p.GetGlobalRect().GetCenter().X, edge)).ToArray();
            require(under.R < .05f && under.G < .05f && under.B < .05f && closed.All(c => c.R > .3f), state + " selected tab opens into the item well " + under + " " + string.Join(",", closed));
        }
        foreach (var card in Part<GridContainer>("grid").GetChildren().OfType<Control>())
            require(card.GetGlobalRect().Position.X >= well.GetGlobalRect().Position.X + ClassicStoreLayout.WellMargin && card.GetGlobalRect().End.X <= well.GetGlobalRect().End.X - ClassicStoreLayout.WellMargin, state + " card inside the item well " + card.GetGlobalRect());
        foreach (var modal in Descendants(window).OfType<PanelContainer>().Where(m => m.Name.ToString() is "pus_details" or "pus_gift_box" && m.IsVisibleInTree()))
        {
            var frame = modal.GetChildren().OfType<Control>().Single(c => c.Name == "pus_modal_frame");
            require(frame.GetGlobalRect() == modal.GetGlobalRect() && frame is ClassicStoreFrame, state + " modal uses the plain store frame " + modal.GetGlobalRect());
        }
    }
}
