using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using NoticeSpec = KnightOnlineUiClassic.Layout.ClassicQuestHudLayout.Notice;
using TrackerSpec = KnightOnlineUiClassic.Layout.ClassicQuestHudLayout.Tracker;
using PremiumSpec = KnightOnlineUiClassic.Layout.ClassicQuestHudLayout.Premium;

public partial class Preview
{
    /// <summary>
    /// The login quest notice (quest_available), the active quest tracker and the premium chips: the client's own
    /// windows and controls built in representative states, captured as the client draws them and with the
    /// Classic skins, their rendered bounds measured, and their native actions driven through real pointer input.
    /// </summary>
    private async Task CapturePremiumQuestAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/premium-quest-audit");
        System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        GetWindow().GuiEmbedSubwindows = true;
        var ground = new Color("252822");
        AddChild(new ColorRect { Color = ground, Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Quest Preview", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new LibreKO.Domain.ItemSlot[InventoryConstants.InventoryTotal]);
        ItemData.EnsureLoaded();
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string message) { if (!valid) throw new Exception("PREMIUM_QUEST_AUDIT: " + message); checks.Add(message); }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var layer = new CanvasLayer { Layer = 74 }; AddChild(layer);
        var viewport = new Rect2(Vector2.Zero, GetViewportRect().Size);
        float Width(Control control, string text, string font = "font", string size = "font_size") =>
            control.GetThemeFont(font).GetStringSize(text, HorizontalAlignment.Left, -1, control.GetThemeFontSize(size)).X;
        void Save(Rect2 rect, string state, object? detail = null)
        {
            var area = rect.Grow(12).Intersection(viewport);
            using var image = GetViewport().GetTexture().GetImage();
            using var crop = image.GetRegion(new Rect2I((int)area.Position.X, (int)area.Position.Y, (int)area.Size.X, (int)area.Size.Y));
            string file = prefix + "-" + state + ".png";
            crop.SavePng(System.IO.Path.Combine(output, file));
            screens.Add(new { state, file, rect = rect.ToString(), detail });
        }
        async Task Move(Vector2 at) { GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2); }
        async Task Click(Control control)
        {
            var at = control.GetGlobalRect().GetCenter();
            await Move(at);
            GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true });
            await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames(3);
        }
        IEnumerable<Node> Everything(Node root) { yield return root; foreach (var child in root.GetChildren(true)) foreach (var node in Everything(child)) yield return node; }
        int Inked(Rect2 rect, Color ink, float tolerance = .08f)
        {
            using var image = GetViewport().GetTexture().GetImage();
            int found = 0;
            for (int y = (int)rect.Position.Y; y < (int)rect.End.Y; y++)
                for (int x = (int)rect.Position.X; x < (int)rect.End.X; x++)
                {
                    var c = image.GetPixel(x, y);
                    if (Mathf.Abs(c.R - ink.R) <= tolerance && Mathf.Abs(c.G - ink.G) <= tolerance && Mathf.Abs(c.B - ink.B) <= tolerance) found++;
                }
            return found;
        }

        // ---- Quest notice (quest_available) ----------------------------------------------------------------------
        var world = PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(world, "QuestNotifications.EnsureNotificationWindow");
        var notice = (HudWindow)DetailField(world, "QuestNotifications.NotificationWindow")!;
        notice.GetParent().RemoveChild(notice); layer.AddChild(notice);
        NativeCharacterNpc.Prepare(notice, world);
        Require(ClassicQuestNotice.Apply(notice.Body) is ClassicFrame, "The notice gets the plain Classic frame");
        Require(notice.GetChildren().OfType<ClassicFrame>().Single() is { Ornate: false }, "The notice frame is plain, not the ornate quest-talk frame");
        var notifications = (List<QuestView>)DetailField(world, "QuestNotifications.QuestNotifications")!;
        string longTitle = "The Very Long Quest About Every Supply Crate The Moradon Guards Lost";
        string longAnswer = "I will gladly bring every one of the supply crates the guards asked for";
        QuestView View(int index, QuestViewState state, string title, string dialogue, string[] topics) =>
            new(62 + index, 1, 21, true, true, false, false, state, 0, title, "Speak to the guards.", dialogue, new QuestObjectives(62 + index, false, []), [], [], topics);
        void Fill()
        {
            notifications.Clear();
            for (int i = 0; i < 56; i++)
                notifications.Add(i switch
                {
                    0 => View(0, QuestViewState.Available, "A New Beginning", "Welcome to Moradon. Help the guards protect the village.", ["Tell me more", "I will help", "Not now"]),
                    1 => View(1, QuestViewState.InProgress, "Daily Supply Delivery", string.Join(" ", Enumerable.Repeat("The guards need help delivering supplies before the next battle.", 4)), ["I will bring the supplies"]),
                    2 => View(2, QuestViewState.Claimable, "Patrick's Letter", "You found the letter. Bring it back to [Guard] Patrick for your reward.", ["Turn in", "Later"]),
                    3 => View(3, QuestViewState.Completed, "Worm Hunt", "You have already thinned out the worms near the bridge.", ["Thank you"]),
                    4 => View(4, QuestViewState.Available, longTitle, "A long quest name is trimmed in the pager row; the full name stays in the client.", [longAnswer, "Not now"]),
                    55 => View(55, QuestViewState.Available, "Path of the Warrior", "Train with the captain in Moradon.", ["I am ready"]),
                    _ => View(i, QuestViewState.Available, "Moradon Errand " + (i + 1), "Help the people of Moradon.", ["I will help"]),
                });
            Native.Set(world, "QuestNotifications.notificationIndex", 0);
        }
        Label NoticeTitle() => Descendants(notice.Header!).OfType<Label>().Last(l => l.Visible);
        Button[] Arrows() => Descendants(notice.Body).OfType<Button>().Where(b => b.TooltipText is ClassicQuestNotice.Previous or ClassicQuestNotice.Next && !b.IsQueuedForDeletion()).ToArray();
        Control[] Rows() => notice.Body.GetChildren().OfType<Control>().Where(c => c.Visible && !c.IsQueuedForDeletion()).ToArray();
        void CheckNotice(string state)
        {
            var frame = notice.GetGlobalRect();
            Require(frame.Size.X == NoticeSpec.Width, state + ": original co_rookietip_us width " + frame.Size);
            Require(viewport.Encloses(frame), state + ": notice inside the viewport " + frame);
            Require(notice.GetChildren().OfType<ClassicFrame>().Single().GetGlobalRect() == frame, state + ": the frame covers the whole window");
            var body = notice.Body.GetGlobalRect();
            Require(body.Position.X - frame.Position.X == NoticeSpec.Margin && frame.End.X - body.End.X == NoticeSpec.Margin && body.Size.X == NoticeSpec.BodyWidth,
                state + ": equal side margins " + (body.Position.X - frame.Position.X) + "/" + (frame.End.X - body.End.X));
            var rows = Rows();
            Require(rows.Length > 0 && frame.End.Y - rows[^1].GetGlobalRect().End.Y == NoticeSpec.Margin, state + ": bottom margin equals the side margins");
            for (int i = 1; i < rows.Length; i++)
                Require(rows[i].GetGlobalRect().Position.Y - rows[i - 1].GetGlobalRect().End.Y == NoticeSpec.Gap, state + ": even gap above row " + i);
            var title = NoticeTitle();
            Require(title.GetThemeFont("font") == Plugin.Kit.Bold && title.GetThemeColor("font_color") == ClassicDesign.FrameTitle, state + ": gold Arial Bold window title");
            Require(Width(title, title.Text) <= title.Size.X, state + ": window title fits '" + title.Text + "'");
            var close = Descendants(notice.Header!).OfType<Button>().Single(b => b.TooltipText == "Close");
            Require(frame.End.X - close.GetGlobalRect().End.X == NoticeSpec.Margin && close.Size == ClassicEventFamilyLayout.Close, state + ": round close knob at the right margin");
            Require(Mathf.Abs(close.GetGlobalRect().GetCenter().Y - title.GetGlobalRect().GetCenter().Y) <= 1, state + ": close knob level with the title");
            foreach (var row in rows)
            {
                var rect = row.GetGlobalRect();
                Require(rect.Position.X >= body.Position.X && rect.End.X <= body.End.X, state + ": row inside the content " + row.GetType().Name);
                switch (row)
                {
                    case HBoxContainer pager:
                        var arrows = pager.GetChildren().OfType<Button>().ToArray();
                        Require(arrows.Length == 2 && arrows.All(a => a.Size == NoticeSpec.Arrow), state + ": Level Guide arrows 32 x 17");
                        Require(arrows[0].GetGlobalRect().Position.X == body.Position.X && arrows[1].GetGlobalRect().End.X == body.End.X, state + ": arrows at both content edges");
                        var name = pager.GetChildren().OfType<Label>().Single();
                        Require(name.GetThemeColor("font_color") == ClassicWorldOverlays.MessageTitle && name.GetThemeFont("font") == Plugin.Kit.Bold, state + ": yellow notice heading");
                        Require(Width(name, name.Text) <= name.Size.X || name.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis, state + ": quest name fits or trims");
                        Require(name.GetGlobalRect().Position.X - arrows[0].GetGlobalRect().End.X == NoticeSpec.PagerGap && arrows[1].GetGlobalRect().Position.X - name.GetGlobalRect().End.X == NoticeSpec.PagerGap, state + ": quest name between the arrows with an even gap");
                        break;
                    case Label caption:
                        Require(caption.HasMeta("quest_status") && caption.GetThemeColor("font_color") == ClassicQuestStatus.Ink((QuestViewState)caption.GetMeta("quest_status").AsInt32()), state + ": state caption in its quest ink " + caption.Text);
                        break;
                    case RichTextLabel text:
                        Require(text.GetThemeColor("default_color") == Colors.White && text.GetThemeFont("normal_font") == Plugin.Kit.Bold, state + ": white Arial Bold text");
                        Require(text.Size.X == NoticeSpec.BodyWidth && text.GetContentHeight() <= text.Size.Y + 1, state + ": text wraps inside the content (" + text.GetContentHeight() + ")");
                        break;
                    case Button answer:
                        Require(rect.Size == new Vector2(NoticeSpec.BodyWidth, NoticeSpec.AnswerHeight), state + ": whole-width answer row '" + answer.Text + "' " + rect.Size);
                        Require(Width(answer, answer.Text) <= answer.Size.X - 2 * NoticeSpec.CaptionInset || answer.TooltipText == answer.Text, state + ": answer caption fits or keeps a tooltip");
                        Require(answer.GetThemeStylebox("normal").ContentMarginLeft >= NoticeSpec.CaptionInset && answer.GetThemeStylebox("hover").ContentMarginRight >= NoticeSpec.CaptionInset, state + ": answer caption inset from the button caps");
                        break;
                }
            }
            Require(Inked(new Rect2(frame.Position.X, frame.GetCenter().Y, 3, 1), ground, .02f) == 0 && Inked(new Rect2(frame.End.X - 3, frame.GetCenter().Y, 3, 1), ground, .02f) == 0,
                state + ": frame line drawn on both sides");
        }
        async Task ShotNotice(string state)
        {
            await Frames();
            CheckNotice(state);
            Save(notice.GetGlobalRect(), "notice-" + state, new { title = NoticeTitle().Text, rows = Rows().Select(r => new { type = r.GetType().Name, rect = r.GetGlobalRect().ToString() }).ToArray() });
        }
        async Task Settle()
        {
            var placement = notice.GetNode<ClassicQuestNoticePlacement>("classic_notice_placement");
            for (int i = 0; i < 30 && notice.Modulate.A < 1; i++) await Frames(1);
            Require(notice.Modulate.A == 1 && placement.OpeningFrames <= 6, "Notice settles within six frames (" + placement.OpeningFrames + ")");
        }

        Fill();
        DetailCall(world, "QuestNotifications.RefreshNotification");
        await Settle(); await Frames();
        var opened = notice.GetGlobalRect();
        Require(Mathf.Abs(opened.GetCenter().X - viewport.GetCenter().X) <= 1 && Mathf.Abs(opened.GetCenter().Y - viewport.GetCenter().Y) <= 1, "Notice opens centred " + opened);
        Require(NoticeTitle().Text == "Quests available (1 of 56)", "Native pager title " + NoticeTitle().Text);
        await ShotNotice("first");
        var top = notice.Position; var arrowsAt = Arrows().Select(a => a.GetGlobalRect()).ToArray();
        var answers = Rows().OfType<Button>().ToArray();
        await Move(answers[1].GetGlobalRect().GetCenter()); await Frames();
        Require(answers[1].GetDrawMode() == BaseButton.DrawMode.Hover, "Answer row receives real hover");
        Save(notice.GetGlobalRect(), "notice-hover");
        await Move(Vector2.Zero);

        // Pages: forward through the states, then back past the first page to the last.
        foreach (var (step, state, index) in new[] { (1, "in-progress", 1), (1, "ready", 2), (1, "completed", 3), (1, "long-title", 4) })
        {
            await Click(Arrows()[1]);
            Require((int)DetailField(world, "QuestNotifications.notificationIndex")! == index, "Next arrow pages the client's notice to " + (index + 1));
            Require(notice.Position == top && Arrows().Select(a => a.GetGlobalRect()).SequenceEqual(arrowsAt), state + ": paging keeps the top edge and the arrows");
            Require(NoticeTitle().Text == $"Quests available ({index + 1} of 56)", state + ": native title " + NoticeTitle().Text);
            Require(Rows().OfType<Button>().All(b => b.HasMeta("classic_quest_answer") && b.GetThemeStylebox("normal") is StyleBoxTexture) && Arrows().All(a => a.GetThemeStylebox("normal") is StyleBoxTexture), state + ": rebuilt rows styled on their first frame");
            await ShotNotice(state);
        }
        var trimmed = Rows().OfType<HBoxContainer>().Single().GetChildren().OfType<Label>().Single();
        Require(trimmed.Text == longTitle && Width(trimmed, trimmed.Text) > trimmed.Size.X, "A long quest name is trimmed with an ellipsis in the pager");
        Native.Set(world, "QuestNotifications.notificationIndex", 0); DetailCall(world, "QuestNotifications.RefreshNotification"); await Frames();
        await Click(Arrows()[0]);
        Require((int)DetailField(world, "QuestNotifications.notificationIndex")! == 55 && NoticeTitle().Text == "Quests available (56 of 56)", "Previous arrow on the first page goes to the last page");
        await ShotNotice("last");

        // Answers and Close all reach the client's own handlers.
        Native.Set(world, "QuestNotifications.notificationIndex", 0); DetailCall(world, "QuestNotifications.RefreshNotification"); await Frames();
        var help = Rows().OfType<Button>().Single(b => b.Text == "I will help");
        await Click(help);
        Require(notifications.Count == 55 && notifications.All(q => q.QuestId != 62) && notice.Visible, "Answering removes that quest through the client and shows the next one");
        Require(NoticeTitle().Text == "Quests available (1 of 55)", "Count follows the answered quest " + NoticeTitle().Text);
        await Click(Rows().OfType<Button>().Single(b => b.Text == "Close all"));
        Require(notifications.Count == 0 && !notice.Visible, "Close all dismisses every quest through the client");

        // A single quest: no pager, the quest name as the title, Close; it reopens centred.
        notifications.Add(View(0, QuestViewState.Available, "A New Beginning", "Welcome to Moradon. Help the guards protect the village.", ["Tell me more", "I will help", "Not now"]));
        notice.Position = new Vector2(20, 20);
        DetailCall(world, "QuestNotifications.RefreshNotification");
        await Settle(); await Frames();
        Require(!Rows().OfType<HBoxContainer>().Any() && NoticeTitle().Text == "A New Beginning" && Rows().OfType<Button>().Last().Text == "Close", "A single quest has no pager and a Close row");
        Require(Mathf.Abs(notice.GetGlobalRect().GetCenter().X - viewport.GetCenter().X) <= 1, "The notice reopens centred");
        await ShotNotice("single");
        await Click(Descendants(notice.Header!).OfType<Button>().Single(b => b.TooltipText == "Close"));
        Require(notifications.Count == 0 && !notice.Visible, "The close knob dismisses the quest through the client");

        // Many answers: the window grows by whole rows and stays inside the screen.
        notifications.Add(View(7, QuestViewState.Available, "Moradon Errands", "Choose an errand.", Enumerable.Range(1, 9).Select(i => "Errand number " + i).ToArray()));
        DetailCall(world, "QuestNotifications.RefreshNotification");
        await Settle(); await ShotNotice("many-answers");
        DetailCall(world, "QuestNotifications.Dismiss"); await Frames(2);

        // ---- Quest tracker ---------------------------------------------------------------------------------------
        var trackerWorld = PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(trackerWorld, "Quest.BuildLog");
        var questViews = (System.Collections.IDictionary)DetailField(trackerWorld, "Session.QuestBook.Views")!;
        var tracker = (PanelContainer)DetailCall(trackerWorld, "Preview.BuildQuestTracker")!;
        var quests = (List<QuestEntry>)DetailField(trackerWorld, "Session.QuestBook.Entries")!;
        var tracked = (HashSet<int>)DetailField(trackerWorld, "Session.QuestBook.Tracked")!;
        var strings = (System.Collections.IDictionary)DetailField(trackerWorld, "Session.QuestBook.Strings")!;
        var objectives = (System.Collections.IDictionary)DetailField(trackerWorld, "Session.QuestBook.Objectives")!;
        var kills = (System.Collections.IDictionary)DetailField(trackerWorld, "Session.QuestBook.Kills")!;
        var stringsType = strings.GetType().GetGenericArguments()[1];
        foreach (var (id, title, count, target) in new[] { (900, "The Very Long Quest About Every Supply Crate Lost Near Moradon", (ushort)2, 6), (901, "Bandicoot Pelts", (ushort)6, 6), (902, "Kecoon Patrol", (ushort)0, 15) })
        {
            var view = new QuestView(id, 14201, 21, false, false, false, false, QuestViewState.InProgress, 0, title, "", "",
                new QuestObjectives(id, false, [new QuestKillGroup(target, [150], id == 902 ? "Kecoon" : "Bandicoot")]), [count], [], []);
            questViews[id] = view;
            strings[id] = Activator.CreateInstance(stringsType, id, title, "")!;
            objectives[id] = view.Objectives;
            kills[id] = view.Counts;
            quests.Add(new QuestEntry(id, 1));
        }
        tracked.Clear();
        var layerTracker = new CanvasLayer { Layer = 65 }; AddChild(layerTracker);
        layerTracker.AddChild(tracker);
        DetailCall(trackerWorld, "QuestTracker.RefreshTracker");
        tracker.Position = new Vector2(600, 40); tracker.ResetSize(); await Frames();
        Save(tracker.GetGlobalRect(), "tracker-native");
        NativeQuestHud.PrepareTracker(trackerWorld);
        Require(tracker.Name == NativeQuestHud.TrackerName && tracker.HasMeta(ClassicQuestTracker.Skinned), "The preparer names the client's tracker and the skin applies");
        var rowsBox = (VBoxContainer)tracker.FindChild(NativeQuestHud.TrackerRows, true, false)!;
        async Task<Rect2> ShowTracker()
        {
            await Frames(2); tracker.ResetSize(); await Frames(3);
            return tracker.GetGlobalRect();
        }
        void CheckTracker(string state)
        {
            var frame = tracker.GetGlobalRect(); var rows = rowsBox.GetGlobalRect();
            Require(frame.Size.X == TrackerSpec.Width, state + ": tracker keeps the quest notify width " + frame.Size);
            Require(viewport.Encloses(frame), state + ": tracker inside the viewport");
            Require(tracker.GetChildren().OfType<ClassicFrame>().Single().GetGlobalRect() == frame, state + ": plain frame covers the tracker");
            Require(rows.Position.X - frame.Position.X == TrackerSpec.PadX && frame.End.X - rows.End.X == TrackerSpec.PadX, state + ": equal side padding");
            Require(rows.Position.Y - frame.Position.Y == TrackerSpec.PadY && frame.End.Y - rows.End.Y == TrackerSpec.PadY, state + ": equal top and bottom padding");
            foreach (var row in rowsBox.GetChildren().OfType<Control>().Where(c => c.Visible && !c.IsQueuedForDeletion()))
            {
                Require(row.HasMeta(ClassicQuestTracker.Row), state + ": row styled " + row.Name);
                Require(row.GetGlobalRect().Position.X == rows.Position.X && row.GetGlobalRect().End.X == rows.End.X, state + ": row spans the content " + row.Name);
                foreach (var label in Descendants(row).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0))
                {
                    Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeConstant("outline_size") == 0, state + ": Classic Arial Bold '" + label.Text + "'");
                    Require(rows.Grow(.5f).Encloses(label.GetGlobalRect()), state + ": '" + label.Text + "' inside the tracker");
                    Require(label.AutowrapMode != TextServer.AutowrapMode.Off || Width(label, label.Text) <= label.Size.X, state + ": '" + label.Text + "' fits");
                }
            }
            var header = (Control)rowsBox.FindChild(NativeQuestHud.TrackerHeader, true, false)!;
            var fold = (Button)header.FindChild(NativeQuestHud.TrackerFold, true, false)!;
            Require(fold.Size == TrackerSpec.Fold && fold.GetGlobalRect().End.X == rows.End.X, state + ": Classic fold arrow at the right edge");
            var titleLabel = Descendants(header).OfType<Label>().First(l => l.Text == "Quests");
            Require(titleLabel.GetThemeColor("font_color") == ClassicDesign.FrameTitle, state + ": tracker title in the frame title gold");
        }
        var trackerRect = await ShowTracker();
        CheckTracker("page-1");
        var pager = rowsBox.GetChildren().OfType<HBoxContainer>().Single(h => h.GetChildren().OfType<Button>().Count() == 2);
        var pageArrows = pager.GetChildren().OfType<Button>().ToArray();
        var pageLabel = pager.GetChildren().OfType<Label>().Single();
        Require(pageArrows.All(a => a.Size == TrackerSpec.Arrow) && pageArrows[0].Disabled && !pageArrows[1].Disabled, "Level Guide arrows; Previous is disabled on the first page");
        Require(pageLabel.Text == "1/2" && pageLabel.GetThemeColor("font_color") == ClassicQuestHudLayout.PageInk, "Page number in the Level Guide page ink " + pageLabel.Text);
        Require(pageArrows[0].GetGlobalRect().Position.X == rowsBox.GetGlobalRect().Position.X && pageArrows[1].GetGlobalRect().End.X == rowsBox.GetGlobalRect().End.X, "Page arrows at both content edges");
        Require(Descendants(rowsBox).OfType<Label>().Any(l => l.IsVisibleInTree() && l.GetThemeColor("font_color") == ClassicQuestStatus.Ink(QuestViewState.Claimable)), "Ready quest shows in the ready ink");
        Save(trackerRect, "tracker-page-1");
        var firstTitle = Descendants(rowsBox).OfType<PanelContainer>().First(p => p.TooltipText == "Open in the quest journal");
        await Move(firstTitle.GetGlobalRect().GetCenter()); await Frames();
        Require(firstTitle.GetThemeStylebox("panel") is StyleBoxFlat lit && lit.BgColor == ClassicDarkFrame.SelectedRow, "Quest name row lights on hover");
        Save(trackerRect, "tracker-hover");
        Native.Set(trackerWorld, "Quest.Selected", -1);
        await Click(firstTitle);
        int firstQuest = (int)DetailField(trackerWorld, "Quest.Selected")!;
        Require(firstQuest == 60, "Clicking a quest name selects it in the client's journal (" + firstQuest + ")");
        await Move(Vector2.Zero);
        await Click(pageArrows[1]);
        Require((int)DetailField(trackerWorld, "QuestTracker.trackerPage")! == 1, "Next arrow turns the client's tracker page");
        trackerRect = await ShowTracker();
        CheckTracker("page-2");
        pager = rowsBox.GetChildren().OfType<HBoxContainer>().Single(h => h.GetChildren().OfType<Button>().Count() == 2);
        Require(pager.GetChildren().OfType<Button>().Last().Disabled && pager.GetChildren().OfType<Label>().Single().Text == "2/2", "Next is disabled on the last page");
        Save(trackerRect, "tracker-page-2");
        await Click(pager.GetChildren().OfType<Button>().First());
        Require((int)DetailField(trackerWorld, "QuestTracker.trackerPage")! == 0, "Previous arrow returns to the first page");
        bool wasFolded = Config.QuestTrackerCollapsed;
        if (wasFolded) Config.SetQuestTrackerCollapsed(false);
        DetailCall(trackerWorld, "QuestTracker.RefreshTracker");
        var unfoldedRect = await ShowTracker();
        var foldButton = (Button)rowsBox.FindChild(NativeQuestHud.TrackerFold, true, false)!;
        // Fold and unfold as the player does: no audit resize, the skin must fit the frame itself.
        async Task<Rect2> Settled() { await Frames(5); return tracker.GetGlobalRect(); }
        await Click(foldButton);
        trackerRect = await Settled();
        Require(tracker.GetChildren().OfType<ClassicFrame>().Single().GetGlobalRect() == trackerRect && trackerRect.Size.Y < unfoldedRect.Size.Y,
            $"Folding shrinks the frame to the header by itself ({unfoldedRect.Size} -> {trackerRect.Size})");
        Require(Config.QuestTrackerCollapsed && rowsBox.GetChildren().OfType<Control>().Count(c => c.Visible && !c.IsQueuedForDeletion()) == 1, "Fold collapses the client's tracker to its header");
        Require(trackerRect.Size.Y == TrackerSpec.HeaderHeight + TrackerSpec.PadY * 2, "Folded tracker is the header in its frame " + trackerRect.Size);
        Require(foldButton.GetNode<ClassicTrackerFold>("classic_fold").IsFolded && foldButton.Text.Length == 0, "Fold button shows the Classic down arrow");
        CheckTracker("folded");
        Save(trackerRect, "tracker-folded");
        await Click(foldButton);
        Require(!Config.QuestTrackerCollapsed, "Fold button unfolds the tracker");
        var restoredRect = await Settled();
        Require(restoredRect == unfoldedRect, $"Unfolding restores the full frame by itself ({restoredRect} / {unfoldedRect})");
        CheckTracker("unfolded");
        Save(restoredRect, "tracker-unfolded");
        Config.SetQuestTrackerCollapsed(wasFolded);
        tracked.Add(900);
        DetailCall(trackerWorld, "QuestTracker.RefreshTracker");
        trackerRect = await ShowTracker();
        CheckTracker("single-long");
        Save(trackerRect, "tracker-single-long");
        tracked.Clear(); quests.Clear(); DetailCall(trackerWorld, "QuestTracker.RefreshTracker"); await Frames();
        Require(!tracker.Visible, "Without active quests the client hides the tracker");
        layerTracker.QueueFree();

        // ---- Premium chips ---------------------------------------------------------------------------------------
        var premiumWorld = PreviewFixtures.Prepared(new WorldRoot());
        var chip = (PanelContainer)DetailCall(premiumWorld, "Preview.BuildPremium")!;
        var chipLayer = new CanvasLayer { Layer = 66 }; AddChild(chipLayer);
        chipLayer.AddChild(chip);
        chip.SetAnchorsPreset(Control.LayoutPreset.TopLeft); chip.GrowHorizontal = Control.GrowDirection.End;
        var bonuses = new[] { new PremiumBonus(PremiumBonusKind.Experience, 10), new PremiumBonus(PremiumBonusKind.Drops, 5), new PremiumBonus(PremiumBonusKind.RepairDiscount, 50), new PremiumBonus(PremiumBonusKind.FreeReset, 0) };
        async Task<Rect2> Status(int status, int type, int hours)
        {
            Native.Call(Game.Session.Premium, "Apply", status, type, hours, bonuses);
            DetailCall(premiumWorld, "Premium.OnStatus", status, type, hours);
            chip.Position = new Vector2(500, 60); chip.Size = Vector2.Zero; chip.ResetSize(); await Frames();
            return chip.GetGlobalRect();
        }
        // PremiumNames has no type constants since 3277df5 (the names come from the client's text table): 5 is Gold,
        // 13 (Switching) is no longer in the table and reads as plain "Premium". The client's chip now shows the name
        // alone ("GOLD PREMIUM") with the time in its tooltip; the Classic plaque keeps the approved title-case caption
        // with the time ("Gold Premium: 2d 4h") and the name, time and benefits in its tooltip.
        const int goldPremium = 5, switchingPremium = 13;
        var chipRect = await Status(1, goldPremium, 52);
        Save(chipRect, "premium-native");
        await Status(0, 0, 0); Save(chip.GetGlobalRect(), "premium-none-native");
        NativeQuestHud.Prepare(premiumWorld);
        Require(chip.Name == NativeQuestHud.PremiumName && chip.HasMeta(ClassicPremiumChip.Skinned), "The preparer names the client's premium chip and the skin applies");
        var chipLabel = (Label)chip.FindChild(NativeQuestHud.PremiumLabel, true, false)!;
        void CheckChip(PanelContainer plate, Label label, string state, bool active)
        {
            var rect = plate.GetGlobalRect();
            Require(rect.Size.Y == PremiumSpec.Height, state + ": information plaque height " + rect.Size);
            Require(label.GetThemeFont("font") == Plugin.Kit.Bold && label.GetThemeFontSize("font_size") == PremiumSpec.FontSize, state + ": Arial Bold caption");
            Require(label.GetThemeColor("font_color") == (active ? ClassicWorldOverlays.PlaqueInk : ClassicQuestHudLayout.PlaqueMuted), state + ": " + (active ? "plaque ink" : "muted plaque ink") + " '" + label.Text + "'");
            Require(rect.Encloses(label.GetGlobalRect()) && Width(label, label.Text) <= label.Size.X, state + ": caption inside the plaque");
            Require(label.GetGlobalRect().Position.X - rect.Position.X == PremiumSpec.Inset && rect.End.X - label.GetGlobalRect().End.X == PremiumSpec.Inset, state + ": even caption insets");
            Require(!Descendants(plate).OfType<Panel>().Any(p => p.IsVisibleInTree()), state + ": the client's status dot gives way to the ink");
        }
        foreach (var (status, type, hours, state, active, caption) in new[] { (0, 0, 0, "premium-none", false, "No Premium"), (1, goldPremium, 52, "premium-gold", true, "Gold Premium: 2d 4h"),
            (2, 0, 5, "premium-pc-room", true, "PC Room: 5h"), (1, switchingPremium, 719, "premium-long", true, "Premium: 29d 23h") })
        {
            chipRect = await Status(status, type, hours);
            Require(chipLabel.Text == caption, state + ": the approved caption '" + caption + "' (shown '" + chipLabel.Text + "')");
            CheckChip(chip, chipLabel, state, active);
            Save(chipRect, state, new { text = chipLabel.Text });
        }
        // The benefits tooltip, opened by real hovering.
        await Status(1, goldPremium, 52);
        await Move(chip.GetGlobalRect().GetCenter());
        PopupPanel? tip = null;
        // Godot opens a tooltip after its delay in seconds; at a high frame rate 90 frames pass sooner.
        ulong until = Time.GetTicksMsec() + 3000;
        for (int i = 0; (i < 90 || Time.GetTicksMsec() < until) && tip == null; i++)
        {
            await Frames(1);
            tip = Everything(GetTree().Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible);
        }
        Require(tip != null, "Hovering the chip opens the client's benefits tooltip");
        Require(chip.IsAncestorOf(tip!), "The tooltip belongs to the chip and takes its theme");
        await Frames(4);
        var tipBox = tip!.GetThemeStylebox("panel", "TooltipPanel") as StyleBoxFlat;
        Require(tipBox != null && tipBox.BgColor.IsEqualApprox(PremiumSpec.TooltipGround), "Tooltip uses the original translucent black ground");
        var tipLabel = Descendants(tip).OfType<Label>().FirstOrDefault();
        Require(tipLabel != null && tipLabel.Text.StartsWith("Gold Premium\n2 days 4 hours left\n") && tipLabel.Text.Contains("EXP +10%") && tipLabel.GetThemeFont("font") == Plugin.Kit.Bold && tipLabel.GetThemeColor("font_color") == Colors.White,
            "Tooltip keeps the name, the time left and the client's benefits text in white Arial Bold: " + tipLabel?.Text.Replace("\n", " / "));
        var tipRect = new Rect2(tip.Position, tip.Size);
        Save(chip.GetGlobalRect().Merge(tipRect), "premium-tooltip", new { tooltip = tipLabel!.Text, rect = tipRect.ToString() });
        await Move(Vector2.Zero); await Frames(4);

        // The clan premium chip.
        DetailCall(premiumWorld, "ClanPremium.BuildChip");
        var clanChip = (PanelContainer)DetailField(premiumWorld, "ClanPremium.chip")!;
        clanChip.GetParent().RemoveChild(clanChip); chipLayer.AddChild(clanChip);
        foreach (var child in clanChip.GetChildren().OfType<HudLayout>().ToArray()) { clanChip.RemoveChild(child); child.Free(); }
        clanChip.SetAnchorsPreset(Control.LayoutPreset.TopLeft); clanChip.GrowHorizontal = Control.GrowDirection.End;
        Native.Set(premiumWorld, "ClanPremium.inClan", true);
        NativeQuestHud.Prepare(premiumWorld);
        var clanLabel = (Label)clanChip.FindChild(NativeQuestHud.ClanPremiumLabel, true, false)!;
        foreach (var (active, state) in new[] { (false, "clan-premium-none"), (true, "clan-premium-active") })
        {
            Native.Set(Game.Session.ClanPremium, "ClanPremiumActive", active);
            Native.Set(Game.Session.ClanPremium, "ClanPremiumEndsAt", active ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3 * 86400 + 7200 : 0L);
            DetailCall(premiumWorld, "ClanPremium.RefreshChip");
            clanChip.Position = new Vector2(500, 120); clanChip.Size = Vector2.Zero; clanChip.ResetSize(); await Frames();
            Require(clanChip.Visible && clanChip.HasMeta(ClassicPremiumChip.Skinned), state + ": clan chip shown for a clan member");
            CheckChip(clanChip, clanLabel, state, active);
            Save(clanChip.GetGlobalRect(), state, new { text = clanLabel.Text });
        }

        // The clan online-bonus chip (3277df5): the client's "Clan" and O/X marks on the same plaque, its tooltip kept.
        var bonusChip = (PanelContainer)DetailCall(premiumWorld, "Preview.BuildClanBonus")!;
        bonusChip.GetParent()?.RemoveChild(bonusChip); chipLayer.AddChild(bonusChip);
        bonusChip.SetAnchorsPreset(Control.LayoutPreset.TopLeft); bonusChip.GrowHorizontal = Control.GrowDirection.End;
        NativeQuestHud.Prepare(premiumWorld);
        bonusChip.Position = new Vector2(500, 180); bonusChip.Size = Vector2.Zero; bonusChip.ResetSize(); await Frames();
        Require(bonusChip.Name == NativeQuestHud.ClanBonusName && bonusChip.HasMeta(ClassicPremiumChip.Skinned), "clan-bonus: the preparer names the client's clan bonus chip and the skin applies");
        var bonusRect = bonusChip.GetGlobalRect();
        var bonusLabels = Descendants(bonusChip).OfType<Label>().Where(l => l.IsVisibleInTree()).ToArray();
        var bonusMarks = (Label)bonusChip.FindChild(NativeQuestHud.ClanBonusMarks, true, false)!;
        Require(bonusRect.Size.Y == PremiumSpec.Height, "clan-bonus: information plaque height " + bonusRect.Size);
        Require(bonusLabels.Length == 2 && bonusLabels.All(l => l.GetThemeFont("font") == Plugin.Kit.Bold && l.GetThemeColor("font_color") == ClassicWorldOverlays.PlaqueInk
            && bonusRect.Encloses(l.GetGlobalRect()) && Width(l, l.Text) <= l.Size.X), "clan-bonus: 'Clan' and the marks in the plaque ink, inside the plaque");
        Require(bonusMarks.Text == NativePremium.BonusLines(true, 24).Marks && bonusChip.TooltipText.Length > 0, "clan-bonus: the client's marks for 24 online ('" + bonusMarks.Text + "') and its tooltip");
        Require(bonusLabels[1].GetGlobalRect().Position.X - bonusLabels[0].GetGlobalRect().End.X == PremiumSpec.BonusGap, "clan-bonus: a word gap between 'Clan' and the marks");
        Require(bonusLabels[0].GetGlobalRect().Position.X - bonusRect.Position.X == PremiumSpec.Inset && bonusRect.End.X - bonusLabels[^1].GetGlobalRect().End.X == PremiumSpec.Inset, "clan-bonus: even caption insets");
        Save(bonusRect, "clan-bonus", new { title = bonusLabels[0].Text, marks = bonusMarks.Text, tooltip = bonusChip.TooltipText });

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-premium-quest.json"), JsonSerializer.Serialize(new { nation = prefix, checks, screens },
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PREMIUM_QUEST_AUDIT_OK " + prefix + " checks=" + checks.Count + " screens=" + screens.Count);
    }
}
