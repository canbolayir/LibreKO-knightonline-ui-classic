using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureRebirthAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false))
            throw new Exception("Missing existing item content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size = new Vector2I(1000, 740);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ReplaceDialogs(request => request.Title == "Rebirth" ? new ClassicServiceNotice(request) : new KnightOnlineUiClassic.Windows.MessageBox(request));
        PluginHost.Ui.ExtendWindow("rebirth", ClassicRebirthSkin.Extend);
        var world = new World();
        var layer = (CanvasLayer)DetailCall(world, "BuildRebirthClassicUiPreview", nation)!;
        AddChild(layer);
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        await Frames();
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.SetMeta("content_open_anchor", true);
        window.Position = new Vector2(300, 100);
        var panel = window.GetChildren().OfType<ClassicRebirthPanel>().Single();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var accept = Find<Button>("rebirth_accept");
        var pick = (RebirthPick)DetailField(world, "_rebirthPick")!;
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("REBIRTH_AUDIT: " + text); checks.Add(text); }
        bool Busy() => (bool)DetailField(world, "_rebirthInFlight")!;
        bool NetBusy() => (bool)typeof(Net).GetField("_rebirthPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(net)!;
        Notice? Confirmation() => NativeRebirth.Confirmation(world);
        async Task KeyInput(Key key)
        {
            var viewport = GetViewport();
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); var viewport = GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        void Reply(params byte[] bytes)
        {
            var p = new Packet(GameOpcodes.GS_CLASS_CHANGE); p.WriteByte(7); p.WriteBytes(bytes);
            typeof(Net).GetMethod("HandleClassChange", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(net, new object[] { p });
        }
        async Task Capture(string state)
        {
            await Frames();
            Require(window.Size == ClassicRebirthLayout.Size, state + " fixed Classic frame dimensions");
            Require(new Rect2(Vector2.Zero, GetViewportRect().Size).Encloses(window.GetGlobalRect()), state + " frame fits viewport");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("rebirth_expected_rect")))
            {
                var expected = c.GetMeta("rebirth_expected_rect").AsRect2();
                Require((c.Position - expected.Position).Length() < .1 && (c.Size - expected.Size).Length() < .1, state + " actual bounds " + c.Name);
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " contained control " + c.Name);
                Require(c.GetThemeFont("font") == Plugin.Kit.Bold && c.GetThemeFontSize("font_size") == 13, state + " shared bold typography " + c.Name);
            }
            for (int row = 0; row < 5; row++)
            {
                Require(Find<Label>("rebirth_total_" + row).Text == "+" + (net.Sheet.RebirthBonusAtRow(row) + (net.Sheet.RebirthLevel >= 15 ? 0 : pick.PickedAt(row))), state + " total preserves existing bonus " + row);
                bool locked = Busy() || Confirmation() != null || net.Sheet.RebirthLevel >= 15 || net.Sheet.Level < 83 || (bool)DetailField(world, "_selfDead")!;
                Require(Find<Button>("rebirth_add_" + row).Disabled == (locked || !pick.CanAdd(row)), state + " add availability " + row);
                Require(Find<Button>("rebirth_remove_" + row).Disabled == (locked || !pick.CanRemove(row)), state + " remove availability " + row);
            }
            if (Confirmation() is { } notice)
            {
                var dialog = notice.GetChildren().OfType<ClassicServiceNotice>().Single();
                var frame = (Control)typeof(ClassicServiceNotice).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Require(frame.Size == new Vector2(344, 196), state + " original shared fee-frame dimensions");
                Require(GetViewportRect().Encloses(frame.GetGlobalRect()), state + " modal fits viewport");
                foreach (var label in Descendants(frame).OfType<Label>())
                { Require(frame.GetGlobalRect().Encloses(label.GetGlobalRect()), state + " modal text inside frame"); Require(label.GetThemeFont("font") == Plugin.Kit.Bold, state + " modal shared font"); }
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
        }
        try
        {
            await Capture("initial"); Require(accept.Disabled && pick.Remaining == 2, "Rebirth starts with exactly two unassigned points");
            Require(!net.SendRebirthStatChange(new byte[] { 1, 0, 0, 0, 0 }) && !net.SendRebirthStatChange(new byte[] { 2 }), "Network rejects incomplete and short allocations");
            Reply(1); Require(net.Sheet.RebirthLevel == 4, "Unsolicited success does not add a rebirth level");
            await Click(Find<Button>("rebirth_add_0")); await Capture("one-point");
            await Click(Find<Button>("rebirth_add_2")); await Capture("split-points");
            Require(pick.PickedAt(0) == 1 && pick.PickedAt(2) == 1 && !accept.Disabled, "Native row clicks allocate independent bonus points");
            await KeyInput(Key.Enter); Require(Confirmation() == null && !Busy() && !NetBusy(), "Service Enter never spends the qualification");
            await Click(Find<Button>("rebirth_remove_2")); await Click(Find<Button>("rebirth_add_0")); await Capture("one-stat");
            Require(pick.PickedAt(0) == 2 && pick.Complete, "Both points can be assigned to one stat");
            await Click(accept); await Capture("confirmation");
            var modal = Confirmation()!.GetChildren().OfType<ClassicServiceNotice>().Single();
            var modalFrame = (Control)typeof(ClassicServiceNotice).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modal)!;
            var modalBefore = modalFrame.Position; var modalPointer = modalFrame.GlobalPosition + new Vector2(40, 20); var modalDelta = new Vector2(-18, -18);
            GetViewport().PushInput(new InputEventMouseMotion { Position = modalPointer, GlobalPosition = modalPointer }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = modalPointer, GlobalPosition = modalPointer, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = modalPointer + modalDelta, GlobalPosition = modalPointer + modalDelta, Relative = modalDelta }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = modalPointer + modalDelta, GlobalPosition = modalPointer + modalDelta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((modalFrame.Position - modalBefore - modalDelta).Length() < 1, "Confirmation header supports native dragging");
            await Capture("confirmation-dragged");
            var oldNotice = Confirmation()!;
            var request = (DialogRequest)typeof(Notice).GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(oldNotice)!;
            DetailCall(world, "OnRebirthPressed"); Require(ReferenceEquals(oldNotice, Confirmation()), "Repeated submit creates only one confirmation");
            DetailCall(world, "EditRebirthPoint", 0, false); Require(pick.PickedAt(0) == 2, "Confirmation locks the captured allocation");
            await KeyInput(Key.Escape); await Capture("cancelled");
            Require(Confirmation() == null && pick.PickedAt(0) == 2 && !Busy(), "Confirmation Escape returns to the unchanged editable allocation");
            await Click(accept); request.Confirm(); Require(!Busy() && Confirmation() != null, "Stale modal callback cannot submit a replacement confirmation");
            await KeyInput(Key.Enter); await Capture("pending");
            Require(Busy() && NetBusy(), "Confirmation Enter sends exactly one native request");
            Require(!net.SendRebirthStatChange(new byte[] { 0, 2, 0, 0, 0 }), "Network independently rejects a second outstanding request");
            Reply(); Reply(2); Reply(1, 0); Require(Busy() && NetBusy(), "Malformed and unknown replies cannot finish a pending allocation");
            Reply(0); await Capture("refused");
            Require(!Busy() && !NetBusy() && pick.PickedAt(0) == 2 && !accept.Disabled, "Refusal retains picks and allows editing and retry");
            typeof(World).GetField("_selfDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, true);
            DetailCall(world, "RefreshRebirthUI"); await Capture("dead");
            DetailCall(world, "OnRebirthPressed"); Require(!Busy() && Confirmation() == null, "Dead character cannot spend the qualification");
            typeof(World).GetField("_selfDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, false);
            net.Sheet.SeedProgress(82, 0, 1); DetailCall(world, "RefreshRebirthUI"); await Capture("level-required");
            net.Sheet.SeedProgress(83, 0, 1); net.Sheet.SeedRebirth(15, 30, 0, 0, 0, 0); DetailCall(world, "RefreshRebirthUI"); await Capture("maximum-level");
            Require(accept.Disabled && !Find<Label>("rebirth_level").Text.Contains("16"), "Fifteen-stage maximum is preserved without advertising stage sixteen");
            net.Sheet.SeedRebirth(14, 27, 0, 0, 1, 0); DetailCall(world, "RefreshRebirthUI"); await Capture("final-stage");
            Require(Find<Label>("rebirth_status").Text.Length == 0, "Eligibility recovery clears stale restriction messages");
            net.Sheet.SeedRebirth(4, 3, 1, 2, 1, 1);
            DetailCall(world, "SetRebirthStatus", "Mekin refused the rebirth. Complete the qualification before trying again.", true);
            DetailCall(world, "RefreshRebirthUI"); await Capture("long-status");
            var viewport = GetViewport(); Vector2 before = window.Position, p = before + new Vector2(50, 20), delta = new(46, 28);
            viewport.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, Pressed = true, ButtonIndex = MouseButton.Left }, true);
            viewport.PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, Pressed = false, ButtonIndex = MouseButton.Left }, true); await Frames();
            Require((window.Position - before - delta).Length() < 1, "Native header drag retains complete frame geometry"); await Capture("dragged");
            await Click(accept); await KeyInput(Key.Enter); await KeyInput(Key.Escape);
            Require(!window.Visible && Busy() && NetBusy(), "Closing pending service retains submitted allocation and identity");
            DetailCall(world, "OpenRebirthPicker"); Require(!window.Visible && Busy(), "Reopening cannot replace a pending operation");
            Reply(0); Require(!window.Visible && !Busy() && !NetBusy(), "Late refusal never reopens a closed service");
            DetailCall(world, "OpenRebirthPicker"); await Click(Find<Button>("rebirth_add_1")); await Click(Find<Button>("rebirth_add_4"));
            GetWindow().Size = new Vector2I(1200, 850); await Capture("wide-viewport");
            await Click(accept); await KeyInput(Key.Enter); net.Disconnect(expected: true);
            Require(!Busy() && !NetBusy() && !window.Visible && pick.Placed == 0, "Disconnect closes service and clears stale operation");
            DetailCall(world, "OpenRebirthPicker"); await Click(Find<Button>("rebirth_add_1")); await Click(Find<Button>("rebirth_add_4"));
            await Click(accept); await KeyInput(Key.Enter); Reply(1); await Frames();
            Require(net.Sheet.RebirthLevel == 5 && net.Sheet.RebirthBonusAtRow(1) == 2 && net.Sheet.RebirthBonusAtRow(4) == 2 && !window.Visible, "Success applies only the confirmed snapshot once and closes the service");
            Reply(1); Require(net.Sheet.RebirthLevel == 5, "Duplicate idle success cannot add another stage");
        }
        finally { DetailCall(world, "RebirthDispose"); layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-rebirth.json", JsonSerializer.Serialize(new { nation, checks, screens, liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic rebirth audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
