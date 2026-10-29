using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using KnightOnlineUiClassic.Native;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureProgressionAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        Native.Call(Game.Session.Self, "SeedPreviewEnter", new MyInfo { Nation = nation, Name = "Classic Preview" });
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("PROGRESSION_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(HudWindow window, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(3);
            var panel = window.GetChildren().OfType<ClassicProgressionPanel>().Single();
            string file = prefix + "-" + window.Id + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { window = window.Id, state, file, bounds = window.GetGlobalRect().ToString() });
            Require(window.Size == ClassicProgressionLayout.Size(window.Id), state + " fixed Classic footprint " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window fits viewport");
            var inside = new Rect2(Vector2.Zero, panel.Size);
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("progression_expected_rect")))
            {
                var rect = control.GetMeta("progression_expected_rect").AsRect2();
                Require(control.GetRect() == rect, state + " rendered bounds " + control.Name + " " + control.GetRect());
                var global = new Rect2(control.GlobalPosition - panel.GlobalPosition, control.Size);
                Require(inside.Encloses(global), state + " inside frame " + control.Name);
            }
            Require(panel.Frame.Get("btn_close") is BaseButton { Visible: true } close && GetViewportRect().Encloses(close.GetGlobalRect()), state + " Close ornament reachable");
        }
        async Task Hover(Control control)
        {
            var at = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(3);
        }
        void Leave() => GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero });
        (WorldRoot World, HudWindow Window, ClassicProgressionPanel Panel) Open(string fixture, string view)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, fixture, view)!;
            AddChild(window);
            var panel = ClassicProgressionSkin.Apply(window.Body)!;
            Require(panel != null, fixture + " " + view + " native controls prepared for the Classic composition");
            return (world, window, panel!);
        }
        T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().First(n => n.Name == name);
        bool Connected(BaseButton button) => button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0;
        async Task Done(WorldRoot world, HudWindow window) { window.QueueFree(); world.QueueFree(); await Frames(2); }

        // Achievements.
        Require(AchievementData.Count > 0, "Achievement definitions are loaded from the client content");
        foreach (string view in new[] { "summary", "list", "claimed", "empty" })
        {
            var (world, window, panel) = Open("BuildAchievementsUiPreview", view);
            await Frames();
            var tabs = Descendants(panel).OfType<Button>().Where(b => b.HasMeta("progression_tab")).ToArray();
            Require(tabs.Length == 6, view + " summary and five category tabs on Classic tab plates");
            Require(tabs.Count(t => t.GetMeta("progression_tab_selected").AsBool()) == 1, view + " exactly one tab selected");
            for (int i = 1; i < tabs.Length; i++) Require(tabs[i].GetGlobalRect().Position.Y == tabs[0].GetGlobalRect().Position.Y && tabs[i].Size == tabs[0].Size, view + " tab row aligned " + tabs[i].Name);
            await Capture(window, view);
            if (view == "summary")
            {
                var page = Named<VBoxContainer>(panel, "ach_summary_page");
                Require(page.Visible && !Named<HBoxContainer>(panel, "ach_list_row").Visible && !Named<HBoxContainer>(panel, "ach_filters").Visible, "Summary page replaces the list and its filters natively");
                Require(Named<PanelContainer>(panel, "ach_title_banner").IsVisibleInTree() && Named<Label>(panel, "ach_title").Text.StartsWith("Title:"), "Worn title banner stays above every page");
                Require(new Rect2(page.GlobalPosition, page.Size).Encloses(Descendants(page).OfType<Control>().Where(c => c.IsVisibleInTree() && c is PanelContainer).Select(c => c.GetGlobalRect()).Aggregate((a, b) => a.Merge(b))), "Summary sections stay inside the summary page");
                Require(Descendants(page).OfType<ProgressBar>().All(b => b.GetThemeStylebox("fill") is StyleBoxTexture), "Category progress uses the original gauge art");
                var war = tabs.First(t => t.Name == "ach_tab_" + (int)AchievementTab.War);
                await Hover(war); await Capture(window, "tab-hover"); Leave();
                war.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(!(bool)DetailField(world, "Achievement.ViewingSummary")! && (AchievementTab)DetailField(world, "Achievement.Tab")! == AchievementTab.War, "Category tab invokes the native category selection");
                Require(war.GetMeta("progression_tab_selected").AsBool(), "Selected category keeps the pressed plate after the native refresh");
                Require(Named<HBoxContainer>(panel, "ach_list_row").Visible && Named<HBoxContainer>(panel, "ach_filters").Visible, "Category list and filters become visible");
            }
            if (view == "list")
            {
                var list = Named<VBoxContainer>(panel, "ach_list");
                var rows = list.GetChildren().OfType<PanelContainer>().ToArray();
                Require(rows.Length > 6, "War category lists its achievements");
                var scroll = Named<ScrollContainer>(panel, "ach_scroll");
                Require(scroll.GetVScrollBar().Visible, "Long category scrolls inside its fixed inset");
                foreach (var row in rows.Where(r => r.IsVisibleInTree()))
                    Require(row.Size.X <= scroll.Size.X && row.GlobalPosition.X >= scroll.GlobalPosition.X, "Achievement row contained horizontally " + row.GetMeta("ach_row"));
                var claim = Descendants(list).OfType<Button>().First(b => b.Name == "ach_claim");
                Require(Connected(claim) && claim.Size.X >= 76 && claim.GetThemeStylebox("normal") is StyleBoxTexture, "Claim keeps its native callback on a Classic button");
                Require(rows.First().GetMeta("ach_claimable").AsBool(), "Claimable achievements are listed first, as natively sorted");
                void WholeRowsOnly(string state)
                {
                    var view = scroll.GetGlobalRect().Grow(.5f);
                    var shown = rows.Where(r => r.Modulate.A > 0).ToArray();
                    Require(shown.Length > 0 && shown.All(r => view.Encloses(r.GetGlobalRect())), state + " shows whole achievement rows only");
                    Require(rows.Where(r => r.Modulate.A == 0).All(r => !view.Encloses(r.GetGlobalRect())
                        && Descendants(r).OfType<BaseButton>().All(b => b.MouseFilter == MouseFilterEnum.Ignore)), state + " hides and disables rows cut by the list edge");
                    Require(shown.Any(r => Math.Abs(r.GlobalPosition.Y - scroll.GlobalPosition.Y) < .5f), state + " starts the list at a whole row");
                }
                WholeRowsOnly("list");
                var bar = scroll.GetVScrollBar();
                bar.Value = rows[1].Position.Y - 10; await Frames();
                Require(Math.Abs(bar.Value - rows[1].Position.Y) < .5f, "Scrolling down steps to the next whole row");
                WholeRowsOnly("list scrolled");
                bar.Value = bar.MaxValue; await Frames();
                Require(rows[^1].Modulate.A > 0, "The last achievement row scrolls fully into view");
                WholeRowsOnly("list end");
                bar.Value = 0; await Frames();
                await Hover(claim); await Capture(window, "claim-hover"); Leave();
                // Category column, search and status filter drive the native list.
                var categories = Named<VBoxContainer>(panel, "ach_categories").GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();
                Require(categories.Length > 1 && categories.All(r => r.Size.Y == ClassicProgressionLayout.Row), "Category column lists All and the war groups on 22-pixel rows");
                Require(categories.Count(r => r.GetMeta("ach_category_chosen", false).AsBool()) == 1 && categories[0].GetMeta("ach_category_chosen", false).AsBool(), "All is the chosen category");
                var column = Named<PanelContainer>(panel, "ach_category_panel");
                Require(panel.Frame.Get("ach_category_frame")!.GetGlobalRect().Encloses(column.GetGlobalRect()) && panel.Frame.Get("ach_list_frame")!.GetGlobalRect().Encloses(scroll.GetGlobalRect()), "Category column and list inside their insets");
                Require(!column.GetGlobalRect().Intersects(scroll.GetGlobalRect()), "Category column and list do not overlap");
                categories[1].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left }); await Frames(3);
                Require((int)DetailField(world, "Achievement.Group")! != AchievementData.AllGroups, "Category row invokes the native group selection");
                var chosen = Named<VBoxContainer>(panel, "ach_categories").GetChildren().OfType<PanelContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();
                Require(chosen.Length > 1 && chosen[1].GetMeta("ach_category_chosen", false).AsBool() && !chosen[0].GetMeta("ach_category_chosen", false).AsBool(), "Chosen category moves to the clicked row");
                await Capture(window, "category");
                var search = Named<LineEdit>(panel, "ach_search");
                Require(search.GetThemeStylebox("normal") is StyleBoxFlat && search.Size.Y == ClassicProgressionLayout.Row, "Search uses the Classic edit field");
                search.Text = "zzzz-no-match"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Frames(3);
                var none = Named<VBoxContainer>(panel, "ach_list").GetChildren().OfType<Label>().Where(l => !l.IsQueuedForDeletion()).FirstOrDefault();
                Require(none != null && none.Text.Contains("matches"), "Search filters the native list and shows its empty message");
                await Capture(window, "search-empty");
                search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, ""); await Frames(3);
                var status = Named<OptionButton>(panel, "ach_status");
                Require(status.ItemCount == 3 && status.GetThemeStylebox("normal") is StyleBoxTexture, "Status filter on the Classic button art");
                status.Select(2); status.EmitSignal(OptionButton.SignalName.ItemSelected, 2L); await Frames(3);
                Require((AchievementStatusFilter)DetailField(world, "Achievement.StatusFilter")! == AchievementStatusFilter.Completed, "Status filter invokes the native filter");
                await Capture(window, "completed");
                var change = Named<Button>(panel, "ach_change_title");
                Require(Connected(change) && change.GetThemeStylebox("normal") is StyleBoxTexture && change.Size.Y == ClassicProgressionLayout.Row, "Change title keeps its native callback on a Classic button");
            }
            if (view == "claimed")
                Require(Named<VBoxContainer>(panel, "ach_list").GetChildren().OfType<PanelContainer>().All(r => !r.GetMeta("ach_claimed").AsBool()), "Hide claimed keeps the native filter");
            if (view == "empty")
            {
                Require(Named<Label>(panel, "ach_summary").Text.Length >= 0, "Empty state renders");
            }
            Named<Control>(panel.Frame, "btn_close").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!window.Visible && !(bool)DetailField(world, "Achievement.Shown")!, view + " Close ornament invokes the native close");
            await Done(world, window);
        }

        // Attendance.
        foreach (string view in new[] { "progress", "empty", "complete", "failed" })
        {
            var (world, window, panel) = Open("BuildAttendanceUiPreview", view);
            await Frames();
            var daily = Named<GridContainer>(panel, "attendance_daily");
            var bonus = Named<GridContainer>(panel, "attendance_bonus");
            Require(daily.GetChildren().OfType<VBoxContainer>().Count() == AttendanceWire.DailySlots && bonus.GetChildren().OfType<VBoxContainer>().Count() == AttendanceWire.BonusSlots, view + " every daily and cumulative reward");
            await Frames(2);
            foreach (var grid in new[] { daily, bonus })
                foreach (var socket in Descendants(grid).OfType<Button>().Where(b => b.Name == "attendance_socket"))
                {
                    Require(socket.Size == new Vector2(ClassicProgressionLayout.Cell, ClassicProgressionLayout.Cell), view + " Classic 45-pixel reward cell");
                    Require(grid.GetGlobalRect().Encloses(socket.GetGlobalRect()), view + " reward cell inside its inset");
                }
            foreach (var caption in Descendants(daily).OfType<Label>().Where(l => l.GetParent() is VBoxContainer))
                Require(caption.GetGlobalRect().End.Y <= daily.GetGlobalRect().End.Y, view + " caption contained " + caption.Text);
            var claimable = Descendants(daily).OfType<VBoxContainer>().Concat(Descendants(bonus).OfType<VBoxContainer>()).Where(c => c.GetMeta("attendance_claimable", false).AsBool()).ToArray();
            if (view != "complete")
            {
                Require(claimable.Length > 0 && claimable.All(c => Descendants(c).Any(n => n.Name == "claim_mark")), view + " claimable rewards are marked");
                var socket = Descendants(claimable[0]).OfType<Button>().First();
                Require(Connected(socket), view + " claimable reward keeps the native claim callback");
            }
            else Require(claimable.Length == 0, "Complete board has nothing left to claim");
            if (view == "failed") Require(Named<Label>(panel, "attendance_notice").Visible, "Claim refusal notice stays visible inside the frame");
            await Capture(window, view);
            if (view == "progress")
            {
                await Hover(Descendants(claimable[0]).OfType<Button>().First()); await Capture(window, "hover"); Leave();
            }
            Named<Control>(panel.Frame, "btn_close").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!window.Visible && !(bool)DetailField(world, "Attendance.shown")!, view + " Close ornament invokes the native close");
            await Done(world, window);
        }

        // PK ladder (Today's Rank).
        foreach (string view in new[] { "full", "short", "empty" })
        {
            var (world, window, panel) = Open("BuildRankUiPreview", view);
            await Frames();
            await Capture(window, view);
            foreach (var side in new[] { "karus", "elmorad" })
            {
                var headings = Enumerable.Range(0, 5).Select(i => Named<Label>(panel, $"rank_{side}_heading_{i}")).ToArray();
                Require(Named<Label>(panel, "rank_caption_" + side).IsVisibleInTree(), view + " " + side + " ladder caption shown");
                var lines = Enumerable.Range(0, PkLadder.RowsShown).Select(i => Named<HBoxContainer>(panel, $"rank_{side}_row_{i}"))
                    .Append(Named<HBoxContainer>(panel, $"rank_{side}_own")).ToArray();
                Require(lines.All(l => l.Size.Y == ClassicProgressionLayout.Row), view + " " + side + " uniform 22-pixel rows");
                for (int i = 1; i < PkLadder.RowsShown; i++)
                    Require(lines[i].GlobalPosition.Y - lines[i - 1].GlobalPosition.Y == ClassicProgressionLayout.Row, view + " " + side + " rows on a 22-pixel pitch " + i);
                foreach (var line in lines)
                {
                    var cells = line.GetChildren().OfType<Control>().ToArray();
                    for (int c = 0; c < 5; c++)
                    {
                        Require(Mathf.Abs(cells[c].GetGlobalRect().Position.X - headings[c].GetGlobalRect().Position.X) < .5f
                            && Mathf.Abs(cells[c].GetGlobalRect().End.X - headings[c].GetGlobalRect().End.X) < .5f, view + " " + line.Name + " column " + c + " under its heading");
                        Require(line.GetGlobalRect().Encloses(cells[c].GetGlobalRect()), view + " " + line.Name + " cell " + c + " inside its row");
                    }
                    Require(cells.OfType<Label>().All(c => c.ClipText), view + " " + line.Name + " long names and clans clipped to their columns");
                }
                var frame = panel.Frame.Get("rank_frame_" + side)!;
                Require(frame.GetGlobalRect().Encloses(lines[^1].GetGlobalRect()) && frame.GetGlobalRect().Encloses(headings[0].GetGlobalRect()), view + " " + side + " ladder inside its inset");
            }
            bool karus = nation == 1;
            string mine = karus ? "karus" : "elmorad", theirs = karus ? "elmorad" : "karus";
            if (view != "empty")
            {
                Require(Named<Label>(panel, $"rank_{mine}_own_name").Text == "Classic Preview" && Named<Label>(panel, $"rank_{mine}_own_rank").Text == "6", view + " own row under the player's nation");
                Require(Named<Label>(panel, $"rank_{theirs}_own_name").Text.Length == 0, view + " other nation's own row stays empty");
                Require(Named<Label>(panel, $"rank_{mine}_row_0_rank").GetThemeColor("font_color") == new Color("ffe094"), view + " top three places in gold");
                Require(Named<TextureRect>(panel, $"rank_{mine}_row_0_mark").Size == new Vector2(36, 20), view + " rank symbol cell keeps the 36 x 20 mark column");
                var bonus = Named<Label>(panel, $"rank_{mine}_row_0_bonus");
                Require(bonus.Visible && bonus.GetGlobalRect().End.X <= Named<Label>(panel, $"rank_{mine}_heading_4").GetGlobalRect().End.X + .5f, view + " kill bonus fits the score column");
                var name = Named<Label>(panel, $"rank_{mine}_row_1_name");
                Require(name.TooltipText == name.Text, view + " clipped names keep a tooltip");
            }
            if (view == "short") Require(Named<Label>(panel, $"rank_{mine}_row_5_name").Text.Length == 0, "Short ladder leaves the remaining rows empty");
            var notice = Named<Label>(panel, "rank_notice");
            Require(notice.Text.Length > 0 && notice.GetLineCount() <= 2, view + " ladder notice fits its two lines");
            Named<Control>(panel.Frame, "btn_close").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!window.Visible && !(bool)DetailField(world, "Rank.shown")!, view + " Close ornament invokes the native close");
            await Done(world, window);
        }

        // Genie.
        foreach (string view in new[] { "configured", "empty", "running", "pending", "claimed" })
        {
            var (world, window, panel) = Open("BuildGenieUiPreview", view);
            var layer = (CanvasLayer)DetailField(world, "Genie.Layer")!; world.RemoveChild(layer); AddChild(layer);
            // The genie quick panel on the same layer gets its Classic box as the live overlay check gives it.
            NativeOverlays.PrepareLater(world);
            await Frames();
            int cellCount = ClassicProgressionLayout.GenieCells.Sum();
            var slots = Enumerable.Range(0, cellCount).Select(i => Named<Button>(panel, "genie_slot_" + i)).ToArray();
            Require(slots.All(s => s.Size == new Vector2(36, 36) && s.GetThemeStylebox("normal") is StyleBoxTexture), view + " " + cellCount + " skill slots on Classic cells");
            for (int group = 0; group < 3; group++)
            {
                var frame = panel.Frame.Get("genie_skill_frame_" + group)!.GetGlobalRect();
                int first = ClassicProgressionLayout.GenieFirstCell(group), cells = ClassicProgressionLayout.GenieCells[group];
                Require(Enumerable.Range(first, cells).All(i => frame.Encloses(slots[i].GetGlobalRect())), view + " column " + group + " cells inside their inset");
            }
            // Support cells carry the client's party check centred under each cell; the mode toggles sit under the cells.
            var party = Enumerable.Range(0, GenieSetup.SupportCells).Select(i => Named<CheckBox>(panel, "genie_party_" + i)).ToArray();
            var supportFrame = panel.Frame.Get("genie_skill_frame_2")!.GetGlobalRect();
            int supportFirst = ClassicProgressionLayout.GenieFirstCell(2);
            for (int i = 0; i < party.Length; i++)
            {
                var cell = slots[supportFirst + i].GetGlobalRect(); var check = party[i].GetGlobalRect();
                Require(party[i].IsVisibleInTree() && supportFrame.Encloses(check) && party[i].GetThemeIcon("checked") is AtlasTexture, view + " Classic party check under support cell " + i);
                Require(check.Position.Y >= cell.End.Y && Math.Abs(check.GetCenter().X - cell.GetCenter().X) <= 1 && check.Size == new Vector2(ClassicProgressionLayout.GeniePartyWidth, ClassicProgressionLayout.GeniePartyHeight),
                    view + " party check " + i + " centred under its cell " + check);
            }
            var modeNames = new[] { ("genie_mode_attack", 0), ("genie_mode_basicattack", 0), ("genie_mode_combo", 0), ("genie_mode_recovery", 1), ("genie_mode_support", 2), ("genie_mode_leadertarget", 2) };
            foreach (var (mode, group) in modeNames)
            {
                var toggle = Named<CheckButton>(panel, mode);
                var frame = panel.Frame.Get("genie_skill_frame_" + group)!.GetGlobalRect();
                Require(toggle.IsVisibleInTree() && frame.Encloses(toggle.GetGlobalRect()) && toggle.GetGlobalRect().Position.Y >= slots[ClassicProgressionLayout.GenieFirstCell(group)].GetGlobalRect().End.Y,
                    view + " " + mode + " under its column's cells");
            }
            var recovery = Named<SpinBox>(panel, "genie_recovery");
            Require(recovery.IsVisibleInTree() && panel.Frame.Get("genie_skill_frame_1")!.GetGlobalRect().Encloses(recovery.GetGlobalRect()) && Named<Label>(panel, "genie_recovery_label").IsVisibleInTree(),
                view + " recovery percent with its caption in the Recovery column");
            var monsterList = Named<ItemList>(panel, "genie_monster_list");
            if (monsterList.ItemCount > 1)
            {
                float pitch = monsterList.GetItemRect(1).Position.Y - monsterList.GetItemRect(0).Position.Y;
                Require(Math.Abs(monsterList.Size.Y - 6 - ClassicProgressionLayout.GenieMonsterRows * pitch) < .5f, view + " monster list shows whole rows (" + pitch + " px rows)");
            }
            var monsterFrame = panel.Frame.Get("genie_monster_frame")!.GetGlobalRect();
            Require(new[] { "genie_monster_list", "genie_monster_add", "genie_monster_remove", "genie_hint" }.All(n => monsterFrame.Encloses(Named<Control>(panel, n).GetGlobalRect())), view + " monster list, its actions and the hint inside the monster inset");
            await Capture(window, view + "-main");
            var start = Named<Button>(panel, "genie_start");
            if (view == "configured")
            {
                Require(slots.Count(s => s.Icon != null) > 0, "Configured skills show their icons in the cells");
                var list = Named<ItemList>(panel, "genie_monster_list");
                int before = list.ItemCount; list.Select(0); Named<Button>(panel, "genie_monster_remove").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
                Require(list.ItemCount == before - 1, "Remove invokes the native monster list edit");
                await Hover(slots[0]); await Capture(window, "slot-hover"); Leave();
                slots[1].EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
                var picker = (PopupMenu)DetailField(world, "Genie.SkillPicker")!;
                Require(picker.Visible && (int)DetailField(world, "Genie.editingSlot")! == 1, "Skill cell opens the native skill picker");
                Require(picker.GetThemeStylebox("panel") is StyleBoxFlat { BgColor.A: 1 }, "Skill picker uses the Classic popup material");
                await Capture(window, "skill-picker"); picker.Hide(); await Frames(2);
                var mode = Named<CheckButton>(panel, "genie_mode_attack"); bool was = mode.ButtonPressed;
                var nativeModes = (System.Collections.IDictionary)DetailField(world, "Genie.modes")!;
                Require(ReferenceEquals(nativeModes[GenieMode.Attack], mode), "The Attack Mode toggle is the client's own GenieMode.Attack control");
                mode.EmitSignal(BaseButton.SignalName.Pressed); mode.ButtonPressed = !was;
                Require(((GenieSetup)DetailField(world, "Genie.Genie")!).Has(GenieMode.Attack) == !was, "Mode toggles are the native settings controls");
                mode.ButtonPressed = was;
                var setup = (GenieSetup)DetailField(world, "Genie.Genie")!;
                bool wasParty = party[2].ButtonPressed; party[2].ButtonPressed = !wasParty;
                Require(setup.SupportParty[2] == !wasParty, "A party check writes the client's SupportParty flag of its cell");
                party[2].ButtonPressed = wasParty;
                Require(party.Where((p, i) => setup.Support[i] == 0).All(p => !p.ButtonPressed) && party.Any(p => p.ButtonPressed), "Ticked party checks follow the configured support cells");
                recovery.Value = 42; Require(setup.RecoveryPercent == 42, "The recovery percent is the client's own setting");
                Require(Connected(Named<Button>(panel, "genie_spirit")) && Connected(Named<Button>(panel, "genie_save")) && Connected(start), "Start, Save Settings and Spirit of Genie keep native callbacks");
            }
            if (view == "running") Require(start.Text == "Stop" && !start.Disabled, "Running genie offers Stop");
            if (view == "pending") Require(start.Disabled && start.GetThemeStylebox("disabled") is StyleBoxTexture, "Pending start is visibly disabled");
            var misc = Named<Button>(panel, "genie_tab_misc");
            misc.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Require(panel.GenieMiscShown && ((TabContainer)Named<Control>(window, "genie_tabs")).CurrentTab == 1, view + " Misc tab shows the native Misc page");
            Require(!Named<Control>(panel, "genie_slot_0").IsVisibleInTree() && Named<Control>(panel, "genie_ptcode").IsVisibleInTree(), view + " pages never overlap");
            var claim = Named<Button>(panel, "genie_claim");
            Require(claim.Disabled == (view == "claimed"), view + " daily reward button follows the native status");
            var options = new[] { "genie_return_home", "genie_hunt_in_place", "genie_hide_range", "genie_cure_curse", "genie_auto_party", "genie_ptcode" }.Select(n => Named<Control>(panel, n)).ToArray();
            var settingsFrame = panel.Frame.Get("genie_settings_frame")!.GetGlobalRect();
            Require(options.All(o => o.IsVisibleInTree() && settingsFrame.Encloses(o.GetGlobalRect())), view + " movement, curse and party options inside the settings inset");
            var potionFrame = panel.Frame.Get("genie_potion_frame")!.GetGlobalRect();
            var potions = new[] { "genie_potion_title", "genie_mode_hppotion", "genie_hp_item", "genie_hp", "genie_mode_mppotion", "genie_mp_item", "genie_mp", "genie_pet_potion", "genie_pet_item", "genie_pet" }.Select(n => Named<Control>(panel, n)).ToArray();
            Require(potions.All(p => p.IsVisibleInTree() && potionFrame.Encloses(p.GetGlobalRect())), view + " HP, MP and Familiar potion options inside the potion inset");
            Require(potions.OfType<Button>().Where(b => b.Name.ToString().EndsWith("_item")).All(b => b.GetThemeStylebox("normal") is StyleBoxTexture && b.Alignment == HorizontalAlignment.Left), view + " the three potion pickers on Classic buttons");
            var home = Named<CheckButton>(panel, "genie_return_home"); bool wasHome = home.ButtonPressed; home.ButtonPressed = !wasHome;
            Require(((CheckButton)DetailField(world, "Genie.returnHome")!).ButtonPressed == !wasHome, view + " options are the client's own settings controls");
            home.ButtonPressed = wasHome;
            var pet = Named<CheckButton>(panel, "genie_pet_potion"); bool wasPet = pet.ButtonPressed; pet.ButtonPressed = !wasPet;
            Require(((GenieSetup)DetailField(world, "Genie.Genie")!).Has(GenieMoreMode.PetPotion) == !wasPet, view + " the Familiar potion toggle writes the client's PetPotion mode");
            pet.ButtonPressed = wasPet;
            foreach (var hint in new[] { "genie_ptcode_hint", "genie_leader_hint", "genie_scroll_hint", "genie_scroll_status", "genie_daily_status", "genie_hint" }.Select(n => Named<Label>(panel, n)))
                Require(hint.GetVisibleLineCount() == hint.GetLineCount(), view + " every line of " + hint.Name + " is shown (" + hint.GetLineCount() + ")");
            foreach (var check in Descendants(panel).OfType<CheckButton>().Where(c => c.IsVisibleInTree()))
                Require(check.TooltipText.Length > 0 && (check.ClipText || check.GetMinimumSize().X <= check.Size.X), view + " " + check.Name + " ends in an ellipsis with its full text as tooltip");
            await Capture(window, view + "-misc");
            if (view == "configured")
            {
                Named<Button>(panel, "genie_scroll_0").EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
                Require(((PopupMenu)DetailField(world, "GenieScrolls.scrollPicker")!).Visible, "Scroll cell opens the native scroll picker");
                ((PopupMenu)DetailField(world, "GenieScrolls.scrollPicker")!).Hide();
            }
            var othersTab = Named<Button>(panel, "genie_tab_others");
            othersTab.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Require(panel.GenieShownPage == 2 && ((TabContainer)Named<Control>(window, "genie_tabs")).CurrentTab == 1, view + " Others tab shows the client's Set Others rows and the hammer");
            var rules = Enumerable.Range(0, ClassicProgressionLayout.GenieOtherRows).Select(i => Named<HBoxContainer>(panel, "genie_other_" + i)).ToArray();
            Require(rules.Length == GenieTownReturn.OtherRows.Length, view + " every Set Others row of the client (" + rules.Length + ")");
            var rulesFrame = panel.Frame.Get("genie_others_frame")!.GetGlobalRect();
            Require(rules.All(r => r.IsVisibleInTree() && rulesFrame.Encloses(r.GetGlobalRect())), view + " every Set Others row inside its inset");
            Require(Named<Label>(panel, "genie_hammer_status").GetVisibleLineCount() == Named<Label>(panel, "genie_hammer_status").GetLineCount(), view + " the hammer note shows every line");
            for (int i = 0; i < rules.Length; i++) Require(rules[i].GetMeta("genie_other").AsInt32() == (int)GenieTownReturn.OtherRows[i].Option, view + " row " + i + " is the client's " + GenieTownReturn.OtherRows[i].Option + " rule");
            Require(rules.Count(r => r.GetChildren().OfType<SpinBox>().Any(c => c.IsVisibleInTree())) == 2, view + " HP and MP rules keep their potion counts");
            Require(rules.All(r => r.GetChildren().OfType<CheckButton>().All(t => t.TooltipText == t.Text && t.GetGlobalRect().End.X <= r.GetGlobalRect().End.X + .5f)), view + " long rules are clipped inside their row with their text as tooltip");
            var hammerFrame = panel.Frame.Get("genie_hammer_frame")!.GetGlobalRect();
            Require(new[] { "genie_hammer_title", "genie_hammer_threshold_label", "genie_hammer_threshold", "genie_hammer_status" }.All(n => Named<Control>(panel, n).IsVisibleInTree() && hammerFrame.Encloses(Named<Control>(panel, n).GetGlobalRect())), view + " the hammer threshold and its note inside the hammer inset");
            Require(!Named<Control>(panel, "genie_ptcode").IsVisibleInTree() && !Named<Control>(panel, "genie_slot_0").IsVisibleInTree(), view + " Others page never overlaps Main or Misc");
            var feed = rules[Array.FindIndex(GenieTownReturn.OtherRows, r => r.Option == GenieOther.FeedFamiliar)].GetChildren().OfType<CheckButton>().First();
            bool wasFeed = feed.ButtonPressed; feed.ButtonPressed = !wasFeed;
            Require(((GenieSetup)DetailField(world, "Genie.Genie")!).Has(GenieOther.FeedFamiliar) == !wasFeed, view + " the familiar feeding row writes the client's FeedFamiliar rule");
            feed.ButtonPressed = wasFeed;
            await Capture(window, view + "-others");
            Named<Button>(panel, "genie_tab_main").EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Require(panel.GenieShownPage == 0, view + " Main tab returns to the main page");
            Named<Control>(panel.Frame, "btn_close").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!window.Visible && !(bool)DetailField(world, "Genie.shown")!, view + " Close ornament invokes the native close");
            layer.QueueFree();
            await Done(world, window);
        }

        System.IO.File.WriteAllText(output + "/" + prefix + "-progression.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("PROGRESSION_AUDIT_OK " + checks.Count);
    }
}
