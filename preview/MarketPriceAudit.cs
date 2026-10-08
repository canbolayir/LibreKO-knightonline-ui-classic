using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using KnightOnlineUiClassic;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureMarketPriceAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        void Premium(int value) => typeof(Net).GetMethod("SeedPreviewPremium", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(offline, new object[] { value, value, 720 });
        Premium(1); var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("MARKET_PRICE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p }, true); await Frames();
        }
        async Task KeyInput(Key key)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildMarketPriceClassicUiPreview")!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicMarketPriceSkin.Apply(window.Body)!;
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var query = Find<LineEdit>("item_search_query"); var results = Find<VBoxContainer>("item_search_results");
        var chart = Find<PriceChart>("market_price_chart"); var status = Find<Label>("market_price_status"); var updated = Find<Label>("market_price_updated");
        async Task Capture(string state)
        {
            await Frames(); Require(window.Size == panel.CustomMinimumSize && GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete frame fits viewport");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("market_price_expected_rect")))
            {
                Require(c.GetRect() == c.GetMeta("market_price_expected_rect").AsRect2(), state + " actual bounds " + c.Name + " " + c.GetRect());
                Require(new Rect2(Vector2.Zero, panel.Size).Encloses(c.GetRect()), state + " control fits panel " + c.Name);
                if (c is Button b && b is not OptionButton && b.Text.Length > 0) Require(b.GetThemeFont("font").GetStringSize(b.Text, HorizontalAlignment.Left, -1, b.GetThemeFontSize("font_size")).X + 8 <= b.Size.X, state + " button caption fits " + b.Text);
            }
            foreach (var row in results.GetChildren().OfType<PanelContainer>())
            {
                Require(row.HasMeta("classic_market_row"), state + " native refreshed result uses Classic composition");
                var canvas = row.GetChildren().OfType<Control>().Single();
                foreach (var c in canvas.GetChildren().OfType<Control>()) Require(canvas.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " row part fits " + c.GetType().Name + " " + c.GetRect());
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        try
        {
            await Frames(); DetailCall(world, "OpenMarketPrice", 0); window.Position = new Vector2(80, 65); await Frames(); await Capture("empty");
            Require(!chart.HasData && status.Visible && query.KeepEditingOnTextSubmit, "Initial selection prompt retains empty chart and repeated query editing");
            await Click(Find<Button>("market_price_search")); Require(status.Visible && !chart.HasData && (int)DetailField(world, "_marketPriceAsked")! == 0, "Search without item does not request item zero");
            var start = window.Position; var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag"); var at = grip.GetGlobalRect().Position + new Vector2(120, 12);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = at + new Vector2(12, 7), GlobalPosition = at + new Vector2(12, 7), Relative = new Vector2(12, 7), ButtonMask = MouseButtonMask.Left }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at + new Vector2(12, 7), GlobalPosition = at + new Vector2(12, 7) }, true); await Frames();
            Require(window.Position == start + new Vector2(12, 7), "Actual header drag forwards native HudLayout"); window.Position = start;
            query.GrabFocus(); query.Text = "Raptor"; await KeyInput(Key.Enter); Require(results.GetChildCount() > 0 && query.HasFocus(), "Query Enter produces native item variants"); await Capture("results");
            query.Text = "zz-no-such-item-zz"; await KeyInput(Key.Enter); Require(results.GetChildCount() == 0, "Repeated query Enter refreshes without stale rows"); await Capture("no-match");
            query.Text = "Raptor"; await Click(Find<Button>("item_search_find")); Require(results.GetChildCount() > 0, "Actual Search restores results");
            var scroll = Find<ScrollContainer>("item_search_scroll"); var rail = Find<Control>("market_price_scroll_rail");
            Require(rail.Visible && scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page, "Long native result list exposes Classic rail");
            var down = rail.GetChildren().OfType<TextureButton>().Last(); await Click(down);
            Require(scroll.ScrollVertical > 0, "Actual Classic rail arrow advances native scroll range");
            scroll.ScrollVertical = 0; await Frames();
            var names = Find<OptionButton>("item_search_name");
            if (names.ItemCount > 1)
            {
                await Click(names); await KeyInput(Key.Down); if (names.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down); await KeyInput(Key.Enter);
                Require(names.Selected == 1 && results.GetChildCount() > 0, "Actual matched-name popup changes native results");
            }
            foreach (string tab in new[] { "Property", "Reverse", "Basic" })
            {
                await Click(Find<Button>("item_search_tab_" + tab)); Require(Find<Button>("item_search_tab_" + tab).ButtonPressed, "Native tab changes to " + tab); await Capture("tab-" + tab.ToLowerInvariant());
            }
            var group = Find<OptionButton>("item_search_group");
            if (group.Visible && group.ItemCount > 1)
            {
                await Click(group); await KeyInput(Key.Down); if (group.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down); await KeyInput(Key.Enter);
                Require(group.Selected == 1 && results.GetChildCount() > 0, "Actual family selector filters native variant group");
            }
            var level = Find<OptionButton>("item_search_level"); await Click(level); Require(level.GetPopup().Visible, "Native upgrade selector opens"); await Capture("level-menu");
            await KeyInput(Key.Escape); Require(!level.GetPopup().Visible && window.Visible, "Popup Escape leaves parent open");
            if (level.ItemCount > 1)
            {
                await Click(level); await KeyInput(Key.Down); if (level.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down); await KeyInput(Key.Enter);
                Require(level.Selected == 1 && results.GetChildCount() > 0, "Popup keyboard applies actual upgrade filter"); await Capture("filtered");
            }
            var select = Descendants(results.GetChild(0)).OfType<Button>().Single(); await Click(select);
            int item = (int)DetailField(world, "_marketPriceItem")!; Require(item > 0 && Find<ItemSlotView>("market_price_item").Item.ItemId == item, "Actual Select retains native item id and selected slot");
            Require(Find<Label>("market_price_name").TooltipText == ItemData.DisplayName(item), "Selected name retains full tooltip");
            var selectedSlot = Find<ItemSlotView>("market_price_item"); var hover = selectedSlot.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
            Require(world.ItemTooltipVisible && Descendants((PanelContainer)DetailField(world, "_itemTipPanel")!).OfType<Label>().Any(l => l.Text == ItemData.DisplayName(item)), "Actual selected-slot hover builds complete native item tooltip");
            DetailCall(world, "HideItemTooltip");
            hover = ((Control)results.GetChild(0)).GetGlobalRect().Position + new Vector2(40, 12);
            GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
            Require(world.ItemTooltipVisible, "Actual result-row hover retains full native item tooltip callback"); DetailCall(world, "HideItemTooltip");
            await Click(Find<Button>("market_price_search")); Require((int)DetailField(world, "_marketPriceAsked")! == item, "Actual Search Price invokes native request");
            var days = new[] { new MarketPriceDay(750_000_000, 930_000_000, 670_000_000), new MarketPriceDay(690_000_000, 810_000_000, 590_000_000), new MarketPriceDay(720_000_000, 850_000_000, 620_000_000), new MarketPriceDay(780_000_000, 950_000_000, 700_000_000), new MarketPriceDay(760_000_000, 890_000_000, 640_000_000) };
            var reply = new MarketPriceReply(MarketPrice.History, item, days, 12_345, new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
            DetailCall(world, "OnMarketPrice", reply); await Frames(); Require(chart.HasData && !status.Visible && updated.Text.Contains("2026"), "History preserves five-day chart and local update stamp"); await Capture("history");
            Require(chart.GetThemeDefaultFont() == Plugin.Kit.Bold, "Native chart drawing uses same bold font as labels");
            foreach (var part in new[] { ("MAX", days[0].Max), ("MIN", days[0].Min), ("AVG", days[0].Average - 15_000_000) })
            {
                float x = (float)typeof(PriceChart).GetMethod("Column", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, new object[] { 0 })!;
                float y = (float)typeof(PriceChart).GetMethod("ValueY", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, new object[] { part.Item2 })!;
                var p = chart.GlobalPosition + new Vector2(x, y); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames();
                Require(chart.TooltipText.Contains(part.Item1) && chart.TooltipText.Contains("gold"), "Actual chart hover preserves " + part.Item1 + " value");
            }
            DetailCall(world, "OnMarketPrice", reply with { ItemId = item + 1, Trades = 9 }); Require(Find<Label>("market_price_trades").Text == reply.Trades.ToString("n0"), "Reply for another item cannot replace selected history");
            DetailCall(world, "OnMarketPrice", reply with { Result = MarketPrice.TooSoon }); Require(status.Visible && chart.HasData, "Retry retains last chart and shows native status"); await Capture("retry");
            DetailCall(world, "OnMarketPrice", reply with { Result = 99 }); Require(status.Text.Contains("99"), "Error code retained"); await Capture("error");
            DetailCall(world, "OnMarketPrice", reply with { Result = MarketPrice.NoHistory }); Require(!chart.HasData && updated.Text == "" && status.Visible, "No-history clears data and update stamp");
            string noHistory = status.Text; DetailCall(world, "SelectMarketPriceItem", item); await Click(Find<Button>("market_price_search"));
            Require(!chart.HasData && updated.Text == "" && status.Text == noHistory, "Cached no-history never shows zero data or year-one date"); await Capture("no-history");
            DetailCall(world, "RequestMarketPriceWindow", item); Require((bool)DetailField(world, "_marketPriceOpenOnReply")!, "External request retains open-on-reply");
            query.GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && !(bool)DetailField(world, "_marketPriceOpenOnReply")!, "Focused query Escape closes and cancels pending opening");
            DetailCall(world, "OnMarketPrice", reply); await Frames(); Require(!window.Visible, "Late history response cannot reopen closed screen");
            DetailCall(world, "OpenMarketPrice", 0); await Frames(); Require(window.Visible && !chart.HasData && (int)DetailField(world, "_marketPriceItem")! == 0, "Native reopen resets item and history");
            GetWindow().Size = new Vector2I(900, 630); await Frames(); window.Position = new Vector2(30, 32); await Capture("compact-viewport");
            await Click(Find<Button>("market_price_cancel")); Require(!window.Visible && !(bool)DetailField(world, "_marketPriceShown")!, "Actual Cancel follows native close callback");
            Premium(0); DetailCall(world, "OpenMarketPrice", 0); Require(!window.Visible && world.GetChildren().OfType<Notice>().Any(), "Premium gate retains original native notice");
            foreach (var notice in world.GetChildren().OfType<Notice>()) notice.Free();
        }
        finally { layer.Free(); world.Free(); offline.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-market-price.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic market price audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
