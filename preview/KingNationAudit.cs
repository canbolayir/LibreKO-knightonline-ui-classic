using Godot;
using LibreKO;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureKingNationAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var offline = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, offline);
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("KING_NATION_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(HudWindow window, string name, string state)
        {
            await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            string tag = name + " " + state;
            Require(window.Size == ClassicKingLayout.Size(window.Id), tag + " original window footprint " + window.Size);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), tag + " complete window fits viewport");
            var frame = new Rect2(Vector2.Zero, window.Size);
            foreach (var control in Descendants(window).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("king_expected_rect")))
            {
                var rect = control.GetRect();
                Require(rect == control.GetMeta("king_expected_rect").AsRect2(), tag + " measured bounds " + control.Name + " = " + rect);
                Require(frame.Encloses(rect), tag + " inside original border " + control.Name);
                if (control is Label { AutowrapMode: not TextServer.AutowrapMode.Off } wrapped && wrapped.Text.Length > 0)
                    Require(wrapped.GetLineCount() <= wrapped.GetVisibleLineCount(), tag + " wrapped text fully visible " + control.Name);
                else if (control is Label or Button && control is not TextEdit)
                {
                    string text = control is Label l ? l.Text : ((Button)control).Text;
                    var font = control.GetThemeFont("font"); int size = control.GetThemeFontSize("font_size");
                    if (text.Length > 0 && !control.Name.ToString().StartsWith("king_vote_clan") && !control.Name.ToString().StartsWith("king_vote_name"))
                    {
                        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X, room = rect.Size.X - (control is Button ? 6 : 0);
                        Require(width <= room, tag + " caption fits " + control.Name + (width > room ? $" '{text}' {width}/{room}" : ""));
                    }
                }
            }
            string file = $"{prefix}-{name}-{state}.png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { window = name, state, file });
        }
        async Task Hover(Control control)
        {
            Vector2 hit = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
        }
        async Task Click(Control control)
        {
            Vector2 hit = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hit, GlobalPosition = hit }); await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = hit, GlobalPosition = hit, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(2);
        }
        async Task Away() { GetViewport().PushInput(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2); }
        async Task<(WorldRoot World, HudWindow Window, ClassicKingPanel Panel)> Open(string view)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); var window = PreviewFixtures.BuildKingNationUiPreview(world, view, nation);
            AddChild(window); var panel = ClassicKingSkin.Apply(window.Body)!;
            Require(panel != null, view + " Classic King skin applied to the prepared native window");
            await Frames(); return (world, window, panel!);
        }
        T Named<T>(Node root, string name) where T : Control => Descendants(root).OfType<T>().Single(c => c.Name == name);
        void Done(WorldRoot world, HudWindow window) { window.QueueFree(); world.QueueFree(); }
        void Drag(HudWindow window, string name)
        {
            window.Position = new Vector2(100, 80); Vector2 origin = window.Position, pointer = origin + new Vector2(60, 6);
            Named<Control>(window, "party_drag").EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, GlobalPosition = pointer });
            window.Layout._Input(new InputEventMouseMotion { GlobalPosition = pointer + new Vector2(30, 15) });
            window.Layout._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = pointer + new Vector2(30, 15) });
            Require(window.Position == origin + new Vector2(30, 15), name + " top area uses native dragging " + (window.Position - origin));
        }
        void Escape(ClassicKingPanel panel) => panel._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });

        // Election officer.
        foreach (string view in new[] { "king-election-main", "king-election-schedule", "king-election-candidates", "king-election-impeachment", "king-election-senators" })
        {
            var (world, window, panel) = await Open(view);
            string state = view.Substring("king-election-".Length);
            await Capture(window, "kingelection", state);
            if (view == "king-election-main")
            {
                Drag(window, "kingelection");
                var check = Named<Button>(window, "king_option_0"); var election = Named<Button>(window, "king_option_1");
                await Hover(check); await Capture(window, "kingelection", "hover");
                Require(check.GetDrawMode() == BaseButton.DrawMode.Hover, "Election option artwork receives actual mouse hover");
                await Click(election);
                Require(Named<Button>(window, "king_option_4").Visible, "Clicking the original option row runs the native page change (five election rows)");
                await Click(Named<Button>(window, "king_option_0"));
                Require((bool)DetailField(world, "KingElection.busy")!, "Nominate row starts the native schedule request");
                await Away(); await Capture(window, "kingelection", "pending");
                Require(Named<Button>(window, "king_option_1").Disabled, "Pending election request disables the option rows");
                DetailCall(world, "KingElection.OnSchedule", new KingSchedule(KingScheduleKind.None, 0, 0, 0, 0, 0));
                await Frames();
                Require(!Named<Button>(window, "king_option_1").Disabled, "Native reply releases the option rows");
                var close = Named<Button>(window, "king_close");
                Require(close.Visible && close.GetRect() == ClassicKingLayout.ElectionClose, "Close stays reachable in the original top corner");
                Escape(panel);
                Require(!window.Visible && !(bool)DetailField(world, "KingElection.electionShown")!, "Escape runs the native election close");
            }
            if (view == "king-election-senators")
                Require(Named<ScrollContainer>(window, "king_senators").GetRect() == ClassicKingLayout.ElectionSenators, "Senator names stay inside their bounded scroll region");
            Done(world, window); await Frames();
        }

        // Nomination.
        {
            var (world, window, panel) = await Open("king-nominate");
            await Capture(window, "kingnominate", "filled");
            Drag(window, "kingnominate");
            var id = Named<LineEdit>(window, "king_nominee");
            id.Text = ""; await Capture(window, "kingnominate", "empty");
            Named<Button>(window, "king_nominate_accept").EmitSignal(BaseButton.SignalName.Pressed);
            Require(window.Visible, "Empty ID is refused by native validation and the window stays open");
            id.Text = "Abcdefghijklmnopqrst"; await Capture(window, "kingnominate", "long");
            id.Text = "Rikka";
            await Click(Named<Button>(window, "king_nominate_accept"));
            Require(!window.Visible && (bool)DetailField(world, "KingElection.boxOpen")!, "OK runs the native nomination confirmation");
            Done(world, window); await Frames();
            (world, window, panel) = await Open("king-nominate");
            Escape(panel);
            Require(!window.Visible && (bool)DetailField(world, "KingElection.electionShown")!, "Escape runs native Cancel and returns to the election officer");
            Done(world, window); await Frames();
        }

        // Nominee board.
        {
            var (world, window, panel) = await Open("king-plan");
            await Capture(window, "kingplan", "filled");
            Drag(window, "kingplan");
            var edit = Named<TextEdit>(window, "king_plan_text");
            edit.Text = string.Concat(Enumerable.Repeat("The treasury funds the Border Defense War for every clan. ", 10));
            edit.EmitSignal(TextEdit.SignalName.TextChanged); await Frames();
            Require(Named<Label>(window, "king_plan_count").GetThemeColor("font_color") == new Color("ff6a5a"), "Plan over the native limit shows a refused count");
            await Capture(window, "kingplan", "over-limit");
            Named<Button>(window, "king_plan_accept").EmitSignal(BaseButton.SignalName.Pressed);
            Require(!(bool)DetailField(world, "KingElection.planBusy")!, "Over-limit plan is refused by native validation");
            edit.Text = "Fair taxes."; edit.EmitSignal(TextEdit.SignalName.TextChanged);
            await Click(Named<Button>(window, "king_plan_accept"));
            Require((bool)DetailField(world, "KingElection.planBusy")!, "OK posts the plan through the native request");
            await Away(); await Capture(window, "kingplan", "pending");
            Require(Named<Button>(window, "king_plan_accept").Disabled, "Pending plan disables duplicate posting");
            Escape(panel);
            Require(!window.Visible, "Escape runs native Cancel");
            Done(world, window); await Frames();
        }

        // Candidate list.
        {
            var (world, window, panel) = await Open("king-vote");
            await Capture(window, "kingvote", "ready");
            Drag(window, "kingvote");
            Require(Named<Button>(window, "king_vote_accept").Disabled, "Vote waits for a selected candidate");
            Require(Named<Button>(window, "king_vote_select_6").Disabled, "Empty candidate rows cannot be selected");
            await Click(Named<Button>(window, "king_vote_select_1"));
            Require(panel.SelectedCandidate == 1 && !Named<Button>(window, "king_vote_accept").Disabled, "Clicking the original check selects the candidate");
            Require(Named<Label>(window, "king_vote_clan_1").Text == "Olympus", "Organization column shows the candidate's clan");
            await Away(); await Capture(window, "kingvote", "selected");
            await Click(Named<Button>(window, "king_vote_accept"));
            Require(world.GetChildren().OfType<Notice>().Any(), "Vote opens the native vote confirmation for the selected row");
            foreach (var notice in world.GetChildren().OfType<Notice>()) notice.Free();
            PreviewFixtures.FillKingCandidates(world); await Frames();
            await Click(Named<Button>(window, "king_vote_select_9"));
            await Away(); await Capture(window, "kingvote", "full");
            await Click(Named<Button>(window, "king_vote_pledge_2"));
            Require((bool)DetailField(world, "KingElection.voteBusy")!, "Pledge runs the native pledge request");
            await Away(); await Capture(window, "kingvote", "pending");
            Require(Named<Button>(window, "king_vote_accept").Disabled && Named<Button>(window, "king_vote_pledge_0").Disabled, "Pending request disables Vote and Pledge");
            Escape(panel);
            Require(!window.Visible && !(bool)DetailField(world, "KingElection.voteShown")!, "Escape runs the native list close");
            Done(world, window); await Frames();
        }

        // Impeachment ballot.
        {
            var (world, window, panel) = await Open("king-ballot");
            await Capture(window, "kingballot", "ready");
            Drag(window, "kingballot");
            await Hover(Named<Button>(window, "king_ballot_against")); await Capture(window, "kingballot", "hover");
            panel._UnhandledKeyInput(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            Require(window.Visible, "Enter never casts a ballot");
            await Click(Named<Button>(window, "king_ballot_against"));
            Require(!window.Visible && (bool)DetailField(world, "KingElection.busy")!, "Against casts the native ballot");
            Done(world, window); await Frames();
            (world, window, panel) = await Open("king-ballot");
            Escape(panel);
            Require(!window.Visible && !(bool)DetailField(world, "KingElection.ballotShown")!, "Escape runs native Cancel");
            Done(world, window); await Frames();
        }

        // Grand Chamberlain.
        {
            var (world, window, panel) = await Open("nation-tax");
            await Capture(window, "nationtax", "king");
            Drag(window, "nationtax");
            await Hover(Named<Button>(window, "nation_tax_rate")); await Capture(window, "nationtax", "hover");
            await Click(Named<Button>(window, "nation_tax_rate"));
            Require((bool)DetailField(world, "KingTreasury.nationTaxBusy")!, "Change Tax Rate runs the native tariff request");
            await Away(); await Capture(window, "nationtax", "pending");
            Require(Named<Button>(window, "nation_tax_fund").Disabled, "Pending treasury request disables the services");
            await Click(Named<Button>(window, "king_close"));
            Require(!window.Visible && !(bool)DetailField(world, "KingTreasury.nationTaxShown")!, "Exit runs the native close");
            Done(world, window); await Frames();
            (world, window, panel) = await Open("nation-tax-citizen");
            await Capture(window, "nationtax", "citizen");
            Require(!Named<Button>(window, "nation_tax_treasury").Visible && Named<Button>(window, "king_close").Visible, "Citizens see the treasury notice and Exit only");
            Escape(panel);
            Require(!window.Visible, "Escape closes the Grand Chamberlain");
            Done(world, window); await Frames();
        }

        // Nation introduction.
        {
            var (world, window, panel) = await Open("nation-intro");
            await Capture(window, "nationintro", "ready");
            Drag(window, "nationintro");
            var edit = Named<TextEdit>(window, "nation_intro_text");
            edit.Text = string.Concat(Enumerable.Repeat("Welcome to our nation, defenders of the realm. ", 6)); await Frames();
            Require(Named<Label>(window, "nation_intro_count").GetThemeColor("font_color") == new Color("ff6a5a"), "Introduction over the native limit shows a refused count");
            await Capture(window, "nationintro", "over-limit");
            edit.Text = "Welcome, defenders of the realm."; await Frames();
            await Click(Named<Button>(window, "nation_intro_revision"));
            Require(window.Visible, "Revision saves through the native request and keeps the editor open");
            await Click(Named<Button>(window, "nation_intro_accept"));
            Require(!window.Visible, "OK saves and closes natively");
            Done(world, window); await Frames();
            (world, window, panel) = await Open("nation-intro");
            Escape(panel);
            Require(!window.Visible && !(bool)DetailField(world, "KingTreasury.nationIntroShown")!, "Escape runs the native close");
            Done(world, window); await Frames();
        }

        System.IO.File.WriteAllText(output + "/" + prefix + "-king-nation.json",
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        offline.Free(); GD.Print("KING_NATION_AUDIT_OK " + checks.Count);
    }
}
