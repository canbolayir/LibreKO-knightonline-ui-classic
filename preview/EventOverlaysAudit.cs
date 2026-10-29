using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;
using Spec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout;

// HUD plates, boards and banners of the new events (soccer score, Monster Stone timer, Knight Royale sign-up, genie
// quick panel, event titles, gather chip, area banner, order message, kill splash, Nereid boards, siege tower
// actions, ranker paper and the Wanted buttons). The client builds each in its own preview state; the overlay
// preparer finds it through its native field (as the live four-times-a-second check does) and skins it.
public partial class Preview
{
    private async Task CaptureEventOverlaysAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1280, 800);
        // A dark ground with a light band: plates and banners must read over both.
        AddChild(new ColorRect { Color = new Color("2f332a"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new ColorRect { Color = new Color("b2ac98"), Position = new Vector2(0, 300), Size = new Vector2(1280, 160), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        Native.Call(Game.Session.Self, "SeedPreviewEnter", new MyInfo { Nation = nation, Name = "Classic Preview" });
        LibreKO.Domain.ItemData.EnsureLoaded();
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("EVENT_OVERLAYS_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var viewport = GetViewportRect();
        bool Inside(Rect2 outer, Rect2 inner) => outer.Grow(.5f).Encloses(inner);
        bool Fits(Label label) => label.AutowrapMode != TextServer.AutowrapMode.Off || label.ClipText
            || label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X <= label.Size.X + 1;
        var layer = new CanvasLayer { Layer = 70 }; AddChild(layer);
        async Task Shot(string state, params Control[] shown)
        {
            await Frames();
            foreach (var control in shown)
            {
                Require(Inside(viewport, control.GetGlobalRect()), state + ": inside the viewport " + control.GetGlobalRect());
                foreach (var label in Descendants(control).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0))
                {
                    // Chat tab captions keep the original tab font (Arial); every other plate text is Arial Bold.
                    Require(label.GetThemeFont("font") == (label is ClassicButtonCaption ? Plugin.Kit.Regular : Plugin.Kit.Bold), state + ": Classic font '" + label.Text + "'");
                    Require(Fits(label), state + ": '" + label.Text + "' fits " + label.Size);
                    Require(Inside(control.GetGlobalRect(), label.GetGlobalRect()), state + ": '" + label.Text + "' inside its plate");
                }
            }
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, bounds = shown.Select(c => c.GetGlobalRect().ToString()).ToArray() });
        }
        Control Show(Control control, Vector2 at)
        {
            if (control.GetParent() != null) control.GetParent().RemoveChild(control);
            layer.AddChild(control); control.Visible = true; control.Position = at;
            return control;
        }
        void Clear() { foreach (var child in layer.GetChildren()) { layer.RemoveChild(child); child.QueueFree(); } }
        void Plaque(Control plate, string what)
        {
            Require(plate.GetThemeStylebox("panel") is StyleBoxEmpty, what + ": the modern panel is gone");
            Require(plate.GetSignalConnectionList(CanvasItem.SignalName.Draw).Count > 0, what + ": the information plaque art is drawn behind it");
            Require(Mathf.Abs(plate.Size.Y - Spec.Plaque.Height) < .5f, what + ": plaque height " + plate.Size.Y + " "
                + string.Join(", ", Descendants(plate).OfType<Control>().Select(c => c.GetType().Name + ":" + c.GetCombinedMinimumSize())));
        }

        // Soccer score (a closable war score strip).
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var board = Show((Control)DetailCall(world, "Preview.BuildSoccer")!, new Vector2(500, 40));
            NativeOverlays.PrepareLater(world);
            await Frames();
            Require(board.Name == "soccer_score" && board.HasMeta("classic_overlay"), "soccer: the score strip is prepared and skinned");
            Require(board.GetThemeStylebox("panel") is StyleBoxEmpty, "soccer: plaque instead of the modern panel");
            var close = Descendants(board).OfType<Button>().Single(b => b.TooltipText == "Close");
            Require(close.GetThemeStylebox("normal") is StyleBoxTexture && close.Size == new Vector2(20, 20), "soccer: the plaque close button art");
            await Shot("soccer", board);
            Clear(); world.Free();
        }
        // Monster Stone and Chaos timer toast.
        foreach (bool completed in new[] { false, true })
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var toast = Show((Control)DetailCall(world, "Preview.BuildNestTimer", completed)!, new Vector2(440, 40));
            NativeOverlays.PrepareLater(world);
            await Frames();
            Require(toast.GetThemeStylebox("panel") is StyleBoxEmpty && Descendants(toast).OfType<TextureRect>().All(r => !r.Visible), "timer: no modern plate or gradient rules");
            Require(Descendants(toast).OfType<Label>().Where(l => l.Visible).All(l => l.GetThemeConstant("outline_size") > 0), "timer: outlined centre-notice text");
            await Shot(completed ? "timer-completed" : "timer", toast);
            Clear(); world.Free();
        }
        // Knight Royale sign-up board and the genie quick panel: the plain event frame.
        foreach (var (fixture, state) in new[] { ("BuildRoyaleBoardUiPreview", "royale-board"), ("BuildGenieSubUiPreview", "genie-quick") })
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var board = Show((Control)DetailCall(world, fixture)!, new Vector2(480, 120));
            NativeOverlays.PrepareLater(world);
            await Frames();
            var frame = board.GetNode<ClassicFrame>("classic_frame");
            Require(frame.Size == board.Size && !frame.Ornate, state + ": the plain Classic frame covers the box");
            foreach (var button in Descendants(board).OfType<Button>().Where(b => b.IsVisibleInTree() && b.Text.Length > 0 && !b.Flat && b is not CheckBox))
                Require(button.GetThemeStylebox("normal") is StyleBoxTexture, state + ": Classic button art " + button.Text);
            foreach (var control in Descendants(board).OfType<Control>().Where(c => c.IsVisibleInTree() && c != frame && c != board && c is Label or Button or ProgressBar))
                Require(Inside(board.GetGlobalRect().Grow(-(ClassicFrame.PlainEdge - 3)), control.GetGlobalRect()), state + ": inside the frame " + control.Name);
            await Shot(state, board);
            Clear(); world.Free();
        }
        // Event title chips: on the bundled notice art (the default) they are myko-hud-audit's; myko-hud-fallback-audit covers the Classic chips.
        Require(ClassicNoticeBars.Available, "titles: the bundled notice art is installed (" + Plugin.Kit.Local.Root + ")");
        // Gather chip, Nereid boards.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            DetailCall(world, "GatherSystem.BuildGatherChip");
            var chip = (Control)DetailField(world, "GatherSystem.gatherChip")!;
            // The chip's screen-position helper needs the live viewport; the audit places the chip itself.
            foreach (var hud in chip.GetChildren().OfType<HudLayout>().ToArray()) { chip.RemoveChild(hud); hud.Free(); }
            ((Label)DetailField(world, "GatherSystem.gatherChipText")!).Text = "Gathering: Iron Ore";
            Show(chip, new Vector2(520, 200));
            var nereid = Show((Control)DetailCall(world, "Preview.BuildNereid")!, new Vector2(500, 360));
            ((Label)DetailField(world, "Nereid.FlagBoard")!).Text = "Monuments  Karus 3 : 2 El Morad";
            ((Label)DetailField(world, "Nereid.ScoreBoard")!).Text = "Karus 1.250 : 980 El Morad";
            // The strip lives on the client's mini map, inside the world.
            var mapStrip = (Label)DetailField(world, "Nereid.Strip")!; mapStrip.GetParent().RemoveChild(mapStrip); world.AddChild(mapStrip);
            NativeOverlays.PrepareLater(world);
            await Frames();
            Plaque(chip, "gather chip");
            var stop = Descendants(chip).OfType<Button>().Single();
            Require(stop.GetThemeStylebox("normal") is StyleBoxTexture && stop.Size == Spec.Plaque.Action, "gather chip: Stop on the chat tab art");
            var strip = (Label)DetailField(world, "Nereid.Strip")!;
            Require(strip.GetParent() is CanvasLayer plates && plates.Name == "classic_map_plates" && plates.GetParent() == world, "nereid strip: re-hosted from the hidden mini map");
            foreach (var board in new[] { "Nereid.FlagBoard", "Nereid.ScoreBoard", "Nereid.Strip" })
            {
                var label = (Label)DetailField(world, board)!;
                Require(label.GetThemeColor("font_color") == ClassicWorldOverlays.PlaqueInk && label.GetNodeOrNull<Control>("plaque_back") is { ShowBehindParent: true } back
                    && (!label.IsInsideTree() || back.Size == label.Size), board + ": plaque art behind the text and plaque ink");
            }
            await Shot("gather-nereid", chip, nereid);
            Clear(); world.Free();
        }
        // Area banner, order message and kill splash: outlined Arial Bold text.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            DetailCall(world, "KnightRoyaleWindow.EnsureAreaBanner");
            var banner = (Control)DetailField(world, "KnightRoyaleWindow.areaBanner")!;
            ((Label)DetailField(world, "KnightRoyaleWindow.areaTitle")!).Text = "Ronark Land";
            ((Label)DetailField(world, "KnightRoyaleWindow.areaSubtitle")!).Text = "PK zone: both nations may attack you";
            banner.Visible = true; banner.GetParent().RemoveChild(banner); layer.AddChild(banner); banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
            var order = (CanvasLayer)DetailCall(world, "Preview.BuildOrderMessage", true, 1280f)!;
            AddChild(order);
            var splash = (Control)DetailCall(world, "KillSplash.Splash", 3, nation, "Zeus")!;
            PreviewFixtures.SetField(world, "KillSplash.killSplash", splash);
            Show(splash, new Vector2(500, 420));
            NativeOverlays.PrepareLater(world);
            await Frames();
            foreach (var label in Descendants(banner).OfType<Label>().Concat(Descendants(order).OfType<Label>()).Where(l => l.Visible))
                Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeConstant("outline_size") > 0, "banner: outlined Arial Bold '" + label.Text + "'");
            await Frames();
            var areaLines = Descendants(banner).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0).Select(l => l.GetGlobalRect()).ToArray();
            foreach (var line in Descendants(order).OfType<Label>().Where(l => l.Visible && l.Text.Length > 0))
                Require(areaLines.All(area => !area.Intersects(line.GetGlobalRect())), "banners: the order message is stacked below the area banner, not drawn over it");
            await Shot("banners", splash);
            order.QueueFree(); Clear(); world.Free();
        }
        // Siege tower Board button under the Classic target frame, and the drop-off action.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var box = Show((Control)DetailCall(world, "Preview.BuildSiegeTower")!, new Vector2(560, 260));
            var target = new TargetFrame(); AddChild(target); target.Visible = true; target.Position = new Vector2(478, 40);
            NativeOverlays.PrepareLater(world);
            await Frames(10);
            var tower = (Button)DetailField(world, "SiegeTowerSystem.TowerButton")!;
            var dropOff = (Button)DetailField(world, "SiegeTowerSystem.DropOff")!;
            Require(tower.GetParent() == target && tower.Visible, "siege tower: the Board button hangs under the Classic target frame");
            Require(Mathf.Abs(tower.GetGlobalRect().GetCenter().X - target.GetGlobalRect().GetCenter().X) <= 1 && tower.GetGlobalRect().Position.Y >= target.GetGlobalRect().End.Y,
                "siege tower: centred under the target frame");
            Require(tower.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0 && dropOff.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0, "siege tower: native callbacks kept");
            foreach (var button in new[] { tower, dropOff }) Require(button.GetThemeStylebox("normal") is StyleBoxTexture, "siege tower: service button art " + button.Text);
            await Shot("siege-tower", tower, dropOff);
            target.QueueFree(); Clear(); world.Free();
        }
        // Ranker paper.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var paper = Show((Control)DetailCall(world, "Preview.BuildRankerPaper")!, new Vector2(400, 150));
            NativeOverlays.PrepareLater(world);
            await Frames();
            foreach (var button in Descendants(paper).OfType<Button>().Where(b => b.IsVisibleInTree())) Require(button.GetThemeStylebox("normal") is StyleBoxTexture, "ranker paper: Classic button " + button.Text);
            await Shot("ranker-paper", paper);
            Clear(); world.Free();
        }
        // Wanted buttons: on the hidden client mini map, re-hosted on a plaque in the Ronark Land ladder zones.
        {
            var world = PreviewFixtures.Prepared(new WorldRoot());
            var vanguard = new CanvasLayer(); world.AddChild(vanguard); PreviewFixtures.SetField(world, "VanguardWindow.Layer", vanguard);
            var map = new Control(); world.AddChild(map);
            var toggle = new Button { TooltipText = "Wanted List UI on/off" }; map.AddChild(toggle); PreviewFixtures.SetField(world, "VanguardWindow.wantedToggle", toggle);
            var open = new Button { TooltipText = "Wanted List UI" }; map.AddChild(open); PreviewFixtures.SetField(world, "VanguardWindow.wantedOpen", open);
            bool opened = false; open.Pressed += () => opened = true;
            Native.Set(world, "ZoneId", 71);
            NativeOverlays.PrepareLater(world);
            world.RemoveChild(vanguard); AddChild(vanguard);
            await Frames();
            var plaque = vanguard.GetNode<ClassicWantedPlaque>("classic_wanted");
            Require(plaque.Visible && toggle.GetParent() == plaque && open.GetParent() == plaque, "wanted: both buttons on the plaque in Ronark Land");
            Require(Mathf.Abs(plaque.GetGlobalRect().End.X - (viewport.Size.X - Spec.Wanted.Margin)) < 1, "wanted: plaque at the top right");
            foreach (var button in new[] { toggle, open })
                Require(button.GetThemeStylebox("normal") is StyleBoxTexture && Inside(plaque.GetGlobalRect(), button.GetGlobalRect()), "wanted: chat tab art inside the plaque " + button.Text + " " + button.GetGlobalRect() + " in " + plaque.GetGlobalRect());
            open.EmitSignal(BaseButton.SignalName.Pressed);
            Require(opened, "wanted: List keeps the native open action");
            await Shot("wanted", plaque);
            Native.Set(world, "ZoneId", 21);
            await Frames(2);
            Require(!plaque.Visible, "wanted: hidden outside the ladder zones");
            vanguard.QueueFree(); world.Free();
        }

        System.IO.File.WriteAllText(output + "/" + prefix + "-event-overlays.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("EVENT_OVERLAYS_AUDIT_OK " + checks.Count);
    }
}
