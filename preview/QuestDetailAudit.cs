using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    private sealed record QuestDetailFixture(int Id, string Title, string Journal, int Zone, int Npc, string Monster, int Need, ushort Have,
        (int Item, int Count)[] Rewards);

    /// <summary>
    /// Quest Details on the Classic character window with a full client World and an emulated server. Every quest
    /// log request the Classic window sends is answered the way the server does (list, texts, objectives, kill
    /// counts, then views, a few frames apart), and the subpage must show the selected quest's real details on
    /// the first and every later selection: title, state, Area and NPC line, journal, hunt progress and rewards.
    /// </summary>
    private async Task CaptureQuestDetailAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/quest-detail-audit");
        System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        var window = GetWindow(); window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled; window.Size = new Vector2I(1000, 700);
        window.GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Canbo", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new ItemSlot[InventoryConstants.InventoryTotal]);
        ItemData.EnsureLoaded();
        // The owner's client runs without the legacy quest table fallback: only quests with a server view are listed.
        bool legacy = OS.GetCmdlineUserArgs().Contains("legacy-quests");
        typeof(Config).GetProperty("LegacyQuestFallback")!.SetValue(null, legacy);
        string prefix = nation == 1 ? "karus" : "human";
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var checks = new List<string>();
        var screens = new List<object>();
        bool explore = OS.GetCmdlineUserArgs().Contains("explore");
        void Require(bool valid, string message)
        {
            if (!valid && explore) { GD.Print("QUEST_DETAIL_FAIL " + message); return; }
            if (!valid) throw new Exception("QUEST_DETAIL_AUDIT: " + message);
            checks.Add(message);
        }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }

        var quests = new[]
        {
            new QuestDetailFixture(127, "Animal Blood", "You have promised [Moradon Merchant] Clark to bring him Animal Blood.", 21, 14435, "Shadow Seeker", 10, 4,
                [(900001000, 675000), (900000000, 2000)]),
            new QuestDetailFixture(130, "Kecoon hunting", "[Sentinel] Patrick asked me to hunt five Kecoons for him.", 21, 14436, "Kecoon", 5, 2,
                [(900001000, 1875)]),
            new QuestDetailFixture(133, "Bulcan of the Fields", "Hunt the Bulcans that trouble the farmers south of Moradon.", 21, 14437, "Bulcan", 8, 7,
                [(900001000, 9000), (900000000, 4000)]),
        };
        QuestView View(QuestDetailFixture q) => new(q.Id, q.Npc, q.Zone, false, false, false, false, QuestViewState.InProgress, 0, q.Title, q.Journal, "",
            new QuestObjectives(q.Id, false, [new QuestKillGroup(q.Need, [1], q.Monster)]), [q.Have],
            [.. q.Rewards.Select(r => new QuestTransfer(false, (byte)(r.Item == 900001000 ? 2 : 1), 0, r.Count, 0))], []);

        var model = PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(model, "Quest.BuildLog");
        var native = new HudWindow("quests", "QUESTS", new Vector2(170, 86), 420, persistLayout: false) { Visible = false };
        native.Body.AddChild((Control)DetailField(model, "Quest.QuestsContent")!);
        ((Dictionary<string, HudWindow>)DetailField(model, "MainWindows.Windows")!)["Quests"] = native;
        var shell = new Control(); AddChild(shell);
        var host = (LibreKO.Plugins.WindowHost)Create(typeof(LibreKO.Plugins.WindowHost), "character_info", "Character", shell, (Action)(() => { }));
        var bridge = CharacterPanelBridge.For(model);
        _windowData!.LiveCharacterPanel = bridge;
        var character = new CharacterWindow(host) { Position = new Vector2(40, 40) }; AddChild(character);
        AddChild(native); await Frames(2);
        NativeCharacterNpc.Prepare(native, model);
        var page = (CharacterEmbeddedPage)CharacterDetailsSkin.Apply(native.Body)!;

        var title = (Label)DetailField(model, "Quest.detailTitle")!;
        var sub = (Label)DetailField(model, "Quest.detailSub")!;
        var facts = (Label)DetailField(model, "Quest.detailFacts")!;
        var desc = (RichTextLabel)DetailField(model, "Quest.detailDesc")!;
        var objectives = (VBoxContainer)DetailField(model, "Quest.objectiveBox")!;
        var body = (Control)DetailField(model, "Quest.detailBody")!;
        var empty = (Control)DetailField(model, "Quest.detailEmpty")!;
        // A loopback socket stands in for the server connection, so the audit sees every packet the client sends.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        var client = new System.Net.Sockets.TcpClient(); client.Connect((System.Net.IPEndPoint)listener.LocalEndpoint);
        var server = listener.AcceptTcpClient(); listener.Stop();
        var connection = typeof(Net).GetField("conn", flags)!.GetValue(net)!;
        typeof(FramedConn).GetField("stream", flags)!.SetValue(connection, client.GetStream());
        typeof(FramedConn).GetField("Connected")!.SetValue(connection, true);
        var inbox = new List<byte>();
        int requests = 0, answers = 0;
        var reloads = new List<object>();
        // Quest log requests (GS_QUEST, sub 1) the client sent since the last call.
        async Task<int> TakeRequests()
        {
            await Frames(1);
            var stream = server.GetStream(); var buffer = new byte[4096];
            while (server.Available > 0) inbox.AddRange(buffer.Take(stream.Read(buffer, 0, buffer.Length)));
            int found = 0;
            while (inbox.Count >= 6)
            {
                int length = inbox[2] | inbox[3] << 8;
                if (inbox[0] != 0xAA || inbox[1] != 0x55) throw new Exception("QUEST_DETAIL_AUDIT: unframed client packet");
                if (inbox.Count < length + 6) break;
                if (length >= 2 && inbox[4] == (byte)GameOpcodes.GS_QUEST && inbox[5] == 1) found++;
                inbox.RemoveRange(0, length + 6);
            }
            requests += found;
            return found;
        }
        // While an answer is coming in, the open subpage keeps real details on every drawn frame: never the empty
        // hint and never a bare "Quest #id" title.
        async Task Reloading(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                await Frames(1);
                if (!page.IsVisibleInTree()) continue;
                bool placeholder = System.Text.RegularExpressions.Regex.IsMatch(title.Text.Trim(), @"^Quest( #?\d+)?$");
                if (reloads.Count < 8 * answers && (!body.IsVisibleInTree() || empty.IsVisibleInTree() || placeholder || i == 0))
                {
                    string file = prefix + "-reload-" + answers + "-" + reloads.Count + ".png";
                    using (var shot = GetViewport().GetTexture().GetImage()) shot.SavePng(System.IO.Path.Combine(output, file));
                    var shown = new { file, selected = DetailField(model, "Quest.Selected"), hint = empty.IsVisibleInTree(), title = title.Text };
                    reloads.Add(shown);
                    GD.Print("QUEST_DETAIL_RELOAD " + shown);
                }
                Require(body.IsVisibleInTree() && !empty.IsVisibleInTree(), "reload " + answers + " frame " + i + ": details stay shown");
                Require(!placeholder, "reload " + answers + " frame " + i + ": title '" + title.Text + "' is not a placeholder");
            }
        }
        // The server's answer to a quest log request, in its order and a few frames apart.
        async Task ServerReply()
        {
            answers++;
            DetailCall(model, "OnQuestLog", quests.Select(q => new QuestEntry(q.Id, 1)).ToList());
            await Reloading(2);
            DetailCall(model, "OnQuestStrings", quests.Select(q => new QuestStrings(q.Id, q.Title, q.Journal)).ToList());
            foreach (var q in quests) DetailCall(model, "OnQuestObjectives", new QuestObjectives(q.Id, false, [new QuestKillGroup(q.Need, [1], q.Monster)]));
            await Reloading(2);
            foreach (var q in quests) DetailCall(model, "OnQuestKillCounts", q.Id, new ushort[] { q.Have });
            await Reloading(2);
            foreach (var q in quests) DetailCall(model, "OnQuestView", View(q));
            // The client refreshes its open journal one deferred call after a view; the audit World is not in the tree.
            if (!model.IsInsideTree() && (bool)DetailCall(model, "MainWindows.IsOpen", "Quests")!) DetailCall(model, "Quest.RefreshLog");
            await Reloading(4);
        }
        // Runs a user action and answers the quest log requests it sends; returns how many it sent.
        async Task<int> Act(Action action)
        {
            action();
            await Frames(2);
            int sent = await TakeRequests();
            for (int i = 0; i < sent; i++) await ServerReply();
            await Frames(4);
            return sent;
        }

        // Login: the client asks for its quest log once.
        await ServerReply();
        var lists = (Dictionary<string, CharacterList>)typeof(CharacterWindow).GetField("_lists", flags)!.GetValue(character)!;
        var extras = (Dictionary<string, Button>)typeof(CharacterWindow).GetField("_extras", flags)!.GetValue(character)!;
        BaseButton Tab(string id) => (BaseButton)character.FindChild(id, true, false)!;
        void Choose(string id) => typeof(CharacterList).GetMethod("Choose", flags)!.Invoke(lists["quest"], [id]);

        async Task Expect(QuestDetailFixture q, string state)
        {
            await Frames(4);
            using (var shot = GetViewport().GetTexture().GetImage()) shot.SavePng(System.IO.Path.Combine(output, prefix + "-" + state + ".png"));
            GD.Print($"QUEST_DETAIL_STATE {state}: selected={DetailField(model, "Quest.Selected")} body={body.IsVisibleInTree()} empty={empty.IsVisibleInTree()} title='{title.Text}' sub='{sub.Text}' facts='{facts.Text}' desc='{desc.GetParsedText()}' titleRect={title.GetGlobalRect()}");
            Require(page.IsVisibleInTree(), state + ": Quest Details subpage is shown");
            Require((int)DetailField(model, "Quest.Selected")! == q.Id, state + ": client selection is quest " + q.Id + " (was " + DetailField(model, "Quest.Selected") + ")");
            Require(body.IsVisibleInTree() && !empty.IsVisibleInTree(), state + ": details body shown instead of the empty hint");
            Require(title.Text == q.Title, state + ": title '" + title.Text + "' is '" + q.Title + "'");
            Require(sub.Text.Contains("In progress") && sub.Text.Contains("Hunt"), state + ": state line '" + sub.Text + "'");
            Require(facts.IsVisibleInTree() && facts.Text.Contains("Area:") && facts.Text.Contains("NPC:"), state + ": Area and NPC line '" + facts.Text + "'");
            Require(desc.GetParsedText().Trim() == q.Journal, state + ": journal '" + desc.GetParsedText().Trim() + "'");
            var bars = Descendants(objectives).OfType<ProgressBar>().Where(b => b.IsVisibleInTree()).ToArray();
            Require(bars.Length == 1 && Mathf.Abs((float)bars[0].Value - (float)q.Have / q.Need) <= .01f, state + ": one hunt progress bar at " + q.Have + "/" + q.Need);
            Require(Descendants(objectives).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.Contains(q.Monster + " " + q.Have + "/" + q.Need)), state + ": hunt row text");
            CheckRewardRows(model, q.Rewards);
            // Rendered bounds: every text line of the details sits inside the page's content viewport with real size.
            var viewport = page.ContentScroll.GetGlobalRect();
            foreach (var control in new Control[] { title, sub, facts, desc })
            {
                var rect = control.GetGlobalRect();
                Require(rect.Size.X >= 200 && rect.Size.Y >= 12 && viewport.Encloses(rect), state + ": " + control.Name + " rendered at " + rect + " inside " + viewport);
            }
            string file = prefix + "-" + state + ".png";
            screens.Add(new { state, file, quest = q.Id, title = title.Text, sub = sub.Text, facts = facts.Text, title_rect = title.GetGlobalRect().ToString() });
            GD.Print("QUEST_DETAIL_OK " + state + " " + q.Id);
        }

        // 1. Open the character window on Character Report, switch to Quest, select the first quest and open Details
        //    before the answer to the Quest tab's request arrives (the first click after opening).
        await Act(() => Tab("btn_state").EmitSignal(BaseButton.SignalName.Pressed));
        Tab("btn_quest").EmitSignal(BaseButton.SignalName.Pressed);
        int inFlight = await TakeRequests();
        Require(inFlight == 1, "the Quest tab asks for the quest log once");
        Choose("127");
        Require(await Act(() => extras["quest:details"].EmitSignal(BaseButton.SignalName.Pressed)) == 0, "Details sends no quest log request");
        for (int i = 0; i < inFlight; i++) await ServerReply();
        await Expect(quests[0], "first-details");

        // 2. Back, a different quest, Details; then alternate.
        foreach (var (q, state) in new[] { (quests[1], "second-quest"), (quests[0], "back-to-first"), (quests[2], "third-quest") })
        {
            await Act(() => page.Back!());
            Choose(q.Id.ToString());
            Require(await Act(() => extras["quest:details"].EmitSignal(BaseButton.SignalName.Pressed)) == 0, state + ": Details sends no quest log request");
            await Expect(q, state);
        }

        // 3. Switch tabs and come back (a new request, answered before the click), then Details again.
        await Act(() => Tab("btn_state").EmitSignal(BaseButton.SignalName.Pressed));
        Require(await Act(() => Tab("btn_quest").EmitSignal(BaseButton.SignalName.Pressed)) == 1, "returning to the Quest tab reloads the quest log");
        Choose("130");
        await Act(() => extras["quest:details"].EmitSignal(BaseButton.SignalName.Pressed));
        await Expect(quests[1], "after-tab-switch");

        // 4. Close and reopen the character window on Character Report, then open Details from the client's tracker
        //    (ShowMainWindow + SelectQuest), which brings the character window to the Quest page.
        typeof(LibreKO.Plugins.WindowHost).GetMethod("RaiseHidden", flags)!.Invoke(host, null); await Frames(2);
        typeof(LibreKO.Plugins.WindowHost).GetMethod("RaiseShown", flags)!.Invoke(host, null); await Frames(2);
        await Act(() => Tab("btn_state").EmitSignal(BaseButton.SignalName.Pressed));
        Require(bridge.SelectedPage == "character", "the character window is back on Character Report");
        Require(await Act(() => { DetailCall(model, "MainWindows.Show", "Quests"); DetailCall(model, "Quest.Select", 133); }) == 0,
            "opening Quest Details from the tracker sends no quest log request");
        Require(bridge.SelectedPage == "quest", "the tracker brings the window to the Quest page");
        await Expect(quests[2], "tracker-open");

        // 5. A reload while the subpage stays open (the answer to a request sent before Details opened).
        await Act(() => page.Back!());
        Choose("127");
        await Act(() => extras["quest:details"].EmitSignal(BaseButton.SignalName.Pressed));
        await ServerReply();
        await Expect(quests[0], "late-reply");

        server.Close(); client.Close();
        _windowData.LiveCharacterPanel = null;
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-quest-detail-audit.json"), JsonSerializer.Serialize(new
        {
            nation = prefix, legacyFallback = legacy, requests, answers, reloads, checks, screens,
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"QUEST_DETAIL_AUDIT_OK {prefix}: {checks.Count} checks, {requests} quest log requests, {answers} answers");
    }
}
