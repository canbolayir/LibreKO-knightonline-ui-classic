using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureMerchantSearchAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        typeof(Net).GetMethod("SeedPreviewPremium", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(offline, new object[] { 1, 1, 720 });
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("MERCHANT_SEARCH_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control control)
        {
            var point = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }, true); await Frames();
        }
        async Task KeyInput(Key key, Viewport? viewport = null)
        {
            viewport ??= GetViewport();
            if (viewport is PopupMenu) viewport = GetViewport();
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildMerchantSearchClassicUiPreview")!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicMerchantSearchSkin.Apply(window.Body)!;
        var model = (MerchantSearch)DetailField(world, "_merchantSearch")!;
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == "merchant_search_" + name);
        var query = Find<LineEdit>("query"); var scope = Find<OptionButton>("scope");
        async Task Capture(string state)
        {
            await Frames();
            Require(window.Size == panel.CustomMinimumSize && GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete fixed window fits viewport");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("merchant_search_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("merchant_search_expected_rect").AsRect2(), state + " actual bounds " + control.Name + " " + control.GetRect());
                Require(new Rect2(Vector2.Zero, panel.Size).Encloses(control.GetRect()), state + " control stays inside panel " + control.Name);
                if (control is Button button && button.Text.Length > 0 && button is not OptionButton)
                    Require(button.GetThemeFont("font").GetStringSize(button.Text, HorizontalAlignment.Left, -1, button.GetThemeFontSize("font_size")).X + 8 <= button.Size.X, state + " full button caption fits " + button.Text);
            }
            var pages = Find<HBoxContainer>("pages");
            Require(pages.GetChildren().OfType<Button>().Count() == model.GroupPages().Count(), state + " page group has no stale duplicate controls");
            foreach (var button in pages.GetChildren().OfType<Button>())
                Require(pages.GetGlobalRect().Encloses(button.GetGlobalRect()), state + " page button fits footer " + button.Text);
            for (int row = 0; row < 10; row++)
            {
                bool populated = row < model.Visible().Count;
                Require(Find<Button>("whisper_" + row).Visible == populated && Find<Button>("move_" + row).Visible == populated && Find<Button>("view_" + row).Visible == populated, state + " row actions match data " + row);
                Require(Find<Label>("name_" + row).Text == (populated ? model.Visible()[row].ItemName : ""), state + " native item caption " + row);
                Require(Find<Label>("price_" + row).Text == (populated ? model.Visible()[row].Price.ToString("n0") : ""), state + " native price formatting " + row);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        try
        {
            await Frames(); window.Position = new Vector2(110, 84); await Frames(); await Capture("loading");
            var start = window.Position; var grip = Descendants(panel).OfType<Control>().Single(c => c.Name == "party_drag");
            var from = grip.GetGlobalRect().GetCenter(); var to = from + new Vector2(23, 17);
            GetViewport().PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to }, true); await Frames();
            Require(window.Position == start + new Vector2(23, 17), "Real viewport header drag forwards native HudLayout");
            window.Position = new Vector2(110, 84);
            var rows = Enumerable.Range(0, 95).Select(i => new MerchantSearchRow(i + 1, "Seller" + i, 120010000, 2_000_000_000 - i * 1_000_000, 0,
                i == 0 ? "A very long weapon name with an elemental attribute and upgrade (+10)" : (i % 3 == 0 ? "Iron Shield" : "Iron Sword") + " " + i.ToString("00"))).ToArray();
            DetailCall(world, "OnMerchantSearchRows", rows);
            DetailCall(world, "OnMerchantSearchRows", new[] { new MerchantSearchRow(100, "Buyer", 120010000, 500_000_000, 1, "Iron Sword (+8)") });
            DetailCall(world, "OnMerchantSearchLoaded"); await Frames(); await Capture("selling");
            Require(Find<Label>("name_0").TooltipText.Contains(rows[0].ItemName) && Find<Label>("name_0").TooltipText.Contains(rows[0].Seller), "Clipped long item retains full item and seller tooltip");
            await Click(Find<Button>("high_sort")); Require(model.Sort == MerchantSearchSort.PriceHighToLow && model.Visible()[0].Price == rows[0].Price, "Actual High to Low click sorts native rows");
            await Click(Find<Button>("low_sort")); Require(model.Sort == MerchantSearchSort.PriceLowToHigh && model.Visible()[0].Price == rows[^1].Price, "Actual Low to High click sorts native rows"); await Capture("low-to-high");
            await Click(Find<Button>("name_sort")); Require(model.Sort == MerchantSearchSort.Name && model.Visible()[0].ItemName == rows[0].ItemName, "Actual name sort preserves native ordinal ordering");
            query.GrabFocus(); query.Text = " shield "; await KeyInput(Key.Enter);
            Require(model.Filter == "shield" && model.Count == 31 && query.HasFocus(), "Focused query Enter trims and filters without leaving edit mode"); await Capture("filtered");
            query.Text = "sword"; await KeyInput(Key.Enter); Require(model.Count == 63, "Repeated Enter submits a second query");
            query.Text = "missing item"; await Click(Find<Button>("find")); Require(model.Count == 0 && Find<Label>("status").Text == "No matching items.", "Actual Search click shows empty result state"); await Capture("empty");
            await Click(Find<Button>("all")); Require(query.Text == "" && model.Count == 95 && model.Page == 0, "Actual View All resets filter and page");
            for (int page = 1; page <= 8; page++) await Click(Find<Button>("next"));
            Require(model.Page == 8 && Find<HBoxContainer>("pages").GetChildren().OfType<Button>().Select(b => b.Text).SequenceEqual(new[] { "9", "10" }), "Eight-page group changes at page nine");
            await Click(Find<HBoxContainer>("pages").GetChildren().OfType<Button>().Last()); Require(model.Page == 9 && model.Visible().Count == 5, "Actual numbered page click opens final partial page"); await Capture("last-page");
            await Click(Find<Button>("next")); Require(model.Page == 9, "Next does not exceed final page");
            await Click(Find<Button>("previous")); Require(model.Page == 8, "Previous returns one page");
            await Click(scope); Require(scope.GetPopup().Visible, "Actual scope button opens native options"); await Capture("scope-menu");
            await KeyInput(Key.Escape, scope.GetPopup()); Require(!scope.GetPopup().Visible && window.Visible, "Scope Escape closes only the popup");
            await Click(scope); Require(scope.GetPopup().Visible, "Scope reopens after Escape");
            await KeyInput(Key.Down, scope.GetPopup());
            if (scope.GetPopup().GetFocusedItem() != 1) await KeyInput(Key.Down, scope.GetPopup());
            Require(scope.GetPopup().GetFocusedItem() == 1, "Native popup arrow navigation focuses buying option: focused=" + scope.GetPopup().GetFocusedItem() + " visible=" + scope.GetPopup().Visible);
            await KeyInput(Key.Enter, scope.GetPopup());
            Require(!scope.GetPopup().Visible && model.Type == MerchantSearch.BuyingType && model.Count == 1, "Native scope keyboard selects buying stalls"); await Capture("buying");
            await Click(Find<Button>("view_0")); Require((int)DetailField(world, "_marketPriceAsked")! == 120010000 && (bool)DetailField(world, "_marketPriceOpenOnReply")!, "Actual View click retains premium price-history request");
            await Click(Find<Button>("move_0")); Require((string)DetailField(world, "_merchantSearchMoveTo")! == "Buyer", "Actual Move click retains selected seller request");
            await Click(Find<Button>("whisper_0")); Require(WhisperTarget(world) == "Buyer", "Actual Whisper click opens native private message for selected seller");
            var whispers = (CanvasLayer)DetailField(world, "_whisperLayer")!; foreach (var child in whispers.GetChildren().OfType<Control>()) child.Visible = false;
            query.GrabFocus(); await KeyInput(Key.Escape); Require(!window.Visible && !(bool)DetailField(world, "_merchantSearchShown")!, "Focused query Escape follows native close callback");
            DetailCall(world, "OnMerchantSearchOpen"); DetailCall(world, "OnMerchantSearchLoaded"); await Frames(); Require(window.Visible && model.Count == 0 && !scope.GetPopup().Visible, "Native reopen clears stale rows and popup");
            GetWindow().Size = new Vector2I(800, 600); await Frames(); window.Position = new Vector2(10, 34); await Capture("small-viewport");
            GetWindow().Size = new Vector2I(1000, 700); await Frames(); window.Position = new Vector2(110, 84);
            double readyBefore = (double)DetailField(world, "_merchantSearchReadyAt")!;
            await Click(Find<Button>("refresh")); double readyAfter = (double)DetailField(world, "_merchantSearchReadyAt")!;
            Require(readyAfter > readyBefore, "Actual Refresh advances native recast request deadline");
            await Click(Find<Button>("refresh")); Require((double)DetailField(world, "_merchantSearchReadyAt")! == readyAfter, "Repeated Refresh respects the cooldown without resetting its deadline");
            await Click(Find<Button>("cancel")); Require(!window.Visible && !(bool)DetailField(world, "_merchantSearchShown")!, "Actual Cancel follows native close callback");
        }
        finally { layer.Free(); world.Free(); offline.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-merchant-search.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic merchant search audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }

    /// <summary>The player addressed by the open private-message window whose input has focus.</summary>
    private static string? WhisperTarget(World world) =>
        (DetailField(world, "_whispers") as System.Collections.IDictionary)?.Values.Cast<object>()
            .Where(chat => Native.Get<LineEdit>(chat, "Input") is { } input && input.HasFocus() && Native.Get<HudWindow>(chat, "Window")!.Visible)
            .Select(chat => Native.Get<string>(chat, "Name")).FirstOrDefault();
}
