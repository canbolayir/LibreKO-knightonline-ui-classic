using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using System.Reflection;

public partial class Preview
{
    private async Task AuditPetEquipment(World world, Net net, CanvasLayer layer, HudWindow window, Control panel,
        Action<bool, string> require, Func<string, Task> capture)
    {
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        T Field<T>(string name) => (T)typeof(World).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        void SetField(string name, object value) => typeof(World).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, value);
        async Task Click(Control control, MouseButton button = MouseButton.Left)
        {
            var at = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed });
            await Frames();
        }
        async Task SendKey(Key key)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            await Frames();
        }
        var originalPosition = window.Position;
        var headerAt = window.GetGlobalRect().Position + new Vector2(180, 22); var shift = new Vector2(35, 18);
        GetViewport().PushInput(new InputEventMouseMotion { Position = headerAt, GlobalPosition = headerAt }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseButton { Position = headerAt, GlobalPosition = headerAt, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = headerAt + shift, GlobalPosition = headerAt + shift, Relative = shift, ButtonMask = MouseButtonMask.Left }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseButton { Position = headerAt + shift, GlobalPosition = headerAt + shift, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(2);
        require(window.Position.DistanceTo(originalPosition + shift) < 1, "Pet original header forwards real pointer dragging to HudLayout");
        window.Position = originalPosition;

        var sheet = net.Pet!; var savedItems = sheet.Items.ToArray();
        int[] equipmentKinds = { PetBag.AutomaticLootingKind, 172, 173, 177 };
        var gear = equipmentKinds.Select(kind => ItemData.All().First(item => PetBag.Holds(item) && item.Kind == kind)).ToArray();
        require(gear.Length == 4, "Existing content supplies four distinct familiar equipment kinds");
        for (int i = 0; i < 4; i++) sheet.Items[i] = new LibreKO.Domain.ItemSlot { ItemId = gear[i].Id, Count = 1, Durability = 1 };
        DetailCall(world, "RefreshPetUI"); await Click(Descendants(panel).OfType<Button>().Single(b => b.Text == "Items"));
        await capture("all-equipment");
        for (int i = 0; i < 4; i++)
        {
            var cell = Find<ItemSlotView>("pet_item_" + i);
            var icon = Descendants(cell).OfType<TextureRect>().Single();
            require(icon.Size == new Vector2(44, 44) && icon.GetGlobalRect().Position == cell.GetGlobalRect().Position + new Vector2(2, 2), "Pet equipment icon has actual 2 px inset " + i);
            require(cell.CountLabel.GetParent().Name == "pet_item_overlay" && cell.CountLabel.OffsetRight == -2 && cell.CountLabel.OffsetBottom == -2,
                "Pet equipment count shares the fixed Classic overlay " + i);
            var at = cell.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames();
            require(world.ItemTooltipVisible, "Actual familiar equipment hover opens native tooltip " + i);
            var tooltip = Field<PanelContainer>("_itemTipPanel");
            require(Descendants(tooltip).OfType<Control>().Prepend(tooltip).All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore),
                "Familiar item tooltip cannot intercept neighboring equipment pointer input " + i);
            require(GetViewportRect().Encloses(tooltip.GetGlobalRect()), "Familiar item tooltip is contained " + i);
            require(Descendants(tooltip).OfType<Label>().Any(label => label.Text == ItemData.DisplayName(gear[i].Id)), "Familiar item tooltip identifies the hovered equipment " + i);
            if (i == 0) await capture("equipment-tooltip");
        }
        GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(20, 20), GlobalPosition = new Vector2(20, 20) }); await Frames();
        require(!world.ItemTooltipVisible, "Leaving familiar equipment hides native tooltip");

        var inventory = (Inventory)typeof(World).GetProperty("Inv", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!; inventory.EnsureLength(InventoryConstants.InventoryTotal);
        for (int i = 0; i < Inventory.GridCount; i++) inventory.ApplySlotUpdate(Inventory.GridStart + i, new LibreKO.Domain.ItemSlot { ItemId = gear[0].Id, Count = 1 });
        await Click(Find<ItemSlotView>("pet_item_0"), MouseButton.Right);
        require(!Field<bool>("_moveInFlight") && Find<Label>("pet_status").Text == "Your bag is full.", "Pet equipment right-click preserves items and reports a full inventory");
        GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(20, 20), GlobalPosition = new Vector2(20, 20) }); await Frames();
        await capture("bag-full");
        inventory.ApplySlotUpdate(Inventory.GridStart, default);
        await Click(Find<ItemSlotView>("pet_item_0"), MouseButton.Right);
        require(Field<bool>("_moveInFlight") && sheet.Items[0].ItemId == gear[0].Id && inventory[Inventory.GridStart].IsEmpty,
            "Pet equipment right-click queues native removal without optimistic item mutation");
        var move = Field<object>("_moveCur"); var moveType = move.GetType();
        require((byte)moveType.GetField("Dir")!.GetValue(move)! == ItemMove.PetToInventory
            && (byte)moveType.GetField("Src")!.GetValue(move)! == 0 && (byte)moveType.GetField("Dst")!.GetValue(move)! == 0,
            "Familiar removal uses original PetToInventory slot encoding");
        SetField("_moveInFlight", false);

        var target = Find<ItemSlotView>("pet_item_0");
        var drop = new Godot.Collections.Dictionary { ["invFrom"] = Inventory.GridStart };
        var item = new LibreKO.Domain.ItemSlot { ItemId = gear[0].Id, Count = 1 };
        inventory.ApplySlotUpdate(Inventory.GridStart, item);
        require(target._CanDropData(Vector2.Zero, drop), "Valid familiar equipment can replace the same kind in its own slot");
        require(!Find<ItemSlotView>("pet_item_1")._CanDropData(Vector2.Zero, drop), "Familiar equipment cannot duplicate a kind in another slot");
        item.UniqueId = 1; inventory.ApplySlotUpdate(Inventory.GridStart, item);
        require(!target._CanDropData(Vector2.Zero, drop), "Linked items follow the server familiar equipment exclusion");
        require(!target._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { ["invFrom"] = "invalid" }), "Malformed familiar drag source is rejected without conversion");
        require(!target._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { ["invFrom"] = -1 }), "Out of range familiar drag source is rejected");
        item.UniqueId = 0; inventory.ApplySlotUpdate(Inventory.GridStart, item);
        SetField("_selfDead", true);
        require(!target._CanDropData(Vector2.Zero, drop), "Dead owner cannot move familiar equipment");
        SetField("_selfDead", false);

        var source = new ItemSlotView(48) { Position = new Vector2(120, 370), DragOut = _ => drop }; source.Set(item); layer.AddChild(source); await Frames();
        var from = source.GetGlobalRect().GetCenter(); var to = target.GetGlobalRect().GetCenter(); var step = new Vector2(18, 2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = from + step, GlobalPosition = from + step, Relative = step, ButtonMask = MouseButtonMask.Left }); await Frames(2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from - step, ButtonMask = MouseButtonMask.Left }); await Frames(2);
        require(GetViewport().GuiIsDragging(), "Actual inventory pointer drag reaches familiar equipment");
        GetViewport().PushInput(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
        require(Field<bool>("_moveInFlight"), "Pointer drop queues the existing familiar equipment move");
        move = Field<object>("_moveCur");
        require((byte)moveType.GetField("Dir")!.GetValue(move)! == ItemMove.InventoryToPet, "Familiar pointer drop uses original InventoryToPet encoding");
        SetField("_moveInFlight", false); source.Free();

        DetailCall(world, "SetPetStatus", "", false);
        var oldName = sheet.Name; sheet.Name = "WWWWWWWWWWWWWWW";
        var entities = (System.Collections.IDictionary)Field<object>("_ents"); var actor = entities[1]!;
        actor.GetType().GetField("Name")!.SetValue(actor, sheet.Name);
        DetailCall(world, "RefreshPetUI"); await capture("long-name");
        require(Find<Label>("pet_name").GetLineCount() == 1 && Find<Label>("pet_name").TooltipText == sheet.Name,
            "Longest wide familiar name stays inside the original single-line box and retains its full hover text");
        sheet.Name = oldName; actor.GetType().GetField("Name")!.SetValue(actor, oldName);
        for (int i = 0; i < savedItems.Length; i++) sheet.Items[i] = savedItems[i];
        for (int i = 0; i < Inventory.GridCount; i++) inventory.ApplySlotUpdate(Inventory.GridStart + i, default);
        DetailCall(world, "RefreshPetUI"); await Frames();
        require(Find<Button>("pet_feed").FocusMode == Control.FocusModeEnum.None, "Pet item actions retain the existing mouse-only Classic button policy");
        await SendKey(Key.Enter);
        require(window.Visible && !Field<bool>("_moveInFlight") && Find<Label>("pet_status").Text.Length == 0, "Unfocused Pet Enter never feeds, moves or dismisses equipment implicitly");
        await SendKey(Key.Escape);
        require(!window.Visible && !world.ItemTooltipVisible, "Pet Escape closes the window and tooltip");
        DetailCall(world, "OpenPet"); await Frames();
        await Click(Descendants(panel).OfType<Button>().Single(b => b.Text == "Skills"));
    }
}
