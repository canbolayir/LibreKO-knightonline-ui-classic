using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureEventsBAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        var checks = new List<string>(); var screens = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require(bool condition, string text) { if (!condition) throw new Exception("EVENTS_B_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(c => c.Name == name);
        float Width(Control control, string text) => control.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, control.GetThemeFontSize("font_size")).X;
        async Task Capture(HudWindow window, ClassicBoardPanel panel, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            string id = window.Id, tag = id + " " + state;
            Require(window.Size == panel.CustomMinimumSize, tag + " window footprint is the composed board " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), tag + " complete window fits viewport");
            var frame = new Rect2(Vector2.Zero, window.Size).Grow(-2);
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("board_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("board_expected_rect").AsRect2(), tag + " measured bounds " + control.Name + " = " + control.GetRect());
                Require(frame.Encloses(control.GetRect()), tag + " inside the board frame " + control.Name);
                if (control is Label { AutowrapMode: TextServer.AutowrapMode.Off } label && label.Text.Length > 0)
                    Require(Width(label, label.Text) <= label.Size.X || label.TooltipText == label.Text, tag + " caption fits or trims with a tooltip " + control.Name + " " + label.Text);
                if (control is Label { AutowrapMode: not TextServer.AutowrapMode.Off } wrapped && wrapped.Text.Length > 0)
                    Require(wrapped.GetVisibleLineCount() >= wrapped.GetLineCount(), tag + " wrapped text fits its lines " + control.Name + " " + wrapped.GetLineCount());
                if (control is Button { Text.Length: > 0 } button)
                    Require(Width(button, button.Text) <= button.Size.X - 2, tag + " button caption fits " + button.Text + " at " + button.GetThemeFontSize("font_size") + " px");
            }
            foreach (var head in Descendants(panel).OfType<Label>().Where(l => l.Name.ToString().StartsWith("board_head_")))
                Require(Width(head, head.Text) <= head.Size.X, tag + " heading " + head.Text + " fits at " + head.GetThemeFontSize("font_size") + " px");
            if (panel.Rows is { } rows)
            {
                var list = rows.GetGlobalRect();
                var heads = Descendants(panel).OfType<Label>().Where(l => l.Name.ToString().StartsWith("board_head_")).OrderBy(l => l.Name.ToString()).ToArray();
                var visible = rows.GetChildren().OfType<Control>().Where(r => r.Visible && !r.IsQueuedForDeletion()).ToArray();
                foreach (var row in visible)
                {
                    var rect = row.GetGlobalRect();
                    // The empty-list line fills the table so it reads centred; real rows keep the row pitch.
                    var expected = new Vector2(ClassicBoardLayout.RowWidth, row is Label ? list.Size.Y : ClassicBoardLayout.RowHeight);
                    Require(rect.Size == expected && rect.Position.X == list.Position.X, tag + " row spans the table " + rect);
                    Require(list.Encloses(rect), tag + " row contained in the list " + rect);
                    foreach (var cell in Descendants(row).OfType<Label>().Where(l => l.IsVisibleInTree() && l.HasMeta("board_cell")))
                    {
                        var box = cell.GetGlobalRect(); int i = cell.GetMeta("board_cell").AsInt32();
                        Require(box.Position.X >= list.Position.X && box.End.X <= list.End.X, tag + " cell inside list " + i);
                        float centre = heads[i].GetGlobalRect().GetCenter().X;
                        Require(box.Position.X <= centre && box.End.X >= centre, tag + " cell under heading " + heads[i].Text);
                        Require(Width(cell, cell.Text) <= cell.Size.X || cell.TooltipText == cell.Text, tag + " long cell trimmed with tooltip " + cell.Text);
                    }
                }
            }
            string file = prefix + "-" + id + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { window = id, state, file });
        }
        async Task Hover(Control control)
        {
            // Another window's focus or mouse change can drop the hover: move again, up to three times.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Vector2 hit = control.GetGlobalRect().GetCenter() + new Vector2(attempt, 0);
                GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
                if (control is not BaseButton button || button.Disabled) return;
                ulong until = Time.GetTicksMsec() + 1500;
                while (button.GetDrawMode() != BaseButton.DrawMode.Hover && Time.GetTicksMsec() < until) await Frames(1);
                if (button.GetDrawMode() == BaseButton.DrawMode.Hover) return;
                if (attempt < 2) GD.Print($"EVENTS_B hover attempt {attempt + 2} on {control.Name}");
            }
        }
        async Task Leave() { GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2); }
        async Task Click(Control control, MouseButton button = MouseButton.Left)
        {
            Vector2 hit = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = button, Pressed = true, Factor = 1 });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = button, Pressed = false, Factor = 1 });
            await Frames(2);
        }
        async Task<(WorldRoot World, HudWindow Window, ClassicBoardPanel Panel)> Open(string id, bool sheriff = false)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildBoardWindowPreview", id, sheriff)!;
            AddChild(window); var panel = ClassicBoardSkin.Apply(window.Body)!;
            Require(panel != null, id + " Classic board skin applied to the prepared native window");
            await Frames(); return (world, window, panel!);
        }
        async Task Close((WorldRoot World, HudWindow Window, ClassicBoardPanel Panel) open, string shown)
        {
            var close = Named<Button>(open.Panel, "board_close");
            await Hover(close); Require(close.GetDrawMode() == BaseButton.DrawMode.Hover, open.Window.Id + " close button receives actual hover");
            await Click(close);
            Require(!open.Window.Visible && !(bool)DetailField(open.World, shown)!, open.Window.Id + " Close mouse click invokes the native close");
            open.Window.QueueFree(); await Frames(2); open.World.Free(); await Frames(2);
        }
        async Task Drag(string id)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "BuildBoardWindowPreview", id, false)!;
            AddChild(window); ClassicBoardSkin.Apply(window.Body); await Frames();
            window.Position = new Vector2(100, 80); Vector2 origin = window.Position, pointer = origin + new Vector2(120, 10);
            Named<Control>(window, "party_drag").EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(30, 15) });
            Require(window.Position == origin + new Vector2(30, 15), id + " board header uses native dragging");
            window.QueueFree(); await Frames(2); world.Free();
        }
        bool Connected(Button button) => button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count > 0;
        Control[] RowsOf(ClassicBoardPanel panel) => panel.LiveRows().Where(r => r.Visible).ToArray<Control>();
        Label Cell(Control row, int index) => Descendants(row).OfType<Label>().Single(l => l.HasMeta("board_cell") && l.GetMeta("board_cell").AsInt32() == index);
        Color Ink(Label label) => label.GetThemeColor("font_color");

        foreach (string id in NativeBoardIds) await Drag(id);

        // Bounty board.
        {
            var open = await Open("bounty"); var panel = open.Panel;
            DetailCall(open.World, "FillBounties", 0, false); await Frames();
            Require(Descendants(panel.Rows!).OfType<Label>().Any(l => l.Text == "No open bounties." && l.IsVisibleInTree()), "Bounty empty list shows the native empty line");
            await Capture(open.Window, panel, "empty");
            DetailCall(open.World, "FillBounties", 6, false); await Frames();
            var claim = Named<Button>(panel, "bounty_claim");
            Require(RowsOf(panel).Length == 6 && claim.Disabled, "Bounty shows the native rows; Claim waits for a selection");
            Require(Cell(RowsOf(panel)[0], 1).Text == $"{25_000_000:N0}g" && Cell(RowsOf(panel)[0], 2).Text == BoardNames(7), "Bounty columns show reward and poster");
            await Capture(open.Window, panel, "filled");
            var row = RowsOf(panel)[2]; await Click(row);
            Require(panel.Selected == row && !claim.Disabled, "Mouse click selects a bounty and enables Claim");
            int claims = 0; Descendants(row).OfType<Button>().Single(b => b.Text == "Claim").Pressed += () => claims++;
            await Capture(open.Window, panel, "selected");
            await Hover(claim); Require(claim.GetDrawMode() == BaseButton.DrawMode.Hover, "Claim receives actual hover");
            await Capture(open.Window, panel, "claim-hover"); await Leave();
            await Click(claim);
            Require(claims == 1, "Claim mouse click presses the selected row's native Claim");
            var target = Named<LineEdit>(panel, "bounty_target"); var reward = Named<LineEdit>(panel, "bounty_reward");
            var post = Named<Button>(panel, "bounty_post");
            target.Text = "Morrigan"; reward.Text = "150000";
            Require(Connected(post) && target.Editable && reward.Editable, "Post keeps the native request and editable fields");
            await Hover(post); Require(post.GetDrawMode() == BaseButton.DrawMode.Hover, "Post receives actual hover");
            await Capture(open.Window, panel, "post-hover"); await Leave();
            await Click(post); DetailCall(open.World, "Bounty.OnPost", true); await Frames(2);
            Require(target.Text == "" && reward.Text == "", "Accepted post clears the native form");
            DetailCall(open.World, "FillBounties", 24, true); await Frames();
            Require(panel.Selected == null, "A new server list clears the selection");
            Require(panel.Pages == 3 && RowsOf(panel).Length == ClassicBoardLayout.Bounty.PageRows, "Long bounty list pages whole rows (" + panel.Pages + ")");
            var up = Named<Button>(panel, "board_page_up"); var down = Named<Button>(panel, "board_page_down");
            Require(up.Disabled && !down.Disabled && Named<Label>(panel, "board_page").Text == "1 / 3", "Page arrows reflect the first page");
            var longCell = Cell(RowsOf(panel)[1], 0);
            Require(longCell.Text.StartsWith("Xx_") && longCell.TooltipText == longCell.Text && Width(longCell, longCell.Text) > longCell.Size.X, "Long target trimmed in its column and kept as tooltip");
            await Capture(open.Window, panel, "long");
            Vector2 hit = panel.Rows!.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 });
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1 });
            await Frames(2);
            Require(panel.Page == 1, "Mouse wheel over the list turns the page");
            await Capture(open.Window, panel, "long-page2");
            await Click(down); await Frames(2);
            Require(panel.Page == 2 && down.Disabled && !up.Disabled && RowsOf(panel).Length == 4, "Down arrow reaches the last page");
            await Capture(open.Window, panel, "long-page3");
            await Click(up); Require(panel.Page == 1, "Up arrow returns a page");
            await Close(open, "Bounty.shown");
        }

        // Fishing hall of fame.
        {
            var open = await Open("fishinghall"); var panel = open.Panel;
            DetailCall(open.World, "FillFishingHall", 0); await Frames();
            Require(Descendants(panel.Rows!).OfType<Label>().Any(l => l.Text == "No anglers ranked yet." && l.IsVisibleInTree()), "Fishing hall empty list shows the native empty line");
            await Capture(open.Window, panel, "empty");
            DetailCall(open.World, "FillFishingHall", 9); await Frames();
            var rows = RowsOf(panel);
            Require(rows.Length == 9 && Cell(rows[0], 0).Text == "#1" && Cell(rows[0], 2).Text == 1_250_000.ToString("N0"), "Fishing hall shows rank, angler and score");
            Require(Ink(Cell(rows[4], 1)) == ClassicBoardPanel.Highlight && Ink(Cell(rows[4], 2)) == ClassicBoardPanel.Highlight, "Own entry is highlighted");
            Require(Ink(Cell(rows[2], 0)) == ClassicBoardPanel.Medal && Ink(Cell(rows[3], 0)) == ClassicBoardPanel.Ink, "Top three ranks keep the gold rank colour");
            Require(Cell(rows[2], 1).TooltipText == Cell(rows[2], 1).Text && Width(Cell(rows[2], 1), Cell(rows[2], 1).Text) > Cell(rows[2], 1).Size.X, "Long angler name trimmed with tooltip");
            await Capture(open.Window, panel, "filled");
            DetailCall(open.World, "FillFishingHall", 30); await Frames();
            Require(panel.Pages == 3 && RowsOf(panel).Length == ClassicBoardLayout.Fishing.PageRows, "Long hall pages whole rows");
            await Capture(open.Window, panel, "long");
            var down = Named<Button>(panel, "board_page_down");
            await Click(down); await Click(down);
            Require(panel.Page == 2 && down.Disabled && RowsOf(panel).Length == 6, "Fishing hall reaches its last page");
            await Capture(open.Window, panel, "long-page3");
            await Close(open, "FishingHall.fishHallShown");
        }

        // Sheriff reports: view only.
        {
            var open = await Open("report"); var panel = open.Panel;
            DetailCall(open.World, "FillReports", 1, 3, false); await Frames();
            var file = Named<Button>(panel, "report_file"); var target = Named<LineEdit>(panel, "report_target");
            Require(file.Disabled && !target.Editable && !Named<LineEdit>(panel, "report_reason").Editable, "View-only gate locks the native form");
            Require(Ink(Named<Label>(panel, "report_gate")) == ClassicBoardPanel.Muted, "View-only gate uses the muted ink");
            await Capture(open.Window, panel, "view-only");
            await Click(RowsOf(panel)[0]);
            Require(panel.Selected != null && Named<Button>(panel, "report_vote_yes").Disabled && Named<Button>(panel, "report_vote_no").Disabled, "View-only votes stay disabled after selection");
            Require(Named<Label>(panel, "report_detail").Text == "Botting in Ronark Land.", "Selected report shows its reason");
            await Capture(open.Window, panel, "view-only-selected");
            await Close(open, "Report.shown");
        }
        // Sheriff reports: King or GM.
        {
            var open = await Open("report", true); var panel = open.Panel;
            DetailCall(open.World, "FillReports", 3, 8, true); await Frames();
            var file = Named<Button>(panel, "report_file"); var status = Named<Label>(panel, "report_status");
            Require(!file.Disabled && Ink(Named<Label>(panel, "report_gate")) == ClassicBoardPanel.Highlight, "Sheriff gate unlocks the form");
            Require(RowsOf(panel).Length == 8 && Named<Label>(panel, "report_page").Text == "1 / 3", "Server page of eight reports with the native page number");
            Require(Cell(RowsOf(panel)[3], 0).TooltipText == Cell(RowsOf(panel)[3], 0).Text && Width(Cell(RowsOf(panel)[3], 0), Cell(RowsOf(panel)[3], 0).Text) > Cell(RowsOf(panel)[3], 0).Size.X, "Long accused name trimmed with tooltip");
            await Capture(open.Window, panel, "sheriff");
            await Click(RowsOf(panel)[1]); await Frames(2);
            var rail = Named<ClassicContentScrollRail>(panel, "report_detail_rail");
            Require(rail.Visible && rail.Bar.MaxValue > rail.Bar.Page, "Long reason overflows into the original scroll rail");
            Require(!Named<Button>(panel, "report_vote_yes").Disabled && !Named<Button>(panel, "report_vote_no").Disabled, "Sheriff selection enables the votes");
            await Capture(open.Window, panel, "selected-long-reason");
            var scroll = Named<ScrollContainer>(panel, "report_detail_scroll"); Vector2 hit = scroll.GetGlobalRect().GetCenter();
            for (int i = 0; i < 4; i++)
            {
                GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 });
                GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1 });
            }
            await Frames(2);
            Require(rail.Bar.Value > 0, "Mouse wheel scrolls the contained reason");
            var detail = Named<Label>(panel, "report_detail");
            float pitch = detail.Size.Y / detail.GetLineCount();
            GD.Print("EVENTS_B detail lines " + detail.GetLineCount() + " height " + detail.Size.Y + " pitch " + pitch + " scroll " + scroll.ScrollVertical);
            Require(Mathf.Abs(pitch - ClassicBoardLayout.Report.DetailLine) < .5f, "Reason lines advance by the whole-line pitch " + pitch);
            Require(scroll.ScrollVertical % (int)ClassicBoardLayout.Report.DetailLine == 0 && scroll.Size.Y % ClassicBoardLayout.Report.DetailLine == 0, "Reason scrolls by whole lines " + scroll.ScrollVertical);
            await Capture(open.Window, panel, "selected-long-reason-scrolled");
            await Click(Named<Button>(panel, "report_vote_yes"));
            Require(status.Text == "Voting yes…", "Vote Yes presses the row's native vote");
            await Click(Named<Button>(panel, "report_vote_no"));
            Require(status.Text == "Voting no…", "Vote No presses the row's native vote");
            DetailCall(open.World, "Report.OnResult", (byte)ReportSubOpcode.VoteNo, false); await Frames();
            Require(status.Text.StartsWith("Vote rejected") && Ink(status) == new Color("ff6a6a"), "Rejected vote keeps the native warning colour");
            await Capture(open.Window, panel, "vote-rejected");
            var next = Named<Button>(panel, "report_next"); var prev = Named<Button>(panel, "report_prev");
            Require(prev.Disabled && !next.Disabled, "Native page arrows reflect the first server page");
            await Click(next);
            Require((byte)DetailField(open.World, "Report.page")! == 1, "Next arrow requests the native next page");
            DetailCall(open.World, "FillReports", 3, 2, false); await Frames();
            Require(Named<Label>(panel, "report_page").Text == "2 / 3" && !prev.Disabled && panel.Selected == null, "Second server page replaces the rows");
            await Capture(open.Window, panel, "page2");
            await Click(prev);
            Require((byte)DetailField(open.World, "Report.page")! == 0, "Previous arrow requests the native previous page");
            var refresh = Named<Button>(panel, "report_refresh");
            await Hover(refresh); Require(refresh.GetDrawMode() == BaseButton.DrawMode.Hover, "Refresh receives actual hover");
            await Capture(open.Window, panel, "refresh-hover"); await Leave();
            await Click(refresh); Require(Connected(refresh) && (byte)DetailField(open.World, "Report.page")! == 0, "Refresh keeps the native request");
            await Click(file);
            Require(status.Text == "Enter the accused player's name.", "Submit with an empty form shows the native validation");
            Require(!rail.Visible, "Short reason hides the reason scroll rail");
            await Capture(open.Window, panel, "file-warning");
            Named<LineEdit>(panel, "report_target").Text = "Talon"; Named<LineEdit>(panel, "report_reason").Text = "Scamming in the Moradon market.";
            await Click(file);
            Require(status.Text == "Filing report…", "Submit files through the native handler");
            await Capture(open.Window, panel, "filing");
            DetailCall(open.World, "Report.OnResult", (byte)ReportSubOpcode.File, true); await Frames();
            Require(status.Text == "Report filed." && Named<LineEdit>(panel, "report_target").Text == "", "Filed report clears the native form");
            DetailCall(open.World, "FillReports", 1, 0, false); await Frames();
            Require(Descendants(panel.Rows!).OfType<Label>().Any(l => l.Text == "No open reports." && l.IsVisibleInTree()) && next.Disabled && prev.Disabled, "Empty report list shows the native empty line");
            await Capture(open.Window, panel, "empty");
            await Close(open, "Report.shown");
        }

        // Nation force.
        {
            var open = await Open("forces"); var panel = open.Panel;
            DetailCall(open.World, "FillForces", false, 0, 0, 0); await Frames();
            var join = Named<Button>(panel, "forces_join"); var leave = Named<Button>(panel, "forces_leave");
            Require(!join.Disabled && leave.Disabled && Named<Label>(panel, "forces_rank").Text == "—", "Not enlisted: Join available, Leave disabled");
            Require(Named<Label>(panel, "forces_percent").Text == "0%", "Anger gauge empty when not enlisted");
            await Capture(open.Window, panel, "not-enlisted");
            await Hover(join); Require(join.GetDrawMode() == BaseButton.DrawMode.Hover, "Join receives actual hover");
            await Capture(open.Window, panel, "join-hover"); await Leave();
            Require(Connected(join) && Connected(leave), "Join and Leave keep their native requests");
            DetailCall(open.World, "FillForces", true, 2, 12_345, 65); await Frames();
            Require(join.Disabled && !leave.Disabled && Named<Label>(panel, "forces_rank").Text == "Veteran", "Enlisted: native rank and Leave available");
            Require(Named<Label>(panel, "forces_points").Text == 12_345.ToString("n0") && Named<Label>(panel, "forces_percent").Text == "65%", "Force points and anger mirror the native status");
            Require(Mathf.IsEqualApprox(Named<ClassicBoardGauge>(panel, "forces_gauge").Fraction, .65f), "Gauge fill follows the native anger bar");
            await Capture(open.Window, panel, "enlisted");
            DetailCall(open.World, "FillForces", true, 5, 9_999_999, 100); await Frames();
            Require(Named<Label>(panel, "forces_rank").Text == "Warlord" && Named<Label>(panel, "forces_percent").Text == "100%", "Highest rank and a full gauge");
            await Capture(open.Window, panel, "enlisted-max");
            await Close(open, "Forces.shown");
        }

        System.IO.File.WriteAllText(output + "/" + prefix + "-events-b.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("EVENTS_B_AUDIT_OK " + checks.Count);
    }

    private static readonly string[] NativeBoardIds = { "bounty", "report", "fishinghall", "forces" };
    private static string BoardNames(int index) => new[]
    {
        "Talon", "Morrigan", "Brightblade", "Kessandra", "Orrin", "Valeska", "Dunmore", "Isolde", "Thorne", "Ravenna",
    }[index % 10];
}
