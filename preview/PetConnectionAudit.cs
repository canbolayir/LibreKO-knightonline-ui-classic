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
    private async Task CapturePetConnectionAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        foreach (string pack in new[] { "knightonline.pck", "content/npcs.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/" + pack), false))
                throw new Exception("Missing existing familiar audit content: " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(800, 680);
        GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Size = new Vector2(800, 680), Color = new Color("252822"), MouseFilter = MouseFilterEnum.Ignore });
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var original = Net.I; var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ExtendWindow("pet", ClassicPetSkin.Extend);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation * 100 + 6, Race = nation == 1 ? 1 : 11,
            Name = "FamiliarTester", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new ItemSlot[InventoryConstants.InventoryTotal]);
        var world = PreviewFixtures.Prepared(new PetAuditWorld { ProcessMode = ProcessModeEnum.Disabled }); AddChild(world);
        var layer = (CanvasLayer)DetailCall(world, "BuildPetEquipmentResponseUiPreview", nation)!;
        layer.ProcessMode = ProcessModeEnum.Always;
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        await Frames();
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.Position = new Vector2(240, 70);
        var panel = window.GetChildren().OfType<ClassicPetPanel>().Single();
        var inventory = (Inventory)Game.Session.Self.Inventory;
        var connection = (GameConn)typeof(Net).GetField("conn", flags)!.GetValue(net)!;
        T Field<T>(object target, string name) => (T)Native.Get<object>(target, name)!;
        object? Pending(string name) => Native.Get<object>(PreviewFixtures.NetOwner(name), name);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool value, string text) { if (!value) throw new Exception("PET_CONNECTION_AUDIT: " + text); checks.Add(text); }
        int resets = 0; Game.Session.Pet.PetResetEvent += () => resets++;
        int item = ItemData.All().First(d => PetBag.Holds(d) && d.Kind == 172).Id;
        var record = new ItemSlot { ItemId = item, Count = 1, Durability = 321, Flag = 2 };
        var seedSheet = Game.Session.Pet.Pet!;
        void Seed()
        {
            seedSheet.Items[1] = default; Native.Set(Game.Session.Pet, "Pet", seedSheet);
            inventory.ApplySlotUpdate(Inventory.GridStart, record); Game.Session.Self.SetInventorySlot(Inventory.GridStart, record);
            DetailCall(world, "ShowPetSheet", seedSheet); DetailCall(world, "InventoryWindow.RefreshUI");
        }
        void Stage()
        {
            var cell = Descendants(panel).OfType<ItemSlotView>().Single(c => c.Name == "pet_item_1");
            cell._DropData(Vector2.Zero, new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart } });
            Require(Field<bool>(world, "InventoryWindow.moveInFlight") && Pending("PendingMove") != null,
                "Actual familiar drop creates matching World and Net pending operations");
            DetailCall(world, "InventoryWindow.Enqueue", ItemMoveDirection.InventoryToInventory, item, (byte)1, (byte)2,
                Inventory.GridStart + 1, Inventory.GridStart + 2, (ushort)0);
            Require(Field<System.Collections.ICollection>(world, "InventoryWindow.moveQueue").Count == 1, "A queued follow-up waits behind the familiar move");
            Game.Requests.Items.Remove(0, 0, item);
            Require(Game.Requests.Pet.Hatch(13016, 600001000, 4, "Kauly"), "Connected incubation can wait beside an inventory move");
        }
        void CheckReset(string state, bool acknowledged = false)
        {
            Require(Game.Session.Pet.Pet == null && Game.Session.Pet.Items.Count == 0 && Pending("PendingIncubation") == null,
                "Session reset discards familiar and incubation state / " + state);
            Require(Pending("PendingMove") == null && (int)Pending("PendingRemoveSlot")! == -1,
                "Session reset discards pending move and destruction records / " + state);
            Require(!Field<bool>(world, "InventoryWindow.moveInFlight") && Field<System.Collections.ICollection>(world, "InventoryWindow.moveQueue").Count == 0,
                "Session reset releases the actual World inventory queue / " + state);
            if (acknowledged)
                Require(inventory[Inventory.GridStart].IsEmpty,
                    "Session reset keeps the familiar move acknowledged before the connection loss / " + state);
            else
                Require(inventory[Inventory.GridStart].Equals(record),
                    "Session reset preserves exact unacknowledged item records / " + state);
        }
        async Task Capture(string state)
        {
            await Frames();
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), "Reset familiar window remains contained / " + state);
            string file = (nation == 1 ? "karus" : "human") + "-connection-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(60000);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        async Task<TcpClient> Connect(bool throughNet)
        {
            if (throughNet) net.ConnectToServer("127.0.0.1", port); else connection.Connect("127.0.0.1", port);
            var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            for (int i = 0; i < 120 && !net.Connected; i++) await Frames(1);
            Require(net.Connected, "Replacement familiar session uses the actual native transport"); return peer;
        }
        async Task DrainRequests(NetworkStream stream, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                var body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)) + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                Require(header[0] == 0xaa && header[1] == 0x55 && body[^2] == 0x55 && body[^1] == 0xaa,
                    "Pending familiar session requests retain native framing");
            }
        }
        async Task SendReply(NetworkStream stream)
        {
            byte[] frame = { 0xaa, 0x55, 3, 0, (byte)GameOpcodes.GS_ITEM_MOVE, 1, 1, 0x55, 0xaa };
            await stream.WriteAsync(frame, timeout.Token);
            for (int i = 0; i < 120 && connection.Incoming.IsEmpty; i++) await Frames(1);
            Require(!connection.Incoming.IsEmpty, "Native receiver decodes the framed move reply before main-thread dispatch");
        }
        try
        {
            using (var peer = await Connect(false))
            {
                Seed(); Stage(); await DrainRequests(peer.GetStream(), 3);
                await SendReply(peer.GetStream());
                net.Disconnect(expected: true); CheckReset("disconnect");
                Require(connection.Incoming.IsEmpty, "Explicit disconnect discards an already decoded move acknowledgement");
                await Capture("disconnect");
            }
            using (var peer = await Connect(false))
            {
                Seed(); Stage(); await DrainRequests(peer.GetStream(), 3);
                net.ReturnToCharSelect(); CheckReset("character-select");
                Require(net.Connected, "Returning to character selection retains its existing connection");
                await SendReply(peer.GetStream());
                typeof(Net).GetField("connectedFired", flags)!.SetValue(net, true);
                net._Process(0); CheckReset("late-character-select-reply");
            }
            net.Disconnect(expected: true);
            using (var peer = await Connect(false))
            {
                Seed(); Stage(); await DrainRequests(peer.GetStream(), 3);
                using var replacement = await Connect(true); CheckReset("new-connect");
                await Capture("new-connect");
                Seed(); Stage(); await DrainRequests(replacement.GetStream(), 3);
                await SendReply(replacement.GetStream());
                typeof(Net).GetField("connectedFired", flags)!.SetValue(net, true);
                net.Reconnect.AutoReconnect = false; replacement.Close();
                for (int i = 0; i < 120 && net.Connected; i++) await Frames(1);
                Require(!net.Connected, "Peer shutdown reaches the native connection-loss path");
                // The client dispatches frames that arrived before the loss, then resets the session.
                net._Process(0); CheckReset("peer-shutdown", acknowledged: true);
                Require(connection.Incoming.IsEmpty, "Peer shutdown dispatches replies decoded before the connection-loss tick");
                await Capture("peer-shutdown");
            }
            using (var peer = await Connect(true))
            {
                Seed();
                DetailCall(world, "InventoryWindow.EnqueuePetMove", ItemMoveDirection.InventoryToPet, item, (byte)0, (byte)1, Inventory.GridStart, 1);
                await DrainRequests(peer.GetStream(), 1); await SendReply(peer.GetStream());
                typeof(Net).GetField("connectedFired", flags)!.SetValue(net, true); net._Process(0); await Frames();
                Require(!Field<bool>(world, "InventoryWindow.moveInFlight") && inventory[Inventory.GridStart].IsEmpty
                    && Game.Session.Pet.Pet!.Items[1].Equals(record), "A new session accepts a fresh familiar equipment move without stale locks or item loss");
                Require(resets >= 5, "Native session entry, exit and loss publish their familiar resets");
                await Capture("fresh-session-equipped");
            }
            net.Disconnect(expected: true);
            Seed(); DetailCall(world, "InventoryWindow.EnqueuePetMove", ItemMoveDirection.InventoryToPet, item, (byte)0, (byte)1, Inventory.GridStart, 1);
            Require(!Field<bool>(world, "InventoryWindow.moveInFlight") && Pending("PendingMove") == null && inventory[Inventory.GridStart].Equals(record),
                "Disconnected familiar equipment input cannot leave an artificial inventory lock");
        }
        finally
        {
            connection.Close(); DetailCall(world, "DisposePetEquipmentUiPreview"); world.Free(); net.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-connection.json",
            JsonSerializer.Serialize(new { checks, screens, completeWorldReconnect = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PET_CONNECTION_AUDIT: " + checks.Count + " checks, " + screens.Count + " states");
    }
}
