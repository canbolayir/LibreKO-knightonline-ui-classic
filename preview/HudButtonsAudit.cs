using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;

// The status bar's letter row on a full native World (hud-buttons-audit runs inside plugin-toggle-audit, after the
// third enable): Q opens the Character window's Quest list (never Quest Details), P/A/D/G/E/R open the client's own
// windows and close them again, E and R carry the alert and new-day states of the top-right icons they replace, and
// those icons stay hidden while the plugin runs (the toggle states check that they come back when it is disabled).
public partial class Preview
{
    private async Task<object> ProbeHudButtons(WorldRoot world, int nation, string output, Action<bool, string> require, Func<Node?, bool> shown)
    {
        string prefix = nation == 1 ? "karus" : "human";
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var checks = new List<string>(); var failures = new List<string>(); var screens = new List<string>();
        var bounds = new Dictionary<string, object>();
        void Require(bool ok, string message)
        {
            (ok ? checks : failures).Add(message);
            require(ok, "hud buttons: " + message);
        }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        object? Get(object owner, string member) => Native.Get<object>(owner, member);
        void Set(WorldRoot owner, string member, object value) => Native.Set(owner, member, value);
        // Screen rectangles: the HUD and the windows sit on canvas layers with their own scale.
        Rect2 Screen(Control c) { var t = c.GetGlobalTransformWithCanvas(); return new Rect2(t.Origin, c.Size * t.Scale); }
        object Rect(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y };
        var windows = PluginHost.Game.Windows;

        var status = Descendants(world).OfType<StatusHud>().Single(s => s.IsVisibleInTree());
        // Two rows (plugin be6856d): Q P A D G, then E R; the list runs row by row.
        var letters = Descendants(status).OfType<ClassicStatusShortcut>().Where(s => s.HasBackground).OrderBy(s => Screen(s).Position.Y).ThenBy(s => Screen(s).Position.X).ToArray();
        var mail = Descendants(status).OfType<ClassicStatusShortcut>().Single(s => !s.HasBackground);
        ClassicStatusShortcut Letter(string letter) => letters.Single(s => s.Letter == letter);
        Rect2 plates = letters.Aggregate(Screen(letters[0]), (r, s) => r.Merge(Screen(s)));
        // With the bundled 2.x menu art (the default) the original clock follows R (opening the Events window).
        var clock = Descendants(status).OfType<ClassicStatusClock>().SingleOrDefault();
        Rect2 row = plates.Merge(Screen(mail)).Merge(clock != null ? Screen(clock) : plates).Grow(6);
        async Task Shot(string state, Rect2? crop = null)
        {
            await Frames();
            using var image = GetViewport().GetTexture().GetImage();
            string file = prefix + "-buttons-" + state + ".png";
            if (crop is { } viewportCrop)
            {
                // The captured image has the window's pixel size; the viewport may be larger (UI scale).
                float scale = image.GetWidth() / GetViewportRect().Size.X;
                var c = new Rect2(viewportCrop.Position * scale, viewportCrop.Size * scale);
                var r = new Rect2I((Vector2I)c.Position.Floor(), (Vector2I)c.Size.Ceil()).Intersection(new Rect2I(Vector2I.Zero, image.GetSize()));
                using var part = image.GetRegion(r); part.SavePng(System.IO.Path.Combine(output, file));
            }
            else image.SavePng(System.IO.Path.Combine(output, file));
            screens.Add(file);
        }

        // A real left click at the plate's rendered centre: the button must receive it above every open window.
        async Task Click(BaseButton button)
        {
            string letter = (button as ClassicStatusShortcut)?.Letter ?? "clock";
            float scale = GetWindow().Size.X / GetViewportRect().Size.X;
            var at = Screen(button).GetCenter() * scale;
            int pressed = 0;
            void Count() => pressed++;
            button.Pressed += Count;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frames(1);
            var hovered = GetViewport().GuiGetHoveredControl();
            string picked = hovered == null ? "none" : hovered.GetPath() + " " + hovered.GetType().Name + " " + Screen(hovered);
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                await Frames(1);
            }
            button.Pressed -= Count;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1270, 790), GlobalPosition = new Vector2(1270, 790) });
            var open = Descendants(world).OfType<HudWindow>().Where(w => shown(w) && Screen(w).HasPoint(at / scale)).Select(w => w.Id).ToList();
            Require(pressed == 1, $"{letter} takes a real click at its rendered centre {at} (windows there: {string.Join(", ", open)}; picked {picked})");
            if (pressed == 0) button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        // ---- The rows: Q P A D G, and under them E R, the clock and mail; one size, one baseline per row, the original pitch.
        Require(letters.Select(s => s.Letter).SequenceEqual(new[] { "Q", "P", "A", "D", "G", "E", "R" }), "the rows are Q P A D G / E R (" + string.Join(" ", letters.Select(s => s.Letter)) + ")");
        var first = Screen(letters[0]);
        float pitch = Screen(letters[1]).Position.X - first.Position.X;
        float rowPitch = Screen(Letter("E")).Position.Y - first.Position.Y;
        Vector2 Cell(int column, int row) => first.Position + new Vector2(pitch * column, rowPitch * row);
        for (int i = 0; i < letters.Length; i++)
        {
            var r = Screen(letters[i]);
            var at = i < 5 ? Cell(i, 0) : Cell(i - 5, 1);
            bounds[letters[i].Letter] = Rect(r);
            Require(r.Size.IsEqualApprox(first.Size) && (r.Position - at).Abs() is var d && d.X < .01f && d.Y < .01f,
                $"{letters[i].Letter} sits in its row at the pitch {pitch} with the plate size {first.Size} ({r}, expected {at})");
            Require(letters[i].IsVisibleInTree() && GetViewportRect().Encloses(r) && Screen(status).Encloses(r), $"{letters[i].Letter} is shown inside the status HUD and the screen");
        }
        Require(pitch >= first.Size.X, $"the plates do not overlap (pitch {pitch}, width {first.Size.X})");
        Require(Mathf.Abs(rowPitch - first.Size.Y - 2) < .01f, $"the second row sits 2 px under the first (row pitch {rowPitch}, height {first.Size.Y})");
        int mailColumn = 2 + (clock != null ? 1 : 0);
        Require((Screen(mail).Position - Cell(mailColumn, 1)).Abs() is var md && md.X < .01f && md.Y < .01f && Screen(mail).Size.IsEqualApprox(first.Size),
            $"the envelope ends the second row ({Screen(mail)}, expected {Cell(mailColumn, 1)})");
        Require((clock != null) == (ClassicStatusClock.Art != null) && (clock != null) == !ClassicFallback, "the clock is shown exactly when the 2.x menu art is installed (by default)");
        if (clock != null)
        {
            var c = Screen(clock);
            Require(c.Size.IsEqualApprox(first.Size) && (c.Position - Cell(2, 1)).Abs() is var cd && cd.X < .01f && cd.Y < .01f,
                $"the clock sits one pitch after R in the second row with the plate size ({c})");
            Require(clock.IsVisibleInTree() && GetViewportRect().Encloses(c) && Screen(status).Encloses(c), "the clock is shown inside the status HUD and the screen");
            bounds["clock"] = Rect(c);
        }
        bounds["row"] = Rect(row); bounds["plates"] = Rect(plates);

        // ---- Tooltips: English titles with the client's key binding where it has one.
        string Tip(string letter) => Letter(letter)._GetTooltip(Vector2.Zero);
        string KeyText(KeyAction action) => KeyBinds.Get(action).Text;
        Require(Tip("Q") == "Quests (" + KeyText(KeyAction.Quests) + ")", "Q tooltip names the Quest Journal key: " + Tip("Q"));
        Require(Tip("P").StartsWith("Power-Up Store (" + KeyText(KeyAction.PowerUpStore) + ")"), "P tooltip: " + Tip("P"));
        Require(Tip("A").StartsWith("Achievements (" + KeyText(KeyAction.Achievements) + ")"), "A tooltip: " + Tip("A"));
        Require(Tip("D").StartsWith("Daily Attendance (" + KeyText(KeyAction.Attendance) + ")"), "D tooltip: " + Tip("D"));
        Require(Tip("G") == "Genie (" + KeyText(KeyAction.Genie) + ")", "G tooltip: " + Tip("G"));
        var tips = letters.ToDictionary(s => s.Letter, s => s._GetTooltip(Vector2.Zero));

        // ---- The replaced top-right icons stay hidden, also after the client shows them again.
        foreach (var field in new[] { "TopIcons.Icon:Genie", "Events.iconButton", "DailyRankWindow.drIcon" })
        {
            var icon = (Control)Native.Get<object>(world, field)!;
            Require(!shown(icon), field + " is hidden while the plugin runs");
            icon.Visible = true; await Frames(1);
            Require(!shown(icon), field + " stays hidden when the client shows it again");
        }
        await Shot("top-right", new Rect2(GetViewportRect().Size.X - 460, 0, 460, 48));

        // ---- States of the whole row.
        windows.Close("inventory"); windows.Close("character_info");
        await Frames(4);
        var covering = Descendants(world).OfType<Control>().Where(c => c.IsVisibleInTree() && c is HudWindow && Screen(c).Intersects(plates)).Select(c => c.Name.ToString()).ToList();
        Require(covering.Count == 0, "no window covers the letter row (" + string.Join(", ", covering) + ")");
        // Quiet E and R first (no event due, rankings viewed) so the normal row is deterministic.
        Set(world, "Events.tickIn", 1e6);
        Set(world, "Events.alert", false); Set(world, "DailyRankWindow.drBlink", false);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        Require(!Letter("E").Lit && !Letter("R").Lit, "E and R are quiet without an event due or new rankings");
        await Shot("row-normal", row);
        var plateButtons = letters.Cast<BaseButton>().Concat(clock != null ? new BaseButton[] { clock } : Array.Empty<BaseButton>()).ToArray();
        foreach (var s in plateButtons) s.EmitSignal(Control.SignalName.MouseEntered);
        await Shot("row-hover", row);
        foreach (var s in plateButtons) { s.ToggleMode = true; s.SetPressedNoSignal(true); s.QueueRedraw(); }
        await Shot("row-pressed", row);
        foreach (var s in plateButtons) { s.SetPressedNoSignal(false); s.ToggleMode = false; s.EmitSignal(Control.SignalName.MouseExited); }
        foreach (var s in plateButtons) { s.Disabled = true; s.QueueRedraw(); }
        await Shot("row-disabled", row);
        foreach (var s in plateButtons) { s.Disabled = false; s.QueueRedraw(); }

        // E and R carry the attention states of the replaced icons: an event starting soon, a new day's rankings.
        Set(world, "Events.alert", true); Set(world, "DailyRankWindow.drBlink", true);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        Require(Letter("E").Lit && Letter("E")._GetTooltip(Vector2.Zero) == "Events — an event starts soon", "E flashes while an event starts soon: " + Letter("E")._GetTooltip(Vector2.Zero));
        Require(Letter("R").Lit && Letter("R")._GetTooltip(Vector2.Zero) == "Daily Rank — new rankings today", "R flashes for a new day's rankings: " + Letter("R")._GetTooltip(Vector2.Zero));
        Require(!Letter("G").Lit && !Letter("Q").Lit, "G and Q have no attention state");
        bool Flash(ClassicStatusShortcut s) => (bool)typeof(ClassicStatusShortcut).GetField("_lit", flags)!.GetValue(s)!;
        for (int i = 0; i < 240 && !(Flash(Letter("E")) && Flash(Letter("R"))); i++) await Frames(1);
        await Shot("row-alert", row);
        Set(world, "Events.tickIn", 0.0);

        // ---- Q: the Quest page's journal list, from every starting point; never the Quest Details subpage.
        var owner = CharacterPageRouter.Owner!;
        var pages = (Dictionary<string, KnightOnlineUiClassic.Layout.LayoutView>)typeof(CharacterWindow).GetField("_pages", flags)!.GetValue(owner)!;
        var lists = (Dictionary<string, CharacterList>)typeof(CharacterWindow).GetField("_lists", flags)!.GetValue(owner)!;
        var extras = (Dictionary<string, Button>)typeof(CharacterWindow).GetField("_extras", flags)!.GetValue(owner)!;
        bool DetailsOpen() => (bool)Native.Call(world, "MainWindows.IsOpen", ["Quests"])!;
        async Task QuestList(string state)
        {
            await Frames(6);
            Require(windows.IsOpen("character_info") && owner.QuestListShown, state + ": the Character window shows the Quest page");
            Require(!DetailsOpen() && Get(owner, "_embedded") == null, state + ": Quest Details is not open");
            Require(pages["quest"].IsVisibleInTree() && lists["quest"].IsVisibleInTree() && !pages["character"].IsVisibleInTree(), state + ": the journal list is the visible page");
            var list = lists["quest"];
            Require(list.Selection.HasValue ? !extras["quest:details"].Disabled : extras["quest:details"].Disabled,
                $"{state}: Details is enabled only with a selected quest ({list.Count} rows, selected '{list.Selected}')");
            var frame = Screen((Control)typeof(CharacterWindow).GetField("_frame", flags)!.GetValue(owner)!);
            // The docked window opens below the Classic status bar, so it covers neither the bar nor its letter row.
            var bar = plates.Merge(Screen(mail)).Merge(Screen(Descendants(status).OfType<Control>().First(c => c.Name == "Img_StateBar")));
            Require(GetViewportRect().Encloses(frame) && !frame.Intersects(bar) && frame.Position.Y - bar.End.Y < 12,
                $"{state}: the window frame is on screen, just below the status bar and its row ({frame} / {bar})");
            bounds["q-" + state] = new { window = Rect(frame), list = Rect(Screen(list)), rows = list.Count, selected = list.Selected };
            await Shot("q-" + state);
        }
        await Click(Letter("Q"));
        await QuestList("closed");
        await Click(Letter("Q"));
        await Frames(4);
        Require(!windows.IsOpen("character_info"), "Q again closes the Character window from its Quest list");

        // The native journal window (the old Q target, and the client's F10) is the Quest Details subpage.
        windows.Open("quests");
        await Frames(8);
        Require(DetailsOpen() && !owner.QuestListShown, "the client's journal window opens as Quest Details");
        await Shot("q-details-reference");
        await Click(Letter("Q"));
        await QuestList("from-details");

        ((BaseButton)owner.FindChild("btn_state", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Require(windows.IsOpen("character_info") && !owner.QuestListShown && pages["character"].IsVisibleInTree(), "the Character Report tab leaves the Quest page");
        await Click(Letter("Q"));
        await QuestList("from-report");
        windows.Close("character_info");
        await Frames(4);

        // ---- The client's "Quest Journal" key (F10): the same toggle as Q, pressed as a real key through the World.
        var questKey = KeyBinds.Get(KeyAction.Quests);
        Require(questKey.Assigned, "the Quest Journal key is bound (" + questKey.Text + ")");
        async Task PressQuestKey()
        {
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = questKey.Key, PhysicalKeycode = questKey.Key, CtrlPressed = questKey.Ctrl,
                    ShiftPressed = questKey.Shift, AltPressed = questKey.Alt, Pressed = down });
                await Frames(1);
            }
        }
        async Task QuestKeyCloses(string state)
        {
            await PressQuestKey();
            await Frames(4);
            Require(!windows.IsOpen("character_info") && !DetailsOpen(), $"{state}: {questKey.Text} on the shown Quest list closes the Character window");
        }
        await PressQuestKey();
        await QuestList("f10-closed");
        await QuestKeyCloses("f10-closed");
        windows.Open("quests");
        await Frames(8);
        Require(DetailsOpen() && !owner.QuestListShown, "f10-from-details: the journal window is open as Quest Details");
        await PressQuestKey();
        await QuestList("f10-from-details");
        await QuestKeyCloses("f10-from-details");
        // A rebind rebuilds the World's hotkey tables; the key keeps its route.
        ((Delegate?)typeof(KeyBinds).GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.DynamicInvoke();
        await Frames(4);
        await PressQuestKey();
        await QuestList("f10-after-rebuild");
        await QuestKeyCloses("f10-after-rebuild");
        var pad = KeyBinds.GetPad(KeyAction.Quests);
        if (pad.Assigned)
        {
            ((Dictionary<PadChord, Action>)DetailField(world, "Hotkeys.padHotkeys")!)[pad]();
            await QuestList("pad-closed");
            windows.Close("character_info");
            await Frames(4);
        }

        // The tracker (like NPC quest flows) still opens the journal on its quest: Quest Details of that quest.
        const int trackedId = 127;
        DetailCall(world, "OnQuestLog", new List<QuestEntry> { new(trackedId, 1) });
        DetailCall(world, "OnQuestStrings", new List<QuestStrings> { new(trackedId, "Animal Blood", "You have promised [Moradon Merchant] Clark to bring him Animal Blood.") });
        var objectives = new QuestObjectives(trackedId, false, [new QuestKillGroup(10, [1], "Shadow Seeker")]);
        DetailCall(world, "OnQuestObjectives", objectives);
        DetailCall(world, "OnQuestView", new QuestView(trackedId, 14435, 21, false, false, false, false, QuestViewState.InProgress, 0, "Animal Blood",
            "You have promised [Moradon Merchant] Clark to bring him Animal Blood.", "", objectives, [4], [], []));
        await Frames(8);
        var trackerRow = Descendants(world).OfType<Control>().FirstOrDefault(c => c.IsVisibleInTree() && c.TooltipText == "Open in the quest journal");
        Require(trackerRow != null, "the tracker lists the active quest with its journal link");
        if (trackerRow != null)
        {
            float scale = GetWindow().Size.X / GetViewportRect().Size.X;
            var at = Screen(trackerRow).GetCenter() * scale;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frames(1);
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                await Frames(1);
            }
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1270, 790), GlobalPosition = new Vector2(1270, 790) });
            bounds["tracker-row"] = Rect(Screen(trackerRow));
        }
        await Frames(8);
        var detailTitle = (Label)DetailField(world, "Quest.detailTitle")!;
        Require(DetailsOpen() && !owner.QuestListShown && Get(owner, "_embedded") != null && (int)DetailField(world, "Quest.Selected")! == trackedId
            && detailTitle.IsVisibleInTree() && detailTitle.Text == "Animal Blood",
            $"a tracker click opens Quest Details on its quest (selected {DetailField(world, "Quest.Selected")}, title '{detailTitle.Text}')");
        await Shot("tracker-details");
        await PressQuestKey();
        await QuestList("f10-from-tracker-details");
        await QuestKeyCloses("f10-from-tracker-details");

        // ---- P A D G E R: the client's own windows, opened and closed again by the same button.
        var natives = new Dictionary<string, Func<bool>>
        {
            ["genie"] = () => (bool)Native.Get<object>(world, "Genie.shown")!,
            ["events"] = () => ((CanvasItem)Native.Get<object>(world, "Events.Window")!).Visible,
            ["daily_rank"] = () => Native.Get<object>(world, "DailyRankWindow.Window") is CanvasItem { Visible: true },
        };
        foreach (var s in letters.Skip(1))
        {
            string id = s.WindowId;
            Require(!windows.IsOpen(id), s.Letter + ": " + id + " starts closed");
            await Click(s);
            await Frames(8);
            var window = Descendants(world).OfType<HudWindow>().Where(w => w.Id == id).OrderByDescending(w => w.Visible).FirstOrDefault();
            Require(windows.IsOpen(id) && window is { } && shown(window), $"{s.Letter} opens the client's {id} window");
            Require(!natives.TryGetValue(id, out var native) || native(), $"{s.Letter}: the client's own state shows {id}");
            if (window != null)
            {
                bounds["open-" + s.Letter] = Rect(Screen(window));
                Require(GetViewportRect().Intersects(Screen(window)), $"{s.Letter}: {id} is on screen ({Screen(window)})");
            }
            if (id == "genie" && window != null)
                Require(status.MiniMapBounds is { } map && map.Size.X > 100 && !Screen(window).Intersects(map),
                    $"G: the Genie window opens clear of the minimap drawn above it ({Screen(window)} / {status.MiniMapBounds})");
            if (id == "daily_rank") await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
            if (id == "daily_rank") Require(!s.Lit && !(bool)Native.Get<object>(world, "DailyRankWindow.drBlink")!, "R records today's rankings as viewed and stops flashing");
            await Shot("open-" + s.Letter);
            await Click(s);
            await Frames(6);
            Require(!windows.IsOpen(id) && (!natives.TryGetValue(id, out native) || !native()), $"{s.Letter} again closes {id}");
        }
        // ---- The clock: the client's Events window in its Classic frame, closed again by the clock.
        if (clock != null)
        {
            var events = natives["events"];
            Require(!windows.IsOpen("events") && !events(), "clock: events starts closed");
            await Click(clock);
            await Frames(8);
            var window = Descendants(world).OfType<HudWindow>().Single(w => w.Id == "events");
            Require(windows.IsOpen("events") && events() && shown(window), "the clock opens the client's Events window");
            Require(window.GetNodeOrNull<Control>("classic_frame") != null, "clock: the Events window keeps its Classic family frame");
            Require(GetViewportRect().Encloses(Screen(window)), $"clock: the schedule is on screen ({Screen(window)})");
            bounds["open-clock"] = Rect(Screen(window));
            await Shot("open-clock");
            await Click(clock);
            await Frames(6);
            Require(!windows.IsOpen("events") && !events(), "the clock again closes the Events window");
        }
        Set(world, "Events.alert", false);
        await Frames(4);

        var result = new { nation, checks, failures, screens, bounds, tooltips = tips };
        File.WriteAllText(System.IO.Path.Combine(output, prefix + "-hud-buttons.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(failures.Count == 0 ? $"HUD_BUTTONS_AUDIT_OK: {checks.Count} checks, {screens.Count} screens" : $"HUD_BUTTONS_AUDIT: {failures.Count} failed");
        return new { checks = checks.Count, failures = failures.Count };
    }
}
