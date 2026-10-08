using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using NativeSlot = LibreKO.Domain.ItemSlot;
using ClassicSlot = KnightOnlineUiClassic.Layout.ItemSlot;

public partial class Preview
{
    private async Task AuditPetEquipmentResults(int nation, string output, Action<bool, string> require)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (string pack in new[] { "characters", "armor", "weapons" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/" + pack + ".pck"), false))
                throw new Exception("Missing existing inventory audit content: " + pack);
        var originalNet = Net.I;
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net, new MyInfo { Nation = nation, Class = nation * 100 + 6, Race = nation == 1 ? 1 : 11,
            Name = "FamiliarTester", Gear = new int[8], Inventory = new NativeSlot[InventoryConstants.InventoryTotal] });
        net.Sheet.SeedWealth(1_786_597_823, 0); net.Sheet.SetMaxWeight(17_100);
        var world = new PetAuditWorld { ProcessMode = ProcessModeEnum.Disabled }; AddChild(world);
        var layer = (CanvasLayer)DetailCall(world, "BuildPetEquipmentResponseUiPreview", nation)!;
        layer.ProcessMode = ProcessModeEnum.Always;
        await Frames();
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.Position = new Vector2(455, 70);
        var panel = window.GetChildren().OfType<ClassicPetPanel>().Single();
        var inventory = (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(world)!;
        var game = Plugin.Kit.Game;
        var games = new[] { game, PluginHost.Game }.Distinct().ToArray();
        var proxyNames = new[] { "_character", "_inventory", "_target", "_windows", "_chat", "_hotbar", "_map", "_commands", "_log", "_quests", "_skills" };
        object?[] Sources(PluginGame g) => proxyNames.Select(name => {
            var proxy = typeof(PluginGame).GetField(name, flags)!.GetValue(g)!;
            return proxy.GetType().GetField("Source")!.GetValue(proxy);
        }).ToArray();
        var savedSources = games.ToDictionary(g => g, Sources);
        var savedAvailable = games.ToDictionary(g => g, g => g.Available);
        var attach = typeof(PluginGame).GetMethod("Attach", flags)!;
        var bridge = Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge", BindingFlags.NonPublic)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { world }, null)!;
        typeof(World).GetField("_pluginBridge", flags)!.SetValue(world, bridge);
        foreach (var g in games) attach.Invoke(g, Enumerable.Repeat(bridge, attach.GetParameters().Length).ToArray());
        // The standalone preview owns a separate plugin context; forward the native notification to its proxy.
        Action forwardInventory = () => typeof(PluginGame).GetMethod("RaiseInventory", flags)!.Invoke(game, null);
        if (!ReferenceEquals(game, PluginHost.Game)) PluginHost.Game.Inventory.Changed += forwardInventory;
        var shell = new Control { Position = new Vector2(25, 55) }; AddChild(shell);
        var host = (WindowHost)Create(typeof(WindowHost), "inventory", "Inventory", shell, (Action)(() => shell.Visible = false));
        var actual = new InventoryWindow(host); shell.AddChild(actual);
        var cells = (List<ClassicSlot>)typeof(InventoryWindow).GetField("_slots", flags)!.GetValue(actual)!;
        var view = (LayoutView)typeof(InventoryWindow).GetField("_view", flags)!.GetValue(actual)!;
        var captures = new List<object>();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        T Field<T>(string name) => (T)typeof(World).GetField(name, flags)!.GetValue(world)!;
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control control, MouseButton button = MouseButton.Left)
        {
            var at = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true); await Frames(2);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed }, true);
            await Frames();
        }
        async Task Drag(Control source, Control target)
        {
            var from = source.GetGlobalRect().GetCenter(); var to = target.GetGlobalRect().GetCenter(); var step = new Vector2(18, 2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = from + step, GlobalPosition = from + step, Relative = step, ButtonMask = MouseButtonMask.Left }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from - step, ButtonMask = MouseButtonMask.Left }, true); await Frames(2);
            require(GetViewport().GuiIsDragging(), "Native Classic inventory pointer drag reaches the familiar equipment window");
            GetViewport().PushInput(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        async Task Capture(string state)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(20, 20), GlobalPosition = new Vector2(20, 20) }, true); await Frames();
            require(GetViewportRect().Encloses(window.GetGlobalRect()) && GetViewportRect().Encloses(actual.GetGlobalRect()), "Received familiar equipment result keeps both actual windows contained / " + state);
            foreach (var entry in view.Where(_ => true).Where(p => p.Control.IsVisibleInTree()))
                require(entry.Control.Size == entry.Node.SizeVec && new Rect2(Vector2.Zero, view.Size).Encloses(new Rect2(entry.Control.Position, entry.Control.Size)), "Received familiar equipment inventory geometry / " + state + " / " + entry.Node.Id);
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("pet_expected_rect") && c.IsVisibleInTree()))
            {
                var rect = control.GetMeta("pet_expected_rect").AsRect2();
                require(control.Position == rect.Position && control.Size == rect.Size && window.GetGlobalRect().Encloses(control.GetGlobalRect()), "Received familiar equipment geometry / " + state + " / " + control.Name);
                if (control is Label label && label.Text.Length > 0)
                    require(label.GetLineCount() == label.GetVisibleLineCount(), "Received familiar equipment label is fully visible / " + state + " / " + control.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-equipment-reply-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); captures.Add(new { state, file });
        }
        void SetBag(int slot, NativeSlot item) { inventory.ApplySlotUpdate(slot, item); net.MirrorInventorySlot(slot, item); }
        void Refresh() { DetailCall(world, "RefreshInventoryUI"); DetailCall(world, "RefreshPetUI"); }
        void Reply(bool accepted)
        {
            var p = new Packet(GameOpcodes.GS_ITEM_MOVE); p.WriteByte(1); p.WriteByte(accepted ? (byte)1 : (byte)0); p.ResetOffset();
            typeof(Net).GetMethod("HandleItemMove", flags)!.Invoke(net, new object[] { p });
        }
        void SummonSheet(NativeSlot[] items, int index = 1)
        {
            var sheet = net.Pet!;
            var p = new Packet(GameOpcodes.GS_PET); p.WriteByte(1); p.WriteByte(5); p.WriteByte((byte)PetSheet.ModeSummoned); p.WriteShort(1);
            p.WriteInt(index); p.WriteString(sheet.Name); p.WriteByte((byte)sheet.Class); p.WriteByte((byte)sheet.Level); p.WriteUShort((ushort)sheet.ExpPercent);
            foreach (int value in new[] { sheet.MaxHp, sheet.Hp, sheet.MaxMp, sheet.Mp, sheet.Satisfaction, sheet.Attack, sheet.Defence }) p.WriteShort((short)value);
            foreach (int resist in sheet.Resists) p.WriteByte((byte)resist);
            foreach (var item in items) { p.WriteInt(item.ItemId); p.WriteShort(item.Durability); p.WriteShort(item.Count); p.WriteByte(item.Flag); p.WriteShort(0); p.WriteInt(0); p.WriteInt(0); }
            p.ResetOffset(); typeof(Net).GetMethod("HandlePet", flags)!.Invoke(net, new object[] { p });
        }
        void CheckSlot(int bag, int petSlot, NativeSlot expectedBag, NativeSlot expectedPet, string state)
        {
            require(inventory[bag].Equals(expectedBag) && net.Pet!.Items[petSlot].Equals(expectedPet), "Native familiar equipment acknowledgement preserves exact item records / " + state);
            require(net.LastEnter.Inventory[bag].Equals(expectedBag), "Confirmed familiar equipment survives the cached inventory used for world reload / " + state);
            require(cells.Single(c => c.Slot == bag).Current.ItemId == expectedBag.ItemId && Find<ItemSlotView>("pet_item_" + petSlot).Item.Equals(expectedPet),
                "Native inventory notifications refresh both rendered item views / " + state);
            require(!Field<bool>("_moveInFlight") && typeof(Net).GetField("_pendingItemMove", flags)!.GetValue(net) == null, "Familiar equipment response releases both native pending moves / " + state);
        }
        var connection = (KoConn)typeof(Net).GetField("_conn", flags)!.GetValue(net)!;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(60000);
        try
        {
            await Frames(); await Click(Descendants(panel).OfType<Button>().Single(b => b.Text == "Items"));
            for (int i = 0; i < 4; i++) net.Pet!.Items[i] = default;
            Refresh(); await Frames();
            connection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
            for (int i = 0; i < 120 && !net.Connected; i++) await Frames(1);
            require(net.Connected, "Familiar equipment uses the native isolated loopback connection");
            using var stream = receiver.GetStream();
            async Task Request(byte direction, int id, int src, int dst)
            {
                require(Field<bool>("_moveInFlight"), "Actual familiar equipment input queues the native request");
                var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)); var body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                require(header[0] == 0xaa && header[1] == 0x55 && body[length] == 0x55 && body[length + 1] == 0xaa && length == 9
                    && body[0] == (byte)GameOpcodes.GS_ITEM_MOVE && body[1] == 1 && body[2] == direction
                    && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3)) == id && body[7] == src && body[8] == dst,
                    "Actual familiar equipment retains exact native request framing and slot encoding / " + direction);
            }
            int[] kinds = { PetBag.AutomaticLootingKind, 172, 173, 177 };
            for (int i = 0; i < kinds.Length; i++)
            {
                int id = ItemData.All().First(d => PetBag.Holds(d) && d.Kind == kinds[i]).Id;
                int bag = Inventory.GridStart + i;
                var item = new NativeSlot { ItemId = id, Count = 1, Durability = 123, Flag = 2 };
                SetBag(bag, item); Refresh(); await Frames();
                var source = cells.Single(c => c.Slot == bag); var target = Find<ItemSlotView>("pet_item_" + i);
                await Drag(source, target); await Request(ItemMove.InventoryToPet, id, i, i);
                require(inventory[bag].Equals(item) && net.Pet!.Items[i].IsEmpty && net.LastEnter.Inventory[bag].Equals(item), "Familiar equipment remains unchanged until the native acknowledgement / " + kinds[i]);
                var truncated = new Packet(GameOpcodes.GS_ITEM_MOVE); truncated.WriteByte(1); truncated.ResetOffset(); typeof(Net).GetMethod("HandleItemMove", flags)!.Invoke(net, new object[] { truncated });
                require(Field<bool>("_moveInFlight"), "Truncated familiar equipment acknowledgement keeps the operation pending / " + kinds[i]);
                Reply(false); await Frames(); CheckSlot(bag, i, item, default, "refused-equip-" + kinds[i]);
                if (i == 0) await Capture("refused");
                await Drag(source, target); await Request(ItemMove.InventoryToPet, id, i, i);
                if (kinds[i] is 172 or 173) { var next = net.Pet!.Items.ToArray(); next[i] = item; SummonSheet(next); await Frames(); }
                Reply(true); await Frames(); CheckSlot(bag, i, default, item, "accepted-equip-" + kinds[i]);
                var reloaded = new Inventory(); reloaded.Reset(net.LastEnter.Inventory);
                require(reloaded[bag].IsEmpty, "A new domain inventory cannot restore an item already equipped on the familiar / " + kinds[i]);
                Reply(true); await Frames(); CheckSlot(bag, i, default, item, "duplicate-equip-" + kinds[i]);
                if (i == 3) await Capture("equipped");
                await Click(target, MouseButton.Right); await Request(ItemMove.PetToInventory, id, i, 0);
                require(inventory[Inventory.GridStart].IsEmpty && net.Pet!.Items[i].Equals(item), "Familiar right-click removal waits for native authority / " + kinds[i]);
                Reply(false); await Frames(); CheckSlot(Inventory.GridStart, i, default, item, "refused-remove-" + kinds[i]);
                await Click(target, MouseButton.Right); await Request(ItemMove.PetToInventory, id, i, 0);
                if (kinds[i] is 172 or 173) { var next = net.Pet!.Items.ToArray(); next[i] = default; SummonSheet(next); await Frames(); }
                Reply(true); await Frames(); CheckSlot(Inventory.GridStart, i, item, default, "accepted-remove-" + kinds[i]);
                reloaded.Reset(net.LastEnter.Inventory); require(reloaded[Inventory.GridStart].Equals(item), "A new domain inventory retains exact familiar equipment removed into the bag / " + kinds[i]);
                Reply(true); await Frames(); CheckSlot(Inventory.GridStart, i, item, default, "duplicate-remove-" + kinds[i]);
                if (i == 3) await Capture("removed");
                SetBag(Inventory.GridStart, default); Refresh(); await Frames();
            }
            // The server can acknowledge a same-kind replacement after its updated stat sheet.
            int attackId = ItemData.All().First(d => PetBag.Holds(d) && d.Kind == 172).Id;
            var oldItem = new NativeSlot { ItemId = attackId, Count = 1, Durability = 99, Flag = 1 };
            var replacement = new NativeSlot { ItemId = attackId, Count = 1, Durability = 321, Flag = 3 };
            net.Pet!.Items[1] = oldItem; SetBag(Inventory.GridStart + 3, replacement); Refresh(); await Frames();
            await Drag(cells.Single(c => c.Slot == Inventory.GridStart + 3), Find<ItemSlotView>("pet_item_1")); await Request(ItemMove.InventoryToPet, attackId, 3, 1);
            var updated = net.Pet!.Items.ToArray(); updated[1] = replacement; SummonSheet(updated); Reply(true); await Frames();
            CheckSlot(Inventory.GridStart + 3, 1, oldItem, replacement, "same-kind-replacement-after-stat-sheet"); await Capture("replacement");
            await Click(Find<ItemSlotView>("pet_item_1"), MouseButton.Right); await Request(ItemMove.PetToInventory, attackId, 1, 0);
            SummonSheet(new NativeSlot[PetSheet.InventorySize], 99); Reply(true); await Frames();
            CheckSlot(Inventory.GridStart, 1, replacement, default, "different-familiar-after-request");
            require(net.Pet!.Index == 99 && net.Pet.Items.All(i => i.IsEmpty), "An earlier familiar move cannot equip or remove items from a different summoned familiar");
            await Capture("changed-familiar");
            await Drag(cells.Single(c => c.Slot == Inventory.GridStart), Find<ItemSlotView>("pet_item_1")); await Request(ItemMove.InventoryToPet, attackId, 0, 1);
            Reply(true); await Frames(); CheckSlot(Inventory.GridStart, 1, default, replacement, "new-familiar-equip");
            await AuditPetFood(world, net, panel, actual, stream, timeout.Token, nation, output, require);
            await Click(Find<ItemSlotView>("pet_item_1"), MouseButton.Right); await Request(ItemMove.PetToInventory, attackId, 1, 0);
            var gone = new Packet(GameOpcodes.GS_PET); gone.WriteByte(1); gone.WriteByte(5); gone.WriteByte((byte)PetSheet.ModeDied); gone.WriteShort(1); gone.WriteInt(99); gone.ResetOffset();
            typeof(Net).GetMethod("HandlePet", flags)!.Invoke(net, new object[] { gone }); Reply(true); await Frames();
            require(inventory[Inventory.GridStart].Equals(replacement) && net.LastEnter.Inventory[Inventory.GridStart].Equals(replacement) && net.Pet == null,
                "Accepted familiar removal after dismissal keeps the bag record without resurrecting the familiar");
            require(!Field<bool>("_moveInFlight"), "Dismissed familiar equipment acknowledgement releases the native inventory lock");
            await Capture("dismissed");
        }
        finally
        {
            connection.Close(); shell.Free(); DetailCall(world, "DisposePetEquipmentUiPreview");
            if (!ReferenceEquals(game, PluginHost.Game)) PluginHost.Game.Inventory.Changed -= forwardInventory;
            foreach (var g in games) { if (savedAvailable[g]) attach.Invoke(g, savedSources[g]); else typeof(PluginGame).GetMethod("Detach", flags)!.Invoke(g, null); }
            layer.Free(); world.Free(); net.Free(); typeof(Net).GetProperty("I")!.SetValue(null, originalNet);
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-equipment-results.json", JsonSerializer.Serialize(new { screens = captures }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
