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
    private async Task CaptureGenderAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/characters.pck"), false)) throw new Exception("Missing existing character pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var world = new World(); var layer = (CanvasLayer)DetailCall(world, "BuildGenderClassicUiPreview", nation)!; AddChild(layer);
        var window = layer.GetChildren().OfType<HudWindow>().Single(); var panel = ClassicGenderSkin.Apply(window.Body)!;
        window.SetMeta("content_open_anchor", true);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("GENDER_AUDIT: " + text); checks.Add(text); }
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
        bool Busy() => (bool)DetailField(world, "_genderInFlight")!;
        bool Armed() => (bool)DetailField(world, "_genderArmed")!;
        Node3D Pivot() => (Node3D)typeof(LookPreview).GetField("_pivot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        void Open() => DetailCall(world, "OpenGenderChange");
        void Load(int cls)
        {
            var me = net.LastEnter; me.Class = cls; typeof(Net).GetMethod("SeedPreviewEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(net, new object[] { me });
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
            foreach (var label in Descendants(editor).OfType<Label>())
            {
                Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == 13, state + " uniform native label font " + label.Name);
                Require(window.GetGlobalRect().Encloses(label.GetGlobalRect()), state + " native editor label bounds " + label.Name);
                Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, 13).X <= label.Size.X, state + " complete label text " + label.Text);
            }
            var races = Find<VBoxContainer>("look_races").GetChildren().OfType<Button>().ToArray();
            Require(races.Length == GenderChange.AllowedRaces(net.LastEnter.Class).Length && races.Count(b => b.ButtonPressed) == 1, state + " every allowed race and exactly one native selection");
            foreach (var button in races)
            {
                Require(button.GetThemeFont("font") == Plugin.Kit.Bold && button.GetThemeFontSize("font_size") == 13, state + " uniform race font " + button.Name);
                Require(editor.GetGlobalRect().Encloses(button.GetGlobalRect()), state + " race within editor " + button.Name);
                Require(button.GetThemeFont("font").GetStringSize(button.Text, HorizontalAlignment.Left, -1, 13).X + 12 <= button.Size.X, state + " complete race text " + button.Text);
                Require(button.Disabled == Busy(), state + " native race lock " + button.Name);
            }
            foreach (var part in new[] { "face", "hair" }) foreach (var side in new[] { "previous", "next" })
            {
                var button = Find<Button>("look_" + part + "_" + side);
                Require(button.Size == new Vector2(19, 19), state + " original arrow size " + button.Name + " " + button.Size);
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                Require(button.Disabled == (Busy() || count <= 1), state + " content-derived arrow availability " + button.Name);
            }
            Require(accept.Disabled == Busy() && colour.Disabled == Busy(), state + " native pending controls");
            Require(preview.Size == new Vector2(220, 300) && preview.ClipContents, state + " full native clipped character preview");
            Require(Pivot().GetChildCount() == 1, state + " exactly one real character model");
            var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
            var vertices = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
            var projected = vertices.Select(camera.UnprojectPosition).ToArray();
            Require(vertices.Length > 0 && projected.All(v => new Rect2(1, 1, 218, 298).HasPoint(v)), state + " complete posed body and head remain inside preview " + (projected.Length == 0 ? "empty" : $"{projected.Min(p => p.X)},{projected.Min(p => p.Y)} to {projected.Max(p => p.X)},{projected.Max(p => p.Y)}"));
            var status = Find<Label>("look_status"); Require(status.GetLineCount() * 17 <= status.Size.Y, state + " warning fits fixed status area: " + status.Text);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString(), popup });
        }
        try
        {
            await Frames(16); window.Position = new Vector2(220, 100); await Capture("initial");
            Require(editor.Hair == net.LastEnter.Hair, "Opening preserves the saved RGB colour and style");
            var other = GenderChange.AllowedRaces(net.LastEnter.Class).First(r => r != editor.Race);
            await Click(Find<Button>("look_race_" + other)); Require(editor.Race == other, "Actual native race selection changes preview"); await Capture("race-selected");
            foreach (var part in new[] { "face", "hair" })
            {
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                int original = part == "face" ? editor.Face : editor.HairStyle;
                await Click(Find<Button>("look_" + part + "_next"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == (original + (count > 1 ? 1 : 0)) % Math.Max(1, count), "Actual " + part + " next respects available content");
                await Click(Find<Button>("look_" + part + "_previous"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == original, "Actual " + part + " previous restores native index");
            }
            await Click(colour); Require(colour.GetPopup().Visible, "Actual hair colour button opens native RGB picker"); await Capture("colour-popup", true);
            Require(GetViewportRect().Encloses(new Rect2(colour.GetPopup().Position, colour.GetPopup().Size)), "Compact native colour popup fits desktop viewport");
            await KeyInput(Key.Escape, colour.GetPopup()); Require(!colour.GetPopup().Visible && window.Visible, "Colour picker Escape dismisses only the popup");
            await Click(colour);
            IEnumerable<Node> PickerTree(Node n) { yield return n; foreach (var child in n.GetChildren(true)) foreach (var item in PickerTree(child)) yield return item; }
            var hex = PickerTree(colour.GetPicker()).OfType<LineEdit>().Single(l => l.IsVisibleInTree() && l.Editable);
            Require(hex.GetThemeFont("font") == Plugin.Kit.Bold && hex.GetThemeFontSize("font_size") == 13, "Native colour hex field shares the service font");
            hex.GrabFocus(); colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = true });
            colour.GetPopup().PushInput(new InputEventKey { Keycode = Key.A, CtrlPressed = true, Pressed = false });
            foreach (char letter in "b95123")
            {
                colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = true });
                colour.GetPopup().PushInput(new InputEventKey { Unicode = letter, Pressed = false });
            }
            await KeyInput(Key.Enter, colour.GetPopup());
            Require((editor.Hair & 0xffffff) == 0xb95123 && !Armed() && !Busy(), "Native hex typing and Enter preserve arbitrary RGB without charging a scroll");
            await KeyInput(Key.Escape, colour.GetPopup()); await Capture("custom-colour");
            var angle = Pivot().Rotation.Y; await Click(Find<Button>("look_turn_left")); Require(Math.Abs(Pivot().Rotation.Y - angle + Mathf.DegToRad(30)) < .01, "Actual left turn rotates native model");
            await Click(Find<Button>("look_turn_right")); Require(Math.Abs(Pivot().Rotation.Y - angle) < .01, "Actual right turn restores native model");
            await KeyInput(Key.Enter); await KeyInput(Key.KpEnter); Require(!Busy() && !Armed(), "Enter and keypad Enter do not arm or consume a gender scroll");
            await Click(accept); Require(Armed() && !Busy() && accept.Text == "Confirm", "First change click only arms original consumable warning"); await Capture("armed");
            await Click(Find<Button>("look_race_" + net.LastEnter.Race)); Require(!Armed(), "Editing appearance disarms previous confirmation");
            await Click(accept); await Click(accept); Require(Busy(), "Second explicit click sends one native request"); await Capture("pending");
            int submittedRace = editor.Race, submittedFace = editor.Face, submittedHair = editor.Hair;
            await Click(Find<Button>("look_race_" + other)); await Click(Find<Button>("look_face_next")); await Click(colour); await Click(accept);
            Require(Busy() && !colour.GetPopup().Visible && editor.Race == submittedRace && editor.Face == submittedFace && editor.Hair == submittedHair, "Pending request cannot be edited or sent again");
            await KeyInput(Key.Escape); Require(!window.Visible && Busy() && Pivot().GetChildCount() == 0, "Escape closes preview and retains pending request");
            Open(); Require(!window.Visible && Busy(), "Reopen cannot replace an outstanding gender request");
            DetailCall(world, "OnGenderChanged"); Require(!window.Visible && !Busy(), "Closed late success clears pending without reopening");
            Open(); DetailCall(world, "OnGenderChanged"); Require(window.Visible, "Unsolicited duplicate success cannot close a new editor");
            foreach (int result in new[] { (int)Net.GenderChangeNoItem, (int)Net.GenderChangeFailed })
            {
                await Click(accept); await Click(accept); DetailCall(world, "OnGenderChangeRefused", result); await Capture(result == Net.GenderChangeNoItem ? "missing-scroll" : "refused");
                Require(!Busy() && !Armed() && !accept.Disabled && !colour.Disabled, "Refusal unlocks editor without changing the pick " + result);
            }
            await Click(accept); await Click(accept); await Click(Find<Button>("look_cancel"));
            Require(!window.Visible && Busy(), "Pending Cancel closes without clearing the sent operation");
            DetailCall(world, "OnGenderChangeRefused", (int)Net.GenderChangeNoItem);
            Require(!window.Visible && !Busy(), "Closed refusal clears pending and keeps the service closed");
            Open(); var restoredColour = editor.Hair;
            DetailCall(world, "OnGenderChangeRefused", (int)Net.GenderChangeFailed);
            Require(!Busy() && editor.Hair == restoredColour && Find<Label>("look_status").Text == "", "Unsolicited refusal cannot change a reopened idle service");
            var availableClass = net.LastEnter.Class;
            DetailCall(world, "CloseGenderChange"); Load(nation * 100 + 15);
            Require(!window.Visible && !Busy(), "Unsupported class cannot open the gender service"); Load(availableClass);
            foreach (int cls in new[] { nation * 100 + 6, nation * 100 + 8, nation * 100 + 10, nation * 100 + 12 })
            {
                if (!GenderChange.CanChange(cls)) continue;
                Load(cls); await Frames();
                for (int i = 0; i < 8; i++)
                {
                    var options = GenderChange.AllowedRaces(cls); editor.Load(options, options[i % options.Length], 0, net.LastEnter.Hair); DetailCall(world, "ShowGenderLook");
                    Require(Find<VBoxContainer>("look_races").GetChildCount() == options.Length && Pivot().GetChildCount() == 1, "Same-frame shared rows and models are removed before reload " + cls + "/" + i);
                }
                await Capture("class-" + cls);
            }
            typeof(World).GetField("_selfDead", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, true); await Click(accept);
            Require(!Busy() && !Armed(), "Dead character cannot arm a charge"); typeof(World).GetField("_selfDead", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, false);
            var originalPosition = window.Position; var p = Find<Control>("party_drag").GetGlobalRect().Position + new Vector2(120, 12);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            var delta = new Vector2(31, 17); GetViewport().PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - originalPosition - delta).Length() < 1, "Actual header dragging uses native HudLayout"); await Capture("dragged");
            await Click(Find<Button>("look_cancel")); Require(!window.Visible && !Busy(), "Actual Cancel closes without submitting"); Open();
            await Click(Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close")); Require(!window.Visible, "Original close artwork invokes native close");
            Open(); GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(320, 140); await Capture("wide-viewport");
        }
        finally { layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-gender.json", JsonSerializer.Serialize(new { nation, checks, screens, fixture = "Native appearance controls, actual viewport input and explicit offline result callbacks", liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic gender audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
