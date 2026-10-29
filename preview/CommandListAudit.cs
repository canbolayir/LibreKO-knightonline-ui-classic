using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    /// <summary>
    /// command-audit: the Classic command list on the original command window for one nation, built by the
    /// client's own preview builder. Checks the original UIF geometry (window, caption, close button, both lists
    /// and scroll bars) at the actual rendered bounds, the original inks and fonts, the Classic grouping (every
    /// client command listed once, the client's list holding the rows it runs), whole rows, the scroll bar
    /// (arrows, thumb, row snapping), hover, selection, the list focus outline, disabled commands and Close.
    /// It also records the client's command texts (names, hints, unused original ids) for the content review.
    /// </summary>
    private async Task CaptureCommandListAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        // The client's text table (command names and hints) ships in the game content pack.
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing pack knightonline.pck");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        ClassicCommandListSkin.Register(PluginHost.Ui);
        var checks = new List<string>(); var screens = new List<object>(); var placements = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require(bool value, string text) { if (!value) throw new Exception("COMMAND_AUDIT: " + text); checks.Add(text); }
        void DetailSet(WorldRoot owner, string field, object? value) => Native.Set(owner, field, value);
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }

        var world = PreviewFixtures.Prepared(new WorldRoot());
        var window = (HudWindow)DetailCall(world, "Preview.BuildCommandList", 0)!;
        var layer = new CanvasLayer { Name = "command_audit" };
        AddChild(layer); layer.AddChild(window); window.Visible = true;
        KnightOnlineUiClassic.Native.NativeCommandList.Prepare(window, world);
        await Frames();
        var frame = window.GetChildren().OfType<ClassicCommandListFrame>().Single();
        Require(frame.Bound, "the Classic command window binds to the client's command list");
        window.Position = ((GetViewportRect().Size - window.Size) / 2).Round();
        await Frames();
        var n = frame.Geometry;
        var groups = frame.GroupList; var entries = frame.CommandList!;
        Require(entries == (ItemList)DetailField(world, "CommandList.entries")!, "the lower list is the client's own command list");
        Require(!((ItemList)DetailField(world, "CommandList.Groups")!).IsVisibleInTree(), "the client's group list waits hidden; the Classic groups are shown");

        // Original UIF geometry.
        var uif = Plugin.Kit.Layout(ClassicCommandListLayout.Uif);
        Rect2 R(KnightOnlineUiClassic.Layout.LayoutNode node) => new(node.X, node.Y, node.W, node.H);
        Require(window.Size == n.Size && frame.Size == n.Size && n.Size == new Vector2(uif.W, uif.H), $"window has the original size {window.Size}");
        var images = uif.Images.ToArray();
        Require(n.Panel == R(images[0]) && n.PanelSource == new Rect2(images[0].SrcX, images[0].SrcY, images[0].SrcW, images[0].SrcH), "panel art is the original piece");
        Require(n.Foot == R(images[1]) && n.FootSource == new Rect2(images[1].SrcX, images[1].SrcY, images[1].SrcW, images[1].SrcH), "foot art is the original piece");
        var caption = uif.Strings.Single();
        Require(n.Title == R(caption) && n.TitleInk.ToHtml(false) == caption.Color.ToHtml(false), "caption rect and ink are the original " + caption.Color.ToHtml(false));
        Require(n.Close == R(uif.Find("btn_cancel")!), "close button rect is the original");
        var listGroups = uif.Find("list_curtailment")!; var listCommands = uif.Find("list_content")!;
        Require(n.Groups == R(listGroups) && n.Commands == R(listCommands), "both lists sit in the original list rectangles");
        foreach (var (list, parts) in new[] { (listGroups, n.GroupScroll), (listCommands, n.CommandScroll) })
        {
            var uifBar = list.Children.Single(c => c.Type == "scrollbar"); var arrows = uifBar.Children.Where(c => c.IsButton).ToArray();
            var track = uifBar.Children.Single(c => c.Type == "trackbar");
            Require(parts.Area == R(uifBar) && parts.Up == R(arrows[0]) && parts.Down == R(arrows[1]) && parts.Track == R(track), "scroll bar parts are the original " + list.Id);
        }
        foreach (var control in Descendants(frame).OfType<Control>().Where(c => c.HasMeta("command_expected_rect") && c is not ItemList))
        {
            var expected = control.GetMeta("command_expected_rect").AsRect2();
            var actual = control.GetParent() == frame ? control.GetRect() : new Rect2(control.GetParent<Control>().Position + control.Position, control.Size);
            Require(actual == expected, "actual bounds of " + control.Name + " " + actual);
        }
        Require(GetViewportRect().Encloses(window.GetGlobalRect()), "window inside the viewport");

        // Typography and inks.
        var title = Descendants(frame).OfType<Label>().Single(l => l.Name == "command_title");
        Require(Math.Abs(title.GetRect().GetCenter().Y - n.Title.GetCenter().Y) <= .5f && title.GetRect().Position.X == n.Title.Position.X && title.Size.X == n.Title.Size.X,
            "caption centred on the original caption band " + title.GetRect());
        Require(title.GetThemeFont("font") == Plugin.Kit.Bold && title.GetThemeFontSize("font_size") == UiKit.FontSize(caption) && title.Text == "Command",
            $"caption Arial Bold {title.GetThemeFontSize("font_size")} px (original {caption.Size} pt)");
        foreach (var list in new[] { groups, entries })
        {
            Require(list.GetThemeFont("font") == Plugin.Kit.Regular && list.GetThemeFontSize("font_size") == ClassicCommandListLayout.ListFontSize, list.Name + ": Arial 13 px rows (original GulimChe 10 pt)");
            Require(list.GetThemeStylebox("selected") is StyleBoxFlat { DrawCenter: false } sel && sel.BorderColor == ClassicCommandListLayout.SelectedLine && sel.BorderWidthTop == 1,
                list.Name + ": selection is the original 1 px green outline");
        }
        float Pitch(ItemList list) => list.ItemCount > 1 ? list.GetItemRect(1).Position.Y - list.GetItemRect(0).Position.Y : list.GetItemRect(0).Size.Y;
        float textHeight = Plugin.Kit.Regular.GetHeight(ClassicCommandListLayout.ListFontSize);
        Require(Math.Abs(Pitch(groups) - textHeight) <= 1 && Math.Abs(Pitch(entries) - textHeight) <= 1, $"rows are stacked by text height ({Pitch(entries)} / {textHeight})");

        // Grouping: every client command listed exactly once, in the client's list when its group is shown.
        var model = frame.Model;
        var all = KnightOnlineUiClassic.Native.NativeCommandList.Read(world);
        var genieRows = KnightOnlineUiClassic.Native.NativeGenieCommands.Rows(world);
        frame.ShowGroup(0);
        Require(groups.ItemCount == model.Count && model.Count == ClassicCommandListLayout.Groups.Length, $"{groups.ItemCount} Classic groups");
        Require(genieRows.Select(r => r.Id).SequenceEqual(KnightOnlineUiClassic.Native.NativeGenieCommands.Ids) && genieRows.All(r => r.Entry == null),
            "the client's Genie actions are listed as Genie rows (no client command entry)");
        var listed = all.Concat(genieRows).ToList();
        Require(model.Sum(g => g.Rows.Count) == listed.Count && listed.Select(r => r.Id).Distinct().Count() == listed.Count
            && listed.All(r => model.Count(g => g.Rows.Any(x => x.Id == r.Id)) == 1), $"all {all.Count} client commands and {genieRows.Count} Genie rows are listed once");
        Require(model.Select(g => g.Title).SequenceEqual(new[] { "Private", "Emotion", "Chat", "Trade", "Party", "Clan", "Knights", "Genie", "King" }),
            "group order: the original Private, Trade, Party, Clan, Knights, King; Emotion and Chat after Private; Genie in the Guardian Monster place");
        for (int g = 0; g < model.Count; g++)
        {
            // An item without its own colour reports Godot's unset colour (opaque black) and draws in the list's font colour.
            var ink = groups.GetItemCustomFgColor(g) != new Color(0, 0, 0) ? groups.GetItemCustomFgColor(g) : groups.GetThemeColor("font_color");
            Require(ink.IsEqualApprox(ClassicCommandListLayout.GroupInk), "group ink yellow " + groups.GetItemText(g) + " " + ink.ToHtml());
            groups.Select(g); groups.EmitSignal(ItemList.SignalName.ItemSelected, (long)g); await Frames(2);
            var shown = (System.Collections.IList)DetailField(world, "CommandList.cmdShown")!;
            int clientRows = model[g].Rows.Count(r => r.Entry != null);
            Require(entries.ItemCount == model[g].Rows.Count && shown.Count == clientRows && model[g].Rows.Take(clientRows).All(r => r.Entry != null),
                groups.GetItemText(g) + ": the client's list holds the group's rows, its entries the client commands (listed first)");
            for (int i = 0; i < entries.ItemCount; i++)
            {
                var row = model[g].Rows[i];
                if (row.Entry == null)
                {
                    // A client action: the client's activation handler has no entry for it; the client's state decides the look.
                    var state = KnightOnlineUiClassic.Native.NativeGenieCommands.StateOf(world, row.Id);
                    Require(entries.GetItemText(i) == row.Text && i >= shown.Count && entries.IsItemDisabled(i) == !state.Enabled
                        && entries.GetItemCustomFgColor(i) == (state.Enabled ? ClassicCommandListLayout.CommandInk : ClassicCommandListLayout.UnavailableInk)
                        && entries.GetItemTooltip(i) == state.Tooltip, groups.GetItemText(g) + ": " + row.Text + (state.Enabled ? " white" : " grey and disabled") + ", tooltip '" + state.Tooltip + "'");
                    continue;
                }
                Require(entries.GetItemText(i) == row.Text && Equals(shown[i], row.Entry), groups.GetItemText(g) + ": row " + row.Text + " runs its own client entry");
                Require(entries.GetItemCustomFgColor(i) == (row.Available ? ClassicCommandListLayout.CommandInk : ClassicCommandListLayout.UnavailableInk)
                    && entries.IsItemDisabled(i) == !row.Available, groups.GetItemText(g) + ": " + row.Text + (row.Available ? " white" : " grey and disabled"));
                Require(entries.GetItemTooltip(i) == row.Tooltip, groups.GetItemText(g) + ": " + row.Text + " keeps the client's hint" + (row.Tooltip.Length == 0 ? " (the client has none)" : ""));
            }
            Require(entries.GetItemRect(entries.ItemCount - 1).End.Y <= entries.Size.Y + .5f, groups.GetItemText(g) + ": every row is whole without scrolling");
        }

        async Task Capture(string state)
        {
            await Frames();
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            var listRect = entries.GetRect();
            Require(listRect.Position == n.Commands.Position && listRect.Size.X == n.Commands.Size.X && listRect.Size.Y <= n.Commands.Size.Y
                && Math.Abs(listRect.Size.Y % Pitch(entries)) < .5f, state + ": command list shows whole rows in its box " + listRect);
            Require(groups.GetRect().Position == n.Groups.Position && groups.GetRect().Size.Y <= n.Groups.Size.Y, state + ": group list inside its box " + groups.GetRect());
            screens.Add(new { state, file, window = window.GetGlobalRect().ToString(), commands = listRect.ToString(), groups = groups.GetRect().ToString() });
        }
        void Move(Vector2 local) => GetViewport().PushInput(new InputEventMouseMotion { Position = window.Position + local, GlobalPosition = window.Position + local }, true);
        Vector2 RowCentre(ItemList list, int i) => list.Position + list.GetItemRect(i).GetCenter();
        int GroupIndex(string name) => model.ToList().FindIndex(g => g.Title == name);

        // Default: Private selected, the category list in use.
        groups.Select(0); groups.EmitSignal(ItemList.SignalName.ItemSelected, 0L); Move(new Vector2(-50, -50)); await Frames();
        var focus = Descendants(frame).OfType<ClassicCommandListFocus>().Single();
        Require(!focus.Commands && focus.Outline == n.Groups.Grow(2), "default: the yellow outline marks the category list");
        await Capture("command-list");

        // Group selected with commands the client has not built (grey, at the end, not selectable).
        int clan = GroupIndex("Clan");
        groups.Select(clan); groups.EmitSignal(ItemList.SignalName.ItemSelected, (long)clan); await Frames();
        int disabled = Enumerable.Range(0, entries.ItemCount).First(i => entries.IsItemDisabled(i));
        Require(Enumerable.Range(disabled, entries.ItemCount - disabled).All(i => entries.IsItemDisabled(i)), "clan: unavailable commands are listed last");
        await Capture("command-list-group");

        // Hover over an available command, then over a disabled one.
        Move(RowCentre(entries, 1)); await Frames();
        Require(entries.GetThemeStylebox("hovered") is StyleBoxFlat { DrawCenter: false } hoverBox && hoverBox.BorderColor == ClassicCommandListLayout.HoverLine, "hover: a dim green outline marks the row under the mouse");
        await Capture("command-list-hover");
        Move(RowCentre(entries, disabled)); await Frames();
        var tap = window.Position + RowCentre(entries, disabled);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = tap, GlobalPosition = tap }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = tap, GlobalPosition = tap }, true);
        await Frames(2);
        Require(entries.GetSelectedItems().Length == 0, "disabled: an unavailable command cannot be selected");
        Require(entries.GetThemeStylebox("hovered") is StyleBoxEmpty, "disabled: an unavailable command shows no hover outline");
        await Capture("command-list-disabled");

        // Selected command: green outline on the row, the yellow outline moves to the command list.
        var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window.Position + RowCentre(entries, 2), GlobalPosition = window.Position + RowCentre(entries, 2) };
        GetViewport().PushInput(press, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = press.Position, GlobalPosition = press.Position }, true);
        Move(new Vector2(-50, -50)); await Frames();
        Require(entries.GetSelectedItems().SequenceEqual(new[] { 2 }) && focus.Commands && focus.Outline == n.Commands.Grow(2), "selected: a click selects the row and moves the focus outline to the commands");
        await Capture("command-list-selected");

        // Genie: the rows follow the client's Genie state (set here as the client's replies would set it; the
        // function audit drives the real replies). Stopped with the time not reported yet, then running.
        int genieGroup = GroupIndex("Genie");
        groups.Select(genieGroup); groups.EmitSignal(ItemList.SignalName.ItemSelected, (long)genieGroup); Move(new Vector2(-50, -50)); await Frames();
        bool Enabled(string id) => !entries.IsItemDisabled(frame.ShownRows.ToList().FindIndex(r => r.Id == id));
        Require(Enabled("GenieWindow") && Enabled("GenieStart") && !Enabled("GenieStop") && !Enabled("GenieSpirit"), "genie: stopped, time not reported yet: Start enabled, Stop and Spirit of Genie grey");
        Move(RowCentre(entries, 1)); await Frames();
        await Capture("command-list-genie");
        var running = new Label { Text = "Genie active \u2022 90 minutes" };
        DetailSet(world, "Genie.RunLabel", running); DetailSet(world, "Genie.Points", 90); DetailSet(world, "Genie.deadline", 1.0);
        Native.Set(Game.Session.Genie, "GenieRunning", true);
        Move(RowCentre(entries, 2)); await Frames();
        int startRow = frame.ShownRows.ToList().FindIndex(r => r.Id == "GenieStart");
        Require(!Enabled("GenieStart") && Enabled("GenieStop") && entries.GetItemTooltip(startRow) == running.Text, "genie running: Start grey with the client's status line, Stop enabled");
        await Capture("command-list-genie-running");
        Native.Set(Game.Session.Genie, "GenieRunning", false);
        running.Text = "No time remaining. Spirit of Genie required."; DetailSet(world, "Genie.Points", 0); await Frames(2);
        Require(!Enabled("GenieStart") && !Enabled("GenieStop") && entries.GetItemTooltip(startRow) == running.Text, "genie without time: Start grey with the client's 'No time remaining' line");
        DetailSet(world, "Genie.deadline", 0.0); running.Text = "Genie stopped."; await Frames(2);

        // Open position: the right edge with the client's 12 px HUD margin, vertically centred, at several screen sizes;
        // HUD on the right half moves it beside narrow parts or keeps it between the parts above and below.
        var size = window.Size;
        foreach (var screen in new[] { new Vector2(800, 600), new Vector2(1024, 768), new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(1600, 900), new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            var free = ClassicCommandListLayout.OpenSpot(screen, size, new List<Rect2>());
            Require(free.X == screen.X - 12 - size.X && (free.Y == Mathf.Floor((screen.Y - size.Y) / 2) || screen.Y < size.Y), $"{screen}: right edge, vertically centred {free}");
            // The Classic HUD on the right: the clock strip, a full party list (8 members), the Genie quick panel, Info at the bottom right.
            var clock = new Rect2(screen.X - 90, 4, 80, 18);
            var party = new Rect2(screen.X - 131, 0, 131, 25 + 8 * 45 + 64);
            var quick = new Rect2(screen.X - 189 - 12, screen.Y / 2, 189, 108);
            var info = new Rect2(screen.X - 430, screen.Y - 190, 420, 180);
            foreach (var (name, hud) in new[] { ("clock", new[] { clock }), ("party", new[] { clock, party }), ("genie", new[] { clock, quick }), ("info", new[] { clock, info }), ("all", new[] { clock, party, quick, info }) })
            {
                var spot = ClassicCommandListLayout.OpenSpot(screen, size, hud);
                var rect = new Rect2(spot, size);
                bool fits = rect.Position.X >= 0 && rect.Position.Y >= 0 && rect.End.X <= screen.X && rect.End.Y <= screen.Y;
                bool clear = hud.All(r => !r.Intersects(rect));
                placements.Add(new { screen = screen.ToString(), hud = name, spot = spot.ToString(), clear });
                Require(fits, $"{screen} {name}: on screen {rect}");
                // The 573 px window fits below the clock from 720 px and between the clock and a 180 px Info from 900 px;
                // on smaller screens it stays centred at the edge over them.
                if (screen.Y >= 720 && (name is not ("info" or "all") || screen.Y >= 900)) Require(clear, $"{screen} {name}: covers no HUD part {rect}");
            }
        }
        // The preview builder drops the client's window layout; the function audit opens the window through it. Here
        // the client's default-position hook the preparer set (HudWindow.DockTo) must give the spot.
        var dock = (Func<Vector2>)typeof(HudWindow).GetField("dock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        var expectedSpot = ClassicCommandListLayout.OpenSpot(GetViewportRect().Size, window.Size, new List<Rect2>());
        Require(dock() == expectedSpot && expectedSpot == new Vector2(GetViewportRect().Size.X - 12 - window.Size.X, Mathf.Floor((GetViewportRect().Size.Y - window.Size.Y) / 2)),
            "the window's default position is its right-middle spot " + dock());
        window.Position = dock(); await Frames(2);
        await Capture("command-list-open");
        window.Position = ((GetViewportRect().Size - window.Size) / 2).Round();
        groups.Select(clan); groups.EmitSignal(ItemList.SignalName.ItemSelected, (long)clan); await Frames(2);
        var press2 = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window.Position + RowCentre(entries, 2), GlobalPosition = window.Position + RowCentre(entries, 2) };
        GetViewport().PushInput(press2, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = press2.Position, GlobalPosition = press2.Position }, true);
        Move(new Vector2(-50, -50)); await Frames();

        // Scrolled: the client's own Private group (19 commands, before the Classic regrouping) is longer than the box.
        frame.ShowRows(all.Where(r => r.ClientGroup == "Private").ToList()); await Frames();
        var scroll = Descendants(frame).OfType<ClassicCommandScroll>().Single(s => s.Name == "command_entry_scroll");
        Require(scroll.Visible && scroll.Up.Disabled && !scroll.Down.Disabled, "scroll: the original scroll bar shows when the rows do not fit, at the top");
        float pitch = Pitch(entries); var bar = entries.GetVScrollBar();
        scroll.Down.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
        Require(Math.Abs(bar.Value - pitch) < .01, "scroll: the down arrow scrolls one row");
        bar.Value = pitch * 1.4; await Frames(2);
        Require(Math.Abs(bar.Value % pitch) < .01, "scroll: the list snaps to whole rows");
        for (int i = 0; i < 30; i++) scroll.Down.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var thumb = scroll.ThumbRect;
        Require(scroll.Down.Disabled && Math.Abs(thumb.End.Y - (n.CommandScroll.Track.End.Y - n.CommandScroll.Area.Position.Y)) < 1, "scroll: at the end the thumb sits at the bottom of the track " + thumb);
        Require(entries.GetItemRect(entries.ItemCount - 1).End.Y - bar.Value <= entries.Size.Y + .5f, "scroll: the last row is whole at the end");
        await Capture("command-list-scrolled");
        Require(!Descendants(frame).OfType<ClassicCommandScroll>().Single(s => s.Name == "command_group_scroll").Visible, "the category list fits and shows no scroll bar");

        // Reopening refills the client's list from its own group; the Classic group comes back.
        window.Visible = false; await Frames(2);
        DetailCall(world, "CommandList.FillEntries", LibreKO.Domain.CommandGroup.Private);
        window.Visible = true; await Frames(3);
        Require(entries.ItemCount == model[clan].Rows.Count && entries.GetItemText(0) == model[clan].Rows[0].Text, "reopen: the selected Classic group is shown again");

        // Close runs the client's close.
        Descendants(frame).OfType<Button>().Single(b => b.Name == "command_close").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
        Require(!window.Visible, "close: the original close button hides the window natively");

        // Without the preparer (client members missing) the client's own two lists stay usable on the original art.
        {
            var bare = PreviewFixtures.Prepared(new WorldRoot());
            var plain = (HudWindow)DetailCall(bare, "Preview.BuildCommandList", 0)!;
            var plainLayer = new CanvasLayer { Name = "command_unbound" };
            AddChild(plainLayer); plainLayer.AddChild(plain); plain.Visible = true;
            await Frames(40);
            plain.Position = ((GetViewportRect().Size - plain.Size) / 2).Round(); await Frames(3);
            var plainFrame = plain.GetChildren().OfType<ClassicCommandListFrame>().Single();
            var lists = Descendants(plainFrame).OfType<ItemList>().Where(l => l.IsVisibleInTree()).ToArray();
            Require(!plainFrame.Bound && lists.Length == 2 && lists.All(l => plainFrame.GetGlobalRect().Encloses(l.GetGlobalRect())), "unbound: the client's two lists show inside the original window " + plainFrame.Bound + " " + string.Join(";", Descendants(plainFrame).OfType<ItemList>().Select(l => l.Name + ":" + l.IsVisibleInTree() + ":" + l.GetGlobalRect())) + " frame " + plainFrame.GetGlobalRect());
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + prefix + "-command-list-unbound.png");
            screens.Add(new { state = "command-list-unbound", file = prefix + "-command-list-unbound.png", window = plain.GetGlobalRect().ToString(), commands = "", groups = "" });
            plainLayer.QueueFree(); bare.QueueFree(); await Frames(2);
        }

        // Client texts for the content review: names, hints and the original ids the catalog skips.
        var texts = new SortedDictionary<int, string>();
        foreach (var (from, to) in new[] { (7800, 7810), (7900, 7910), (8000, 8030), (8100, 8130), (8200, 8212), (8300, 8312), (8400, 8410), (8500, 8510), (8600, 8615), (8700, 8715), (8800, 8810), (8900, 8910), (9000, 9025), (9100, 9125), (9200, 9210), (9300, 9310), (9400, 9410), (9500, 9510) })
            for (int id = from; id <= to; id++) { string t = LibreKO.Domain.ItemData.Text(id, ""); if (t.Length > 0) texts[id] = t; }
        var listing = model.Select(g => new { group = g.Title, commands = g.Rows.Select(r => new { r.Id, r.ClientGroup, r.Text, r.Available, r.Tooltip }) });
        System.IO.File.WriteAllText(output + "/" + prefix + "-command-texts.json", JsonSerializer.Serialize(new { texts, listing }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        System.IO.File.WriteAllText(output + "/" + prefix + "-command.json", JsonSerializer.Serialize(new { nation, checks, screens, placements }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("COMMAND_AUDIT_OK " + prefix + " " + checks.Count + " checks / " + screens.Count + " screens");
        layer.QueueFree(); world.QueueFree();
        await Frames(); offline.Free();
    }
}
