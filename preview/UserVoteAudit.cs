using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    /// <summary>
    /// user-vote-audit (3277df5, social area B): the Juraid user vote windows on the Classic message box (select
    /// with its reasons and other text, question with the countdown and the client's automatic Draw, warning
    /// notice), the Classic Party War Board beside the party list, and the clan page's online bonus and own memo
    /// action. Every window is captured for one nation; actual rendered bounds are checked against the declared
    /// rectangles, everything stays inside its frame and the viewport, and every action runs the client's own
    /// handler through the plugin facades.
    /// </summary>
    private async Task CaptureUserVoteAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/user-vote-audit");
        System.IO.Directory.CreateDirectory(output);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        ItemData.EnsureLoaded();
        PreviewFixtures.LinkClientFacades(Plugin.Kit.Game);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool ok, string text) { if (!ok) throw new Exception("USER_VOTE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Shot(string state, object? extra = null)
        {
            await Frames(2);
            string file = prefix + "-" + state + ".png";
            using (var image = GetViewport().GetTexture().GetImage()) image.SavePng(System.IO.Path.Combine(output, file));
            screens.Add(new { state, file, extra });
        }
        var world = PreviewFixtures.Prepared(new WorldRoot());
        void Set(string field, object? value) => Native.Set(world, field, value);
        T Get<T>(string field) => (T)Native.Get<object>(world, field)!;
        double Now() => Time.GetTicksMsec() / 1000.0;
        Set("MyId", 300);
        Set("ZoneId", TempleZones.JuraidMountain);
        Game.Session.Party.SeedPreview(
            new PartyMember(301, 1, "Rikka", 4210, 3900, 83, nation * 100 + 8, 1820, 1500),
            new PartyMember(300, 1, "Canbo", 5120, 5120, 81, nation * 100 + 6, 1400, 1210),
            new PartyMember(302, 1, "LongCharacterName1234", 3890, 2210, 80, nation * 100 + 10, 2300, 2300),
            new PartyMember(303, 1, "Healer", 4470, 4470, 82, nation * 100 + 12, 1990, 640));
        // The own bars and ping, the members' pings and level progress (ff78eef) for the party list beside the board.
        Game.Session.Self.Vitals.Seed(5120, 5120, 1210, 1400);
        typeof(Net).GetProperty("PingMs")!.SetValue(net, 42);
        foreach (var (id, ping, permille) in new[] { (301, 84, 625), (302, 172, 310), (303, 290, 880) })
            Game.Session.Party.SeedMemberPreview(id, ping, permille, Array.Empty<int>());
        var social=Native.Get<object>(Native.Call(world,"CreatePluginAdapters")!,"Social")!;Native.Call(PluginHost.Game,"AttachSocial",social,social,social);Native.Call(PluginHost.Game,"AttachWarp",social);
        DetailCall(world, "UserVoteWindow.Build");
        PreviewFixtures.Adopt<CanvasLayer>(world, "UserVoteWindow.layer", this);
        var vote = Plugin.Kit.Game.UserVote;

        // Checks one vote window: the Classic message box replaced the client's flow, sits centred in the
        // viewport, and every placed control has its declared bounds and stays inside the box.
        async Task<ClassicUserVoteDialog> VoteWindow(string field, string state)
        {
            await Frames(2);
            var window = Get<HudWindow>(field);
            var dialog = ClassicUserVoteSkin.Apply(window.Body) ?? throw new Exception("USER_VOTE_AUDIT: " + state + ": the Classic skin did not apply");
            await Frames(4);
            Require(window.GetChildren().OfType<Control>().Where(c => c.Visible).SequenceEqual(new Control[] { dialog }), state + ": the client's flow content is hidden behind the Classic box");
            Require(dialog.Size.X == ClassicOverlayLayout.Message.Width && window.Size == dialog.Size, state + ": message box width and window size " + window.Size);
            var rect = window.GetGlobalRect();
            Require(GetViewportRect().Encloses(rect), state + ": the box fits the viewport " + rect);
            Require((window.Position - ((GetViewportRect().Size - window.Size) / 2).Floor()).Length() < 1, state + ": the box is centred " + window.Position);
            foreach (var control in dialog.GetChildren().OfType<Control>().Where(c => c.HasMeta("overlay_expected_rect")))
            {
                var expected = control.GetMeta("overlay_expected_rect").AsRect2();
                Require(control.GetRect() == expected, state + ": actual bounds of " + control.Name + " " + control.GetRect() + " = " + expected);
                Require(new Rect2(Vector2.Zero, dialog.Size).Grow(-8).Encloses(control.GetRect()), state + ": " + control.Name + " stays inside the frame line");
            }
            foreach (var label in dialog.GetChildren().OfType<Label>().Where(l => l.AutowrapMode != TextServer.AutowrapMode.Off))
                Require(label.GetVisibleLineCount() >= label.GetLineCount(), state + ": wrapped text " + label.Name + " shows all " + label.GetLineCount() + " lines");
            var buttons = dialog.GetChildren().OfType<Button>().Where(b => b is not CheckBox).ToArray();
            Require(buttons.Length > 0 && buttons.All(b => b.Size.Y == ClassicOverlayLayout.Message.Button.Y) && buttons.Select(b => b.Position.Y).Distinct().Count() == 1, state + ": one row of original message buttons");
            float bottom = dialog.Size.Y - ClassicOverlayLayout.Message.ButtonBottom;
            Require(Mathf.IsEqualApprox(buttons[0].GetRect().End.Y, bottom), state + ": buttons 10 px above the bottom of the box");
            var content = dialog.GetChildren().OfType<Control>().Where(c => c is not Button && c.HasMeta("overlay_expected_rect") && c.Name != "vote_title").Max(c => c.GetRect().End.Y);
            float gap = buttons[0].Position.Y - content;
            Require(gap >= 14 && gap <= 17, state + ": the box is sized to its content (gap above the buttons " + gap + ")");
            return dialog;
        }

        // 1. Select: opened from the party facade's user vote (the Juraid member menu entry).
        Require(PluginHost.Game.Party.UserVoteHere, "party API: user vote is offered in Juraid");
        PluginHost.Game.Party.UserVote(302);
        var select = await VoteWindow("UserVoteWindow.select", "vote-select");
        var reasons = select.GetChildren().OfType<CheckBox>().ToArray();
        Require(reasons.Length == UserVote.Reasons && reasons[0].ButtonPressed && reasons.Count(r => r.ButtonPressed) == 1, "select: four reasons, the first picked");
        // The client's reason texts; where its text table has none (it returns "Reason n"), the English captions.
        string[] reasonCaptions = { "Inappropriate language", "Intentional disturbance", "Long-term inactivity", "Other (30 letters max)" };
        var expectedReasons = vote.Reasons.Select((text, i) => text == $"Reason {i + 1}" ? reasonCaptions[i] : text).ToArray();
        Require(reasons.Select(r => r.Text).SequenceEqual(expectedReasons) && reasons.All(r => !r.Text.StartsWith("Reason ")), "select: reason captions " + string.Join(" / ", reasons.Select(r => r.Text)));
        Require(select.GetNode<LineEdit>("vote_other").PlaceholderText == expectedReasons[3], "select: the field shows the other reason's caption");
        foreach (var reason in reasons)
            Require(reason.GetThemeFont("font").GetStringSize(reason.Text, HorizontalAlignment.Left, -1, reason.GetThemeFontSize("font_size")).X + 30 <= reason.Size.X, "select: caption fits its row " + reason.Text);
        Require(select.GetNode<Label>("vote_prompt").Text.Contains("LongCharacterName1234"), "select: the prompt names the member");
        Require(select.GetNode<Label>("vote_cost").Text.Length > 0 && select.GetNode<Label>("vote_cost").GetThemeColor("font_color") == ClassicDesign.FrameTitle, "select: the cost line in gold");
        await Shot("vote-select");
        reasons[1].EmitSignal(BaseButton.SignalName.Pressed); reasons[1].ButtonPressed = true;
        Require(select.Reason == 2, "select: a reason row picks its reason");
        var other = select.GetNode<LineEdit>("vote_other");
        Require(other.MaxLength == UserVote.OtherTextMaxLetters, "select: the other reason takes 30 letters");
        other.Text = "left the bridge to the enemy"; other.EmitSignal(LineEdit.SignalName.TextChanged, other.Text);
        await Frames(2);
        Require(select.Reason == UserVote.ReasonOther && reasons[3].ButtonPressed && !reasons[1].ButtonPressed, "select: typing picks the other reason, as in the client");
        await Shot("vote-select-other");
        select.GetNode<Button>("vote_cancel").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(Get<HudWindow?>("UserVoteWindow.select") == null && (int)DetailField(world, "UserVoteWindow.member")! == -1, "select: Cancel runs the client's cancel and frees the window");
        PluginHost.Game.Party.UserVote(301);
        select = await VoteWindow("UserVoteWindow.select", "vote-select-again");
        select.GetNode<Button>("vote_ok").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(Get<HudWindow?>("UserVoteWindow.select") == null && !double.IsNegativeInfinity((double)DetailField(world, "UserVoteWindow.sentAt")!), "select: OK sends the vote through the client and frees the window");

        // 2. Question: the reason and the countdown; the answers and the client's automatic Draw.
        DetailCall(world, "UserVoteWindow.OnAsk", new UserVoteAsk("LongCharacterName1234", UserVote.ReasonOther, "left the bridge to the enemy"));
        var ask = await VoteWindow("UserVoteWindow.askWindow", "vote-ask");
        var bar = ask.GetNode<ProgressBar>("vote_countdown");
        Require(bar.MaxValue == UserVote.AnswerSeconds && bar.Value > UserVote.AnswerSeconds - 2, "ask: the countdown starts full " + bar.Value);
        Require(ask.GetNode<Label>("vote_reason").Text == "left the bridge to the enemy", "ask: the typed reason in gold");
        var answers = ask.GetChildren().OfType<Button>().ToArray();
        Require(answers.Select(b => b.Text).SequenceEqual(new[] { "Yes", "Draw", "No" }) && answers.Select(b => b.Size).Distinct().Count() == 1, "ask: Yes, Draw and No of one size");
        Require(Mathf.IsEqualApprox(answers[0].Position.X, ClassicOverlayLayout.Message.Width - answers[2].GetRect().End.X), "ask: equal side margins");
        Require(!Get<HudWindow>("UserVoteWindow.askWindow").GetChildren().OfType<Control>().Any(c => c.Visible && c is not ClassicUserVoteDialog), "ask: no close button (the question must be answered)");
        await Shot("vote-ask");
        Set("UserVoteWindow.askUntil", Now() + 7);
        await Frames(4);
        Require(bar.Value < 8 && bar.GetMeta(KnightOnlineUiClassic.Native.NativeOverlays.Urgent, false).AsBool(), "ask: the last ten seconds use the urgent bar art");
        await Shot("vote-ask-urgent");
        answers[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(Get<HudWindow?>("UserVoteWindow.askWindow") == null, "ask: Yes answers through the client and frees the window");
        DetailCall(world, "UserVoteWindow.OnAsk", new UserVoteAsk("Rikka", 2, ""));
        ask = await VoteWindow("UserVoteWindow.askWindow", "vote-ask-listed-reason");
        string listed = vote.AskReason.Length > 0 ? vote.AskReason : reasonCaptions[1];
        Require(ask.HasNode("vote_reason") && ask.GetNode<Label>("vote_reason").Text == listed, "ask: a listed reason shows the client's text, or its caption when the client has none: " + listed);
        await Shot("vote-ask-listed-reason");
        Set("UserVoteWindow.askUntil", Now() - 1);
        DetailCall(world, "UserVoteWindow.UserVoteTick", Now());
        await Frames(2);
        Require(Get<HudWindow?>("UserVoteWindow.askWindow") == null, "ask: the client answers Draw when the countdown ends");

        // 3. Notice after leaving Juraid.
        DetailCall(world, "UserVoteWindow.ShowVoteNotice", 2, 3);
        var notice = await VoteWindow("UserVoteWindow.notice", "vote-notice");
        Require(notice.GetChildren().OfType<Label>().Count(l => l.Name.ToString().StartsWith("vote_notice_")) == 3, "notice: the client's three warning lines");
        Require(notice.GetNode<Button>("vote_notice_ok").GetRect() == ClassicOverlayLayout.Message.Single(notice.Size.Y), "notice: OK in the single centred place");
        await Shot("vote-notice");
        notice.GetNode<Button>("vote_notice_ok").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(Get<HudWindow?>("UserVoteWindow.notice") == null, "notice: OK closes through the client");

        // 4. The Classic war board beside the Classic party list.
        DetailCall(world, "WarBoard.Build");
        // As the client's HUD seam does in the game: the replaced board's layer stays in the tree, hidden.
        var nativeBoard = Get<PanelContainer>("WarBoard.Panel").GetParent<CanvasLayer>();
        nativeBoard.Reparent(this); nativeBoard.Visible = false;
        float savedAlpha = PluginHost.Game.Party.WarBoardAlpha; var savedMode = Config.PartyBoardMode;
        var hud = new CanvasLayer { Layer = 64 }; AddChild(hud);
        var party = new ClassicPartyHud(PluginHost.Game.Party, () => world); hud.AddChild(party);
        var board = new ClassicWarBoard(PluginHost.Game.Party); hud.AddChild(board);
        await Frames(4);
        Require(!board.Visible, "war board: hidden until the client opens it");
        party.GetNode<Control>("party_actions").GetChildren().OfType<Button>().Single(b => b.Text == "War Board").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        var mode = PluginHost.Game.Party.WarBoardMode;
        DetailCall(world, "WarBoard.OnWarBoard", new WarBoardReply(PartyWarBoardTarget.Board, mode,
            new List<WarBoardShare> { new(300, 312), new(301, 241), new(302, 168), new(303, 0) }));
        await Frames(4);
        Require(board.Visible && PluginHost.Game.Party.WarBoardOpen, "war board: War Board on the party list opens the client's board in the Classic frame");
        party.Position = new Vector2(GetViewportRect().Size.X - party.Size.X, 0);
        board.Position = new Vector2(party.Position.X - board.Size.X - 8, 0);
        var rows = board.GetChildren().OfType<ClassicWarBoardRow>().Where(r => r.Visible).ToArray();
        Require(rows.Length == 4 && rows.Select(r => r.Row.Share).SequenceEqual(rows.Select(r => r.Row.Share).OrderByDescending(s => s)), "war board: four rows ranked by share");
        Require(rows[0].Row.Name == "Canbo" && rows[0].Row.Rank == 1, "war board: the client's ranking and names");
        Require(board.Size == new Vector2(ClassicPartyHudLayout.WarBoard.Width, ClassicPartyHudLayout.WarBoard.Height(4)), "war board: size fits its rows " + board.Size);
        foreach (var control in board.GetChildren().OfType<Control>().Where(c => c.HasMeta("war_board_expected_rect") && c.Visible))
            Require(control.GetRect() == control.GetMeta("war_board_expected_rect").AsRect2(), "war board: actual bounds of " + control.Name + " " + control.GetRect());
        Require(GetViewportRect().Encloses(board.GetGlobalRect()) && !board.GetGlobalRect().Intersects(party.GetGlobalRect()), "war board: inside the viewport, beside the party list");
        var damage = board.GetNode<Button>("war_board_damage"); var healing = board.GetNode<Button>("war_board_healing");
        Require(damage.ButtonPressed == (mode == PartyBoardMeterMode.Damage) && healing.ButtonPressed == (mode == PartyBoardMeterMode.Healing), "war board: the tab of the client's mode is selected");
        await Shot("war-board");
        // The board's tooltips use the original tooltip look (co_tooltip_us), as the party list's do.
        {
            // A member row (the fixture does not seed the own class, so the own row would read "Class 0").
            var row = rows[1];
            var over = row.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = over, GlobalPosition = over });
            PopupPanel? tip = null;
            // Godot opens a tooltip after its delay in seconds; at a high frame rate 120 frames pass sooner.
            ulong until = Time.GetTicksMsec() + 3000;
            for (int i = 0; (i < 120 || Time.GetTicksMsec() < until) && tip == null; i++)
            {
                await Frames(1);
                tip = PartyEverything(GetTree().Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible && row.IsAncestorOf(p));
            }
            Require(tip != null, "war board: a row tooltip opens on hover");
            await Frames(4);
            var tipLabel = PartyEverything(tip!).OfType<Label>().First();
            var ground = tip!.GetThemeStylebox("panel", "TooltipPanel") as StyleBoxFlat;
            Require(ground != null && ground.BgColor.IsEqualApprox(new Color(0, 0, 0, 187f / 255f)) && tipLabel.GetThemeFont("font") == Plugin.Kit.Bold
                && tipLabel.GetThemeColor("font_color") == Colors.White, "war board: the row tooltip uses the original tooltip look (translucent black, white Arial Bold)");
            Require(tipLabel.Text == row.TooltipText && tipLabel.GetLineCount() == 3 && !tipLabel.Text.Split('\n')[1].StartsWith("Class ") && GetViewportRect().Encloses(new Rect2(tip.Position, tip.Size)),
                "war board: the row tooltip shows the member, class and share inside the window " + new Rect2(tip.Position, tip.Size));
            await Shot("war-board-tooltip");
            var away = new Vector2(4, GetViewportRect().Size.Y - 4);
            GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
            await Frames(6);
            Require(!GodotObject.IsInstanceValid(tip) || !tip.Visible, "war board: the row tooltip closes when the pointer leaves");
        }
        healing.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Require(PluginHost.Game.Party.WarBoardMode == PartyBoardMeterMode.Healing && healing.ButtonPressed && !damage.ButtonPressed, "war board: Healing switches the client's board mode");
        // The tab changed within five seconds of the last request: the client holds it and counts down. The board
        // reads the seconds from the party API (ff78eef) when the client raises Changed, without polling.
        var cooldown = board.GetNode<Label>("war_board_cooldown");
        Require(!board.IsProcessing(), "war board: no per-frame polling of the client's board");
        DetailCall(world, "WarBoard.WarBoardTick", Now()); await Frames(4);
        int coolSeconds = PluginHost.Game.Party.WarBoardCooldown;
        Require(coolSeconds is >= 1 and <= 5 && cooldown.Visible && cooldown.Text == coolSeconds.ToString(),
            "war board: the client's tab cooldown (IGameParty.WarBoardCooldown) shows beside the tabs (" + cooldown.Text + ")");
        // A later second: the client notifies the change and the digits follow.
        Set("WarBoard.warBoardSentAt", Now() - PartyWarBoard.BoardTabSeconds + 2.5);
        DetailCall(world, "WarBoard.WarBoardTick", Now()); await Frames(4);
        Require(PluginHost.Game.Party.WarBoardCooldown == 3 && cooldown.Visible && cooldown.Text == "3",
            "war board: the digits follow the client's later second (" + PluginHost.Game.Party.WarBoardCooldown + " / " + cooldown.Text + ")");
        Require(cooldown.GetRect() == ClassicPartyHudLayout.WarBoard.Cooldown && !cooldown.GetRect().Intersects(healing.GetRect()) && new Rect2(Vector2.Zero, board.Size).Encloses(cooldown.GetRect()),
            "war board: the cooldown sits right of the tabs inside the board " + cooldown.GetRect());
        var coolFont = cooldown.GetThemeFont("font"); int coolSize = cooldown.GetThemeFontSize("font_size");
        Require(coolFont.GetStringSize(cooldown.Text, HorizontalAlignment.Left, -1, coolSize).X <= cooldown.Size.X && coolFont.GetHeight(coolSize) <= cooldown.Size.Y + 1, "war board: the cooldown digits fit their place");
        await Shot("war-board-cooldown");
        Set("WarBoard.warBoardSentAt", Now() - PartyWarBoard.BoardTabSeconds - 1);
        DetailCall(world, "WarBoard.WarBoardTick", Now()); await Frames(4);
        Require(PluginHost.Game.Party.WarBoardCooldown == 0 && !cooldown.Visible, "war board: the cooldown hides once the client sends the held tab");
        var alpha = board.GetNode<HSlider>("war_board_alpha");
        alpha.Value = .5;
        await Frames(4);
        Require(Mathf.IsEqualApprox(PluginHost.Game.Party.WarBoardAlpha, .5f) && Mathf.IsEqualApprox(board.GetNode<ClassicFrame>("war_board_frame").BackgroundAlpha, .5f), "war board: the transparency slider sets the client's board alpha (" + PluginHost.Game.Party.WarBoardAlpha + ", " + board.GetNode<ClassicFrame>("war_board_frame").BackgroundAlpha + ", " + alpha.Value + ", " + Config.PartyBoardAlpha + ")");
        DetailCall(world, "WarBoard.OnWarBoard", new WarBoardReply(PartyWarBoardTarget.Board, PartyBoardMeterMode.Healing,
            new List<WarBoardShare> { new(303, 655), new(300, 120) }));
        await Frames(4);
        Require(board.GetChildren().OfType<ClassicWarBoardRow>().Count(r => r.Visible) == 2, "war board: rows follow the client's reply");
        await Shot("war-board-healing");
        board.GetNode<Button>("war_board_close").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Require(!board.Visible && !PluginHost.Game.Party.WarBoardOpen, "war board: the close button closes the client's board");
        Config.SetPartyBoardAlpha(savedAlpha); Config.SetPartyBoardMode(savedMode);
        hud.QueueFree();

        // 5. Clan page: the online bonus line and the own row's memo action.
        var fixture = new CharacterAuditData(nation) { ClanBonus = "O O O X X X", ClanBonusTip = "EXP 39% increase" };
        foreach (var proxy in _proxies) proxy.CharacterAudit = fixture;
        var shell = new Control(); AddChild(shell);
        var host = (WindowHost)Create(typeof(WindowHost), "character_info", "Character", shell, (Action)(() => { }));
        var character = new CharacterWindow(host) { Position = new Vector2(32, 46) }; AddChild(character);
        fixture.SelectPage("clan");
        typeof(CharacterWindow).GetMethod("Refresh", flags)!.Invoke(character, null);
        await Settled();
        var bonus = Descendants(character).OfType<Label>().Single(l => l.Name == "clan_bonus" && l.IsVisibleInTree());
        Require(bonus.Text == "Bonus  O O O X X X" && bonus.TooltipText == "EXP 39% increase", "clan page: the clan online bonus line and its explanation");
        var name = Descendants(character).OfType<Label>().First(l => l.IsVisibleInTree() && l.Text == "canCLAN");
        Require(!name.GetGlobalRect().Intersects(bonus.GetGlobalRect()) && character.GetGlobalRect().Encloses(bonus.GetGlobalRect()), "clan page: the bonus keeps clear of the clan name inside the window " + bonus.GetGlobalRect());
        await Shot("clan-bonus");
        var list = ((Dictionary<string, CharacterList>)typeof(CharacterWindow).GetField("_lists", flags)!.GetValue(character)!)["clan"];
        var own = list.GetChildren().OfType<Button>().First(b => b.TooltipText == "Chief / online");
        var at = own.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, ButtonMask = MouseButtonMask.Right, Pressed = true, Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(4);
        var menu = ClassicContextMenu.OpenLayer?.GetChildren().OfType<ClassicContextMenu>().FirstOrDefault() ?? throw new Exception("USER_VOTE_AUDIT: own row menu did not open");
        Require(menu.GetGlobalRect().Grow(2).HasPoint(at) || (menu.GetGlobalRect().Position - at).Length() < 24, "clan page: the own row menu opens at the cursor " + menu.GetGlobalRect());
        var captions = Descendants(menu).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text).ToArray();
        Require(captions.SequenceEqual(new[] { "User information", "Write memo" }), "clan page: the own row offers the memo (" + string.Join(", ", captions) + ")");
        await Shot("clan-own-menu");
        Descendants(menu).OfType<Button>().Single(b => b.Text == "Write memo").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Require(fixture.LastAct == ("clan_memo", "Canbo"), "clan page: Write memo runs the page's memo action");
        ClassicContextMenu.Close();
        // The live page reads the bonus from the client: 24 members online light three marks.
        Native.Set(Game.Session.Clan, "MyClan", new MyClanInfo { InClan = true, ClanId = 11, Name = "canCLAN" });
        Native.Set(Game.Session.Clan, "ClanOnlineCount", 24);
        var bridge = CharacterPanelBridge.For(world);
        Require(bridge.Status("clan_bonus").Replace(" ", "") == "OOOXXX" && bridge.Status("clan_bonus_tip").Length > 0, "clan page: the live bonus follows the client's online count (" + bridge.Status("clan_bonus") + ")");
        Native.Set(Game.Session.Clan, "ClanOnlineCount", ClanSession.NoClanBonus);
        Require(bridge.Status("clan_bonus") == "", "clan page: no bonus line before the server reports one");

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-user-vote.json"), JsonSerializer.Serialize(new { nation = prefix, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"USER_VOTE_AUDIT_OK {prefix} {checks.Count} checks / {screens.Count} screens");
        character.QueueFree(); world.Free();
        await Frames(2);
        net.Free();
    }
}
