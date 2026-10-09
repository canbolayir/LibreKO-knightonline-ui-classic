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
    private async Task CaptureNationTransferAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing content: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ReplaceDialogs(request => request.Title == "Nation Transfer" ? new ClassicServiceNotice(request) : new KnightOnlineUiClassic.Windows.MessageBox(request));
        PluginHost.Ui.ExtendWindow("nationtransfer", ClassicGenderSkin.Extend);
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildNationTransferClassicUiPreview", nation)!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.SetMeta("content_open_anchor", true);
        await Frames(4);
        var panel = window.GetChildren().OfType<ClassicGenderPanel>().Single();
        var checks = new List<string>(); var screens = new List<object>();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var editor = Find<LookEditor>("look_editor"); var preview = Find<LookPreview>("look_preview");
        var colour = Find<ColorPickerButton>("look_colour"); var accept = Find<Button>("look_accept");
        var candidates = nation == 1 ? new[] {
            new NationTransferCandidate(2, "AppearanceTester2026", 13, 2, 212, 0, 0x5a3820),
            new NationTransferCandidate(0, "FrontlineWarrior2026", 11, 2, 206, 0, 0x302010),
            new NationTransferCandidate(3, "KurianPreview", 14, 2, 215, 0, (7 << 24) | 0x102030),
            new NationTransferCandidate(1, "ArcaneMage2026", 12, 2, 210, 0, 0x405060),
        } : new[] {
            new NationTransferCandidate(2, "AppearanceTester2026", 4, 1, 112, 0, 0x5a3820),
            new NationTransferCandidate(0, "FrontlineWarrior2026", 1, 1, 106, 0, 0x302010),
            new NationTransferCandidate(3, "KurianPreview", 6, 1, 115, 0, (7 << 24) | 0x102030),
            new NationTransferCandidate(1, "ArcaneMage2026", 3, 1, 110, 0, 0x405060),
        };
        void Require(bool valid, string text) { if (!valid) throw new Exception("NATION_TRANSFER_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task KeyInput(Key key, Viewport? viewport = null)
        {
            viewport ??= GetViewport(); viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); var viewport = GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(2);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        void Dispatch(Packet packet) => typeof(Net).GetMethod("HandleNationTransfer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(net, new object[] { packet });
        Packet CandidatePacket(IReadOnlyList<NationTransferCandidate> list)
        {
            var p = new Packet(GameOpcodes.GS_NATION_TRANSFER); p.WriteByte(2); p.WriteByte(1); p.WriteByte((byte)list.Count);
            foreach (var c in list) { p.WriteShort((short)c.Slot); p.WriteString(c.Name); p.WriteByte((byte)c.Race); p.WriteByte((byte)c.Nation); p.WriteShort((short)c.Class); p.WriteByte((byte)c.Face); p.WriteInt(c.Hair); }
            return p;
        }
        void Open(IReadOnlyList<NationTransferCandidate> list) => Dispatch(CandidatePacket(list));
        void Reply(byte sub, byte result, params byte[] tail)
        { var p = new Packet(GameOpcodes.GS_NATION_TRANSFER); p.WriteByte(sub); p.WriteByte(result); p.WriteBytes(tail); Dispatch(p); }
        bool Busy() => (bool)DetailField(world, "_transferInFlight")!;
        bool NetBusy() => (bool)typeof(Net).GetField("_nationTransferPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(net)!;
        Notice? Confirmation() => (Notice?)DetailField(world, "_transferNotice");
        Dictionary<int, NationTransferPick> Picks() => (Dictionary<int, NationTransferPick>)DetailField(world, "_transferPicks")!;
        Node3D Pivot() => (Node3D)typeof(LookPreview).GetField("_pivot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        async Task Capture(string state, bool popup = false)
        {
            await Frames(); Require(window.Size == ClassicNationTransferLayout.Size, state + " fixed service dimensions");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("look_expected_rect")))
            {
                var expected = c.GetMeta("look_expected_rect").AsRect2();
                Require((c.Position - expected.Position).Length() < .1 && (c.Size - expected.Size).Length() < .1, state + " actual bounds " + c.Name + " " + c.Size);
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " contained control " + c.Name);
            }
            foreach (var label in Descendants(panel).OfType<Label>().Where(l => l.IsVisibleInTree()))
            {
                Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == 13, state + " shared bold caption " + label.Name);
                Require(label.GetLineCount() * 17 <= label.Size.Y + 1, state + " caption fits " + label.Name + ": " + label.Text);
            }
            foreach (var button in Find<VBoxContainer>("transfer_characters").GetChildren().OfType<Button>())
            {
                Require(button.GetThemeFont("font") == Plugin.Kit.Bold && button.GetThemeFontSize("font_size") == 13 && button.Size.Y == 52, state + " candidate typography and height " + button.Name);
                if (window.Visible) Require(button.Disabled == (Busy() || Confirmation() != null), state + " candidate lock matches operation " + button.Name);
            }
            if (Pivot().GetChildCount() > 0)
            {
                Require(Pivot().GetChildCount() == 1 && preview.Size == new Vector2(220, 300), state + " one native full-scale model");
                var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
                var vertices = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
                Require(vertices.Length > 0 && vertices.All(p => new Rect2(1, 1, 218, 298).HasPoint(camera.UnprojectPosition(p))), state + " complete model contained in preview");
            }
            foreach (var notice in layer.GetChildren().OfType<Notice>())
            {
                foreach (var label in Descendants(notice).OfType<Label>().Where(c => c.IsVisibleInTree())) Require(label.GetLineCount() * 17 <= label.Size.Y, state + " confirmation text fits: " + label.Text);
                foreach (var c in Descendants(notice).OfType<Control>().Where(c => c.IsVisibleInTree() && (c is Label or Button)))
                    Require(c.GetThemeFont("font") == Plugin.Kit.Bold && c.GetThemeFontSize("font_size") == 13, state + " confirmation shared bold type " + c.Name + " " + c.GetThemeFontSize("font_size"));
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString(), popup });
        }
        try
        {
            await Frames(16); window.Position = new Vector2(120, 80);
            byte[] complete = CandidatePacket(candidates).GetData();
            for (int length = 0; length < complete.Length; length++)
            {
                var p = new Packet(GameOpcodes.GS_NATION_TRANSFER); p.WriteBytes(complete.Take(length).ToArray()); Dispatch(p);
                Require(!window.Visible && Picks().Count == 0, "Truncated candidate packet cannot open a partial account " + length);
            }
            foreach (var invalid in new[] {
                candidates.Concat(new[] { candidates[0] }).ToArray(),
                candidates.Select((c, i) => i == 0 ? c with { Nation = 3 } : c).ToArray(),
                candidates.Select((c, i) => i == 0 ? c with { Race = 255 } : c).ToArray(),
                candidates.Select((c, i) => i == 0 ? c with { Name = "" } : c).ToArray(),
                candidates.Select((c, i) => i == 0 ? c with { Slot = -1 } : c).ToArray(),
            }) { Open(invalid); Require(!window.Visible && Picks().Count == 0, "Invalid candidate identity/race/target is rejected"); }
            Open(Array.Empty<NationTransferCandidate>()); await Capture("empty"); Require(accept.Disabled && !editor.Visible, "Empty candidate list cannot submit");
            Require(!preview.Visible && !Find<Button>("look_turn_left").Visible && !Find<Button>("look_turn_right").Visible, "Empty service has no floating model controls");
            await Click(Find<Button>("look_cancel")); Open(candidates); await Capture("initial");
            Require(Find<VBoxContainer>("transfer_characters").GetChildren().OfType<Button>().Select(b => b.Name.ToString()).SequenceEqual(new[] { "transfer_character_0", "transfer_character_1", "transfer_character_2", "transfer_character_3" }), "Account rows are sorted by slot");
            Require(Picks().Count == 4 && Picks()[3].Hair == candidates.Single(c => c.Slot == 3).Hair, "All character picks preserve their saved packed RGB");
            var preserved = Picks().Values.OrderBy(p => p.Slot).ToArray(); Open(candidates.Take(1).ToArray());
            Require(Picks().Values.OrderBy(p => p.Slot).SequenceEqual(preserved), "Unsolicited open cannot discard the current account draft");
            var first = Picks()[0]; await Click(Find<Button>("look_face_next")); await Capture("first-edited"); var editedFirst = Picks()[0];
            await Click(Find<Button>("transfer_character_2")); await Capture("character-selected");
            Require(editor.Hair == Picks()[2].Hair && Picks()[0] == editedFirst, "Switching preserves separate character drafts");
            await Click(Find<Button>("look_colour")); Require(colour.GetPopup().Visible, "Native arbitrary RGB colour picker opens"); await Capture("colour-popup", true);
            IEnumerable<Node> PickerTree(Node n) { yield return n; foreach (var child in n.GetChildren(true)) foreach (var item in PickerTree(child)) yield return item; }
            var hex = PickerTree(colour.GetPicker()).OfType<LineEdit>().Single(l => l.IsVisibleInTree() && l.Editable);
            hex.GrabFocus(); colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = true });
            colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = false });
            foreach (char letter in "b95123") { colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = true }); colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = false }); }
            await KeyInput(Key.Enter, colour.GetPopup());
            Require((Picks()[2].Hair & 0xffffff) == 0xb95123 && !Busy() && Confirmation() == null, "Hex Enter updates only the selected character RGB and never transfers");
            await KeyInput(Key.Escape, colour.GetPopup()); Require(window.Visible && !colour.GetPopup().Visible && Confirmation() == null, "Popup Escape closes only the colour picker");
            await KeyInput(Key.Enter); Require(Confirmation() == null && !Busy(), "Service Enter never bypasses certificate confirmation");
            await Click(accept); Require(Confirmation() != null && accept.Disabled && !Busy(), "Transfer button opens one captured certificate confirmation"); await Capture("confirmation");
            var notice = Confirmation()!; var confirmCallback = (Action)typeof(Notice).GetField("_onConfirm", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(notice)!;
            var modal = (Control)typeof(ClassicServiceNotice).GetField("_panel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Descendants(notice).OfType<ClassicServiceNotice>().Single())!;
            var modalBefore = modal.Position; var modalGrip = modal.GetGlobalRect().Position + new Vector2(100, 20); var modalDelta = new Vector2(19, 13);
            GetViewport().PushInput(new InputEventMouseMotion { Position = modalGrip, GlobalPosition = modalGrip }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = modalGrip, GlobalPosition = modalGrip, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = modalGrip + modalDelta, GlobalPosition = modalGrip + modalDelta, Relative = modalDelta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = modalGrip + modalDelta, GlobalPosition = modalGrip + modalDelta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((modal.Position - modalBefore - modalDelta).Length() < 1, "Certificate confirmation can be dragged from its original header"); await Capture("confirmation-dragged");
            await KeyInput(Key.Escape); Require(Confirmation() == null && !accept.Disabled && Picks()[0] == editedFirst, "Confirmation Escape restores unchanged draft without submitting");
            confirmCallback(); Require(!Busy(), "Stale cancelled confirmation cannot submit"); await Capture("confirmation-cancelled");
            await Click(accept); confirmCallback(); Require(!Busy() && Confirmation() != null, "Stale callback cannot submit a replacement confirmation");
            await KeyInput(Key.Enter); Require(Busy() && NetBusy() && Confirmation() == null, "Certificate confirmation Enter submits exactly once"); await Capture("pending");
            var picks = Picks().Values.OrderBy(p => p.Slot).ToArray();
            Require(!net.SendNationTransfer(picks), "Network refuses a second outstanding account transfer");
            await Click(Find<Button>("transfer_character_0")); await Click(Find<Button>("look_face_next"));
            Require(Picks().Values.OrderBy(p => p.Slot).SequenceEqual(picks), "Pending character and appearance controls cannot replace captured picks");
            Reply(3, 99);
            Require(!Busy() && !NetBusy() && window.Visible && !accept.Disabled && Find<Label>("look_status").Text == ItemData.Text(NationTransferWire.FailedText, "Transfer failed"),
                "Unknown submit result is a refusal that clears the pending transfer with the failure text");
            Require(Picks().Values.OrderBy(p => p.Slot).SequenceEqual(picks), "Unknown-result refusal keeps the submitted picks for another attempt");
            await Capture("unknown-refused");
            await Click(accept); await KeyInput(Key.Enter); Require(Busy() && NetBusy() && Confirmation() == null, "Certificate confirmation submits again after a refusal");
            Open(candidates.Take(1).ToArray()); Require(Picks().Count == 4 && Busy(), "Open response cannot replace a pending account");
            await KeyInput(Key.Escape); Require(!window.Visible && Busy() && Pivot().GetChildCount() == 0, "Pending Escape clears preview and retains request identity");
            Open(candidates); Require(!window.Visible && Busy(), "Reopen cannot replace a closed pending transfer");
            Reply(3, 5); Require(!Busy() && !NetBusy() && !window.Visible, "Late refusal does not reopen a closed service");
            Open(candidates); await Click(accept); await KeyInput(Key.Enter); Reply(3, 7); await Capture("refused");
            Require(window.Visible && !Busy() && !accept.Disabled, "Certificate refusal restores editing");
            Reply(1, 8, 2, 1); await Capture("war-status"); Require(Find<Label>("look_status").Text.Contains("2") && Find<Label>("look_status").Text.Contains("1"), "War warning retains both server scores");
            await Click(Find<Button>("transfer_character_3")); await Capture("hairless"); Require(colour.Disabled && editor.Hair == candidates.Single(c => c.Slot == 3).Hair, "Hairless candidate preserves packed style and disables ineffective colour");
            for (int i = 0; i < 6; i++) { await Click(Find<Button>("transfer_character_0")); await Click(Find<Button>("transfer_character_2")); Require(Pivot().GetChildCount() == 1, "Fast switching retains one preview model " + i); }
            foreach (var c in candidates)
            {
                await Click(Find<Button>("transfer_character_" + c.Slot));
                foreach (int race in GenderChange.AllowedRaces(c.Class))
                {
                    await Click(Find<Button>("look_race_" + race));
                    Require(editor.Race == race && Picks()[c.Slot].Race == race, "Actual race selection updates only its account slot " + c.Slot + "/" + race);
                    for (int angle = 0; angle < 8; angle++)
                    {
                        preview.Turn(45); await Frames(2);
                        var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
                        var vertices = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
                        Require(vertices.Length > 0 && vertices.All(p => new Rect2(1, 1, 218, 298).HasPoint(camera.UnprojectPosition(p))), "Every transfer race and full rotation remains contained " + race + "/" + angle);
                    }
                }
            }
            await Click(Find<Button>("transfer_character_2"));
            var before = window.Position; var grip = Find<Control>("party_drag").GetGlobalRect().Position + new Vector2(100, 12); var delta = new Vector2(23, 17);
            GetViewport().PushInput(new InputEventMouseMotion { Position = grip, GlobalPosition = grip }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = grip, GlobalPosition = grip, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseMotion { Position = grip + delta, GlobalPosition = grip + delta, Relative = delta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = grip + delta, GlobalPosition = grip + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - before - delta).Length() < 1, "Native header dragging retains geometry"); await Capture("dragged");
            await Click(accept); await KeyInput(Key.Enter); net.Disconnect(expected: true); Require(!Busy() && !NetBusy() && !window.Visible, "Disconnect clears operation and closes stale transfer draft");
            Reply(1, 8, 2, 1); await Capture("closed-war", true);
            Require(layer.GetChildren().OfType<Notice>().Count() == 1, "Closed service war response shows one native message");
            await KeyInput(Key.Escape); Require(!window.Visible && layer.GetChildren().OfType<Notice>().Count() == 0, "Closed war Escape dismisses only its message");
            Open(new[] { candidates[0] with { Face = 255, Hair = (127 << 24) | 0xb95123 } });
            Require(editor.Face < CharacterPreview.FaceCount(editor.Race) || CharacterPreview.FaceCount(editor.Race) == 0, "Out-of-range source face is normalized to available content");
            Require((editor.Hair >> 24) < CharacterPreview.HairCount(editor.Race) || CharacterPreview.HairCount(editor.Race) == 0, "Out-of-range source hair is normalized to available content");
            Require((Picks()[candidates[0].Slot].Hair & 0xffffff) == 0xb95123, "Appearance normalization retains saved RGB");
            await Capture("normalized-appearance"); await Click(Find<Button>("look_cancel"));
            Open(candidates.Take(1).ToArray()); await Capture("single-character");
            GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(220, 140); await Capture("wide-viewport");
            await Click(accept); await KeyInput(Key.Enter); Reply(3, 1); await Frames();
            Require(!window.Visible && !Busy() && !NetBusy() && net.Nation == candidates[0].Nation, "Success applies captured target and closes service");
            Require(layer.GetChildren().OfType<Notice>().Count() == 1, "Success shows one native return-to-character notice");
            await Capture("success", true);
            Reply(3, 1); Require(layer.GetChildren().OfType<Notice>().Count() == 1, "Idle duplicate success cannot create another reconnect notice");
        }
        finally { DetailCall(world, "NationTransferDispose"); layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-nationtransfer.json", JsonSerializer.Serialize(new { nation, checks, screens, liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic nation transfer audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
