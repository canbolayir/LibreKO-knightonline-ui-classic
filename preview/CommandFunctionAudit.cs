using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    /// <summary>
    /// command-function-audit: every command of the Classic command list, run from the list in a full native
    /// World (all facets, the real Classic plugin and chat) over an isolated loopback session. Each row is
    /// activated the way a double click does; the audit records the requests the client sends, the messages it
    /// prints, the windows and states it changes, and the chat prefill the Classic chat shows. Prefilled commands
    /// are then completed with arguments and submitted through the Classic chat input. The character has no
    /// target, party or clan, so context commands are expected to refuse or do nothing; the JSON lists each result.
    /// </summary>
    private async Task CaptureCommandFunctionAudit(int nation, PluginInfo info)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck", "content/armor.pck", "content/weapons.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing pack " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1200, 850);
        GetViewport().GuiEmbedSubwindows = true;
        new Plugin().Initialize((PluginContext)Create(typeof(PluginContext), info, new List<PluginInfo> { info }, PluginHost.Ui, PluginHost.Game));
        var original = Net.I; var net = new Net(); GetTree().Root.AddChild(net);
        var originalLogin = Game.LoginServer; var loginNet = new LoginNet(); GetTree().Root.AddChild(loginNet);
        loginNet.Login("offline-audit", "offline-audit");
        var project = new ConfigFile(); project.Load(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/project.godot"));
        foreach (string action in project.GetSectionKeys("input")) if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        typeof(Config).GetProperty(nameof(Config.PingEnabled))!.SetValue(null, false);
        var template = new PetReconnectWorld { Name = "World" };
        template.AddChild(new Node3D { Name = "Entities" });
        template.AddChild(new Camera3D { Name = "Camera", Current = true });
        template.AddChild(new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(400, 400) } });
        foreach (var child in template.GetChildren()) child.Owner = template;
        var worldScene = new PackedScene(); worldScene.Pack(template); template.Free();
        worldScene.TakeOverPath("res://scenes/World.tscn");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
        var checks = new List<string>();
        void Require(bool ok, string message) { if (!ok) throw new Exception("COMMAND_FUNCTION_AUDIT: " + message); checks.Add(message); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Until(Func<bool> condition, string message)
        {
            ulong deadline = Time.GetTicksMsec() + 60000;
            while (!condition() && Time.GetTicksMsec() < deadline) await Frames(1);
            Require(condition(), message);
        }
        object? Get(object owner, string field) => Native.Get<object>(owner, field);
        void Set(object owner, string field, object? value) => Native.Set(owner, field, value);
        async Task Send(NetworkStream stream, Packet packet)
        {
            byte[] body = packet.GetBytes(); byte[] frame = new byte[body.Length + 6]; frame[0] = 0xaa; frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)body.Length); body.CopyTo(frame, 4); frame[^2] = 0x55; frame[^1] = 0xaa;
            await stream.WriteAsync(frame);
        }
        async Task<byte[]> ReadFrame(NetworkStream stream, CancellationToken token)
        {
            byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
            byte[] body = new byte[length + 2]; await stream.ReadExactlyAsync(body, token);
            return body;
        }
        async Task Read(NetworkStream stream, GameOpcodes expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int i = 0; i < 100; i++) if ((await ReadFrame(stream, timeout.Token))[0] == (byte)expected) return;
            throw new Exception("Missing native request " + expected);
        }
        void Record(Packet p) { p.WriteInt(0); p.WriteShort(0); p.WriteShort(0); p.WriteByte(0); p.WriteShort(0); p.WriteInt(0); p.WriteInt(0); }
        Packet MyInfoPacket()
        {
            var p = new Packet(GameOpcodes.GS_MYINFO); p.WriteInt(42); p.WriteSByteString("CommandTester");
            p.WriteShort(100); p.WriteShort(100); p.WriteShort(0); p.WriteByte((byte)nation); p.WriteByte((byte)(nation == 1 ? 1 : 11));
            p.WriteShort((short)(nation * 100 + 6)); p.WriteByte(0); p.WriteInt(0);
            for (int i = 0; i < 4; i++) p.WriteByte(0);
            p.WriteByte(60); p.WriteShort(0); p.WriteLong(1000000); p.WriteLong(10000); p.WriteInt(0); p.WriteInt(0);
            p.WriteShort(0); p.WriteByte(0); p.WriteLong(0); p.WriteUShort(0); p.WriteInt(0); p.WriteLong(0);
            p.WriteShort(1000); p.WriteShort(1000); p.WriteShort(1000); p.WriteShort(1000); p.WriteInt(17000); p.WriteInt(0);
            for (int i = 0; i < 5; i++) { p.WriteByte(100); p.WriteByte(0); }
            p.WriteShort(100); p.WriteShort(100); for (int i = 0; i < 6; i++) p.WriteByte(0);
            p.WriteInt(100000); p.WriteByte(1); p.WriteByte(255); p.WriteByte(255); for (int i = 0; i < 9; i++) p.WriteByte(0);
            for (int i = 0; i < InventoryConstants.MyInfoWireTotal; i++) Record(p);
            return p;
        }

        var results = new List<Dictionary<string, object?>>();
        var placements = new List<object>();
        var sent = new ConcurrentQueue<string>();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Game.Session.PreGame.LoginResultEvent += (ok, _) => { if (ok) Game.Requests.PreGame.SelectCharacter("CommandTester"); };
        Game.Session.PreGame.EnterWorldEvent += _ => GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        GetTree().CurrentScene = null;
        using var stop = new CancellationTokenSource();
        try
        {
            net.BeginGameLogin("127.0.0.1", port, "offline-audit", "offline-audit");
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30)); var stream = peer.GetStream();
            await Read(stream, GameOpcodes.GS_VERSION_CHECK);
            var version = new Packet(GameOpcodes.GS_VERSION_CHECK); version.WriteByte(1); version.WriteUShort(1298); await Send(stream, version);
            await Read(stream, GameOpcodes.GS_LOGIN); var login = new Packet(GameOpcodes.GS_LOGIN); login.WriteByte((byte)nation); await Send(stream, login);
            await Read(stream, GameOpcodes.GS_SELECT_CHARACTER); var select = new Packet(GameOpcodes.GS_SELECT_CHARACTER); select.WriteByte(1); select.WriteShort(32700); select.WriteShort(100); select.WriteShort(100); select.WriteShort(0); await Send(stream, select);
            await Read(stream, GameOpcodes.GS_GAMESTART); await Send(stream, MyInfoPacket());
            await Until(() => GetTree().CurrentScene is WorldRoot w && (bool)Get(w, "WorldReady")!, "Native World scene builds every facet");
            var world = (WorldRoot)GetTree().CurrentScene;
            await Until(() => Get(world, "loadingLayer") == null, "Native loading screen completes");
            // Every request the client sends from here on is recorded by opcode.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var body = await ReadFrame(stream, stop.Token); var op = (GameOpcodes)body[0];
                        // Genie requests carry their sub-command (1 = info request; 1 spirit, 2 load, 3 save, 4 start, 5 stop).
                        sent.Enqueue(op == GameOpcodes.GS_GENIE_SYSTEM && body.Length > 4 ? $"{op}:{body[1]}:{body[2]}" : op.ToString());
                    }
                }
                catch (Exception) { }
            });
            await Frames(10);
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            var background = new HashSet<string>(); while (sent.TryDequeue(out var idle)) background.Add(idle);

            DetailCall(world, "CommandList.Toggle"); await Frames(6);
            var window = (HudWindow)Get(world, "CommandList.Window")!;
            var frame = window.GetChildren().OfType<ClassicCommandListFrame>().Single();
            await Until(() => frame.Bound, "The game's command window is the Classic window, bound to the client's list");
            Require(window.Visible && window.IsVisibleInTree(), "The command list key action opens the Classic window");
            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(1190, 10), GlobalPosition = new Vector2(1190, 10) }, true);
            await Frames();
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + prefix + "-command-world.png");

            var chat = Get(world, "Chat")!;
            var classicChat = Descendants(world).OfType<ChatWindow>().Single();
            var classicInput = (LineEdit)typeof(ChatWindow).GetField("_input", flags)!.GetValue(classicChat)!;
            // 3277df5: the combat log is a TabbedLog (All/Status/Item/System); a fresh one per observation keeps only its lines.
            TabbedLog Log() => (TabbedLog)Get(world, "Log.log")!;
            void ClearLog() => Set(world, "Log.log", new TabbedLog(Native.Get<int>(Native.StaticOwner("LibreKO.Features.CombatLog.CombatLogFeature"), "MaxLines")));
            object exitIdle = Get(world, "GameMenu.battleTimer")!;
            string[] stateFields = { "Avatar.Running", "EntityStatesSystem.sitRequestPending", "Achievement.Shown", "Merchant.MenuShown", "SeekParty.SeekShown", "NationChannels.NationShown", "MarketPrice.marketPriceShown",
                "Config.TradeRequestsBlocked", "Session.UserOption.PartyRequestsBlocked", "GameMenu.exitArmed", "VipWarehouse.shown", "Genie.shown", "GameMenu.shown" };
            Dictionary<string, string> State() => stateFields.Select(f => { string path = f; return (Field: f, Owner: PreviewFixtures.Target(world, ref path), Path: path); })
                .Where(s => Native.Has(s.Owner, s.Path)).ToDictionary(s => s.Field, s => Native.Get<object>(s.Owner, s.Path)?.ToString() ?? "");
            List<Node> Dialogs() => Descendants(GetTree().Root).Where(n => n is KnightOnlineUiClassic.Windows.MessageBox || n is AcceptDialog).Where(n => n is not CanvasItem c || c.IsVisibleInTree()).ToList();
            string Clean(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"\[/?[a-z]+(=[^\]]*)?\]", "");

            async Task<Dictionary<string, object?>> Observe(string label, Func<Task> act)
            {
                ClearLog(); while (sent.TryDequeue(out _)) { }
                var before = State(); var dialogs = Dialogs();
                string? error = null;
                try { await act(); } catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                await Frames(8); await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout); await Frames(2);
                var packets = new List<string>(); while (sent.TryDequeue(out var op)) packets.Add(op);
                var after = State();
                var changed = after.Where(kv => before[kv.Key] != kv.Value).Select(kv => kv.Key + "=" + kv.Value).ToList();
                var opened = Dialogs().Except(dialogs).ToList();
                string dialogText = string.Join(" | ", opened.SelectMany(d => Descendants(d).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0)).Distinct());
                foreach (var dialog in opened) if (dialog is MessageBox box) box.QueueFree(); else if (dialog is AcceptDialog accept) accept.Hide();
                return new Dictionary<string, object?>
                {
                    ["run"] = label, ["requests"] = packets, ["messages"] = Log().Lines(LogTab.All).Select(Clean).ToList(), ["state"] = changed,
                    ["chat"] = classicInput.Text, ["dialog"] = dialogText, ["error"] = error,
                };
            }

            var model = frame.Model;
            var prefills = new List<(string Id, string Text)>();
            for (int g = 0; g < model.Count; g++)
            {
                frame.GroupList.Select(g); frame.GroupList.EmitSignal(ItemList.SignalName.ItemSelected, (long)g); await Frames(2);
                var entries = frame.CommandList!;
                for (int i = 0; i < model[g].Rows.Count; i++)
                {
                    var row = model[g].Rows[i];
                    Require(entries.GetItemText(i) == row.Text, model[g].Title + ": row " + i + " is " + row.Text);
                    // Client actions (the Genie rows) are run below, with the server's Genie replies.
                    if (row.Entry == null) continue;
                    if (row.Id == "Exit") Set(world, "GameMenu.battleTimer", 0.0);
                    var result = await Observe("list", () => { KnightOnlineUiClassic.Native.NativeCommandList.Activate(world, i); return Task.CompletedTask; });
                    result["group"] = model[g].Title; result["id"] = row.Id; result["text"] = row.Text; result["available"] = row.Available;
                    bool prefill = row.Entry!.GetType().GetProperty("Prefill")?.GetValue(row.Entry) is true;
                    result["prefill"] = prefill;
                    string chatText = classicInput.Text;
                    if (prefill && row.Available)
                    {
                        Require(chatText == "/" + row.Text + " ", row.Text + ": the Classic chat input shows the prefill '" + chatText + "'");
                        prefills.Add((row.Id, row.Text));
                    }
                    if (!row.Available) Require(((List<string>)result["requests"]!).Count == 0 && chatText.Length == 0 && ((List<string>)result["state"]!).Count == 0, row.Text + ": an unavailable command does nothing");
                    // Leave the world as it was for the next row.
                    classicInput.Clear(); classicInput.ReleaseFocus();
                    Native.TryCall(chat, "Close", out _);
                    if (row.Id == "Exit") { Set(world, "GameMenu.exitArmed", Enum.ToObject(Native.ClientType("LibreKO.Features.Windows.GameMenuFeature+ExitKind")!, 0)); Set(world, "GameMenu.battleTimer", exitIdle); }
                    foreach (var (field, close) in new[] { ("Achievement.Shown", "CloseAchievements"), ("Merchant.MenuShown", "CloseMerchantMenu"), ("SeekParty.SeekShown", "SeekParty.ToggleSeek"), ("NationChannels.NationShown", "CloseNationChat") })
                        if (Get(world, field) is true) DetailCall(world, close);
                    results.Add(result);
                    await Frames(2);
                }
            }
            // Prefilled commands completed with arguments and submitted from the Classic chat input.
            var arguments = new Dictionary<string, string>
            {
                ["Pm"] = "Tester hello", ["AddFriend"] = "Tester", ["BanFromClan"] = "Tester", ["AppointVice"] = "Tester", ["ClanChat"] = "hello clan",
                ["ClanNotice"] = "Siege tonight", ["RoyalOrder"] = "Gather at the castle", ["Prize"] = "Tester 100", ["ExperiencePoint"] = "10",
                ["DropRate"] = "10", ["Rain"] = "1", ["Clear"] = "1",
            };
            foreach (var (id, word) in prefills)
            {
                string line = "/" + word + " " + (arguments.TryGetValue(id, out var args) ? args : "Tester");
                var result = await Observe("chat", () =>
                {
                    classicInput.GrabFocus(); classicInput.Text = line;
                    classicInput.EmitSignal(LineEdit.SignalName.TextSubmitted, line);
                    return Task.CompletedTask;
                });
                result["id"] = id; result["text"] = line;
                results.Add(result);
                classicInput.Clear(); Native.TryCall(chat, "Close", out _);
                foreach (var (field, close) in new[] { ("Achievement.Shown", "CloseAchievements"), ("Merchant.MenuShown", "CloseMerchantMenu"), ("SeekParty.SeekShown", "SeekParty.ToggleSeek"), ("NationChannels.NationShown", "CloseNationChat") })
                    if (Get(world, field) is true) DetailCall(world, close);
            }
            // Context commands with a name typed after them (the client's own argument forms).
            foreach (string line in new[] { "Party Tester", "Individual_Battle Tester" })
            {
                var result = await Observe("chat", () => { classicInput.GrabFocus(); classicInput.Text = "/" + line; classicInput.EmitSignal(LineEdit.SignalName.TextSubmitted, "/" + line); return Task.CompletedTask; });
                result["id"] = line.Split(' ')[0]; result["text"] = "/" + line;
                results.Add(result);
                classicInput.Clear(); Native.TryCall(chat, "Close", out _);
            }

            // Genie rows, Escape and the open position.
            async Task GenieAndPlacement()
            {
                var list = model.ToList();
                int genie = list.FindIndex(g => g.Title == "Genie");
                Require(genie >= 0 && list[genie].Rows.Select(r => r.Id).SequenceEqual(KnightOnlineUiClassic.Native.NativeGenieCommands.Ids),
                    "Genie group: Genie Window, Start Genie, Stop Genie, Use Spirit of Genie");
                Require(genie == list.FindIndex(g => g.Title == "Knights") + 1 && list[genie + 1].Title == "King",
                    "Genie takes the original Guardian Monster place, between Knights and King");
                var entries = frame.CommandList!;
                var ink = KnightOnlineUiClassic.Layout.ClassicCommandListLayout.CommandInk;
                var grey = KnightOnlineUiClassic.Layout.ClassicCommandListLayout.UnavailableInk;
                async Task ShowGenie() { frame.GroupList.Select(genie); frame.GroupList.EmitSignal(ItemList.SignalName.ItemSelected, (long)genie); await Frames(3); }
                int RowOf(string id) => frame.ShownRows.ToList().FindIndex(r => r.Id == id);
                var shownEntries = (System.Collections.IList)Get(world, "CommandList.cmdShown")!;
                void Expect(string state, bool start, bool stop, bool spirit, string? startTip = null, string? stopTip = null)
                {
                    Require(shownEntries.Count == 0, state + ": the Genie rows are not client entries; the client's handler ignores them");
                    foreach (var (id, enabled, tip) in new[] { ("GenieWindow", true, (string?)null), ("GenieStart", start, startTip), ("GenieStop", stop, stopTip), ("GenieSpirit", spirit, null) })
                    {
                        int i = RowOf(id);
                        string shownTip = entries.GetItemTooltip(i);
                        Require(i >= 0 && entries.IsItemDisabled(i) == !enabled && entries.GetItemCustomFgColor(i) == (enabled ? ink : grey) && (tip == null || shownTip == tip),
                            $"{state}: {entries.GetItemText(i)} {(enabled ? "enabled" : "grey and disabled")}, tooltip '{shownTip}'");
                    }
                }
                async Task<Dictionary<string, object?>> Run(string id, string state)
                {
                    int i = RowOf(id);
                    var result = await Observe("genie", () => { KnightOnlineUiClassic.Native.NativeCommandList.Activate(world, i); return Task.CompletedTask; });
                    result["group"] = "Genie"; result["id"] = id; result["text"] = entries.GetItemText(i); result["available"] = !entries.IsItemDisabled(i); result["case"] = state;
                    results.Add(result);
                    return result;
                }
                List<string> Requests(Dictionary<string, object?> r) => (List<string>)r["requests"]!;
                List<string> Messages(Dictionary<string, object?> r) => (List<string>)r["messages"]!;
                async Task Server(byte command, params ushort[] values)
                {
                    var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM); p.WriteByte(1); p.WriteByte(command);
                    foreach (ushort v in values) p.WriteUShort(v);
                    await Send(stream, p);
                }
                var net = Net.I;
                Label runLine = (Label)Get(world, "Genie.RunLabel")!;
                void Key(Key key)
                {
                    GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }, true);
                    GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }, true);
                }
                string startHint = LibreKO.Domain.ItemData.Text(31008, "Start Knight Genie"), stopHint = LibreKO.Domain.ItemData.Text(31009, "Disable Knight Genie");

                await ShowGenie();
                Expect("before the server reports the Genie time", true, false, false, startTip: startHint);
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + prefix + "-command-genie.png");

                // Genie Window: the client's ToggleGenie (the Genie key / quick panel button); it asks for the options and time.
                var opened = await Run("GenieWindow", "open the Genie window");
                Require(Get(world, "Genie.shown") is true && Requests(opened).Contains("GS_GENIE_SYSTEM:1:2"), "Genie Window opens the client's Genie window, which asks for its options and time");
                await Frames(4);
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + prefix + "-command-genie-window.png");
                var again = await Run("GenieWindow", "Genie window already open");
                Require(Get(world, "Genie.shown") is true && Requests(again).Count == 0, "Genie Window leaves an open Genie window open");

                // Escape: the Genie window first (the client's order), then the command list, then the game menu as before.
                int ours = PreviewFixtures.EscapeEntries(world).Count(e => PreviewFixtures.EscapeEntryFrom(e.Entry, typeof(Plugin).Assembly) && e.IsOpen());
                Require(ours >= 1, "the command list has its Escape entry while it is open (" + ours + " open plugin entries)");
                Key(Godot.Key.Escape); await Frames(4);
                Require(Get(world, "Genie.shown") is false && window.Visible, "Escape closes the Genie window first");
                Key(Godot.Key.Escape); await Frames(4);
                Require(!window.Visible && Get(world, "GameMenu.shown") is false, "Escape closes the command list (and does not open the game menu)");
                Key(Godot.Key.Escape); await Frames(4);
                Require(Get(world, "GameMenu.shown") is true, "with nothing open, Escape opens the game menu as before");
                Native.Call(world, "GameMenu.Toggle", false); await Frames(2);
                DetailCall(world, "CommandList.Toggle"); await Frames(4);
                Require(window.Visible, "the client's command list toggle (J) reopens the window");
                await ShowGenie();

                // The server reports no time: Start is refused with the client's own line.
                await Server(6, 0);
                await Until(() => runLine.Text.StartsWith("No time"), "the server reports no Genie time");
                await Frames(2);
                Expect("no Genie time", false, false, false, startTip: runLine.Text);
                var refused = await Run("GenieStart", "no Genie time");
                Require(Requests(refused).Count == 0, "Start Genie without time sends nothing");

                // Time left: Start runs the quick panel's Start (the client's ToggleGenieFromSub: options saved, start requested).
                await Server(6, 120);
                await Until(() => (int)Get(world, "Genie.Points")! == 120, "the server reports 120 Genie minutes");
                await Frames(2);
                Expect("120 minutes left, stopped", true, false, false, startTip: startHint, stopTip: runLine.Text);
                var started = await Run("GenieStart", "start");
                Require(Requests(started).Contains("GS_GENIE_SYSTEM:1:4"), "Start Genie sends the client's start request");
                Expect("waiting for the server", false, false, false, startTip: runLine.Text, stopTip: runLine.Text);
                await Server(4, 1, 120);
                await Until(() => Game.Session.Genie.GenieRunning, "the server starts the Genie");
                await Frames(3);
                Expect("Genie running", false, true, false, startTip: runLine.Text, stopTip: stopHint);
                // Reopened, the window opens beside the Genie quick panel that the Genie shows while it has time.
                DetailCall(world, "CommandList.Toggle"); await Frames(2); DetailCall(world, "CommandList.Toggle"); await Frames(6); await ShowGenie();
                var quick = (Control)Get(world, "GenieSub.Sub")!;
                Require(quick.IsVisibleInTree() && !quick.GetGlobalRect().Intersects(window.GetGlobalRect()), $"the command list opens beside the Genie quick panel {quick.GetGlobalRect()} / {window.GetGlobalRect()}");
                GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(5, 5), GlobalPosition = new Vector2(5, 5) }, true); await Frames(2);
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + prefix + "-command-genie-running.png");
                placements.Add(new { state = "genie-running", window = window.GetGlobalRect().ToString(), quickPanel = quick.GetGlobalRect().ToString() });

                // Stop within the client's 10 s toggle cooldown: the client's own message, no request.
                var early = await Run("GenieStop", "stop during the toggle cooldown");
                Require(Requests(early).Count == 0 && Messages(early).Any(m => m.Contains("Knight Genie will be available after")), "Stop Genie right after Start: the client's cooldown message " + string.Join(" | ", Messages(early)));
                await ToSignal(GetTree().CreateTimer(10.2), SceneTreeTimer.SignalName.Timeout);
                var stopped = await Run("GenieStop", "stop");
                Require(Requests(stopped).Contains("GS_GENIE_SYSTEM:1:5") && !Game.Session.Genie.GenieRunning, "Stop Genie sends the client's stop request and stops the Genie");
                Expect("stopped again", true, false, false, stopTip: runLine.Text);

                // Spirit of Genie: enabled only with one in the bag; it presses the Genie window's own button.
                var spiritLocked = await Run("GenieSpirit", "no Spirit of Genie");
                Require(Requests(spiritLocked).Count == 0, "Use Spirit of Genie without one in the bag sends nothing");
                var bag = (LibreKO.Domain.Inventory)Get(world, "Session.Self.Inventory")!;
                int slot = bag.FirstFreeGridSlot();
                bag[slot] = new LibreKO.Domain.ItemSlot { ItemId = 810378000, Count = 1, Durability = 1 };
                await Frames(3);
                Expect("a Spirit of Genie in the bag", true, false, true);
                var spirit = await Run("GenieSpirit", "use Spirit of Genie");
                Require(Requests(spirit).Contains("GS_GENIE_SYSTEM:1:1"), "Use Spirit of Genie sends the client's Spirit of Genie request");
                bag[slot] = default;
                await Frames(2);

                // Open position: the right edge, vertically centred, beside the Classic HUD, at several screen sizes.
                Set(world, "Genie.Points", 0); DetailCall(world, "GenieSub.RefreshSub"); await Frames(2);
                foreach (var size in new[] { new Vector2I(1200, 850), new Vector2I(1024, 768), new Vector2I(1280, 720), new Vector2I(1600, 900), new Vector2I(1920, 1080) })
                {
                    if (window.Visible) DetailCall(world, "CommandList.Toggle");
                    // The OS may scale the window (display scaling); ask again for the size the viewport should have.
                    GetWindow().Size = size; await Frames(6);
                    var got = GetViewport().GetVisibleRect().Size;
                    if (got != (Vector2)size) { GetWindow().Size = new Vector2I(Mathf.RoundToInt(size.X * size.X / got.X), Mathf.RoundToInt(size.Y * size.Y / got.Y)); await Frames(6); }
                    await Frames(4);
                    DetailCall(world, "CommandList.Toggle"); await Frames(10);
                    var screen = GetViewport().GetVisibleRect().Size;
                    var rect = window.GetGlobalRect();
                    var hud = KnightOnlineUiClassic.Native.NativeCommandList.HudRects(world);
                    var spot = KnightOnlineUiClassic.Native.NativeCommandList.OpenSpot(window, world);
                    string tag = $"{(int)screen.X}x{(int)screen.Y}";
                    Require(rect.Position == spot, $"{tag}: the window opens at its Classic spot {rect} (expected {spot})");
                    // Without HUD in the way the spot is the right edge (12 px HUD margin), vertically centred.
                    var free = KnightOnlineUiClassic.Layout.ClassicCommandListLayout.OpenSpot(screen, rect.Size, new List<Rect2>());
                    Require(Math.Abs(free.X + rect.Size.X - (screen.X - KnightOnlineUiClassic.Layout.ClassicCommandListLayout.Edge)) <= 1 && Math.Abs(free.Y + rect.Size.Y / 2 - screen.Y / 2) <= 1,
                        $"{tag}: the free spot is the right edge, vertically centred {free}");
                    Require(rect.Position == free || hud.Any(r => r.Intersects(new Rect2(free, rect.Size))), $"{tag}: the window leaves the free spot only for HUD in the way ({rect.Position} / {free})");
                    var covered = hud.Where(r => r.Intersects(rect)).ToList();
                    Require(covered.Count == 0, $"{tag}: covers none of the {hud.Count} HUD parts" + (covered.Count > 0 ? " " + string.Join(";", covered) : ""));
                    GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(5, 5), GlobalPosition = new Vector2(5, 5) }, true); await Frames(2);
                    string file = prefix + "-command-open-" + tag + ".png";
                    GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
                    placements.Add(new { state = "open", screen = screen.ToString(), requested = size.ToString(), window = rect.ToString(), free = free.ToString(), centred = rect.Position == free, hud = hud.Select(r => r.ToString()).ToList(), file });
                }

                // A dragged window reopens where the player left it; resetting window positions brings it back.
                GetWindow().Size = new Vector2I(1200, 850); await Frames(10);
                var restored = GetViewport().GetVisibleRect().Size;
                if (restored != new Vector2(1200, 850)) { GetWindow().Size = new Vector2I(Mathf.RoundToInt(1200 * 1200 / restored.X), Mathf.RoundToInt(850 * 850 / restored.Y)); await Frames(10); }
                if (window.Visible) DetailCall(world, "CommandList.Toggle");
                DetailCall(world, "CommandList.Toggle"); await Frames(8);
                var home = window.Position;
                var grip = Descendants(frame).OfType<Control>().Single(c => c.Name == "command_drag");
                var from = grip.GetGlobalRect().GetCenter(); var to = from + new Vector2(-420, 40);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from }, true); await Frames(1);
                GetViewport().PushInput(new InputEventMouseMotion { Position = from + new Vector2(-200, 20), GlobalPosition = from + new Vector2(-200, 20), ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
                GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to }, true); await Frames(3);
                var dragged = window.Position;
                Require(dragged == home + new Vector2(-420, 40), $"drag: the caption band moves the window ({home} -> {dragged})");
                DetailCall(world, "CommandList.Toggle"); await Frames(2); DetailCall(world, "CommandList.Toggle"); await Frames(8);
                Require(window.Position == dragged, "drag: the window reopens where the player left it " + window.Position);
                string windowsCfg = (string)typeof(Config).GetMethod("WindowsPath", flags)!.Invoke(null, null)!;
                bool hadCfg = File.Exists(windowsCfg);
                HudLayout.ResetAll(); await Frames(6);
                if (!hadCfg && File.Exists(windowsCfg)) File.Delete(windowsCfg);
                Require(window.Position == home, "reset window positions: the window returns to its right-middle spot " + window.Position);
                placements.Add(new { state = "drag-reset", home = home.ToString(), dragged = dragged.ToString(), reset = window.Position.ToString() });
            }

            await GenieAndPlacement();

            Require(results.All(r => r["error"] == null), "No command throws: " + string.Join(", ", results.Where(r => r["error"] != null).Select(r => r["text"] + " " + r["error"])));
            Require(window.IsInsideTree(), "The command window survives every command");
            File.WriteAllText(output + "/" + prefix + "-command-functions.json", JsonSerializer.Serialize(new { checks, background, results, placements, transport = "isolated loopback fixture; no target, party or clan; no premium" },
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            GD.Print("COMMAND_FUNCTION_AUDIT_OK " + prefix + " " + checks.Count + " checks / " + results.Count + " runs");
        }
        finally
        {
            stop.Cancel();
            net.Reconnect.AutoReconnect = false; net.Disconnect(expected: true);
            if (GetTree().CurrentScene is WorldRoot world) { GetTree().CurrentScene = null; world.QueueFree(); await Frames(); }
            GetTree().CurrentScene = this; net.Free(); loginNet.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
            typeof(Game).GetProperty(nameof(Game.LoginServer))!.SetValue(null, originalLogin);
        }
    }
}
