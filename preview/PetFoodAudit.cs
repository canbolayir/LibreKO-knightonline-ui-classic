using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using NativeSlot = LibreKO.Domain.ItemSlot;
using ClassicSlot = KnightOnlineUiClassic.Layout.ItemSlot;

public partial class Preview
{
    private async Task AuditPetFood(World world, Net net, Control panel, InventoryWindow actual,
        NetworkStream stream, CancellationToken token, int nation, string output, Action<bool, string> require)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var inventory = (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(world)!;
        var cells = (List<ClassicSlot>)typeof(InventoryWindow).GetField("_slots", flags)!.GetValue(actual)!;
        var window = (HudWindow)panel.GetParent();
        var view = (LayoutView)typeof(InventoryWindow).GetField("_view", flags)!.GetValue(actual)!;
        var foods = ItemData.All().Where(i => i.Kind == 176 && i.Damage > 0).OrderBy(i => i.Damage).ThenBy(i => i.Id).ToArray();
        require(foods.Length > 1 && foods[0].Damage < foods[^1].Damage, "Existing content supplies distinct familiar food strengths");
        var weak = foods[0]; var strong = foods[^1];
        int weakSlot = Inventory.GridStart + 7, strongSlot = Inventory.GridStart + 8, duplicateSlot = Inventory.GridStart + 6;
        var saved = new[] { weakSlot, strongSlot, duplicateSlot }.ToDictionary(s => s, s => inventory[s]);
        int oldSatisfaction = net.Pet!.Satisfaction;
        var records = new List<object>();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var feed = Find<Button>("pet_feed"); var status = Find<Label>("pet_status");
        int mutations = 0, results = 0, refusals = 0;
        Action<int, NativeSlot> mutation = (_, _) => mutations++;
        Action<int, int, int, int> result = (_, _, _, _) => results++;
        Action<int> refusal = _ => refusals++;
        net.InventorySlotEvent += mutation; net.PetFedEvent += result; net.PetFoodRefusedEvent += refusal;
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Set(int slot, NativeSlot item) { inventory.ApplySlotUpdate(slot, item); net.MirrorInventorySlot(slot, item); DetailCall(world, "RefreshInventoryUI"); }
        async Task ClickFeed()
        {
            var at = feed.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true); await Frames(2);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            await Frames();
        }
        void Receive(Packet p) { p.ResetOffset(); typeof(Net).GetMethod("HandlePet", flags)!.Invoke(net, new object[] { p }); }
        Packet Food(byte success, int slot, int id, short count = 0, short increase = 0)
        {
            var p = new Packet(GameOpcodes.GS_PET); p.WriteByte(1); p.WriteByte(16); p.WriteByte(success); p.WriteByte((byte)slot); p.WriteInt(id);
            if (success == 1) { p.WriteShort(count); p.WriteShort(0); p.WriteInt(0); p.WriteShort(increase); }
            return p;
        }
        void Satisfaction(int value)
        {
            var p = new Packet(GameOpcodes.GS_PET); p.WriteByte(1); p.WriteByte(15); p.WriteShort((short)value); p.WriteInt(1); Receive(p);
        }
        async Task Request(int slot, int id)
        {
            var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)); var body = new byte[length + 2]; await stream.ReadExactlyAsync(body, token);
            require(length == 10 && header[0] == 0xaa && header[1] == 0x55 && body[length] == 0x55 && body[length + 1] == 0xaa
                && body[0] == (byte)GameOpcodes.GS_PET && body[1] == 1 && body[2] == 16 && body[3] == slot - Inventory.GridStart
                && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)) == id && body[8] == 1 && body[9] == 0,
                "Actual familiar Feed click submits the original food request and selected inventory slot");
        }
        async Task Capture(string state)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(20, 20), GlobalPosition = new Vector2(20, 20) }, true); await Frames();
            require(GetViewportRect().Encloses(window.GetGlobalRect()) && GetViewportRect().Encloses(actual.GetGlobalRect()), "Familiar feeding keeps both actual windows contained / " + state);
            foreach (var entry in view.Where(_ => true).Where(p => p.Control.IsVisibleInTree()))
                require(entry.Control.Size == entry.Node.SizeVec && new Rect2(Vector2.Zero, view.Size).Encloses(new Rect2(entry.Control.Position, entry.Control.Size)), "Familiar feeding inventory geometry / " + state + " / " + entry.Node.Id);
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("pet_expected_rect") && c.IsVisibleInTree()))
            {
                var rect = control.GetMeta("pet_expected_rect").AsRect2();
                require(control.Position == rect.Position && control.Size == rect.Size && window.GetGlobalRect().Encloses(control.GetGlobalRect()), "Familiar feeding actual geometry / " + state + " / " + control.Name);
                if (control is Label label && label.Text.Length > 0) require(label.GetLineCount() == label.GetVisibleLineCount(), "Familiar feeding complete text / " + state + " / " + control.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-food-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); records.Add(new { state, file });
        }
        try
        {
            await ClickFeed();
            require(status.Text == "You have no familiar food." && status.GetThemeColor("font_color") == UiTheme.Bad && !stream.DataAvailable,
                "Familiar feeding without food reports the native warning and sends no packet");
            await Capture("no-food");
            var weakItem = new NativeSlot { ItemId = weak.Id, Count = 1, Durability = 11 };
            var strongItem = new NativeSlot { ItemId = strong.Id, Count = 2, Durability = 321, Flag = (byte)ItemFlag.Sealed };
            Set(weakSlot, weakItem); Set(strongSlot, strongItem); Set(duplicateSlot, new NativeSlot { ItemId = strong.Id, Count = 3, Flag = (byte)ItemFlag.Duplicate });
            Satisfaction(9000); await Frames(); await ClickFeed(); await Request(strongSlot, strong.Id);
            require(inventory[strongSlot].Equals(strongItem) && mutations == 0, "Familiar Feed chooses the strongest usable food and never spends it optimistically");
            Receive(Food(0, strongSlot - Inventory.GridStart, strong.Id)); await Frames();
            require(refusals == 1 && results == 0 && mutations == 0 && inventory[strongSlot].Equals(strongItem) && status.Text.Contains(ItemData.DisplayName(strong.Id))
                && status.GetThemeColor("font_color") == UiTheme.Bad, "Authoritative familiar food refusal preserves every item and displays its native warning");
            await Capture("refused");
            await ClickFeed(); await Request(strongSlot, strong.Id);
            var success = Food(1, strongSlot - Inventory.GridStart, strong.Id, 1, 1000);
            var bytes = success.GetData(); string warning = status.Text;
            for (int length = 0; length < bytes.Length; length++)
            {
                var prefix = new Packet(GameOpcodes.GS_PET); prefix.WriteBytes(bytes[..length]); Receive(prefix);
                require(refusals == 1 && results == 0 && mutations == 0 && status.Text == warning && inventory[strongSlot].Equals(strongItem), "Truncated familiar food response cannot mutate items or become a false refusal / " + length);
            }
            Receive(Food(1, InventoryConstants.HaveMax, strong.Id, 1, 1000)); Receive(Food(1, strongSlot - Inventory.GridStart, weak.Id, 1, 1000));
            require(results == 0 && mutations == 0 && inventory[strongSlot].Equals(strongItem), "Invalid or mismatched familiar food replies cannot overwrite another inventory record");
            Receive(success); Satisfaction(10000); await Frames();
            var left = strongItem; left.Count = 1;
            require(results == 1 && mutations == 1 && inventory[strongSlot].Equals(left) && net.LastEnter.Inventory[strongSlot].Equals(left),
                "Authoritative familiar feeding preserves remaining item flags and durability in both inventories");
            require(cells.Single(c => c.Slot == strongSlot).Current.Count == 1 && Find<Label>("pet_satisfaction_value").Text == $"{100f:0.00}%"
                && status.Text == $"{10f:0.00}% satisfaction rate increase" && status.GetThemeColor("font_color") == UiTheme.TextHi,
                "Native familiar feeding refreshes the actual count satisfaction value and result text");
            Receive(success); await Frames(); require(results == 1 && mutations == 1, "Duplicate familiar food success cannot spend food or notify twice");
            await Capture("accepted");
            await ClickFeed(); require(status.Text == "Your familiar is already full." && !stream.DataAvailable && inventory[strongSlot].Equals(left),
                "A full familiar sends no feeding packet and preserves the remaining food");
            await Capture("full");
            Satisfaction(9900); await Frames(); await ClickFeed(); await Request(strongSlot, strong.Id);
            Receive(Food(1, strongSlot - Inventory.GridStart, strong.Id, 0, 100)); Satisfaction(10000); await Frames();
            require(inventory[strongSlot].Equals(default(NativeSlot)) && net.LastEnter.Inventory[strongSlot].Equals(default(NativeSlot)) && cells.Single(c => c.Slot == strongSlot).Current.IsEmpty,
                "Eating the final familiar food clears its exact native and rendered inventory record");
            await Capture("consumed");
            typeof(World).GetField("_selfDead", flags)!.SetValue(world, true); DetailCall(world, "RefreshPetUI"); await Frames(); await ClickFeed();
            require(feed.Disabled && !stream.DataAvailable && inventory[weakSlot].Equals(weakItem), "Dead owner cannot submit familiar feeding or spend food");
            await Capture("dead");
            typeof(World).GetField("_selfDead", flags)!.SetValue(world, false); Satisfaction(9000); Set(weakSlot, default); await Frames(); await ClickFeed();
            require(status.Text == "You have no familiar food." && !stream.DataAvailable && inventory[duplicateSlot].Count == 3,
                "Duplicate familiar food follows the authoritative server exclusion and is never selected");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed }, true);
            await Frames(); require(!stream.DataAvailable, "Unfocused familiar Enter never submits a food request");
        }
        finally
        {
            net.InventorySlotEvent -= mutation; net.PetFedEvent -= result; net.PetFoodRefusedEvent -= refusal;
            typeof(World).GetField("_selfDead", flags)!.SetValue(world, false);
            foreach (var entry in saved) Set(entry.Key, entry.Value);
            Satisfaction(oldSatisfaction); DetailCall(world, "SetPetStatus", "", false); DetailCall(world, "RefreshPetUI"); await Frames();
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-food.json", JsonSerializer.Serialize(new { screens = records }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
