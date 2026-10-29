using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using QuickSpec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout.GenieQuick;

public partial class Preview
{
    /// <summary>
    /// genie-audit: the Knight Genie in a full native World (all facets, the real Classic plugin) over an isolated
    /// loopback session. Part 1 is the Classic quick panel: the remaining time, minimise, restore, close and the
    /// reopening by the Genie window and by a Genie start, with rendered bounds. Part 2 is the Genie's own R Attack
    /// option against monsters the fixture spawns: selection, approach, following, the switch after a kill, the
    /// monster list, the Genie range, R Attack off, and R Attack between attack skills.
    /// Every request the client sends is recorded; the Genie relays its actions as GS_GENIE_SYSTEM 2/n.
    /// </summary>
    private async Task CaptureGenieAudit(int nation, PluginInfo info)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck", "content/armor.pck", "content/weapons.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing pack " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1200, 850);
        GetViewport().GuiEmbedSubwindows = true;
        NativeGenieQuick.Minimised = false; NativeGenieQuick.Closed = false;
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
        var checks = new List<string>();
        var findings = new List<object>();
        var bounds = new Dictionary<string, object>();
        void Require(bool ok, string message) { if (!ok) throw new Exception("GENIE_AUDIT: " + message); checks.Add(message); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
        async Task Until(Func<bool> condition, string message, double seconds = 30) => await UntilWith(condition, () => message, seconds);
        async Task UntilWith(Func<bool> condition, Func<string> message, double seconds = 30)
        {
            ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
            while (!condition() && Time.GetTicksMsec() < deadline) await Frames(1);
            Require(condition(), message());
        }
        object? Get(object owner, string field) => Native.Get<object>(owner, field);
        object? Call(WorldRoot w, string method, params object?[] args) => Native.Call(w, method, args);
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
            var p = new Packet(GameOpcodes.GS_MYINFO); p.WriteInt(42); p.WriteSByteString("GenieTester");
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

        var sent = new ConcurrentQueue<string>();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Game.Session.PreGame.LoginResultEvent += (ok, _) => { if (ok) Game.Requests.PreGame.SelectCharacter("GenieTester"); };
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
            await Until(() => GetTree().CurrentScene is WorldRoot w && (bool)Get(w, "WorldReady")!, "Native World scene builds every facet", 60);
            var world = (WorldRoot)GetTree().CurrentScene;
            await Until(() => Get(world, "loadingLayer") == null, "Native loading screen completes", 60);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var body = await ReadFrame(stream, stop.Token); var op = (GameOpcodes)body[0];
                        sent.Enqueue(op == GameOpcodes.GS_GENIE_SYSTEM && body.Length > 4 ? $"{op}:{body[1]}:{body[2]}" : op.ToString());
                    }
                }
                catch (Exception) { }
            });
            await Frames(10); await Seconds(1);
            // The OS may scale the window (display scaling); ask for the size that gives a 1200 x 850 viewport.
            var got = GetViewport().GetVisibleRect().Size;
            if (got != new Vector2(1200, 850)) { GetWindow().Size = new Vector2I(Mathf.RoundToInt(1200 * 1200 / got.X), Mathf.RoundToInt(850 * 850 / got.Y)); await Frames(10); }
            List<string> Drain() { var list = new List<string>(); while (sent.TryDequeue(out var op)) list.Add(op); return list; }
            Drain();
            async Task Server(byte command, params ushort[] values)
            {
                var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM); p.WriteByte(1); p.WriteByte(command);
                foreach (ushort v in values) p.WriteUShort(v);
                await Send(stream, p);
            }
            void Mouse(Vector2 at, bool? pressed = null)
            {
                if (pressed == null) GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
                else GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed.Value, Position = at, GlobalPosition = at }, true);
            }
            async Task Click(Control control)
            {
                var at = control.GetGlobalRect().GetCenter();
                Mouse(at); await Frames(1); Mouse(at, true); await Frames(1); Mouse(at, false); await Frames(3);
                Mouse(new Vector2(5, 5)); await Frames(2);
            }
            string R(Rect2 r) => FormattableString.Invariant($"{r.Position.X} {r.Position.Y} {r.Size.X} {r.Size.Y}");
            void Shot(string name, Rect2? crop = null)
            {
                var image = GetViewport().GetTexture().GetImage();
                if (crop is { } c)
                {
                    // The viewport texture can differ from the visible rect under display scaling.
                    var scale = (Vector2)image.GetSize() / GetViewport().GetVisibleRect().Size;
                    var area = new Rect2I((Vector2I)((c.Position - new Vector2(16, 16)) * scale).Floor(), (Vector2I)((c.Size + new Vector2(32, 32)) * scale).Ceil())
                        .Intersection(new Rect2I(Vector2I.Zero, image.GetSize()));
                    image = image.GetRegion(area);
                }
                image.SavePng(output + "/" + prefix + "-" + name + ".png");
            }

            // ---- Part 1: the quick panel ---------------------------------------------------------------------------
            var panel = (PanelContainer)Get(world, "GenieSub.Sub")!;
            await Server(6, 87);
            await Until(() => panel.IsVisibleInTree(), "the client shows the quick panel once the server reports Genie time");
            await Until(() => panel.HasMeta("classic_overlay") && panel.FindChild(ClassicGenieQuick.Minimise, true, false) != null, "the Classic quick panel skin applies to the client's _genieSub");
            await Frames(4);
            var head = (Control)panel.FindChild(NativeGenieQuick.Head, true, false)!;
            var minimise = (Button)panel.FindChild(ClassicGenieQuick.Minimise, true, false)!;
            var close = (Button)panel.FindChild(ClassicGenieQuick.Close, true, false)!;
            var toggle = (Button)panel.FindChild(NativeGenieQuick.Toggle, true, false)!;
            var settings = (Button)panel.FindChild(NativeGenieQuick.Settings, true, false)!;
            var gauge = (Control)panel.FindChild(NativeGenieQuick.Gauge, true, false)!;
            var points = (Label)panel.FindChild(NativeGenieQuick.Points, true, false)!;
            var title = (Label)panel.FindChild(NativeGenieQuick.Title, true, false)!;
            var rows = Enumerable.Range(0, 3).Select(i => (Control)panel.FindChild(NativeGenieQuick.Row + i, true, false)!).ToList();
            var frame = panel.GetNode<ClassicFrame>("classic_frame");
            var screen = new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size);
            float edge = KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Board.Padding;
            float top = KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Board.PaddingTop;
            void Layout(string state, bool minimised)
            {
                var box = panel.GetGlobalRect();
                var content = new Rect2(box.Position + new Vector2(edge, top), box.Size - new Vector2(edge * 2, top * 2));
                Require(screen.Encloses(box), state + ": the quick panel is inside the screen " + R(box));
                Require(frame.GetGlobalRect() == box, state + ": the plain frame covers the panel");
                Require(minimise.Size == QuickSpec.Caption && close.Size == QuickSpec.Caption, state + ": minimise and close are the 19 x 15 whisper buttons");
                Require(Math.Abs(close.GetGlobalRect().End.X - content.End.X) < .5f && Math.Abs(close.GetGlobalRect().Position.X - minimise.GetGlobalRect().End.X - QuickSpec.CaptionGap) < .5f,
                    state + $": close at the content edge, minimise {QuickSpec.CaptionGap} px before it " + R(minimise.GetGlobalRect()) + " " + R(close.GetGlobalRect()));
                Require(Math.Abs(minimise.GetGlobalRect().GetCenter().Y - title.GetGlobalRect().GetCenter().Y) <= 1, state + ": caption buttons centred on the title");
                foreach (var control in new Control[] { title, minimise, close, toggle, settings, gauge, points })
                    Require(content.Grow(.5f).Encloses(control.GetGlobalRect()), state + ": " + control.Name + " inside the padding " + R(control.GetGlobalRect()));
                Require(toggle.IsVisibleInTree() && settings.IsVisibleInTree() && points.IsVisibleInTree(), state + ": time, Start/Stop and settings stay visible");
                Require(toggle.Size == QuickSpec.Toggle && settings.Size == QuickSpec.Settings && Math.Abs(toggle.GetGlobalRect().Position.Y - settings.GetGlobalRect().Position.Y) < .5f,
                    state + ": Start/Stop and the square settings button share one height " + R(toggle.GetGlobalRect()) + " " + R(settings.GetGlobalRect()));
                Require(Math.Abs(gauge.GetGlobalRect().GetCenter().Y - toggle.GetGlobalRect().GetCenter().Y) <= 1, state + ": the gauge is centred on the time row");
                Require(gauge.GetGlobalRect().Position.Y >= head.GetGlobalRect().End.Y && !gauge.GetGlobalRect().Intersects(toggle.GetGlobalRect()), state + ": the gauge sits on the time row beside Start/Stop");
                Require(rows.All(r => r.IsVisibleInTree() == !minimised), state + (minimised ? ": the monster rows are folded away" : ": the three monster rows are shown"));
                if (!minimised)
                    Require(rows.All(r => Math.Abs(r.Size.Y - rows[0].Size.Y) < .5f) && Math.Abs(rows[2].GetGlobalRect().Position.Y - rows[1].GetGlobalRect().Position.Y - (rows[1].GetGlobalRect().Position.Y - rows[0].GetGlobalRect().Position.Y)) < .5f,
                        state + ": the monster rows are evenly spaced, the last one (no action button since 3277df5) included: " + string.Join(" / ", rows.Select(r => R(r.GetGlobalRect()))));
                var last = minimised ? toggle.GetParent<Control>().GetGlobalRect() : rows[^1].GetGlobalRect();
                Require(Math.Abs(box.End.Y - last.End.Y - top) <= 1, state + ": the frame fits its rows (bottom padding " + (box.End.Y - last.End.Y) + ")");
                bounds[state] = new { panel = R(box), caption = R(head.GetGlobalRect()), minimise = R(minimise.GetGlobalRect()), close = R(close.GetGlobalRect()),
                    gauge = R(gauge.GetGlobalRect()), time = points.Text, toggle = R(toggle.GetGlobalRect()), settings = R(settings.GetGlobalRect()),
                    rows = rows.Select(r => r.IsVisibleInTree() ? R(r.GetGlobalRect()) : "hidden").ToList() };
            }
            Require(points.Text == "87 min", "the remaining time names its unit: " + points.Text);
            Require(toggle.Text == "Start" && !NativeGenieQuick.Running, "stopped: the toggle offers Start");
            Layout("full", false);
            var full = panel.GetGlobalRect();
            Shot("genie-quick-full", full);
            Mouse(minimise.GetGlobalRect().GetCenter()); await Frames(3);
            Require(minimise.TooltipText == "Minimise", "the minimise button names its action");
            Shot("genie-quick-hover-minimise", full);
            Mouse(new Vector2(5, 5)); await Frames(2);

            await Click(minimise);
            Require(NativeGenieQuick.Minimised && minimise.TooltipText == "Restore", "minimise folds the panel; the button becomes restore");
            Layout("minimised", true);
            var small = panel.GetGlobalRect();
            Require(small.Position == full.Position && small.Size.X == full.Size.X && small.Size.Y < full.Size.Y, $"minimised: same place and width, shorter ({R(full)} -> {R(small)})");
            Shot("genie-quick-minimised", full);
            // The client keeps refreshing the panel; the fold stays.
            await Server(6, 86); await Until(() => points.Text == "86 min", "the time follows the server while minimised"); await Frames(3);
            Layout("minimised-refreshed", true);

            await Click(minimise);
            Require(!NativeGenieQuick.Minimised, "restore brings the full panel back");
            Layout("restored", false);
            Require(panel.GetGlobalRect() == full, "restored: the full panel at its size " + R(panel.GetGlobalRect()));

            await Click(close);
            Require(NativeGenieQuick.Closed && !panel.Visible, "close hides the quick panel");
            await Server(6, 85); await Frames(6);
            Require(!panel.Visible, "closed: the client's refresh does not bring the panel back");
            Shot("genie-quick-closed", full);
            Call(world, "Genie.Toggle"); await Frames(6);
            Require(panel.IsVisibleInTree() && !NativeGenieQuick.Closed, "opening the Genie window (Genie key, command list, settings) brings the panel back");
            Call(world, "Genie.Close"); await Frames(2);

            // Close it again, then start the Genie: a start shows the panel with the running state.
            await Click(close);
            Require(!panel.Visible, "closed again");
            Drain();
            Call(world, "Genie.ToggleRun");
            var startRequests = new List<string>();
            await UntilWith(() => { startRequests.AddRange(Drain()); return startRequests.Contains("GS_GENIE_SYSTEM:1:4"); },
                () => "Start asks the server to start the Genie: " + string.Join(" ", startRequests) + " pending=" + Get(world, "Genie.RequestPending") + " dead=" + Get(world, "Avatar.Dead"));
            await Server(4, 1, 120);
            await Until(() => Game.Session.Genie.GenieRunning, "the server starts the Genie");
            await Frames(6);
            Require(panel.IsVisibleInTree() && toggle.Text == "Stop" && points.Text == "120 min", "a Genie start brings the panel back with Stop and the time");
            Layout("running", false);
            Shot("genie-quick-running", panel.GetGlobalRect());
            await Click(minimise);
            Layout("running-minimised", true);
            Shot("genie-quick-running-minimised", full);
            await Click(minimise);
            Shot("genie-world-running");

            // ---- Part 2: the Genie's R Attack -----------------------------------------------------------------------
            // R Attack is the Genie's basic attack: with Attack Mode on it selects a monster from the list (all monsters
            // when the list is empty) within its range of the start point, walks into reach and runs the client's auto
            // attack (the R key's attack), relayed as GS_GENIE_SYSTEM 2/3. Attack skills, when set, are cast in between.
            var modes = (System.Collections.IDictionary)Get(world, "Genie.modes")!;
            // The client replaces its GenieSetup wholesale when options arrive; read it again for every edit.
            GenieSetup Setup() => (GenieSetup)Get(world, "Genie.Genie")!;
            Array.Clear(Setup().Attack);
            CheckButton Mode(GenieMode mode) => (CheckButton)modes[mode]!;
            var mobIndex = (System.Collections.IDictionary?)Get(world, "EntityLooksSystem.MobIndex");
            int model = mobIndex?.Keys.Cast<object>().Select(Convert.ToInt32).FirstOrDefault() ?? 1;
            int nextMob = 30001;
            var ents = (EntityRegistry)Get(world, "EntitySystem.Ents")!;
            var monsters = (HashSet<string>)Get(world, "Genie.Monsters")!;
            Vector2 Self() => new((float)Get(world, "Avatar.MapX")!, (float)Get(world, "Avatar.MapZ")!);
            Packet NpcIn(int id, short npcId, Vector2 at)
            {
                float y = (float)Get(world, "Avatar.MapY")!;
                var p = new Packet(GameOpcodes.GS_NPC_INOUT); p.WriteByte(1); p.WriteInt(id); p.WriteShort(npcId); p.WriteByte(1); p.WriteShort((short)model);
                p.WriteInt(0); p.WriteByte(0); p.WriteInt(0); p.WriteShort(100); p.WriteInt(0); p.WriteInt(0); p.WriteByte(0); p.WriteByte(10);
                p.WriteShort((short)Math.Round(at.X * 10)); p.WriteShort((short)Math.Round(at.Y * 10)); p.WriteShort((short)Math.Round(y * 10));
                p.WriteInt(0); p.WriteByte(0); p.WriteShort(0); p.WriteShort(0); p.WriteByte(0); p.WriteByte(0);
                return p;
            }
            async Task<int> Spawn(float distance, short npcId = 101, float side = 0)
            {
                int id = nextMob++;
                await Send(stream, NpcIn(id, npcId, Self() + new Vector2(distance, side)));
                await Until(() => ents.ContainsKey(id), "the fixture monster " + id + " spawns");
                return id;
            }
            async Task MoveMob(int id, Vector2 to)
            {
                float y = (float)Get(world, "Avatar.MapY")!;
                var p = new Packet(GameOpcodes.GS_NPC_MOVE); p.WriteByte(1); p.WriteInt(id);
                p.WriteShort((short)Math.Round(to.X * 10)); p.WriteShort((short)Math.Round(to.Y * 10)); p.WriteShort((short)Math.Round(y * 10)); p.WriteUShort(20);
                await Send(stream, p);
            }
            async Task Kill(int id) { var p = new Packet(GameOpcodes.GS_DEAD); p.WriteInt(id); p.WriteInt(42); await Send(stream, p); await Frames(2); }
            async Task Despawn(int id)
            {
                var p = new Packet(GameOpcodes.GS_NPC_INOUT); p.WriteByte(2); p.WriteInt(id); await Send(stream, p);
                await Until(() => !ents.ContainsKey(id), "the fixture monster " + id + " leaves");
            }
            async Task<List<string>> Watch(double seconds) { Drain(); await Seconds(seconds); return Drain(); }
            int Count(List<string> list, string op) => list.Count(o => o == op);
            const string RelayAttack = "GS_GENIE_SYSTEM:2:3", RelayMove = "GS_GENIE_SYSTEM:2:1", RelayMagic = "GS_GENIE_SYSTEM:2:4";
            int Selected() => (int)Get(world, "Selection.SelectedId")!;
            bool Auto() => Get(world, "BasicAttackSystem.AutoAttack") is true;
            int AutoTarget() => (int)Get(world, "BasicAttackSystem.AutoTargetId")!;
            void Finding(string scenario, List<string> requests, bool ok, string note)
            {
                findings.Add(new { scenario, ok, note, attacks = Count(requests, RelayAttack), moves = Count(requests, RelayMove), magic = Count(requests, RelayMagic),
                    plainAttacks = Count(requests, "GS_ATTACK"), autoAttack = Auto(), autoTarget = AutoTarget(), selected = Selected(),
                    distance = Selected() >= 0 && ents.ContainsKey(Selected()) ? Distance(Selected()) : -1, basicReach = Selected() >= 0 && ents.ContainsKey(Selected()) ? BasicReach(Selected()) : -1,
                    requests = requests.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count()) });
            }
            object Ent(int id) => ents[id]!;
            object? EntField(int id, string name) => Ent(id).GetType().GetField(name)?.GetValue(Ent(id)) ?? Ent(id).GetType().GetProperty(name)?.GetValue(Ent(id));
            float BasicReach(int id) => (float)Native.Call(world, "BasicAttackSystem.BasicAttackRange", Ent(id))!;
            float Distance(int id)
            {
                var a = ((Node3D)EntField(id, "Body")!).GlobalPosition; var b = ((Node3D)Get(world, "Avatar.Body")!).GlobalPosition;
                return new Vector2(a.X - b.X, a.Z - b.Z).Length();
            }

            Mode(GenieMode.Attack).ButtonPressed = true; Mode(GenieMode.BasicAttack).ButtonPressed = true; Mode(GenieMode.Combo).ButtonPressed = false; Mode(GenieMode.LeaderTarget).ButtonPressed = false;
            monsters.Clear(); DetailCall(world, "Genie.RefreshMonsters");

            // 1. A monster in reach: selected and attacked.
            int near = await Spawn(1.0f);
            var basic = await Watch(4);
            bool nearOk = Count(basic, RelayAttack) > 0 && Selected() == near && Auto() && AutoTarget() == near;
            Finding("R Attack, monster in reach", basic, nearOk, "select and swing");
            Require(nearOk, "R Attack: the Genie selects the monster in reach and keeps swinging (" + Count(basic, RelayAttack) + " relayed attacks in 4 s)");
            Shot("genie-world-r-attack");

            // 2. Dead target: the Genie switches to the next monster.
            int second = await Spawn(1.5f, 101, 1.0f);
            await Kill(near);
            var switched = await Watch(4);
            bool switchOk = Selected() == second && AutoTarget() == second && Count(switched, RelayAttack) > 0;
            Finding("R Attack, target dies, next monster", switched, switchOk, "switch to the living monster and swing");
            Require(switchOk, $"R Attack: after the target dies the Genie switches to the next monster and swings (selected {Selected()}, {Count(switched, RelayAttack)} attacks)");
            await Despawn(near); await Despawn(second);

            // 3. A distant monster: the Genie walks into reach, then swings.
            int far = await Spawn(8f);
            var approach = await Watch(6);
            bool farOk = Count(approach, RelayMove) > 0 && Count(approach, RelayAttack) > 0 && Distance(far) <= BasicReach(far) + .5f;
            Finding("R Attack, monster 8 m away", approach, farOk, "approach then swing");
            Require(farOk, $"R Attack: the Genie walks to a distant monster and swings ({Count(approach, RelayMove)} moves, {Count(approach, RelayAttack)} attacks)");

            // 4. The monster walks away: the Genie follows and keeps attacking.
            await MoveMob(far, Self() + new Vector2(5f, 3f));
            await Seconds(.5);
            var follow = await Watch(6);
            bool followOk = Count(follow, RelayMove) > 0 && Count(follow, RelayAttack) > 0 && Selected() == far;
            Finding("R Attack, monster moves away", follow, followOk, "follow and swing again");
            Require(followOk, $"R Attack: the Genie follows a monster that walks away and swings again ({Count(follow, RelayMove)} moves, {Count(follow, RelayAttack)} attacks)");
            await Despawn(far);

            // 5. The monster list: only listed monsters are attacked, even when another one is nearer.
            int unlisted = await Spawn(1.0f, 101);
            int listed = await Spawn(-3.0f, 102);
            string listedName = (string)EntField(listed, "Name")!, unlistedName = (string)EntField(unlisted, "Name")!;
            Require(listedName != unlistedName, "two monster kinds with different names: " + listedName + " / " + unlistedName);
            monsters.Add(listedName); DetailCall(world, "Genie.RefreshMonsters");
            var filtered = await Watch(5);
            bool listOk = Selected() == listed && AutoTarget() == listed && Count(filtered, RelayAttack) > 0;
            Finding("R Attack, monster list", filtered, listOk, "only the listed monster, though another is nearer");
            Require(listOk, $"R Attack: with a monster list the Genie attacks only listed monsters ({listedName}; selected {Selected()})");
            monsters.Clear(); DetailCall(world, "Genie.RefreshMonsters");
            await Despawn(unlisted); await Despawn(listed);

            // 6. Out of the Genie's range from its start point: ignored.
            var range = (SpinBox)Get(world, "Genie.range")!;
            Call(world, "BasicAttackSystem.StopAutoAttack");
            int outside = await Spawn((float)range.Value + 10f);
            var ignored = await Watch(3);
            bool rangeOk = Count(ignored, RelayAttack) == 0 && Selected() != outside;
            Finding("R Attack, monster outside the Genie range", ignored, rangeOk, "not chased");
            Require(rangeOk, "R Attack: a monster outside the Genie's range from its start point is left alone");
            await Despawn(outside);

            // 7. R Attack off, no skills: no swings.
            Mode(GenieMode.BasicAttack).ButtonPressed = false;
            int idle = await Spawn(1.0f);
            var none = await Watch(3);
            Finding("R Attack off, no skills", none, Count(none, RelayAttack) == 0, "no basic swings");
            Require(Count(none, RelayAttack) == 0, "R Attack off: the Genie does not swing");
            await Despawn(idle);

            // 8-10. R Attack with an attack skill in the slots: while no slotted skill can be cast (cooling down, no mana,
            // not castable with this gear) the Genie must close in and swing, then cast again once a skill is ready.
            // A melee skill reaches further than the basic attack (weapon + radii + 2 against weapon + half the radii);
            // a ranged spell much further. The fixture switches the character's class for the mage case.
            Mode(GenieMode.BasicAttack).ButtonPressed = true;
            // The fixture character has spent no skill points; it gets enough in every tree to own its class skills.
            var slots = Game.Session.Self.Mastery.ToArray(); Array.Fill(slots, (byte)80); Game.Session.Self.Mastery.Seed(slots);
            bool approachFixed = typeof(WorldRoot).Assembly.GetType("LibreKO.Domain.GenieApproach") != null;
            int ownClass = (int)Get(world, "Session.Self.Class")!;
            var vitals = Game.Session.Self.Vitals;
            async Task SkillCase(string label, int classCode, bool ranged, string blocker)
            {
                Native.Set(Game.Session, "Self.Class", classCode);
                var skill = LibreKO.Domain.SkillData.ForClass(classCode)
                    // Anger Explosion casts only in rage mode (3277df5, SkillUsable.AngerAllows); the fixture has no rage.
                    .Where(k => k.IsEnemy && k.Range > 0 && Game.Session.Self.SkillLearned(k) && LibreKO.Domain.SkillUsable.AngerAllows(k.Id, Game.Session.Pvp.AngerFull)
                        && (ranged ? LibreKO.Domain.SkillReach.Kind(k.Type1, k.Type2) != LibreKO.Domain.SkillReach.KindMelee && k.Range >= 10
                                   : LibreKO.Domain.SkillReach.Kind(k.Type1, k.Type2) == LibreKO.Domain.SkillReach.KindMelee))
                    .OrderBy(k => k.Range).FirstOrDefault();
                if (skill == null) { checks.Add($"{label}: class {classCode} has no learned {(ranged ? "ranged" : "melee")} attack skill; skipped"); Native.Set(Game.Session, "Self.Class", ownClass); return; }
                Array.Clear(Setup().Attack); Setup().Attack[0] = skill.Id; DetailCall(world, "Genie.RefreshSlots");
                Call(world, "BasicAttackSystem.StopAutoAttack");
                int probe = await Spawn(1.0f);
                float reach = BasicReach(probe);
                await Despawn(probe);
                Call(world, "BasicAttackSystem.StopAutoAttack");
                float distance = ranged ? Math.Min(skill.Range, 8f) : reach + 1.2f;
                int mob = await Spawn(distance);
                bool inSkill = (bool)Native.Call(world, "SkillCastSystem.InSkillRange", (object)mob, skill)!;
                Require(inSkill && distance > reach, $"{label}: the monster stands inside {skill.Name}'s reach and outside the basic reach ({distance:0.0} m, basic {reach:0.0})");
                int mana = vitals.Mp;
                if (blocker == "cooldown") Native.Call(Game.Session.Self, "StartCooldown", skill.Id, Time.GetTicksMsec() / 1000.0 + 600);
                else typeof(LibreKO.Domain.Vitals).GetProperty("Mp")!.SetValue(vitals, 0);
                var waiting = await Watch(5);
                bool waitOk = Count(waiting, RelayMove) > 0 && Count(waiting, RelayAttack) > 0 && Distance(mob) <= BasicReach(mob) + .5f;
                Finding($"{label}: {skill.Name} ({skill.Id}, range {skill.Range}) blocked by {blocker}, monster {distance:0.0} m away (basic reach {reach:0.0})",
                    waiting, waitOk, "close in and R-attack while no skill can be cast");
                string waitText = $"{label}: with {skill.Name} blocked by {blocker} the Genie closes in and R-attacks ({Count(waiting, RelayMove)} moves, {Count(waiting, RelayAttack)} attacks, {Distance(mob):0.0} m)";
                if (approachFixed) Require(waitOk, waitText);
                else checks.Add((waitOk ? "" : "KNOWN CORE DEFECT (no GenieApproach in this client): ") + waitText);
                Shot("genie-world-" + label + "-" + blocker);
                Native.Call(Game.Session.Self, "CancelCooldown", skill.Id);
                if (blocker != "cooldown") typeof(LibreKO.Domain.Vitals).GetProperty("Mp")!.SetValue(vitals, mana);
                var ready = await Watch(5);
                bool gear = (bool)Native.Call(world, "SkillCastSystem.CanCastWithGear", skill, false)!;
                bool readyOk = Count(ready, RelayAttack) + Count(ready, RelayMagic) > 0 && (!gear || Count(ready, RelayMagic) > 0);
                Finding($"{label}: {skill.Name} ready again (castable with this gear: {gear})", ready, readyOk, "the skill is cast again when castable, the swings go on otherwise");
                string readyText = $"{label}: once {skill.Name} is ready the Genie casts it (when the gear allows it) or keeps swinging ({Count(ready, RelayMagic)} magic, {Count(ready, RelayAttack)} attacks, castable {gear})";
                if (approachFixed) Require(readyOk, readyText);
                else checks.Add((readyOk ? "" : "KNOWN CORE DEFECT (no GenieApproach in this client): ") + readyText);
                Setup().Attack[0] = 0; DetailCall(world, "Genie.RefreshSlots");
                Call(world, "BasicAttackSystem.StopAutoAttack");
                await Despawn(mob);
                Native.Set(Game.Session, "Self.Class", ownClass);
            }
            await SkillCase("melee", ownClass, false, "cooldown");
            await SkillCase("mage", nation * 100 + 9, true, "cooldown");
            await SkillCase("mage", nation * 100 + 9, true, "no-mana");

            // Stop the Genie (after the client's toggle cooldown); the panel returns to Start.
            Mode(GenieMode.BasicAttack).ButtonPressed = true;
            Call(world, "Genie.ToggleRun");
            await Until(() => !Game.Session.Genie.GenieRunning, "the Genie stops");
            await Server(5, 1, 118); await Frames(4);
            Require(toggle.Text == "Start" && points.Text == "118 min", "stopped: Start and the time");

            File.WriteAllText(output + "/" + prefix + "-genie.json", JsonSerializer.Serialize(new { client = typeof(WorldRoot).Assembly.GetType("LibreKO.Domain.GenieApproach") != null ? "with the GenieApproach core fix" : "upstream without the GenieApproach core fix", checks, bounds, findings, transport = "isolated loopback fixture; the server replies are scripted" },
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            GD.Print("GENIE_AUDIT_OK " + prefix + " " + checks.Count + " checks");
        }
        catch (Exception e)
        {
            File.WriteAllText(output + "/" + prefix + "-genie.json", JsonSerializer.Serialize(new { failed = e.Message, checks, bounds, findings },
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            throw;
        }
        finally
        {
            stop.Cancel();
            NativeGenieQuick.Minimised = false; NativeGenieQuick.Closed = false;
            net.Reconnect.AutoReconnect = false; net.Disconnect(expected: true);
            if (GetTree().CurrentScene is WorldRoot world) { GetTree().CurrentScene = null; world.QueueFree(); await Frames(); }
            GetTree().CurrentScene = this; net.Free(); loginNet.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
            typeof(Game).GetProperty(nameof(Game.LoginServer))!.SetValue(null, originalLogin);
        }
    }
}
