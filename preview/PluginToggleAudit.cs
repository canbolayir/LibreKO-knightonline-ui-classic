using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Native;
using System.Buffers.Binary;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

// Settings > Plugins switches UI themes at runtime: the host shuts the plugin down, removes its UI registrations and
// game handlers and rebuilds the World in place; the assembly stays loaded and a re-enable creates a new plugin
// instance. This audit drives that host path on a full native World (loopback fixture session): native start,
// enable, disable, re-enable, disable. Disabled states must carry no Classic node, skin, registration or handler and
// show the native HUD; every enabled state must be the same Classic UI with the same registrations and handlers.
public partial class Preview
{
    private sealed partial class AuditLog : Godot.Logger
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = new();

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify,
            int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            lock (_gate) _lines.Add($"{(errorType == 1 ? "WARNING" : "ERROR")}: {(rationale.Length > 0 ? rationale : code)} ({file}:{line})");
        }

        public override void _LogMessage(string message, bool error)
        {
            lock (_gate) _lines.Add((error ? "ERROR: " : "") + message.TrimEnd());
        }

        public string[] Take()
        {
            lock (_gate) { var copy = _lines.ToArray(); _lines.Clear(); return copy; }
        }
    }

    private async Task CapturePluginToggleAudit(int nation, PluginInfo info, Plugin boot, bool hudButtons = false)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck", "content/armor.pck", "content/weapons.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing pack " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1280, 800);
        GetViewport().GuiEmbedSubwindows = true;
        var log = new AuditLog(); OS.AddLogger(log);

        var checks = new List<string>(); var failures = new List<string>();
        var states = new Dictionary<string, object>();
        void Require(bool ok, string message)
        {
            if (ok) { checks.Add(message); return; }
            failures.Add(message); GD.PushError("PLUGIN_TOGGLE_AUDIT: " + message);
        }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Until(Func<bool> condition, string message, int seconds = 60)
        {
            ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
            while (!condition() && Time.GetTicksMsec() < deadline) await Frames(1);
            if (!condition()) throw new Exception("PLUGIN_TOGGLE_AUDIT: " + message);
            checks.Add(message);
        }
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object? Read(object owner, string member)
        {
            var type = owner as Type ?? owner.GetType();
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.GetField(member, Any | BindingFlags.DeclaredOnly) is { } f) return f.GetValue(owner is Type ? null : owner);
                if (t.GetProperty(member, Any | BindingFlags.DeclaredOnly) is { } p) return p.GetValue(owner is Type ? null : owner);
            }
            throw new Exception("Missing member " + type.Name + "." + member);
        }
        var plugin = typeof(Plugin).Assembly;
        bool Ours(Node node) => node.GetType().Assembly == plugin;

        // ---- The boot instance of the preview harness is shut down: the toggles start from a clean plugin state.
        boot.Shutdown();
        Require(!Plugin.Active && NativeHooks.Count == 0, "Shutting down the preview's boot instance leaves no plugin hook and no kit");

        // ---- The host: this plugin is the installed UI theme, its assembly is the one already loaded.
        PluginHost.PersistChoices = false;
        var all = (List<PluginInfo>)Read(typeof(PluginHost), "_all")!;
        var assemblies = (Dictionary<string, Assembly>)Read(typeof(PluginHost), "_assemblies")!;
        string dll = System.IO.Path.GetFullPath(System.IO.Path.Combine(info.Directory, info.Manifest!.Assembly));
        all.Clear(); all.Add(info); assemblies[dll] = plugin;
        typeof(PluginInfo).GetProperty("State")!.SetValue(info, PluginState.Disabled);
        typeof(PluginInfo).GetProperty("Enabled")!.SetValue(info, false);
        IPlugin? Instance() => (IPlugin?)typeof(PluginInfo).GetProperty("Instance", Any)!.GetValue(info);

        // ---- Loopback session and the native World scene (the same fixture as pet-world-reconnect-audit).
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
        T Field<T>(WorldRoot world, string field) => (T)Native.Get<object>(world, field)!;
        async Task Send(NetworkStream stream, Packet packet)
        {
            byte[] body = packet.GetBytes(); byte[] frame = new byte[body.Length + 6]; frame[0] = 0xaa; frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)body.Length); body.CopyTo(frame, 4); frame[^2] = 0x55; frame[^1] = 0xaa;
            await stream.WriteAsync(frame);
        }
        async Task Read2(NetworkStream stream, GameOpcodes expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int i = 0; i < 100; i++)
            {
                byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                byte[] body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                if (body[0] == (byte)expected) return;
            }
            throw new Exception("Missing native request " + expected);
        }
        void Record(Packet p, int item = 0, short count = 0, short durability = 0)
        {
            p.WriteInt(item); p.WriteShort(durability); p.WriteShort(count); p.WriteByte(0); p.WriteShort(0); p.WriteInt(0); p.WriteInt(0);
        }
        Packet MyInfo()
        {
            var p = new Packet(GameOpcodes.GS_MYINFO); p.WriteInt(42); p.WriteSByteString("ToggleTester");
            p.WriteShort(100); p.WriteShort(100); p.WriteShort(0); p.WriteByte((byte)nation); p.WriteByte((byte)(nation == 1 ? 1 : 11));
            p.WriteShort((short)(nation * 100 + 6)); p.WriteByte(0); p.WriteInt(0);
            for (int i = 0; i < 4; i++) p.WriteByte(0);
            p.WriteByte(60); p.WriteShort(0); p.WriteLong(1000000); p.WriteLong(10000); p.WriteInt(0); p.WriteInt(0);
            p.WriteShort(0); p.WriteByte(0); p.WriteLong(0); p.WriteUShort(0); p.WriteInt(0); p.WriteLong(0);
            p.WriteShort(1000); p.WriteShort(1000); p.WriteShort(1000); p.WriteShort(1000); p.WriteInt(17000); p.WriteInt(0);
            for (int i = 0; i < 5; i++) { p.WriteByte(100); p.WriteByte(0); }
            p.WriteShort(100); p.WriteShort(100); for (int i = 0; i < 6; i++) p.WriteByte(0);
            p.WriteInt(100000); p.WriteByte(1); p.WriteByte(255); p.WriteByte(255); for (int i = 0; i < 9; i++) p.WriteByte(0);
            for (int i = 0; i < InventoryConstants.MyInfoWireTotal; i++)
            {
                int slot = InventoryConstants.MyInfoWireSlot(i);
                if (slot == Inventory.GridStart) Record(p, 389070000, 5, 456);
                else Record(p);
            }
            return p;
        }

        // ---- State probes ----------------------------------------------------------------------------------
        int Sum(object? collection) => collection switch
        {
            IDictionary map => map.Values.Cast<object?>().Sum(v => v is ICollection c ? c.Count : 1),
            ICollection list => list.Count,
            _ => 0,
        };
        Dictionary<string, int> Registries() => new()
        {
            ["NativeWindows.preparers"] = Sum(Read(typeof(NativeWindows), "_preparers")),
            ["NativeWindows.patterns"] = Sum(Read(typeof(NativeWindows), "_patterns")),
            ["NativeHud.hidden"] = Sum(Read(typeof(NativeHud), "_hidden")),
            ["NativeHud.extenders"] = Sum(Read(typeof(NativeHud), "_extenders")),
            ["NativeOverlays.skins"] = Sum(Read(typeof(NativeOverlays), "_skins")),
            ["NativeHud.whisperStyler"] = Read(typeof(NativeHud), "WhisperStyler") == null ? 0 : 1,
            ["host.ui.owned"] = OwnedBy(Read(PluginHost.Ui, "_store")!, info.Id),
        };
        int OwnedBy(object store, string owner)
        {
            int count = 0;
            foreach (var field in store.GetType().GetFields(Any))
            {
                var value = field.GetValue(store);
                if (value is IDictionary map) foreach (var entry in map.Values) count += entry == null ? 0 : OwnedIn(entry, owner);
                else if (value != null) count += OwnedIn(value, owner);
            }
            return count;
        }
        int OwnedIn(object value, string owner)
        {
            var type = value.GetType();
            if (type.Name.StartsWith("Owned", StringComparison.Ordinal))
                return ((IEnumerable)Read(value, "_entries")!).Cast<object>().Count(e => Equals(e.GetType().GetField("Item1")!.GetValue(e), owner));
            if (type.Name == "WindowRule")
                return type.GetFields(Any).Select(f => f.GetValue(value)).Where(v => v != null).Sum(v => OwnedIn(v!, owner));
            return 0;
        }
        Dictionary<string, int> Handlers() => new(NativeHooks.Counts)
        {
            ["(total hooks)"] = NativeHooks.Count,
            ["(net delegates)"] = NativeHooks.Attached(Net.I),
            ["(game delegates)"] = NativeHooks.Attached(PluginHost.Game),
            ["(tree watchers)"] = NativeHooks.TreeWatchers(),
        };
        // Generated names carry instance ids (Godot's @Type@id names and the plugin's per-instance gauge names).
        string Normal(string path) => Regex.Replace(Regex.Replace(path, @"@(\w+)@\d+", "@$1@"), @"\d{6,}", "#");
        List<string> Signature(Node root)
        {
            var rows = new List<string>();
            foreach (var node in Descendants(root).Where(Ours))
            {
                string where = Normal(root.GetPathTo(node).ToString()) + "#" + node.GetIndex();
                if (node is Control c) rows.Add($"{where} {node.GetType().Name} {c.GetGlobalRect()} {c.IsVisibleInTree()}");
                else rows.Add($"{where} {node.GetType().Name}");
            }
            return rows;
        }
        IEnumerable<Node> TreeNodes() => Descendants(GetTree().Root).Where(n => n != this && !IsAncestorOf(n));
        HashSet<string> MetaKeys() => TreeNodes().SelectMany(n => n.GetMetaList().Select(k => k.ToString())).ToHashSet();
        bool Shown(Node? node)
        {
            for (var n = node; n != null && n is not WorldRoot; n = n.GetParent())
                if (n is CanvasItem { Visible: false } || n is CanvasLayer { Visible: false }) return false;
            return node != null && GodotObject.IsInstanceValid(node);
        }
        Dictionary<string, bool> HudState(WorldRoot world) => new()
        {
            ["status bars"] = Shown(Field<Control?>(world, "StatusHud.Root")),
            ["hotbar"] = Shown(Field<CanvasLayer>(world, "Hotbar.layer").GetChildren().OfType<Control>().FirstOrDefault()),
            ["exp bar"] = Field<CanvasLayer?>(world, "ExpBar.layer") is { Visible: true },
            ["launcher"] = Field<CanvasLayer>(world, "HudLauncher.layer").Visible,
            ["mini map"] = Shown(Field<MiniMap>(world, "Map.MiniMap")),
            ["plugin hud parts"] = ((IDictionary)Native.Get<object>(world, "pluginHud")!).Count > 0,
            // The top-right icons whose windows the Classic status bar's G, E and R open.
            ["genie icon"] = Shown(Field<Control?>(world, "TopIcons.Icon:Genie")),
            ["events icon"] = Shown(Field<Control?>(world, "Events.iconButton")),
            ["daily rank icon"] = Shown(Field<Control?>(world, "DailyRankWindow.drIcon")),
        };
        Image? Shot(string state)
        {
            var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(output, prefix + "-" + state + ".png"));
            return image;
        }
        // Pixels compared: the Classic controls (top-level plugin controls, with their transforms), except the inventory
        // portrait, which is a live 3D render of the character (idle animation). Native top icons blink and stay out.
        Rect2 Screen(Control c) { var t = c.GetGlobalTransformWithCanvas(); return new Rect2(t.Origin, c.Size * t.Scale); }
        bool[] Classic(WorldRoot w, Image image, out int covered, out int live)
        {
            int width = image.GetWidth(), height = image.GetHeight();
            var mask = new bool[width * height];
            void Mark(Rect2 r, bool value)
            {
                for (int y = Math.Max(0, (int)r.Position.Y); y < Math.Min(height, (int)Math.Ceiling(r.End.Y)); y++)
                    for (int x = Math.Max(0, (int)r.Position.X); x < Math.Min(width, (int)Math.Ceiling(r.End.X)); x++) mask[y * width + x] = value;
            }
            foreach (var c in Descendants(w).OfType<Control>().Where(c => Ours(c) && c.IsVisibleInTree() && !Ours(c.GetParent())))
                Mark(Screen(c), true);
            var portraits = Descendants(w).OfType<KnightOnlineUiClassic.Windows.InventoryPortrait>().Where(c => c.IsVisibleInTree()).ToList();
            foreach (var portrait in portraits) Mark(Screen(portrait).Grow(1), false);
            covered = mask.Count(m => m); live = portraits.Count;
            return mask;
        }
        int Compare(Image a, Image b, bool[] mask)
        {
            if (a.GetSize() != b.GetSize() || mask.Length != a.GetWidth() * a.GetHeight()) return -1;
            int width = a.GetWidth(), differing = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                Color p = a.GetPixel(i % width, i / width), q = b.GetPixel(i % width, i / width);
                if (Math.Abs(p.R - q.R) > 2 / 255f || Math.Abs(p.G - q.G) > 2 / 255f || Math.Abs(p.B - q.B) > 2 / 255f) differing++;
            }
            return differing;
        }

        WorldRoot? world = null;
        async Task<WorldRoot> Rebuilt(WorldRoot? previous, string why)
        {
            await Until(() => GetTree().CurrentScene is WorldRoot w && !ReferenceEquals(w, previous) && Field<bool>(w, "WorldReady"), "World scene is rebuilt in place: " + why);
            var next = (WorldRoot)GetTree().CurrentScene;
            await Until(() => Field<LoadingScreen?>(next, "loadingLayer") == null, "World loading completes: " + why);
            Require(previous == null || !GodotObject.IsInstanceValid(previous), "The previous World is freed: " + why);
            await Frames(10);
            return next;
        }
        async Task OpenWindows()
        {
            PluginHost.Game.Windows.Open("inventory");
            PluginHost.Game.Windows.Open("character_info");
            await Frames(12);
        }

        Dictionary<string, int>? enabledRegistries = null, enabledHandlers = null;
        List<string>? enabledSignature = null; Image? enabledShot = null;
        HashSet<string>? nativeMetas = null; Dictionary<string, bool>? nativeHud = null;
        IPlugin? firstInstance = null;

        async Task Disabled(string state)
        {
            world = (WorldRoot)GetTree().CurrentScene;
            await OpenWindows();
            var registries = Registries(); var handlers = Handlers(); var hud = HudState(world);
            var classicNodes = TreeNodes().Where(Ours).Select(n => n.GetPath().ToString()).ToList();
            var metas = MetaKeys();
            Require(!Plugin.Active && Instance() == null && PluginHost.ActiveTheme == null, $"{state}: no plugin instance, kit or active theme");
            Require(classicNodes.Count == 0, $"{state}: no Classic node anywhere in the scene tree ({string.Join(", ", classicNodes.Take(5))})");
            Require(registries.Values.All(v => v == 0), $"{state}: every static registry and host UI registration is empty ({Json(registries)})");
            Require(handlers.Values.All(v => v == 0), $"{state}: no plugin hook, network delegate, game API delegate or scene-tree watcher ({Json(handlers)})");
            nativeHud ??= hud;
            Require(hud["status bars"] && hud["hotbar"] && hud["launcher"] && hud["mini map"] && !hud["plugin hud parts"]
                && hud["genie icon"] && hud["events icon"] && hud["daily rank icon"] && Json(hud) == Json(nativeHud),
                $"{state}: the native HUD is shown as on the native start ({Json(hud)})");
            var open = Descendants(world).OfType<HudWindow>().Where(Shown).Select(w => w.Id).ToList();
            Require(open.Count >= 2, $"{state}: the native inventory and character windows open ({string.Join(", ", open)})");
            if (nativeMetas == null) nativeMetas = metas;
            else
            {
                var extra = metas.Except(nativeMetas).ToList();
                Require(extra.Count == 0, $"{state}: native nodes carry no meta the plugin sets (new keys: {string.Join(", ", extra.Take(8))})");
            }
            Require(!metas.Any(k => k.Contains("classic", StringComparison.OrdinalIgnoreCase)), $"{state}: no node carries a Classic skin marker");
            await Frames(4);
            Shot(state);
            states[state] = new { registries, handlers, nativeHud = hud, classicNodes = classicNodes.Count, metaKeys = metas.Count, windows = open, log = Settled(state) };
        }

        async Task Enabled(string state)
        {
            world = (WorldRoot)GetTree().CurrentScene;
            await OpenWindows();
            var instance = Instance();
            var registries = Registries(); var handlers = Handlers(); var hud = HudState(world);
            var signature = Signature(world);
            Require(Plugin.Active && instance != null && instance.GetType().Assembly == plugin && PluginHost.ActiveTheme == info,
                $"{state}: the theme runs a plugin instance from the loaded assembly");
            Require(signature.Count > 50 && hud["plugin hud parts"] && !hud["status bars"] && !hud["mini map"] && !hud["launcher"]
                && !hud["genie icon"] && !hud["events icon"] && !hud["daily rank icon"],
                $"{state}: the Classic HUD replaces the native HUD ({signature.Count} Classic nodes, {Json(hud)})");
            Require(handlers["(total hooks)"] > 0 && handlers["(tree watchers)"] > 0 && registries["NativeWindows.preparers"] > 0,
                $"{state}: the plugin registered its preparers, hooks and watchers");
            await Frames(4);
            var image = Shot(state)!;
            if (enabledSignature == null)
            {
                enabledRegistries = registries; enabledHandlers = handlers; enabledSignature = signature; enabledShot = image; firstInstance = instance;
            }
            else
            {
                Require(!ReferenceEquals(instance, firstInstance), $"{state}: re-enabling creates a new plugin instance");
                Require(Json(registries) == Json(enabledRegistries), $"{state}: registries match the first enable, no duplicate registration ({Json(registries)})");
                Require(Json(handlers) == Json(enabledHandlers), $"{state}: hooks and handlers match the first enable ({Json(handlers)} vs {Json(enabledHandlers)})");
                var missing = enabledSignature.Except(signature).Take(5).ToList(); var added = signature.Except(enabledSignature).Take(5).ToList();
                Require(signature.SequenceEqual(enabledSignature), $"{state}: the Classic node tree, bounds and visibility match the first enable (missing: {string.Join(" | ", missing)}; added: {string.Join(" | ", added)})");
                var mask = Classic(world, image, out int covered, out int live);
                int differing = Compare(enabledShot!, image, mask);
                Require(live > 0 && covered > 100000 && differing == 0,
                    $"{state}: the rendered Classic UI matches the first enable pixel for pixel ({differing} of {covered} Classic pixels differ, live portrait left out)");
                states[state + "-pixels"] = new { differing, covered };
            }
            states[state] = new { registries, handlers, nativeHud = hud, classicNodes = signature.Count, log = Settled(state) };
        }

        string[] Settled(string state)
        {
            var lines = log.Take();
            var problems = lines.Where(l => l.StartsWith("ERROR") && (l.Contains("KnightOnlineUiClassic") || l.Contains("knightonline-ui-classic"))
                || l.Contains("game handler(s) subscribed")).ToList();
            Require(problems.Count == 0, $"{state}: no plugin error while the World was rebuilt and used ({string.Join(" | ", problems.Take(3))})");
            return lines;
        }
        string Toggle(Action action)
        {
            log.Take();
            action();
            return string.Join("\n", log.Take());
        }
        void ToggleLog(string state, string lines)
        {
            var problems = lines.Split('\n').Where(l => l.Length > 0 && (l.StartsWith("ERROR") || l.Contains("game handler(s) subscribed")
                || l.Contains("not recorded") || l.Contains("KnightOnlineUiClassic") && l.StartsWith("WARNING"))).ToList();
            Require(problems.Count == 0, $"{state}: the switch logs no error and no leftover-handler warning ({string.Join(" | ", problems.Take(3))})");
            states[state + "-switch-log"] = lines.Split('\n');
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Game.Session.PreGame.LoginResultEvent += (ok, _) => { if (ok) Game.Requests.PreGame.SelectCharacter("ToggleTester"); };
        Game.Session.PreGame.EnterWorldEvent += _ => GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        GetTree().CurrentScene = null;
        RenderingServer.SetDefaultClearColor(new Color("34362b"));
        GetViewport().Disable3D = true;
        TcpClient? peer = null;
        try
        {
            net.BeginGameLogin("127.0.0.1", port, "offline-audit", "offline-audit");
            peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30)); var stream = peer.GetStream();
            await Read2(stream, GameOpcodes.GS_VERSION_CHECK);
            var version = new Packet(GameOpcodes.GS_VERSION_CHECK); version.WriteByte(1); version.WriteUShort(1298); await Send(stream, version);
            await Read2(stream, GameOpcodes.GS_LOGIN); var login = new Packet(GameOpcodes.GS_LOGIN); login.WriteByte((byte)nation); await Send(stream, login);
            await Read2(stream, GameOpcodes.GS_SELECT_CHARACTER); var select = new Packet(GameOpcodes.GS_SELECT_CHARACTER); select.WriteByte(1); select.WriteShort(32700); select.WriteShort(100); select.WriteShort(100); select.WriteShort(0); await Send(stream, select);
            await Read2(stream, GameOpcodes.GS_GAMESTART); await Send(stream, MyInfo());

            world = await Rebuilt(null, "native login");
            await Disabled("native");

            var lines = Toggle(() => PluginHost.SwitchTheme(info.Id));
            ToggleLog("enable", lines);
            world = await Rebuilt(world, "enable");
            await Enabled("enabled");

            lines = Toggle(() => PluginHost.SwitchTheme(null));
            ToggleLog("disable", lines);
            world = await Rebuilt(world, "disable");
            await Disabled("disabled");

            lines = Toggle(() => PluginHost.SwitchTheme(info.Id));
            ToggleLog("re-enable", lines);
            world = await Rebuilt(world, "re-enable");
            await Enabled("re-enabled");

            lines = Toggle(() => PluginHost.SwitchTheme(null));
            ToggleLog("disable again", lines);
            world = await Rebuilt(world, "disable again");
            await Disabled("disabled-again");

            lines = Toggle(() => PluginHost.SwitchTheme(info.Id));
            ToggleLog("third enable", lines);
            world = await Rebuilt(world, "third enable");
            await Enabled("re-enabled-again");
            if (hudButtons) states["hud-buttons"] = await ProbeHudButtons(world, nation, output, Require, Shown);
        }
        finally
        {
            GetViewport().Disable3D = false;
            net.Reconnect.AutoReconnect = false; net.Disconnect(expected: true); peer?.Close();
            if (GetTree().CurrentScene is WorldRoot current) { GetTree().CurrentScene = null; current.QueueFree(); await Frames(); }
            GetTree().CurrentScene = this; net.Free(); loginNet.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
            typeof(Game).GetProperty(nameof(Game.LoginServer))!.SetValue(null, originalLogin);
            OS.RemoveLogger(log);
        }
        File.WriteAllText(System.IO.Path.Combine(output, prefix + "-plugin-toggle.json"), JsonSerializer.Serialize(new { checks, failures, states },
            new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count > 0) throw new Exception($"PLUGIN_TOGGLE_AUDIT: {failures.Count} failed check(s): {string.Join(" || ", failures)}");
        GD.Print("PLUGIN_TOGGLE_AUDIT: " + checks.Count + " checks");
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);
}
