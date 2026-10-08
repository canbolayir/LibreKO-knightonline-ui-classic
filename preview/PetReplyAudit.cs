using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

public partial class Preview
{
    private async Task AuditPetIncubationReplies(Action<bool, string> require)
    {
        var net = new Net();
        var connection = (KoConn)typeof(Net).GetField("_conn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(net)!;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(30000);
        try
        {
            connection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
            for (int i = 0; i < 120 && !net.Connected; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            require(net.Connected, "Incubation requests use an isolated native loopback connection");
            using var stream = receiver.GetStream();
            async Task ReadRequest(byte sub)
            {
                var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                var body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                require(header[0] == 0xaa && header[1] == 0x55 && body[length] == 0x55 && body[length + 1] == 0xaa,
                    "Incubation request retains native packet framing");
                require(body[0] == (byte)GameOpcodes.GS_ITEM_UPGRADE && body[1] == sub,
                    "Incubation request retains original operation identity");
            }
            void Receive(Packet packet)
            {
                packet.ResetOffset();
                typeof(Net).GetMethod("HandleItemUpgrade", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(net, new object[] { packet });
            }
            Packet Refused(byte sub, byte code)
            {
                var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteByte(sub); packet.WriteByte(0); packet.WriteByte(code); return packet;
            }
            Packet Success(byte sub, int item, int slot = 4, int index = 9, int scroll = 700019001, int scrollSlot = 7)
            {
                var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteByte(sub); packet.WriteByte(1);
                packet.WriteInt(item); packet.WriteByte((byte)slot); packet.WriteInt(index); packet.WriteString("Kauly");
                packet.WriteByte(101); packet.WriteByte(12); packet.WriteUShort(4200); packet.WriteShort(7300);
                if (sub == 10) { packet.WriteByte(0); packet.WriteInt(scroll); packet.WriteByte((byte)scrollSlot); }
                return packet;
            }
            bool Pending() => typeof(Net).GetField("_petIncubationRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(net) != null;
            ItemSlot Slot(int bag) => net.LastEnter.Inventory[InventoryConstants.InventoryStart + bag];
            int hatched = 0, transformed = 0, refused = 0, mutations = 0;
            net.PetHatchedEvent += (_, _) => hatched++;
            net.PetTransformedEvent += (_, _) => transformed++;
            net.PetHatchFailedEvent += _ => refused++;
            net.PetTransformFailedEvent += _ => refused++;
            net.InventorySlotEvent += (_, _) => mutations++;
            net.MirrorInventorySlot(InventoryConstants.InventoryStart + 4, new ItemSlot { ItemId = 600001000, Count = 1 });
            net.MirrorInventorySlot(InventoryConstants.InventoryStart + 7, new ItemSlot { ItemId = 700019001, Count = 3 });
            Receive(Success(6, 610001000));
            require(hatched == 0 && mutations == 0 && Slot(4).ItemId == 600001000, "Unsolicited incubation success does not overwrite inventory");
            require(net.SendPetHatch(13016, 600001000, 4, "Kauly"), "Native hatch request starts one pending operation"); await ReadRequest(6);
            require(!net.SendPetHatch(13016, 600001000, 4, "Kauly") && !net.SendPetTransform(13016, 610001000, 4, 700019001, 7),
                "Pending incubation cannot submit a duplicate or another operation");
            Receive(Refused(10, 1));
            require(Pending() && refused == 0, "Transformation reply cannot release a pending hatch");
            var hatchData = Success(6, 610001000).GetData();
            for (int length = 1; length < hatchData.Length; length++)
            {
                var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteBytes(hatchData[..length]); Receive(packet);
                require(Pending() && refused == 0 && mutations == 0, "Truncated hatch preserves pending authority at byte " + length);
            }
            Receive(Refused(6, 2));
            require(!Pending() && refused == 1 && mutations == 0 && Slot(4).ItemId == 600001000,
                "Complete hatch refusal releases the pending operation without changing items");
            require(net.SendPetHatch(13016, 600001000, 4, "Kauly"), "Refused hatch can be retried"); await ReadRequest(6);
            Receive(Success(6, 610001000, slot: 5));
            require(Pending() && mutations == 0, "Mismatched hatch slot cannot overwrite another inventory item");
            var hatch = Success(6, 610001000); Receive(hatch); Receive(hatch);
            require(!Pending() && hatched == 1 && mutations == 1 && Slot(4).ItemId == 610001000 && Slot(4).UniqueId == 9,
                "Hatch success applies and notifies exactly once");
            require(net.SendPetTransform(13016, 610001000, 4, 700019001, 7), "Native transform request starts one pending operation"); await ReadRequest(10);
            Receive(Refused(6, 1));
            require(Pending() && refused == 1, "Hatch reply cannot release a pending transformation");
            var transformData = Success(10, 610015000).GetData();
            for (int length = 1; length < transformData.Length; length++)
            {
                var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteBytes(transformData[..length]); Receive(packet);
                require(Pending() && transformed == 0 && mutations == 1 && Slot(7).Count == 3,
                    "Truncated transformation preserves familiar and scroll at byte " + length);
            }
            foreach (var invalid in new[] { Success(10, 610015000, index: 999), Success(10, 610015000, scrollSlot: 8),
                Success(10, 610015000, scroll: 700019002), Success(10, 610001000) }) Receive(invalid);
            require(Pending() && mutations == 1 && Slot(7).Count == 3, "Mismatched familiar, scroll and unchanged form replies cannot spend an item");
            var transformedReply = Success(10, 610015000); Receive(transformedReply); Receive(transformedReply);
            require(!Pending() && transformed == 1 && mutations == 3 && Slot(4).ItemId == 610015000 && Slot(7).Count == 2,
                "Duplicate transformation reply cannot spend the scroll twice");
            require(net.SendPetTransform(13016, 610015000, 4, 700019001, 7), "Completed transformation allows a new deliberate request"); await ReadRequest(10);
            Receive(transformedReply);
            require(Pending() && transformed == 1 && Slot(7).Count == 2, "Old success cannot complete a new transformation from the current form");
            Receive(Refused(10, 1));
            require(!Pending() && refused == 2 && transformed == 1 && Slot(7).Count == 2, "Complete transformation refusal preserves the familiar and remaining scrolls");
            require(net.SendPetTransform(13016, 610015000, 4, 700019001, 7), "Refused transformation can be retried"); await ReadRequest(10);
            Receive(Success(10, 610001000));
            require(transformed == 2 && Slot(4).ItemId == 610001000 && Slot(7).Count == 1, "A later valid transformation applies even when returning to an earlier form");
            require(net.SendPetTransform(13016, 610001000, 4, 700019001, 7), "Transformation can begin before a connection reset"); await ReadRequest(10);
            typeof(Net).GetMethod("ResetPet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(net, null);
            Receive(Success(10, 610015000));
            require(!Pending() && transformed == 2 && Slot(7).Count == 1 && net.PetItems.Count == 0,
                "Connection reset discards pending incubation and ignores its delayed reply");
            connection.Close();
            require(!net.SendPetHatch(13016, 600001000, 4, "Kauly") && !Pending(), "Disconnected incubation does not create a pending request");
        }
        finally { connection.Close(); net.Free(); }
    }
}
