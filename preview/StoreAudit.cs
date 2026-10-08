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
        var checks = new List<string>(); var screens = new List<object>();
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
            var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildPowerUpStoreClassicUiPreview", variant)!; AddChild(layer);
            var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicStoreSkin.Apply(window.Body)!;
            T Find<T>(string name) where T : Control => Descendants(window).OfType<T>().Single(c => c.Name == "pus_" + name);
            var cart = (PowerUpStoreCart)DetailField(world, "_pusCart")!;
            async Task Capture(string state)
            {
                await Frames(); Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " window fits viewport " + window.GetGlobalRect());
                Require(window.Body.GetRect() == ClassicStoreLayout.Body(panel.Size), state + " native body matches declarative rectangle " + window.Body.GetRect());
                var close = Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close");
                Require(close.GetRect() == ClassicStoreLayout.Close(panel.Size.X), state + " original CLOSE actual bounds");
                foreach (var c in new[] { Find<Control>("categories"), Find<Control>("cart"), Find<Control>("grid_scroll") }.Where(c => c.IsVisibleInTree()))
                    Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " column fits frame " + c.Name);
                foreach (var modal in new[] { Find<PanelContainer>("details"), Find<PanelContainer>("gift_box") }.Where(m => m.IsVisibleInTree()))
                    Require(window.GetGlobalRect().Encloses(modal.GetGlobalRect()), state + " active modal stays inside native window " + modal.GetGlobalRect());
                foreach (var c in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("classic_store_control") && c is Label or Button or LineEdit))
                {
                    Require(c.GetThemeFont("font") == Plugin.Kit.Bold, state + " shared bold font " + c.Name);
                    if (c is Button b && b is not OptionButton && b.Text.Length > 0)
                    {
                        if (b.HasMeta("pus_category_selected")) Require(b.ClipText && b.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && b.TooltipText.StartsWith(b.Text), state + " category retains native ellipsis and full caption tooltip " + b.Text);
                        else Require(b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X + 4 <= b.Size.X, state + " button caption fits " + b.Text + " " + b.Size);
                    }
                }
                foreach (var c in Find<GridContainer>("grid").GetChildren().OfType<PanelContainer>())
                {
                    Require(c.HasMeta("pus_card_id"), state + " actual native catalogue card metadata");
                    Require(c.GetThemeStylebox("panel") is StyleBoxFlat box && box.CornerRadiusTopLeft == 0, state + " dynamic card retains Classic material after native refresh");
                }
                string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
            }
            try
            {
                await Frames(16); DetailCall(world, "FitPowerUpStore"); await Frames(); await Capture(stateVariant);
                if (touch) { Require(Find<Button>("checkout").Size.Y >= 40, "Touch checkout retains native minimum hit target"); continue; }
                if (variant == "pus-details")
                {
                    var description = Find<Label>("details_description"); description.Text = string.Join(" ", Enumerable.Repeat("This item can be used during your adventures in Moradon and other zones.", 20)); description.Visible = true;
                    await Frames(); var scroll = Find<ScrollContainer>("description_scroll"); Require(scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page && scroll.Size.Y == 84, "Long details scroll inside fixed eighty-four-pixel area"); await Capture("pus-long-description");
                    int oldUnits = cart.Units; await Click(Find<Button>("details_buy"));
                    Require(DetailField(world, "_pusPending")!.ToString() == "BuyNow" && Find<Button>("details_buy").Disabled && Find<Button>("details_add").Disabled, "Actual Buy now enters native pending state and disables duplicate actions");
                    await KeyInput(Key.Escape); Require(Find<Control>("overlay").Visible && window.Visible, "Pending Buy now Escape retains protected native details"); await Capture("pus-buy-now-pending");
                    DetailCall(world, "OnShoppingMallPurchase", PowerUpStoreResult.Unavailable, 12_500); await Frames();
                    Require(Find<PanelContainer>("details").IsVisibleInTree() && Find<Label>("details_status").Visible && cart.Units == oldUnits, "Native Buy-now refusal preserves details and existing cart"); await Capture("pus-buy-now-failed");
                    await Click(Find<Button>("details_buy")); DetailCall(world, "OnShoppingMallPurchase", PowerUpStoreResult.Succeeded, 12_000); await Frames();
                    Require(!Find<Control>("overlay").Visible && cart.Units == oldUnits && Find<Label>("cart_status").Text.Contains("mailbox"), "Native Buy-now success preserves existing cart and mailbox delivery"); await Capture("pus-buy-now-success");
                }
                if (variant == "pus-short") Require(Find<Button>("checkout").Disabled && Find<Label>("shortfall").Visible, "Insufficient cash keeps native disabled checkout and shortfall");
                if (variant != "pus-category") continue;
                var query = Find<LineEdit>("query");
                var timers = (List<(Label Label, DateTime EndsAt)>)DetailField(world, "_pusTimerLabels")!;
                Require(timers.Count > 0, "Native sale timer remains attached to catalogue");
                timers[0] = (timers[0].Label, DateTime.UtcNow.AddSeconds(-1)); DetailCall(world, "TickPusTimers");
                Require((bool)DetailField(world, "_pusRefreshing")! && timers[0].Label.Text == "", "Native expired sale timer requests catalogue refresh");
                var catalogue = ((List<PowerUpStoreEntry>)DetailField(world, "_pusCatalog")!).ToList(); DetailCall(world, "OnShoppingMallCatalog", catalogue); await Frames();
                Require(!(bool)DetailField(world, "_pusRefreshing")!, "Native catalogue reply clears refresh state and reprices cart");
                var start = window.Position; var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var at = grip.GetGlobalRect().Position + new Vector2(100, 12);
                GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true }, true);
                GetViewport().PushInput(new InputEventMouseMotion { Position = at + new Vector2(12, 8), GlobalPosition = at + new Vector2(12, 8), Relative = new Vector2(12, 8), ButtonMask = MouseButtonMask.Left }, true);
                GetViewport().PushInput(new InputEventMouseButton { Position = at + new Vector2(12, 8), GlobalPosition = at + new Vector2(12, 8), ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
                Require(window.Position == start + new Vector2(12, 8), "Actual store header drag forwards native HudLayout"); window.Position = start;
                await Click(query); foreach (char c in "zzmissing") await KeyInput(Key.None, c);
                Require(query.Text == "zzmissing" && Find<GridContainer>("grid").GetChildCount() == 0 && Find<Label>("grid_empty").Visible, "Actual typed query updates native empty catalogue"); await Capture("pus-no-match");
                await KeyInput(Key.Enter); Require(DetailField(world, "_pusPending")!.ToString() == "None", "Query Enter never buys");
                await Click(Find<Button>("category_1")); Require(query.Text == "" && Find<GridContainer>("grid").GetChildCount() > 0, "Actual category click clears query and restores native catalogue");
                var sort = Find<OptionButton>("sort"); await Click(sort); Require(sort.GetPopup().Visible, "Native sort popup opens"); await Capture("pus-sort-menu");
                await KeyInput(Key.Escape); Require(!sort.GetPopup().Visible && window.Visible, "Sort popup Escape closes only popup"); await Click(sort);
                await KeyInput(Key.Down); if (sort.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down); await KeyInput(Key.Enter);
                Require(sort.Selected == 1 && !sort.GetPopup().Visible, "Sort keyboard retains native selection callback");
                var card = Find<GridContainer>("grid").GetChildren().OfType<PanelContainer>().First();
                var icon = Descendants(card).OfType<PanelContainer>().Single(c => c.Name == "pus_card_icon"); var hover = icon.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                Require(world.ItemTooltipVisible, "Actual catalogue icon hover opens complete native item tooltip"); await Capture("pus-item-tooltip"); DetailCall(world, "HideItemTooltip");
                var savedLines = cart.Lines.ToArray();
                var cartIcon = Descendants(Find<VBoxContainer>("cart_lines")).OfType<TextureRect>().First(); hover = cartIcon.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                Require(world.ItemTooltipVisible, "Actual cart icon hover opens native item tooltip"); DetailCall(world, "HideItemTooltip");
                int lineCount = savedLines.Length; await Click(Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "Remove from the cart"));
                Require(cart.Lines.Count() == lineCount - 1, "Actual cart remove updates native line count");
                await Click(Find<Button>("clear_cart")); Require(cart.IsEmpty && Find<Label>("cart_empty").Visible && Find<Button>("clear_cart").Disabled, "Actual Clear all restores empty cart and disabled action");
                foreach (var saved in savedLines) cart.Add(saved.Entry, saved.Count); DetailCall(world, "RenderPusCart"); await Frames();
                int before = cart.Units; await Click(card); Require(Find<PanelContainer>("details").IsVisibleInTree(), "Actual catalogue card opens native details");
                await KeyInput(Key.Escape); Require(!Find<Control>("overlay").Visible && window.Visible, "Details Escape returns to open store"); await Click(card);
                await Click(Find<Button>("details_add")); Require(cart.Units == before + 1 && !Find<Control>("overlay").Visible, "Actual Add to cart retains native cart and closes details");
                var plus = Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "One more"); await Click(plus); Require(cart.Units == before + 2, "Actual cart stepper increases one unit");
                var minus = Descendants(Find<VBoxContainer>("cart_lines")).OfType<Button>().First(b => b.TooltipText == "One less"); await Click(minus); Require(cart.Units == before + 1, "Actual cart stepper decreases one unit");
                await Click(Find<Button>("gift")); Require(Find<PanelContainer>("gift_box").IsVisibleInTree(), "Actual gift button opens native recipient picker");
                var giftQuery = Find<LineEdit>("gift_query"); await Click(giftQuery); await KeyInput(Key.Escape); Require(!Find<Control>("overlay").Visible && window.Visible, "Focused gift query Escape closes modal before store");
                await Click(Find<Button>("gift")); await Click(giftQuery); foreach (char c in "Rikka") await KeyInput(Key.None, c); await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout); await Frames();
                Require(Find<VBoxContainer>("gift_list").GetChildCount() > 0, "Actual gift typing preserves debounced contact matching");
                await KeyInput(Key.Enter); Require((string)DetailField(world, "_pusGiftChecking")! == "Rikka", "Gift query Enter checks native recipient without buying");
                DetailCall(world, "OnShoppingMallRecipient", PowerUpStoreResult.RecipientNotAllowed, "Rikka", 72, 211); Require(Find<Label>("gift_status").Visible && !Find<PanelContainer>("gift_card").Visible, "Native membership refusal retains picker and warning"); await Capture("pus-recipient-refused");
                await KeyInput(Key.Enter); DetailCall(world, "OnShoppingMallRecipient", PowerUpStoreResult.Succeeded, "Rikka", 72, 211); await Frames();
                Require(Find<PanelContainer>("gift_card").Visible && Find<Label>("recipient_name").Text == "Rikka", "Repeated Enter retains native verified recipient card");
                var confirm = Descendants(Find<PanelContainer>("gift_card")).OfType<Button>().Single(b => b.Text == "Confirm recipient"); await Click(confirm);
                Require(Find<PanelContainer>("gift_banner").Visible && !Find<Control>("overlay").Visible, "Actual recipient confirmation updates native cart banner"); await Capture("pus-recipient-confirmed");
                await Click(Find<Button>("checkout")); Require(DetailField(world, "_pusPending")!.ToString() == "Cart" && Find<Button>("checkout").Disabled, "Actual checkout enters native pending state and prevents duplicate buy"); await Capture("pus-pending");
                DetailCall(world, "OnShoppingMallPurchase", PowerUpStoreResult.PriceChanged, 12_500); await Frames(); Require(cart.Units > 0 && (bool)DetailField(world, "_pusRefreshing")!, "Native price-change refusal keeps cart and requests refresh"); await Capture("pus-price-changed");
                await Click(Find<Button>("checkout")); DetailCall(world, "OnShoppingMallPurchase", PowerUpStoreResult.Succeeded, 10_000); await Frames();
                Require(cart.IsEmpty && !Find<PanelContainer>("gift_banner").Visible && Find<Label>("cart_status").Text.Contains("Gift sent"), "Native gift success clears cart and recipient while retaining mailed-delivery status"); await Capture("pus-purchase-success");
                DetailCall(world, "OnShoppingMallOpen", (short)-5, (short)0); await Frames();
                Require(Find<Label>("grid_empty").Visible && Find<Label>("grid_empty").Text.Contains("zone"), "Native zone refusal retains store restriction message"); await Capture("pus-zone-refused");
                DetailCall(world, "OnShoppingMallOpen", (short)1, (short)0); await Frames();
                GetWindow().Size = new Vector2I(1000, 740); DetailCall(world, "FitPowerUpStore"); await Frames(); await Capture("pus-compact");
                GetWindow().Size = new Vector2I(1600, 1000); DetailCall(world, "FitPowerUpStore"); await Frames(); await Capture("pus-wide");
                GetWindow().Size = new Vector2I(1200, 850); DetailCall(world, "FitPowerUpStore"); await Frames();
                query.GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && !(bool)DetailField(world, "_pusShown")!, "Focused store query Escape closes native store");
            }
            finally { layer.Free(); world.Free(); Platform.Override = null; }
        }
        net.Free();
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-store.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic store audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
