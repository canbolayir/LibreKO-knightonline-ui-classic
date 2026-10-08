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
    private async Task CaptureEquipViewAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildEquipViewClassicUiPreview")!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicEquipViewSkin.Apply(window.Body)!;
        window.SetMeta("content_open_anchor", true); window.Position = new Vector2(180, 80);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("EQUIPVIEW_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task KeyInput(Key key)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        async Task Click(Control control, MouseButton button = MouseButton.Left, bool twice = false)
        {
            var p = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = button, Pressed = true, DoubleClick = twice }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = button, Pressed = false }, true); await Frames();
        }
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        ItemSlotView[] Cells() => Descendants(panel).OfType<ItemSlotView>().OrderBy(c => c.Index).ToArray();
        Net.EquipmentView Snapshot(string name, int targetNation, bool empty = false, bool maximum = false) => (Net.EquipmentView)typeof(World).GetMethod("EquipViewPreviewSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { name, targetNation, empty, maximum })!;
        void Request(string name) => DetailCall(world, "RequestEquipmentView", name);
        void Reply(Net.EquipmentView view) => DetailCall(world, "OnEquipmentView", Net.EquipmentViewResult.Accepted, view);
        bool Busy() => (bool)DetailField(world, "_equipViewInFlight")!;
        var inv = (Inventory)typeof(World).GetProperty("Inv", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        var inventory = inv[InventoryConstants.InventoryStart];
        async Task Capture(string state, bool tooltip = false)
        {
            if (!tooltip) DetailCall(world, "HideItemTooltip"); await Frames();
            if (tooltip) { DetailCall(world, "UpdateInventoryTooltip"); await Frames(); }
            Require(window.Size == ClassicEquipViewLayout.Size, state + " fixed window size");
            Require(Find<Label>("inspect_summary").Text.Length == 0 || Find<Label>("inspect_status").Text.Length == 0, state + " summary and status share one row without overlap");
            foreach (var name in new[] { "inspect_name", "inspect_summary", "inspect_status" })
            {
                var label = Find<Label>(name);
                Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, 13).X <= label.Size.X, state + " complete native heading text " + name);
            }
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("inspect_expected_rect")))
            {
                var rect = c.GetMeta("inspect_expected_rect").AsRect2();
                Require((c.Position - rect.Position).Length() < .1f && (c.Size - rect.Size).Length() < .1f, state + " declared and actual bounds " + c.Name);
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " contained control " + c.Name);
            }
            var cells = Cells(); Require(cells.Length == 23 && cells.Select(c => c.Index).Distinct().Count() == 23, state + " all 23 unique native worn slots");
            foreach (var c in cells)
            {
                Require(c.Size == new Vector2(45, 45) && c.DragOut == null && c.CanDrop == null && c.Dropped == null, state + " original-size read-only socket " + c.Index);
                Require(c._GetDragData(Vector2.Zero).VariantType == Variant.Type.Nil && !c._CanDropData(Vector2.Zero, Variant.From(0)), state + " no inspection drag or inventory drop " + c.Index);
                var frame = c.GetChildren().OfType<ClassicInspectSocket>().Single();
                Require(frame.Position == new Vector2(-4, -3) && frame.Size == new Vector2(51, 52) && window.GetGlobalRect().Encloses(frame.GetGlobalRect()), state + " inventory frame geometry " + c.Index);
                Require((c.CountLabel.GetGlobalRect().End - (c.GetGlobalRect().End - new Vector2(2, 2))).Length() < .1f, state + " exact inventory count inset " + c.Index);
                Require(c.CountLabel.GetThemeFontSize("font_size") == 11 && c.CountLabel.GetThemeFont("font") == Plugin.Kit.Bold, state + " shared count font " + c.Index);
                var textures = c.GetChildren().OfType<TextureRect>().ToArray();
                Require(textures.Last().Texture != null && textures.Last().Visible == c.Item.IsEmpty, state + " correct empty socket artwork visibility " + c.Index);
                if (!c.Item.IsEmpty) Require(textures.First().Texture != null && ItemData.Icon(c.Item.ItemId).GetInstanceId() != ItemData.Icon(0).GetInstanceId(), state + " real item icon without placeholder " + c.Index);
            }
            foreach (var row in Find<VBoxContainer>("inspect_stats").GetChildren().OfType<HBoxContainer>())
                foreach (var label in row.GetChildren().OfType<Label>())
                {
                    Require(window.GetGlobalRect().Encloses(label.GetGlobalRect()), state + " native statistic bounds " + label.Text);
                    Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == 13, state + " uniform stat font " + label.Text);
                    Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, 13).X <= label.Size.X, state + " complete statistic text " + label.Text);
                }
            Require(inv[InventoryConstants.InventoryStart].ItemId == inventory.ItemId && inv[InventoryConstants.InventoryStart].Count == inventory.Count, state + " local inventory unchanged");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        try
        {
            await Frames(16); window.Position = new Vector2(180, 80);
            Request("Tester10"); await Capture("requesting"); Require(Busy(), "Request begins one native in-flight operation");
            Request("OtherPlayer"); Require((string)DetailField(world, "_equipViewPending")! == "Tester10", "Second target cannot overwrite pending request identity");
            Reply(Snapshot("OtherPlayer", nation)); Require(Busy() && Find<Label>("inspect_name").Text == "Tester10", "Wrong-name accepted reply is ignored");
            Reply(Snapshot("Tester10", nation)); await Capture("populated");
            Require(!Busy() && Find<VBoxContainer>("inspect_stats").GetChildren().OfType<HBoxContainer>().Count() == 15, "Native snapshot retains all fifteen statistic rows");
            Require(Cells().Count(c => c.Index >= InventoryConstants.CospreStart && !c.Item.IsEmpty) == 9, "All nine received costume items are visible");
            Require(Cells().Single(c => c.Index == InventoryConstants.RightHand).Item.Flag == (byte)ItemFlag.Sealed && Cells().Single(c => c.Index == InventoryConstants.RightHand).Item.Durability == 0, "Real sealed flag and zero durability preserved for tooltip");
            Require(Find<Label>("inspect_summary").Text.Contains("83/5"), "Rebirth retained in native summary");
            var original = window.Position; var p = Find<Control>("party_drag").GetGlobalRect().Position + new Vector2(120, 12);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            var delta = new Vector2(31, 17); GetViewport().PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - original - delta).Length() < 1, "Actual header drag uses native HudLayout " + original + " -> " + window.Position + " / grip " + p + " / locked " + window.Layout.Locked); await Capture("dragged");
            var weapon = Cells().Single(c => c.Index == InventoryConstants.RightHand);
            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(3, 3), GlobalPosition = new Vector2(3, 3) }, true); await Frames(2);
            p = weapon.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames();
            Require(world.ItemTooltipVisible, "Actual native item hover opens snapshot tooltip"); await Capture("weapon-tooltip-callback", tooltip: true);
            var tip = (Control)DetailField(world, "_itemTipPanel")!;
            Require(GetViewportRect().Encloses(tip.GetGlobalRect()), "Actual snapshot tooltip remains within viewport");
            Require(tip.Position.X > 0 && tip.Position.Y > 0, "Actual tooltip follows native mouse position");
            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(3, 3), GlobalPosition = new Vector2(3, 3) }, true); await Frames();
            Require(!world.ItemTooltipVisible, "Actual item hover exit hides snapshot tooltip");
            await Click(weapon); await Click(weapon, MouseButton.Right); await Click(weapon, MouseButton.Left, true); await KeyInput(Key.Enter);
            Require(window.Visible && !Busy() && weapon.Item.ItemId == 156210008, "Click right-click double-click and Enter cannot change inspected equipment or send request");
            Reply(Snapshot("Duplicate", nation)); Require(Find<Label>("inspect_name").Text == "Tester10", "Duplicate response cannot replace completed snapshot");
            Request("TwentyCharacterNameX"); Reply(Snapshot("TwentyCharacterNameX", nation == 1 ? 2 : 1, maximum: true)); await Capture("long-name-opposite-nation");
            Request("EmptyPlayer"); Reply(Snapshot("EmptyPlayer", nation, empty: true)); await Capture("empty-equipment"); Require(Cells().All(c => c.Item.IsEmpty), "Empty snapshot clears every normal and costume item immediately");
            foreach (var result in new[] { Net.EquipmentViewResult.NoSuchUser, Net.EquipmentViewResult.CannotChooseYourself, Net.EquipmentViewResult.NotInSameRegion, Net.EquipmentViewResult.NoViewEquipmentItem })
            {
                Request("Tester10"); DetailCall(world, "OnEquipmentView", result, default(Net.EquipmentView)); await Capture("refused-" + result);
                Require(!Busy() && Cells().All(c => c.Item.IsEmpty), "Refusal clears previous snapshot " + result);
            }
            Request("ClosedTarget"); await KeyInput(Key.Escape); Require(!window.Visible && Busy(), "Actual Escape retains outstanding request until reply");
            Request("ReopenTarget"); Require(!window.Visible && (string)DetailField(world, "_equipViewPending")! == "ClosedTarget", "Closed window cannot reopen over pending request");
            Reply(Snapshot("ClosedTarget", nation)); Require(!window.Visible && !Busy(), "Late closed reply cannot reopen or populate window");
            Request("FreshTarget"); Reply(Snapshot("FreshTarget", nation)); await Capture("reopened");
            await Click(Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close")); Require(!window.Visible && !(bool)DetailField(world, "_equipViewShown")!, "Actual close button updates native shown state");
            Request("FreshTarget"); Reply(Snapshot("FreshTarget", nation)); GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(280, 130); await Capture("wide-viewport");
            for (int i = 0; i < 12; i++) { Request("Tester10"); Reply(Snapshot("Tester10", nation, empty: i % 2 == 0)); Require(Find<VBoxContainer>("inspect_stats").GetChildren().OfType<HBoxContainer>().Count() == 15, "Same-frame rebuild does not retain old stat rows " + i); }
        }
        finally { layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-equipview.json", JsonSerializer.Serialize(new { nation, checks, screens, fixture = "Native read-only Equipment View callbacks, real controls and explicit offline snapshots", liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic equipment view audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
