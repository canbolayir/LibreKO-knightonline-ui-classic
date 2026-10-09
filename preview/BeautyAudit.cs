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
    private async Task CaptureBeautyAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing content pack: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildBeautyClassicUiPreview", nation)!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicGenderSkin.Apply(window.Body)!;
        window.SetMeta("content_open_anchor", true);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("BEAUTY_AUDIT: " + text); checks.Add(text); }
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
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var editor = Find<LookEditor>("look_editor"); var preview = Find<LookPreview>("look_preview");
        var colour = Find<ColorPickerButton>("look_colour"); var accept = Find<Button>("look_accept");
        bool Busy() => (bool)DetailField(world, "_changeHairInFlight")!;
        bool NetBusy() => PreviewFixtures.ChangeHairPending(net);
        Node3D Pivot() => (Node3D)typeof(LookPreview).GetField("_pivot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        void Open() => DetailCall(world, "OpenChangeHair");
        void Reply(byte result)
        {
            var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR); packet.WriteByte(result);
            typeof(Net).GetMethod("HandleChangeHair", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(net, new object[] { packet });
        }
        void LoadRace(int race)
        {
            DetailCall(world, "CloseChangeHair");
            var me = net.LastEnter; me.Race = race; me.Face = 0; me.Hair = 0x5A3820;
            typeof(Net).GetMethod("SeedPreviewEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(net, new object[] { me });
            foreach (var field in new[] { ("_selfRace", race), ("_selfFace", 0), ("_selfHair", me.Hair) })
                typeof(World).GetField(field.Item1, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, field.Item2);
            Open();
        }
        async Task Capture(string state, bool popup = false)
        {
            await Frames(); Require(window.Size == ClassicGenderLayout.Size, state + " fixed window size");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("look_expected_rect")))
            {
                var expected = c.GetMeta("look_expected_rect").AsRect2();
                Require((c.Position - expected.Position).Length() < .1f && (c.Size - expected.Size).Length() < .1f, state + " actual declared bounds " + c.Name + " " + c.Size);
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " window-contained control " + c.Name);
            }
            foreach (var label in Descendants(panel).OfType<Label>().Where(l => l.IsVisibleInTree()))
            {
                Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == 13, state + " uniform native label font " + label.Name);
                Require(window.GetGlobalRect().Encloses(label.GetGlobalRect()), state + " native label bounds " + label.Name);
                if (label.AutowrapMode == TextServer.AutowrapMode.Off)
                    Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, 13).X <= label.Size.X, state + " complete label text " + label.Text);
            }
            Require(!Find<Control>("look_race_heading").Visible && !Find<Control>("look_races").Visible && editor.Race == net.LastEnter.Race, state + " beauty cannot change race");
            foreach (var part in new[] { "face", "hair" }) foreach (var side in new[] { "previous", "next" })
            {
                var button = Find<Button>("look_" + part + "_" + side);
                Require(button.Size == new Vector2(19, 19), state + " original arrow size " + button.Name);
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                Require(button.Disabled == (Busy() || count <= 1), state + " content-derived arrow availability " + button.Name);
            }
            Require(accept.Disabled == Busy() && colour.Disabled == (Busy() || CharacterPreview.HairCount(editor.Race) == 0) && Busy() == NetBusy(), state + " native pending controls and network guard");
            Require(preview.Size == new Vector2(220, 300) && preview.ClipContents, state + " full native clipped character preview");
            Require(Pivot().GetChildCount() == 1, state + " exactly one real character model");
            var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
            var vertices = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
            var projected = vertices.Select(camera.UnprojectPosition).ToArray();
            Require(vertices.Length > 0 && projected.All(v => new Rect2(1, 1, 218, 298).HasPoint(v)), state + " complete posed body and head remain inside preview " + (projected.Length == 0 ? "empty" : $"{projected.Min(p => p.X)},{projected.Min(p => p.Y)} to {projected.Max(p => p.X)},{projected.Max(p => p.Y)}"));
            var status = Find<Label>("look_status"); Require(status.GetLineCount() * 17 <= status.Size.Y, state + " fixed warning fits separate area");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString(), popup });
        }
        try
        {
            await Frames(16); window.Position = new Vector2(220, 100); await Capture("initial");
            Require(editor.Face == 0 && editor.HairStyle == 0 && editor.Hair == net.LastEnter.Hair, "Opening preserves zero-based face, hairstyle and saved RGB");
            foreach (var part in new[] { "face", "hair" })
            {
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                await Click(Find<Button>("look_" + part + "_previous"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == Math.Max(0, count - 1), "Actual " + part + " previous wraps to last content variant");
                await Click(Find<Button>("look_" + part + "_next"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == 0, "Actual " + part + " next wraps to first content variant");
            }
            await Click(colour); Require(colour.GetPopup().Visible, "Actual colour button opens native RGB picker"); await Capture("colour-popup", true);
            Require(GetViewportRect().Encloses(new Rect2(colour.GetPopup().Position, colour.GetPopup().Size)), "RGB picker fits the desktop viewport");
            await KeyInput(Key.Escape, colour.GetPopup()); Require(window.Visible && !colour.GetPopup().Visible && !Busy(), "Popup Escape closes only the colour picker");
            await Click(colour);
            IEnumerable<Node> PickerTree(Node n) { yield return n; foreach (var child in n.GetChildren(true)) foreach (var item in PickerTree(child)) yield return item; }
            var hex = PickerTree(colour.GetPicker()).OfType<LineEdit>().Single(l => l.IsVisibleInTree() && l.Editable);
            hex.GrabFocus(); colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = true });
            colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = false });
            foreach (char letter in "b95123")
            {
                colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = true });
                colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = false });
            }
            await KeyInput(Key.Enter, colour.GetPopup());
            Require((editor.Hair & 0xffffff) == 0xb95123 && !Busy(), "Hex Enter changes colour without submitting the service");
            await KeyInput(Key.Escape, colour.GetPopup()); await Capture("custom-colour");
            var angle = Pivot().Rotation.Y; await Click(Find<Button>("look_turn_left")); Require(Math.Abs(Pivot().Rotation.Y - angle + Mathf.DegToRad(30)) < .01, "Native left turn rotates model");
            await Capture("turned"); await Click(Find<Button>("look_turn_right")); Require(Math.Abs(Pivot().Rotation.Y - angle) < .01, "Native right turn restores model");
            var status = Find<Label>("look_status");
            await KeyInput(Key.Enter);
            Require(!Busy() && !NetBusy() && !colour.GetPopup().Visible && status.Text == ItemData.Text(BeautyShop.NoCouponText, "You need a Makeover Coupon."),
                "Without a Makeover Coupon Enter sends nothing and shows text 18901");
            await Capture("no-coupon");
            var bag = (Inventory)typeof(World).GetProperty("Inv", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
            bag.EnsureLength(InventoryConstants.InventoryTotal);
            bag[Inventory.GridStart] = new LibreKO.Domain.ItemSlot { ItemId = BeautyShop.Coupon, Count = 3, Durability = 1 };
            await KeyInput(Key.Enter); Require(Busy() && NetBusy(), "Enter submits one beauty request with a Makeover Coupon in the bag"); await Capture("pending");
            int face = editor.Face, hair = editor.Hair;
            await Click(Find<Button>("look_face_next")); await Click(colour); await Click(accept); await KeyInput(Key.KpEnter);
            Require(Busy() && editor.Face == face && editor.Hair == hair && !colour.GetPopup().Visible, "Pending edit, repeated click and keypad Enter cannot replace the submitted look");
            Require(!net.SendChangeHair(0x543210, 1), "Network independently refuses a second outstanding beauty request");
            Reply(99); Require(Busy() && NetBusy(), "Unknown result does not complete a pending request");
            Reply(Net.ChangeHairOpenShop); Require(Busy() && editor.Hair == hair, "Open-shop packet cannot replace pending picks");
            await KeyInput(Key.Escape); Require(!window.Visible && Busy() && Pivot().GetChildCount() == 0, "Escape closes preview and retains pending identity");
            Open(); Require(!window.Visible && Busy(), "Reopen cannot replace an outstanding beauty request");
            Reply(Net.ChangeHairResultOk);
            Require(!window.Visible && !Busy() && !NetBusy() && net.LastEnter.Face == face && net.LastEnter.Hair == hair, "Closed late success updates the submitted network snapshot");
            Require((int)DetailField(world, "_selfFace")! == face && (int)DetailField(world, "_selfHair")! == hair, "Success updates live world appearance");
            Open(); Require(editor.Hair == hair && editor.Face == face, "Reopen loads the newly applied appearance");
            Reply(Net.ChangeHairResultOk); Reply(Net.ChangeHairResultFail);
            Require(window.Visible && !Busy() && editor.Hair == hair, "Idle duplicate responses cannot alter or close a reopened editor");
            await Click(accept); Reply(Net.ChangeHairResultFail); await Capture("refused");
            Require(!Busy() && !accept.Disabled && editor.Hair == hair, "Refusal unlocks the same pick without changing saved appearance");
            await KeyInput(Key.KpEnter); Require(Busy(), "Keypad Enter submits a new request after refusal");
            await Click(Find<Button>("look_cancel")); Require(!window.Visible && Busy(), "Cancel retains an already submitted operation");
            Reply(Net.ChangeHairResultFail); Require(!window.Visible && !Busy(), "Closed late refusal stays closed"); Open();
            await Click(accept); net.Disconnect(expected: true);
            Require(!Busy() && !NetBusy() && window.Visible, "Disconnect clears pending request without applying a look"); await Capture("disconnected");
            typeof(World).GetField("_selfDead", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, true);
            await Click(accept); await KeyInput(Key.Enter); Require(!Busy(), "Dead character cannot submit by mouse or Enter");
            typeof(World).GetField("_selfDead", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, false);
            foreach (int race in nation == 1 ? new[] { 1, 2, 3, 4, 6 } : new[] { 11, 12, 13, 14 })
            {
                LoadRace(race); await Frames();
                for (int i = 0; i < 4; i++)
                {
                    editor.Load(Array.Empty<int>(), race, 0, 0x5A3820); DetailCall(world, "ShowChangeHairLook");
                    Require(Pivot().GetChildCount() == 1 && Find<VBoxContainer>("look_races").GetChildCount() == 0, "Fast reload leaves one model and no race choices " + race + "/" + i);
                }
                void CheckModel(string stage)
                {
                    var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
                    var points = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
                    Require(points.Length > 0 && points.All(point => new Rect2(1, 1, 218, 298).HasPoint(camera.UnprojectPosition(point))), "Every variant and turn fits the native preview " + race + "/" + stage);
                }
                foreach (string part in new[] { "face", "hair" })
                {
                    int count = part == "face" ? CharacterPreview.FaceCount(race) : CharacterPreview.HairCount(race);
                    for (int i = 0; i < count; i++)
                    {
                        await Click(Find<Button>("look_" + part + "_next"));
                        Require((part == "face" ? editor.Face : editor.HairStyle) == (i + 1) % count, "Actual content variant and wrap " + race + "/" + part + "/" + i);
                        CheckModel(part + "-" + i);
                    }
                }
                for (int i = 0; i < 8; i++) { preview.Turn(45); await Frames(2); CheckModel("turn-" + i); }
                await Capture("race-" + race);
            }
            Require(CharacterPreview.HairCount(editor.Race) == 0 && colour.Disabled, "Hairless race disables a colour control that cannot affect its model");
            var saved = net.LastEnter; saved.Hair = (7 << 24) | 0x5A3820;
            DetailCall(world, "CloseChangeHair");
            typeof(Net).GetMethod("SeedPreviewEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(net, new object[] { saved });
            typeof(World).GetField("_selfHair", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, saved.Hair);
            Open(); await Click(colour);
            Require(editor.Hair == saved.Hair && !colour.GetPopup().Visible, "Unavailable hair meshes preserve the saved hairstyle and RGB on reopening");
            var originalPosition = window.Position; var p = Find<Control>("party_drag").GetGlobalRect().Position + new Vector2(120, 12);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            var delta = new Vector2(31, 17); GetViewport().PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - originalPosition - delta).Length() < 1, "Actual header dragging uses native HudLayout"); await Capture("dragged");
            await Click(Find<Button>("look_cancel")); Require(!window.Visible && !Busy(), "Cancel closes without submitting"); Open();
            await Click(Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close")); Require(!window.Visible && !Busy(), "Original close artwork invokes native close");
            Open(); GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(320, 140); await Capture("wide-viewport");
        }
        finally { DetailCall(world, "ChangeHairDispose"); layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-beauty.json", JsonSerializer.Serialize(new { nation, checks, screens, fixture = "Actual native viewport input and network result dispatch; no live transactions", liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic beauty audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
