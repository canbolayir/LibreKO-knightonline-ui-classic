using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CapturePetWorldReconnectAudit(int nation, PluginInfo info)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/characters.pck", "content/armor.pck", "content/weapons.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false)) throw new Exception("Missing existing pack " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1200, 850);
        GetViewport().GuiEmbedSubwindows = true;
        new Plugin().Initialize((PluginContext)Create(typeof(PluginContext), info, new List<PluginInfo> { info }, PluginHost.Ui, PluginHost.Game));
        var original = Net.I; var net = new Net(); GetTree().Root.AddChild(net);
        var originalLogin = LoginNet.I; var loginNet = new LoginNet(); GetTree().Root.AddChild(loginNet);
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
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var checks = new List<string>();
        void Require(bool ok, string message) { if (!ok) throw new Exception("PET_WORLD_RECONNECT_AUDIT: " + message); checks.Add(message); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Until(Func<bool> condition, string message)
        {
            ulong deadline = Time.GetTicksMsec() + 60000;
            while (!condition() && Time.GetTicksMsec() < deadline) await Frames(1);
            Require(condition(), message);
        }
        T Field<T>(World world, string field) => (T)typeof(World).GetField(field, flags)!.GetValue(world)!;
        async Task Send(NetworkStream stream, Packet packet)
        {
            byte[] body = packet.GetBytes(); byte[] frame = new byte[body.Length + 6]; frame[0] = 0xaa; frame[1] = 0x55;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)body.Length); body.CopyTo(frame, 4); frame[^2] = 0x55; frame[^1] = 0xaa;
            await stream.WriteAsync(frame);
        }
        async Task<byte[]> Read(NetworkStream stream, GameOpcodes expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (int i = 0; i < 100; i++)
            {
                byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                byte[] body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                if (body[0] == (byte)expected) { Require(header[0] == 0xaa && header[1] == 0x55 && body[^2] == 0x55 && body[^1] == 0xaa, "Native framed request " + expected); return body; }
            }
            throw new Exception("Missing native request " + expected);
        }
        void Record(Packet p, int item = 0, short count = 0, short durability = 0, byte flag = 0, int uid = 0)
        {
            p.WriteInt(item); p.WriteShort(durability); p.WriteShort(count); p.WriteByte(flag); p.WriteShort(0); p.WriteInt(uid);
            if (uid != 0) { p.WriteString("Kauly"); p.WriteByte(101); p.WriteByte(12); p.WriteUShort(4375); p.WriteShort(7240); p.WriteByte(0); }
            p.WriteInt(0);
        }
        Packet MyInfoPacket(short count)
        {
            var p = new Packet(GameOpcodes.GS_MYINFO); p.WriteInt(42); p.WriteSByteString("FamiliarTester");
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
                if (slot == InventoryConstants.Pet) Record(p, 610001000, 1, 1, 0, 77);
                else if (slot == Inventory.GridStart) Record(p, 389070000, count, 456, 2);
                else Record(p);
            }
            return p;
        }
        Packet Summon()
        {
            var p = new Packet(GameOpcodes.GS_PET); p.WriteByte(1); p.WriteByte(5); p.WriteByte(1); p.WriteShort(1);
            p.WriteInt(77); p.WriteString("Kauly"); p.WriteByte(101); p.WriteByte(12); p.WriteUShort(4375);
            p.WriteShort(168); p.WriteShort(131); p.WriteShort(190); p.WriteShort(170); p.WriteShort(7240); p.WriteShort(51); p.WriteShort(110);
            for (int i = 0; i < 6; i++) p.WriteByte(20);
            for (int i = 0; i < PetSheet.InventorySize; i++) Record(p);
            return p;
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        net.LoginResultEvent += (ok, _) => { if (ok) net.SelectChar("FamiliarTester"); };
        net.EnterWorldEvent += _ => GetTree().ChangeSceneToFile("res://scenes/World.tscn");
        // Keep the audit runner alive when the native reconnect path replaces CurrentScene.
        GetTree().CurrentScene = null;
        try
        {
            net.BeginGameLogin("127.0.0.1", port, "offline-audit", "offline-audit");
            World? previous = null;
            for (int session = 0; session < 2; session++)
            {
                using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(30)); var stream = peer.GetStream();
                await Read(stream, GameOpcodes.GS_VERSION_CHECK);
                var version = new Packet(GameOpcodes.GS_VERSION_CHECK); version.WriteByte(1); version.WriteUShort(1298); await Send(stream, version);
                await Read(stream, GameOpcodes.GS_LOGIN); var login = new Packet(GameOpcodes.GS_LOGIN); login.WriteByte((byte)nation); await Send(stream, login);
                await Read(stream, GameOpcodes.GS_SELECT_CHARACTER); var select = new Packet(GameOpcodes.GS_SELECT_CHARACTER); select.WriteByte(1); select.WriteShort(32700); select.WriteShort(100); select.WriteShort(100); select.WriteShort(0); await Send(stream, select);
                await Read(stream, GameOpcodes.GS_GAMESTART); await Send(stream, MyInfoPacket((short)(session == 0 ? 2 : 5)));
                await Until(() => GetTree().CurrentScene is World w && !ReferenceEquals(w, previous) && Field<bool>(w, "_worldReady"), "Native World scene builds every facet / session " + session);
                var world = (World)GetTree().CurrentScene;
                Require(previous == null || !GodotObject.IsInstanceValid(previous), "Previous World is disposed before reconstruction");
                var inventory = (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(world)!;
                Require(inventory[Inventory.GridStart].Count == (session == 0 ? 2 : 5) && inventory[Inventory.GridStart].Durability == 456 && inventory[Inventory.GridStart].Flag == 2,
                    "Fresh MYINFO owns reconstructed inventory count, durability and flags / session " + session);
                Require(!Field<bool>(world, "_moveInFlight") && !Field<bool>(world, "_petHatchInFlight"), "No pending inventory or trainer lock survives scene creation");
                Require(net.Pet == null && net.PetItems.ContainsKey(77), "MYINFO restores owned familiar metadata without a stale summoned state");
                await Send(stream, Summon()); await Until(() => net.Pet != null, "Summoned familiar reply accepted after world initialization");
                DetailCall(world, "OpenPet"); await Frames();
                var panel = Field<HudWindow>(world, "_petPanel");
                Require(Descendants(panel).OfType<ClassicPetPanel>().Any() && Field<Label>(world, "_petNameLbl").Text == "Kauly", "Full native bridge presents Classic Familiar controls");
                await Until(() => Field<LoadingScreen?>(world, "_loadingLayer") == null, "Native loading screen completes before visual review");
                GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(1190, 10), GlobalPosition = new Vector2(1190, 10) }, true);
                await Frames();
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-world-" + session + ".png");
                if (session == 0)
                {
                    Require(net.SendPetHatch(13016, 600001000, 4, "Kauly"), "Pre-disconnect trainer request enters native pending state");
                    previous = world; peer.Close();
                    await Until(() => net.ReconnectState != Net.ReconnectPhase.Idle, "Native transport loss starts automatic reconnect");
                    Require(net.Pet == null && net.PetItems.Count == 0, "Disconnect clears the old familiar session before relogin");
                }
            }
            Require(net.ReconnectState == Net.ReconnectPhase.Idle && net.ReconnectAttempt == 1, "Native relogin, selection, MYINFO and scene reconstruction complete on one reconnect");
        }
        finally
        {
            net.AutoReconnect = false; net.Disconnect(expected: true);
            if (GetTree().CurrentScene is World world) { GetTree().CurrentScene = null; world.QueueFree(); await Frames(); }
            GetTree().CurrentScene = this; net.Free(); loginNet.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
            typeof(LoginNet).GetProperty("I")!.SetValue(null, originalLogin);
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-world-reconnect.json", JsonSerializer.Serialize(new { checks, fullWorldReconstruction = true, transport = "isolated loopback fixture", zone = "unbaked placeholder; no live server or account" }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PET_WORLD_RECONNECT_AUDIT: " + checks.Count + " checks");
    }
}
