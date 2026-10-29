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
    private async Task CaptureBeautyAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing content pack: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740); GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ReplaceDialogs(request => new KnightOnlineUiClassic.Windows.MessageBox(request));
        var world = PreviewFixtures.Prepared(new WorldRoot()); var layer = (CanvasLayer)DetailCall(world, "BuildBeautyClassicUiPreview", nation)!; AddChild(layer);
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
        var swatch = Find<Button>("look_palette"); var accept = Find<Button>("look_accept"); var zoom = Find<Button>("look_zoom");
        bool Busy() => (bool)DetailField(world, "ChangeHair.BeautyInFlight")!;
        bool NetBusy() => PreviewFixtures.ChangeHairPending(net);
        Node3D Pivot() => (Node3D)typeof(LookPreview).GetField("pivot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        void Open() => DetailCall(world, "ChangeHair.Open");
        void Reply(byte result)
        {
            var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR); packet.WriteByte(result);
            PreviewPackets.Receive(packet);
        }
        // The client parents its notices to the world; the audit shows them above the preview.
        Notice? TakeNotice()
        {
            var notice = world.GetChildren().OfType<Notice>().LastOrDefault(n => !n.IsQueuedForDeletion());
            if (notice == null) return null;
            world.RemoveChild(notice); AddChild(notice); return notice;
        }
        DialogRequest Request(Notice notice) => (DialogRequest)typeof(Notice).GetField("request", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(notice)!;
        void Dismiss(Notice notice) { var request = Request(notice); if (request.HasCancel) request.Cancel(); else request.Dismiss(); notice.Free(); }
        void LoadRace(int race)
        {
            DetailCall(world, "ChangeHair.Close");
            var me = Game.Session.Self.LastEnter; me.Race = race; me.Face = 0; me.Hair = 0x5A3820;
            Native.Call(Game.Session.Self, "SeedPreviewEnter", me);
            foreach (var field in new[] { ("Self.Race", race), ("Self.Face", 0), ("Self.Hair", me.Hair) })
                Native.Set(Game.Session, field.Item1, field.Item2);
            Open();
        }
        void CheckFraming(string stage, Rect2 inside)
        {
            var camera = preview.GetChild<SubViewport>(0).GetCamera3D();
            var points = Descendants(Pivot()).OfType<MeshInstance3D>().Where(m => m.IsVisibleInTree() && m.Mesh != null).SelectMany(InventoryPortrait.FramingPoints).ToArray();
            var projected = points.Select(camera.UnprojectPosition).ToArray();
            Require(points.Length > 0 && projected.All(inside.HasPoint), stage + " complete posed body and head remain inside preview " + (projected.Length == 0 ? "empty" : $"{projected.Min(p => p.X)},{projected.Min(p => p.Y)} to {projected.Max(p => p.X)},{projected.Max(p => p.Y)}"));
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
            // Since ff78eef the client keeps the race list in the Beauty Shop editor, hidden (it no longer leaks it).
            Require(Descendants(editor).OfType<VBoxContainer>().Where(v => v.Name == "look_races").All(v => !v.IsVisibleInTree())
                && !Descendants(editor).OfType<Label>().Any(l => l.Name == "look_race_heading" && l.IsVisibleInTree())
                && editor.Race == Game.Session.Self.LastEnter.Race, state + " beauty cannot change race");
            foreach (var part in new[] { "face", "hair" }) foreach (var side in new[] { "previous", "next" })
            {
                var button = Find<Button>("look_" + part + "_" + side);
                Require(button.Size == new Vector2(19, 19), state + " original arrow size " + button.Name);
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                Require(button.Disabled == (Busy() || count <= 1), state + " content-derived arrow availability " + button.Name);
            }
            Require(accept.Disabled == Busy() && swatch.Disabled == (Busy() || CharacterPreview.HairCount(editor.Race) == 0), state + " native pending controls");
            Require(swatch.IsVisibleInTree() && swatch.Size.Y == 26 && swatch.Size.X >= 80 && editor.GetGlobalRect().Encloses(swatch.GetGlobalRect()) && Find<ColorRect>("look_palette_swatch").Color == editor.HairColour, state + " hair colour swatch shows the edited colour");
            Require(preview.Size == new Vector2(220, 300) && preview.ClipContents, state + " full native clipped character preview");
            Require(Pivot().GetChildCount() == 1, state + " exactly one real character model");
            if (!KnightOnlineUiClassic.Native.LookFraming.Zoomed(preview)) CheckFraming(state, new Rect2(1, 1, 218, 298));
            var status = Find<Label>("look_status"); Require(status.GetLineCount() * 17 <= status.Size.Y, state + " fixed warning fits separate area");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString(), popup });
        }
        async Task CaptureNotice(string state, Notice notice, string text)
        {
            await Frames();
            var box = notice.GetChildren().OfType<KnightOnlineUiClassic.Windows.MessageBox>().Single();
            Require(Descendants(box).OfType<Label>().Any(l => l.Text == text), state + " Classic message box shows the client's text: " + text);
            Require(GetViewportRect().Encloses(Descendants(box).OfType<Control>().Single(c => c.Name == "message_art").GetGlobalRect()), state + " message box inside the viewport");
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, notice = text });
        }
        HudWindow? palette = null; ClassicHairPalettePanel? palettePanel = null;
        async Task OpenPalette()
        {
            await Click(swatch);
            palette = PreviewFixtures.PrepareHairPalette(world);
            Require(palette != null && palette.Visible && palette.GetParent() == layer, "The colour swatch opens the client's hair palette beside the shop");
            palettePanel ??= ClassicHairPaletteSkin.Apply(palette!.Body);
            await Frames();
        }
        async Task CapturePalette(string state)
        {
            await Frames();
            Require(palette!.Size == ClassicHairPaletteLayout.Size, state + " fixed palette size");
            Require(GetViewportRect().Encloses(palette.GetGlobalRect()), state + " palette inside the viewport");
            Require(!palette.GetGlobalRect().Intersects(window.GetGlobalRect()), state + " palette does not cover the shop " + palette.GetGlobalRect() + " / " + window.GetGlobalRect());
            foreach (var c in Descendants(palettePanel!).OfType<Control>().Where(c => c.HasMeta("look_expected_rect")))
            {
                var expected = c.GetMeta("look_expected_rect").AsRect2();
                Require((c.Position - expected.Position).Length() < .1f && (c.Size - expected.Size).Length() < .1f, state + " palette declared bounds " + c.Name + " " + c.Size);
                Require(palette.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " palette-contained control " + c.Name);
            }
            var picker = Descendants(palettePanel!).OfType<ColorPicker>().Single();
            var content = Descendants(picker).OfType<Control>().Where(c => c.IsVisibleInTree() && c is not ColorPicker && c.Size.X > 0).ToArray();
            Require(content.All(c => palette.GetGlobalRect().Grow(.5f).Encloses(c.GetGlobalRect())), state + " picker field, hue strip and hex row stay inside the palette");
            Require(ClassicHairPaletteSkin.PickerTree(picker).OfType<LineEdit>().Count(l => l.IsVisibleInTree()) == 1, state + " one hex row");
            Require(Descendants(palettePanel!).OfType<Label>().Single(l => l.Name == "palette_prompt").Text == ItemData.Text(18905, "Please select your hair color."), state + " client's palette prompt");
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png"; GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = palette.GetGlobalRect().ToString() });
        }
        try
        {
            await Frames(16); window.Position = new Vector2(40, 100); await Capture("initial");
            Require(editor.Face == 0 && editor.HairStyle == 0 && editor.Hair == Game.Session.Self.LastEnter.Hair, "Opening preserves zero-based face, hairstyle and saved RGB");
            foreach (var part in new[] { "face", "hair" })
            {
                int count = part == "face" ? CharacterPreview.FaceCount(editor.Race) : CharacterPreview.HairCount(editor.Race);
                await Click(Find<Button>("look_" + part + "_previous"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == Math.Max(0, count - 1), "Actual " + part + " previous wraps to last content variant");
                await Click(Find<Button>("look_" + part + "_next"));
                Require((part == "face" ? editor.Face : editor.HairStyle) == 0, "Actual " + part + " next wraps to first content variant");
            }

            // Hair palette: the client's own window, opened from the Classic swatch.
            await OpenPalette(); await CapturePalette("palette");
            var picker = Descendants(palettePanel!).OfType<ColorPicker>().Single();
            var savedColour = editor.HairColour;
            picker.Color = new Color("2050c0");
            await KeyInput(Key.Escape);
            Require(!palette!.Visible && window.Visible && editor.HairColour == savedColour && !Busy(), "Palette Escape closes only the palette and keeps the colour");
            await OpenPalette();
            var hex = ClassicHairPaletteSkin.PickerTree(picker).OfType<LineEdit>().Single(l => l.IsVisibleInTree());
            hex.GrabFocus(); hex.Text = "b95123"; hex.EmitSignal(LineEdit.SignalName.TextSubmitted, hex.Text); await Frames();
            hex.ReleaseFocus();
            await KeyInput(Key.Enter);
            Require(!palette!.Visible && (editor.Hair & 0xffffff) == 0xb95123 && !Busy() && !NetBusy(), "Palette hex and Enter apply the colour without submitting the service");
            await Capture("custom-colour");
            await OpenPalette();
            await Click(Descendants(palettePanel!).OfType<Button>().Single(b => b.Name == "palette_cancel"));
            Require(!palette!.Visible && (editor.Hair & 0xffffff) == 0xb95123, "Palette Cancel keeps the applied colour");
            // Without room on the right the palette opens on the shop's left.
            window.Position = new Vector2(400, 100); await Frames();
            await OpenPalette(); await CapturePalette("palette-left");
            Require(palette!.Position.X + palette.Size.X <= window.Position.X, "Palette opens on the free left side");
            await Click(Descendants(palettePanel!).OfType<Button>().Single(b => b.Name == "palette_accept"));
            Require(!palette.Visible && (editor.Hair & 0xffffff) == 0xb95123, "Palette Apply without a change keeps the colour");
            window.Position = new Vector2(40, 100); await Frames();

            // Head zoom from the client, Classic framing when zoomed out.
            await Click(zoom); Require(KnightOnlineUiClassic.Native.LookFraming.Zoomed(preview), "Zoom shows the face close up");
            await Capture("zoomed");
            await Click(Find<Button>("look_face_next")); Require(KnightOnlineUiClassic.Native.LookFraming.Zoomed(preview) && Pivot().GetChildCount() == 1, "Changing the face keeps the zoom");
            await Click(Find<Button>("look_face_previous"));
            await Click(zoom); Require(!KnightOnlineUiClassic.Native.LookFraming.Zoomed(preview), "Zoom toggles back"); CheckFraming("zoomed out", new Rect2(1, 1, 218, 298));
            var angle = Pivot().Rotation.Y; await Click(Find<Button>("look_turn_left")); Require(Math.Abs(Pivot().Rotation.Y - angle + Mathf.DegToRad(30)) < .01, "Native left turn rotates model");
            await Capture("turned"); await Click(Find<Button>("look_turn_right")); Require(Math.Abs(Pivot().Rotation.Y - angle) < .01, "Native right turn restores model");

            // No coupon: the client refuses with its text and closes the shop.
            await KeyInput(Key.Enter);
            var notice = TakeNotice();
            Require(notice != null && !Busy() && !NetBusy() && !window.Visible, "Without a Makeover Coupon Enter sends nothing and the client closes the shop");
            await CaptureNotice("no-coupon", notice!, ItemData.Text(18901, "You do not have a Makeover Coupon.")); Dismiss(notice!);
            Open();
            var bag = (Inventory)Game.Session.Self.Inventory;
            bag.EnsureLength(InventoryConstants.InventoryTotal);
            bag[Inventory.GridStart] = new LibreKO.Domain.ItemSlot { ItemId = BeautyShop.Coupon, Count = 3, Durability = 1 };
            await KeyInput(Key.Enter); notice = TakeNotice();
            Require(notice != null && Request(notice).HasCancel && !Busy() && !NetBusy(), "With a coupon Enter asks the client's coupon confirmation first");
            await CaptureNotice("confirm", notice!, "Use a Make Over Coupon to change your look?");
            Dismiss(notice!); Require(!Busy() && !NetBusy() && window.Visible, "Declining the confirmation sends nothing");
            await Click(accept); notice = TakeNotice(); Request(notice!).Confirm(); notice!.Free(); await Frames();
            Require(Busy() && NetBusy(), "Confirming sends one beauty request"); await Capture("pending");
            int face = editor.Face, hair = editor.Hair;
            await Click(Find<Button>("look_face_next")); await Click(swatch); await Click(accept); await KeyInput(Key.KpEnter);
            Require(Busy() && editor.Face == face && editor.Hair == hair && TakeNotice() == null && PreviewFixtures.PrepareHairPalette(world) is not { Visible: true },
                "Pending edits, palette, repeated click and keypad Enter cannot replace the submitted look");
            Require(!Game.Requests.ChangeHair.Change(0, 1, 0x543210), "Network independently refuses a second outstanding beauty request");
            Reply(99); Require(Busy() && NetBusy(), "Unknown result does not complete a pending request");
            Reply((byte)ChangeHairResult.Failed); notice = TakeNotice();
            Require(notice != null && !Busy() && !NetBusy() && window.Visible && !accept.Disabled && editor.Hair == hair, "Refusal unlocks the same pick and keeps the shop open");
            await CaptureNotice("refused", notice!, ItemData.Text(18903, "Feature modification failed.")); Dismiss(notice!);
            await Click(accept); notice = TakeNotice(); Request(notice!).Confirm(); notice!.Free();
            Reply((byte)ChangeHairResult.Succeeded); notice = TakeNotice();
            Require(notice != null && !window.Visible && !Busy() && !NetBusy() && Game.Session.Self.LastEnter.Face == face && Game.Session.Self.LastEnter.Hair == hair, "Success closes the shop and updates the network snapshot");
            Require((int)DetailField(world, "Session.Self.Face")! == face && (int)DetailField(world, "Session.Self.Hair")! == hair, "Success updates live world appearance");
            await CaptureNotice("applied", notice!, ItemData.Text(18902, "Feature modification succeeded.")); Dismiss(notice!);
            Open(); Require(editor.Hair == hair && editor.Face == face, "Reopen loads the newly applied appearance");
            Reply((byte)ChangeHairResult.Succeeded); Require(window.Visible && !Busy() && TakeNotice() == null, "Idle duplicate responses cannot alter or close a reopened editor");
            await Click(accept); notice = TakeNotice(); Request(notice!).Confirm(); notice!.Free();
            net.Disconnect(expected: true); notice = TakeNotice();
            Require(!Busy() && !NetBusy() && window.Visible && notice != null, "Disconnect clears the pending request without applying a look"); Dismiss(notice!);
            await Capture("disconnected");
            foreach (int race in nation == 1 ? new[] { 1, 2, 3, 4, 6 } : new[] { 11, 12, 13, 14 })
            {
                LoadRace(race); await Frames();
                for (int i = 0; i < 4; i++)
                {
                    editor.Load(new[] { race }, race, 0, 0x5A3820); DetailCall(world, "ChangeHair.ShowBeautyLook");
                    Require(Pivot().GetChildCount() == 1, "Fast reload leaves one model " + race + "/" + i);
                }
                foreach (string part in new[] { "face", "hair" })
                {
                    int count = part == "face" ? CharacterPreview.FaceCount(race) : CharacterPreview.HairCount(race);
                    for (int i = 0; i < count; i++)
                    {
                        await Click(Find<Button>("look_" + part + "_next"));
                        Require((part == "face" ? editor.Face : editor.HairStyle) == (i + 1) % count, "Actual content variant and wrap " + race + "/" + part + "/" + i);
                        CheckFraming("Every variant fits the native preview " + race + "/" + part + "-" + i, new Rect2(1, 1, 218, 298));
                    }
                }
                for (int i = 0; i < 8; i++) { preview.Turn(45); await Frames(2); CheckFraming("Every turn fits the native preview " + race + "/turn-" + i, new Rect2(1, 1, 218, 298)); }
                await Capture("race-" + race);
            }
            Require(CharacterPreview.HairCount(editor.Race) == 0 && swatch.Disabled, "Hairless race disables the hair colour swatch");
            await Click(swatch); Require(PreviewFixtures.PrepareHairPalette(world) is not { Visible: true }, "A disabled swatch opens no palette");
            LoadRace(nation == 1 ? 2 : 12);
            var originalPosition = window.Position; var p = Find<Control>("party_drag").GetGlobalRect().Position + new Vector2(120, 12);
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true); await Frames(1);
            var delta = new Vector2(31, 17); GetViewport().PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - originalPosition - delta).Length() < 1, "Actual header dragging uses native HudLayout"); await Capture("dragged");
            await OpenPalette(); await KeyInput(Key.Escape); Require(!palette!.Visible && window.Visible, "Escape closes the open palette before the shop");
            await KeyInput(Key.Escape); Require(!window.Visible && !Busy(), "Escape then closes the shop without submitting"); Open();
            await Click(Find<Button>("look_cancel")); Require(!window.Visible && !Busy(), "Cancel closes without submitting"); Open();
            await Click(Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close")); Require(!window.Visible && !Busy(), "Original close artwork invokes native close");
            Open(); GetWindow().Size = new Vector2I(1200, 850); window.Position = new Vector2(320, 140); await Capture("wide-viewport");
        }
        finally { DetailCall(world, "ChangeHair.Dispose"); layer.Free(); world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-beauty.json", JsonSerializer.Serialize(new { nation, checks, screens, fixture = "Actual native viewport input and network result dispatch; no live transactions", liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic beauty audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
