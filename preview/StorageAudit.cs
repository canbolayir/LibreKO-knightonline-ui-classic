using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using NativeSlot = LibreKO.Domain.ItemSlot;

public partial class Preview
{
    private async Task CaptureStorageAudit(PluginGame game, int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/warehouse-window-audit/screens");
        System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(800, 650);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(800, 650), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Storage Preview", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new NativeSlot[InventoryConstants.InventoryTotal]);
        Game.Session.Self.Sheet.SeedWealth(1_234_567, 50_000); Game.Session.Self.Sheet.SetMaxWeight(17100);
        ItemData.EnsureLoaded();
        var world = PreviewFixtures.Prepared(new WorldRoot());
        var inventory = (Inventory)Game.Session.Self.Inventory;
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        int potion = 389010000, weapon = 156210008;
        RequireDefinition(potion); RequireDefinition(weapon);
        inventory[Inventory.GridStart] = new NativeSlot { ItemId = potion, Count = 100, Durability = 1 };
        inventory[Inventory.GridStart + 1] = new NativeSlot { ItemId = weapon, Count = 1, Durability = 7000 };
        inventory[Inventory.GridStart + 2] = new NativeSlot { ItemId = 379021000, Count = 1, Durability = 1 };
        DetailCall(world, "InventoryWindow.BuildPanel");
        var orphan = (Control)DetailField(world, "InventoryWindow.InvContent")!; orphan.Visible = false; world.AddChild(orphan);
        DetailCall(world, "Warehouse.BuildPanel"); DetailCall(world, "VipWarehouse.BuildPanel"); DetailCall(world, "ClanWarehouse.BuildPanel");
        var bridge = Native.Call(world, "CreatePluginAdapters")!;
        var attach = typeof(PluginGame).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Native.Call(bridge, "AttachTo", game);
        foreach (string field in new[] { "Warehouse.layer", "VipWarehouse.layer", "ClanWarehouse.layer", "ItemTooltip.tipLayer", "Warehouse.AmountPrompt" })
            ((Node)DetailField(world, field)!).Reparent(this);
        var windows = new[] { "Warehouse.Panel", "VipWarehouse.Panel", "ClanWarehouse.Panel" }.Select(f => (HudWindow)DetailField(world, f)!).ToArray();
        var storages = windows.Select(w => NativeStorage.Prepare(w, world)!).ToArray();
        foreach (Node node in new Node[] { storages[1].Amount!, storages[2].Amount!, storages[1].PinPrompt! }) node.Reparent(this);
        var panels = windows.Select(w => ClassicStorageSkin.Apply(w.Body)!).ToArray();
        var pin = storages[1].PinPrompt!; ClassicStoragePin.Apply(pin.Window.Body);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string message) { if (!valid) throw new Exception("STORAGE_AUDIT: " + message); checks.Add(message); }
        void Set(string field, object value) => Native.Set(world, field, value);
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(int index, string name)
        {
            for (int i = 0; i < 3; i++) windows[i].Visible = i == index;
            await Frames(); windows[index].Position = new Vector2(216, 42); await Frames(2);
            var controls = Descendants(panels[index]).OfType<Control>().Where(c => c.IsVisibleInTree()).ToArray();
            foreach (var c in controls.Where(c => c.HasMeta("storage_expected_rect")))
                Require(c.GetRect() == c.GetMeta("storage_expected_rect").AsRect2(), "Measured control bounds: " + c.Name + " / " + name + " actual=" + c.GetRect() + " expected=" + c.GetMeta("storage_expected_rect").AsRect2());
            Require(windows[index].Size == KnightOnlineUiClassic.Windows.ClassicStorageLayout.Size(index == 1), "Original storage window proportions / " + name + " " + windows[index].Size);
            if (index == 1)
            {
                // VIP: the PIN row and the expiry row close the upper part with the warehouse rhythm, no empty band.
                var pins = controls.OfType<Button>().Where(b => b.Name.ToString() is "storage_pin_set" or "storage_pin_clear").OrderBy(b => b.Position.X).ToArray();
                var cells = controls.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToArray();
                var expiry = controls.OfType<Label>().Single(l => l.Name == "storage_expiry");
                var weight = controls.OfType<Label>().Where(l => l.Text.StartsWith("Weight")).ToArray();
                Require(pins.Length == 2 && pins[0].Size == pins[1].Size && pins[0].Position.Y == pins[1].Position.Y, "VIP PIN buttons are equal and on one row / " + name);
                Require(pins[0].Position.X == cells.Min(c => c.Position.X) && pins[1].GetRect().End.X == expiry.GetRect().End.X, "VIP PIN row spans the expiry row / " + name);
                Require(pins[0].Position.Y - cells.Max(c => c.GetRect().End.Y) == 31 && KnightOnlineUiClassic.Windows.ClassicStorageLayout.FieldTop - KnightOnlineUiClassic.Windows.ClassicStorageLayout.VipCut - pins[0].GetRect().End.Y == 12,
                    "VIP PIN row sits in the status band and 12 pixels above the expiry field / " + name);
                Require(expiry.Position.Y == KnightOnlineUiClassic.Windows.ClassicStorageLayout.StoredGold.Position.Y - KnightOnlineUiClassic.Windows.ClassicStorageLayout.VipCut, "VIP expiry uses the stored-coins row / " + name);
                Require(weight.Length == 0 || weight[0].Position.Y == KnightOnlineUiClassic.Windows.ClassicStorageLayout.Weight(true).Position.Y, "VIP carried inventory follows the shortened upper part / " + name);
            }
            Require(GetViewportRect().Encloses(windows[index].GetGlobalRect()), "Complete storage window fits viewport / " + name);
            var bag = controls.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToArray();
            Require(bag.Length == 28, "All 28 inventory slots visible / " + name);
            Require(!controls.OfType<LineEdit>().Any(), "Classic storage has no search input / " + name);
            Require(!controls.OfType<Label>().Any(c => c.Text.StartsWith("Right-click") || c.Text.StartsWith("Slots used") || c.Text.Contains("slots ·") || c.Text.StartsWith("Members may") || c.Text.StartsWith("Anyone may")), "Redundant storage instructions and capacity text are hidden / " + name);
            Require(bag.Min(c => c.Position.X) == 13 && bag.Max(c => c.GetRect().End.X) == 352, "Inventory cell side margins differ by at most one pixel / " + name);
            Require(controls.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).Min(c => c.Position.X) == bag.Min(c => c.Position.X), "Stored and carried cells share the same left edge / " + name);
            var pageLabel = controls.OfType<Label>().Single(c => c.Name == "storage_page");
            var storedCells = controls.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToArray();
            Require(pageLabel.GetRect().GetCenter().Y == (storedCells.Min(c => c.Position.Y) + storedCells.Max(c => c.GetRect().End.Y)) / 2, "Page controls are centered on their visible stored cells / " + name);
            foreach (var label in controls.OfType<Label>().Where(c => c.HasMeta("storage_expected_rect")))
                Require(label.GetThemeFontSize("font_size") == 12, "Consistent storage body text size / " + name);
            Require(pageLabel.Text == $"{windows[index].GetMeta("storage_page").AsInt32() + 1} / {windows[index].GetMeta("storage_pages").AsInt32()}", "Displayed page matches storage capacity / " + name);
            foreach (var cell in controls.OfType<ItemSlotView>())
            {
                // The item icon sits in the Classic overlay (first child); a VIP lock adds its own small icon.
                var icon = cell.GetChild(0).GetChildren().OfType<TextureRect>().Single();
                Require(icon.GetGlobalRect() == new Rect2(cell.GlobalPosition + new Vector2(2, 2), cell.Size - new Vector2(4, 4)), "Inventory-aligned icon inset / " + name);
                Require(cell.CountLabel.GetGlobalRect() == new Rect2(cell.GlobalPosition, cell.Size - new Vector2(2, 2)), "Inventory-aligned bottom-right count / " + name);
                Require(cell.CountLabel.GetThemeConstant("outline_size") == 3 && cell.CountLabel.GetThemeFontSize("font_size") == 11, "Shared inventory count typography / " + name);
                Require(icon.GetParent().GetIndex() < cell.GetChildren().OfType<UpgradeBadge>().Single().GetIndex(), "Item icon remains below its upgrade badge / " + name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + name + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { name, file, controls = controls.Where(c => c.HasMeta("storage_expected_rect")).Select(c => new { name = c.Name.ToString(), rect = c.GetGlobalRect().ToString() }).ToArray() });
        }
        var warehouse = (NativeSlot[])DetailField(world, "Warehouse.StoredItems")!;
        Set("Warehouse.shown", true); Set("Warehouse.Money", 4_820_000);
        DetailCall(world, "Warehouse.Refresh"); await Capture(0, "warehouse-empty");
        warehouse[0] = new NativeSlot { ItemId = weapon, Count = 1, Durability = 6200 };
        warehouse[1] = new NativeSlot { ItemId = potion, Count = 40, Durability = 1 };
        warehouse[2] = new NativeSlot { ItemId = 379021000, Count = 1, Durability = 1 };
        warehouse[30] = warehouse[0]; warehouse[191] = warehouse[2];
        DetailCall(world, "Warehouse.Refresh"); await Capture(0, "warehouse-ready");
        var normalBag = Descendants(panels[0]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
        var normalCells = Descendants(panels[0]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_") && c.Visible).ToDictionary(c => c.Index);
        void RightClick(ItemSlotView cell) => cell._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
        normalBag[Inventory.GridStart]._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
        normalBag[Inventory.GridStart]._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left });
        Require(!(bool)DetailField(world, "Warehouse.inFlight")!, "A plain left click does not transfer an item");
        RightClick(normalBag[Inventory.GridStart + 2]);
        Require((bool)DetailField(world, "Warehouse.inFlight")! && !((QuantityPrompt)DetailField(world, "Warehouse.AmountPrompt")!).Visible, "A single countable unit transfers directly after right-click");
        DetailCall(world, "Warehouse.OnResult", (byte)2, (byte)0);
        Require(inventory[Inventory.GridStart + 2].Count == 1, "A refused single-unit transfer preserves the source");
        ((StatusLabel)DetailField(world, "Warehouse.Status")!).ResetStatus();
        Require(Descendants(panels[0]).OfType<ItemSlotView>().Count(c => c.Name.ToString().StartsWith("storage_cell_") && c.IsVisibleInTree()) == 24, "Classic storage has 24 slots per page");
        for (int i = 0; i < 9; i++) storages[0].TurnPage(1);
        Require(storages[0].Page == 7, "Last of eight pages is reachable and clamps at the end");
        await Capture(0, "warehouse-last-page");
        storages[0].TurnPage(-7);
        var normalNext = Descendants(panels[0]).OfType<Button>().Single(c => c.Name == "storage_next");
        var normalPrev = Descendants(panels[0]).OfType<Button>().Single(c => c.Name == "storage_prev");
        normalNext.EmitSignal(Button.SignalName.Pressed);
        Require(storages[0].Page == 1, "Next page button changes the visible storage page");
        await Capture(0, "warehouse-pagination"); normalPrev.EmitSignal(Button.SignalName.Pressed);
        Require(storages[0].Page == 0, "Previous page button restores the first page");
        var qty = (QuantityPrompt)DetailField(world, "Warehouse.AmountPrompt")!;
        RightClick(normalBag[Inventory.GridStart]);
        Require(qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 100, "Normal storage asks quantity with the full carried stack");
        await Capture(0, "warehouse-quantity");
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Escape }); await Frames(); Require(!qty.Visible, "Escape cancels quantity");
        Variant normalDrag = normalBag[Inventory.GridStart].DragOut!(normalBag[Inventory.GridStart]);
        Require(normalCells[1]._CanDropData(Vector2.Zero, normalDrag), "A left drag accepts a matching stored stack");
        normalCells[1]._DropData(Vector2.Zero, normalDrag);
        Require(qty.Visible, "A drag transfer asks quantity before committing");
        Descendants(qty).OfType<MoneyEdit>().Single().Value = 25;
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Enter }); await Frames();
        Require((bool)DetailField(world, "Warehouse.inFlight")!, "Quantity confirmation starts one transfer");
        DetailCall(world, "Warehouse.OnResult", (byte)3, (byte)1); Require((bool)DetailField(world, "Warehouse.inFlight")!, "Unrelated acknowledgement cannot complete a pending transfer");
        DetailCall(world, "Warehouse.OnResult", (byte)2, (byte)1);
        Require(inventory[Inventory.GridStart].Count == 75 && warehouse[1].Count == 65, "Partial deposit merges into the existing stack after acknowledgement");
        await Capture(0, "warehouse-partial-merged");
        RightClick(normalCells[1]);
        Require(qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 65, "Right-click withdrawal starts with the complete stored count");
        Descendants(qty).OfType<Button>().Single(c => c.Name == "classic_trade_cancel").EmitSignal(Button.SignalName.Pressed);
        Require(!qty.Visible && !(bool)DetailField(world, "Warehouse.inFlight")! && warehouse[1].Count == 65, "Cancel closes quantity without moving the stored item");
        RightClick(normalBag[Inventory.GridStart]); var changed = inventory[Inventory.GridStart]; changed.Count = 74; inventory[Inventory.GridStart] = changed; qty.Confirm();
        Require(!(bool)DetailField(world, "Warehouse.inFlight")!, "Changing the source stack while a prompt is open invalidates its confirmation");
        DetailCall(world, "Warehouse.AskGoldTransfer", true); Require(qty.Visible, "Clicking the coin opens a gold quantity prompt"); await Capture(0, "warehouse-coins"); qty.Close();
        DetailCall(world, "Warehouse.Refresh"); await Frames(2);
        // Mouse-wheel amounts (ff78eef): a part picked on the drag moves at once; the whole stack keeps the count prompt.
        int PendingCount(string field) { var pending = DetailField(world, field)!; return (int)pending.GetType().GetField("Count")!.GetValue(pending)!; }
        Variant Picked(ItemSlotView cell, int amount) { var data = cell.DragOut!(cell); data.AsGodotDictionary()[DragLayer.AmountKey] = amount; return data; }
        var whStatus = (StatusLabel)DetailField(world, "Warehouse.Status")!;
        normalCells[1]._DropData(Vector2.Zero, Picked(normalBag[Inventory.GridStart], 30));
        Require(!qty.Visible && (bool)DetailField(world, "Warehouse.inFlight")! && PendingCount("Warehouse.pending") == 30, "A part picked with the mouse wheel is stored at once, without the prompt");
        DetailCall(world, "Warehouse.OnResult", (byte)2, (byte)0); whStatus.ResetStatus();
        Require(inventory[Inventory.GridStart].Count == 74 && warehouse[1].Count == 65, "A refused part deposit keeps both stacks");
        normalCells[1]._DropData(Vector2.Zero, Picked(normalBag[Inventory.GridStart], 74));
        Require(qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 74 && !(bool)DetailField(world, "Warehouse.inFlight")!,
            "A drag that keeps the whole stack on the wheel still asks the count");
        qty.Close();
        normalBag[Inventory.GridStart + 3]._DropData(Vector2.Zero, Picked(normalCells[1], 5));
        Require(!qty.Visible && (bool)DetailField(world, "Warehouse.inFlight")! && PendingCount("Warehouse.pending") == 5, "A part picked on a stored stack is taken out at once into the chosen bag cell");
        DetailCall(world, "Warehouse.OnResult", (byte)3, (byte)0); whStatus.ResetStatus();
        Require(normalCells[5]._CanDropData(Vector2.Zero, Picked(normalCells[1], 5)), "A stored stack may be dragged to an empty stored cell");
        normalCells[5]._DropData(Vector2.Zero, Picked(normalCells[1], 5));
        Require(!(bool)DetailField(world, "Warehouse.inFlight")! && whStatus.Text == "Only whole stacks move inside the storage." && warehouse[5].IsEmpty,
            "A part of a stack does not move inside the storage (the client's whole-stack rule and text)");
        await Capture(0, "warehouse-whole-stacks-only"); whStatus.ResetStatus();
        normalBag[Inventory.GridStart + 3]._DropData(Vector2.Zero, Picked(normalBag[Inventory.GridStart], 4));
        Require((bool)DetailField(world, "InventoryWindow.moveInFlight")!, "A part picked in the embedded bag starts an inventory move");
        DetailCall(world, "DeliverItemMoveResult", true);
        Require(inventory[Inventory.GridStart].Count == 70 && inventory[Inventory.GridStart + 3].ItemId == potion && inventory[Inventory.GridStart + 3].Count == 4,
            "A part picked in the embedded bag splits the stack, as the client's bag does");
        inventory[Inventory.GridStart + 3] = default; changed.Count = 74; inventory[Inventory.GridStart] = changed; DetailCall(world, "Warehouse.Refresh"); await Frames(2);
        {
            // The real gesture: the client's dial on a dragged stored-bag stack, the wheel over a stored cell.
            var source = normalBag[Inventory.GridStart]; var target = normalCells[3];
            Vector2 from = source.GetGlobalRect().GetCenter(), to = target.GetGlobalRect().GetCenter();
            void Wheel(MouseButton button, Vector2 at, bool dragging = false)
            {
                var mask = dragging ? MouseButtonMask.Left : 0;
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, ButtonMask = mask });
            }
            Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to }); await Frames(2);
            Wheel(MouseButton.WheelDown, to); await Frames(2);
            Require(storages[0].Page == 1, "Outside a drag the wheel over the stored cells still turns the page");
            Wheel(MouseButton.WheelUp, to); await Frames(2);
            Require(storages[0].Page == 0, "The wheel turns back to the first page");
            Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = from + new Vector2(18, 0), GlobalPosition = from + new Vector2(18, 0), Relative = new Vector2(18, 0), ButtonMask = MouseButtonMask.Left }); await Frames(2);
            Require(GetViewport().GuiIsDragging(), "A carried stack is dragged with the pointer");
            Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left }); await Frames(2);
            for (int i = 0; i < 3; i++) Wheel(MouseButton.WheelDown, to, dragging: true);
            await Frames(2);
            // The client's count label sits on the drag icon (a GM build also labels the item id beside it).
            var dragCount = Descendants(GetTree().Root.GetNode("DragLayer")).OfType<Label>().Single(l => l.IsVisibleInTree() && l.GetParent() is TextureRect);
            Require(storages[0].Page == 0 && dragCount.Text == "71" && dragCount.GetThemeColor("font_color") == UiTheme.GoldBright,
                "While a stack is dragged the wheel picks the amount on the client's dial (gold for a part) and does not turn the page");
            // The item art is drawn as a centred square in the client's drag icon (wider in a GM build, which labels the item id).
            var dragIcon = (Control)dragCount.GetParent();
            float side = Math.Min(dragIcon.Size.X, dragIcon.Size.Y);
            var drawn = new Rect2(dragIcon.GetGlobalRect().Position + (dragIcon.Size - new Vector2(side, side)) / 2, new Vector2(side, side));
            Require(dragCount.GetThemeFontSize("font_size") == KnightOnlineUiClassic.Layout.ItemCountStyle.FontSize && dragCount.GetThemeConstant("outline_size") == 3
                && dragCount.GetGlobalRect().End == drawn.End - new Vector2(2, 2) && side == source.Size.Y,
                "The client's drag count takes the cell's Classic count type and corner");
            await Capture(0, "warehouse-wheel-drag");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero }); await Frames(2);
            Require(!GetViewport().GuiIsDragging() && !qty.Visible && (bool)DetailField(world, "Warehouse.inFlight")! && PendingCount("Warehouse.pending") == 71,
                "Dropping the dragged part stores the picked amount without the prompt");
            DetailCall(world, "Warehouse.OnResult", (byte)2, (byte)0); whStatus.ResetStatus();
        }
        {
            // The storage's bag companion (a right-click or a drop in the Classic bag window) takes the same route.
            var companionType = Native.ClientType("LibreKO.Features.BagCompanion")!;
            var fitType = Native.ClientType("LibreKO.Features.BagFit")!;
            Delegate Bind(Type type, string path) => Native.Get<object>(world, path[..path.LastIndexOf('.')]) is { } owner
                ? Delegate.CreateDelegate(type, owner, owner.GetType().GetMethod(path[(path.LastIndexOf('.') + 1)..], BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!)
                : throw new Exception("No client object at " + path);
            var companion = Activator.CreateInstance(companionType, Bind(typeof(Func<int, bool>), "Warehouse.TakeFromBag"), Bind(typeof(Func<,>).MakeGenericType(typeof(int), fitType), "Warehouse.WhBagFit"),
                (Func<int, string>)(_ => ""), Bind(typeof(Action), "Warehouse.Close"), Bind(typeof(Func<int, int, int?, bool>), "Warehouse.WithdrawInto"))!;
            Set("Warehouse.companion", companion); await Frames(2);
            var take = (Func<int, bool>)companionType.GetProperty("Take")!.GetValue(companion)!;
            var intoBag = (Func<int, int, int?, bool>)companionType.GetProperty("IntoBag")!.GetValue(companion)!;
            Require(take(Inventory.GridStart) && qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 74 && !(bool)DetailField(world, "Warehouse.inFlight")!,
                "A right-click in the bag window while the storage is open asks the count through the storage's bag companion");
            qty.Close();
            Require(intoBag(1, Inventory.GridStart + 3, null) && qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 65,
                "A stored stack dropped on the bag window asks the count through the bag companion");
            qty.Close();
            Require(intoBag(1, Inventory.GridStart + 3, 7) && !qty.Visible && (bool)DetailField(world, "Warehouse.inFlight")! && PendingCount("Warehouse.pending") == 7,
                "A part picked for the bag window is taken out at once");
            DetailCall(world, "Warehouse.OnResult", (byte)3, (byte)0); whStatus.ResetStatus();
        }
        var vip = (NativeSlot[])DetailField(world, "VipWarehouse.StoredItems")!; vip[0] = warehouse[0]; vip[1] = warehouse[1]; vip[47] = warehouse[2];
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Set("Warehouse.shown", false); Set("VipWarehouse.shown", true); Set("VipWarehouse.open", true); Set("VipWarehouse.expiresAt", now + 172800u); DetailCall(world, "VipWarehouse.Refresh");
        await Capture(1, "vip-ready");
        var vipKey = Descendants(panels[1]).OfType<ItemSlotView>().Single(c => c.Name == "storage_key");
        Require(!vipKey.Item.IsEmpty && vipKey.IsVisibleInTree() && vipKey.GetRect() == KnightOnlineUiClassic.Windows.ClassicStorageLayout.Key,
            "VIP key plaque shows the active key beside the expiry field");
        string expiryText = ((Label)DetailField(world, "VipWarehouse.expiryLbl")!).Text;
        Require(expiryText.StartsWith("2d") || expiryText.StartsWith("1d"), "VIP expiry shows the remaining rental time");
        var vipCells = Descendants(panels[1]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToDictionary(c => c.Index);
        Require(vipCells.Values.All(c => !c.Locked), "An open VIP vault shows unlocked cells");
        Variant vipDrag = vipCells[0].DragOut!(vipCells[0]);
        Require(vipCells[5]._CanDropData(Vector2.Zero, vipDrag), "VIP accepts a move into an empty slot");
        vipCells[5]._DropData(Vector2.Zero, vipDrag);
        Require((bool)DetailField(world, "VipWarehouse.inFlight")! && !vip[0].IsEmpty, "VIP rearrangement waits for the server");
        DetailCall(world, "VipWarehouse.OnResult", (byte)VipWarehouseSubOpcode.Store, (byte)VipWarehouseResult.Succeeded);
        Require(vip[0].IsEmpty && vip[5].ItemId == weapon, "VIP rearrangement commits only after acknowledgement");
        await Capture(1, "vip-rearranged");
        RightClick(Descendants(panels[1]).OfType<ItemSlotView>().Single(c => c.Name == "storage_bag_0"));
        var vipAmount = storages[1].Amount!;
        Require(vipAmount == DetailField(world, "VipWarehouse.amountPrompt") && vipAmount.Visible && Descendants(vipAmount).OfType<MoneyEdit>().Single().Value == 74,
            "VIP storage asks quantity in the client's prompt rather than moving the entire stack");
        await Capture(1, "vip-quantity"); Descendants(vipAmount).OfType<MoneyEdit>().Single().Value = 10; vipAmount.Confirm();
        DetailCall(world, "VipWarehouse.OnResult", (byte)VipWarehouseSubOpcode.Input, (byte)VipWarehouseResult.Succeeded);
        Require(inventory[Inventory.GridStart].Count == 64 && vip[1].Count == 75, "VIP partial deposit preserves the remainder and merges");
        storages[1].TurnPage(3); storages[1].TurnPage(1);
        Require(storages[1].Page == 3 && storages[1].Pages == 4, "Classic VIP pages show all 48 slots and clamp at the last page");
        await Capture(1, "vip-last-page");
        Require(vipCells[11].Item.ItemId == warehouse[2].ItemId, "The last VIP page shows slot 48");
        storages[1].TurnPage(-3);
        DetailCall(world, "VipWarehouse.ShowPinDialog", (byte)VipWarehouseSubOpcode.SetPassword, "Choose a new 4-digit PIN:"); await Capture(1, "vip-pin");
        pin.Input.Text = "12ab"; pin.ConfirmButton.EmitSignal(Button.SignalName.Pressed);
        Require(pin.Visible && pin.Message.Text == ((Label)DetailField(world, "VipWarehouse.pinPrompt")!).Text && pin.Message.Text.Contains("4 digits"),
            "PIN rejects non-digits without sending and shows the client's reprompt"); await Capture(1, "vip-invalid-pin"); pin.Hide();
        var setPin = Descendants(panels[1]).OfType<Button>().Single(b => b.Name == "storage_pin_set");
        var clearPin = Descendants(panels[1]).OfType<Button>().Single(b => b.Name == "storage_pin_clear");
        Require(setPin.Text == "Set PIN" && clearPin.Visible && clearPin.Disabled, "Without a PIN the vault offers Set PIN and keeps Clear PIN visible but disabled");
        Set("VipWarehouse.pinSet", true); DetailCall(world, "VipWarehouse.Refresh"); await Capture(1, "vip-pin-set");
        Require(setPin.Text == "Change PIN" && clearPin.Visible && !clearPin.Disabled, "With a PIN the vault offers Change PIN and Clear PIN");
        {
            var vipBag = Descendants(panels[1]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
            var vipStatusLabel = (StatusLabel)DetailField(world, "VipWarehouse.Status")!;
            vipBag[Inventory.GridStart + 3]._DropData(Vector2.Zero, Picked(vipCells[1], 5));
            Require(!vipAmount.Visible && (bool)DetailField(world, "VipWarehouse.inFlight")! && PendingCount("VipWarehouse.pending") == 5, "VIP: a part picked with the mouse wheel is taken out at once");
            DetailCall(world, "VipWarehouse.OnResult", (byte)VipWarehouseSubOpcode.Output, (byte)VipWarehouseResult.Failed); vipStatusLabel.ResetStatus();
            vipCells[1]._DropData(Vector2.Zero, Picked(vipBag[Inventory.GridStart], 64));
            Require(vipAmount.Visible && Descendants(vipAmount).OfType<MoneyEdit>().Single().Value == 64 && !(bool)DetailField(world, "VipWarehouse.inFlight")!, "VIP: a whole stack dragged in asks the count");
            vipAmount.Close();
            vipCells[6]._DropData(Vector2.Zero, Picked(vipCells[1], 5));
            Require(!(bool)DetailField(world, "VipWarehouse.inFlight")! && vipStatusLabel.Text == "Only whole stacks move inside the storage." && vip[6].IsEmpty,
                "VIP: a part of a stack does not move inside the vault");
            await Capture(1, "vip-whole-stacks-only"); vipStatusLabel.ResetStatus();
        }
        var clan = (NativeSlot[])DetailField(world, "ClanWarehouse.slots")!; clan[0] = warehouse[0]; clan[1] = warehouse[1]; clan[191] = warehouse[2];
        Set("VipWarehouse.shown", false); Set("ClanWarehouse.shown", true); Set("ClanWarehouse.money", 9_000_000);
        Native.Set(Game.Session.Clan, "MyClan", new MyClanInfo { InClan = true, Fame = ClanRanks.Trainee });
        DetailCall(world, "ClanWarehouse.Refresh"); await Capture(2, "clan-member");
        var clanDeposit = Descendants(panels[2]).OfType<Button>().Single(b => b.Name == "storage_deposit");
        var clanWithdraw = Descendants(panels[2]).OfType<Button>().Single(b => b.Name == "storage_withdraw");
        Require(!clanDeposit.Disabled && clanWithdraw.Disabled, "Clan members may deposit coins but not withdraw them");
        RightClick(Descendants(panels[2]).OfType<ItemSlotView>().Single(c => c.Name == "storage_cell_1"));
        var clanStatus = (Label)DetailField(world, "ClanWarehouse.status")!;
        Require(!storages[2].Amount!.Visible && !(bool)DetailField(world, "ClanWarehouse.inFlight")! && clanStatus.Text.StartsWith("Only the chief"), "Clan members cannot initiate withdrawal");
        await Capture(2, "clan-member-refused");
        RightClick(Descendants(panels[2]).OfType<ItemSlotView>().Single(c => c.Name == "storage_bag_1"));
        Require((bool)DetailField(world, "ClanWarehouse.inFlight")!, "Clan members may store items (the client's clan-bank rights)");
        DetailCall(world, "ClanWarehouse.OnResult", (byte)WarehouseSubOpcode.Input, (byte)ClanWarehouseResult.Failed);
        Require(!inventory[Inventory.GridStart + 1].IsEmpty, "A refused clan deposit keeps the carried item");
        Native.Set(Game.Session.Clan, "MyClan", new MyClanInfo { InClan = true, Fame = ClanRanks.Chief });
        DetailCall(world, "ClanWarehouse.Refresh");
        Require(!clanWithdraw.Disabled, "The clan chief may withdraw coins");
        RightClick(Descendants(panels[2]).OfType<ItemSlotView>().Single(c => c.Name == "storage_cell_1"));
        var clanAmount = storages[2].Amount!;
        Require(clanAmount == DetailField(world, "ClanWarehouse.amountPrompt") && clanAmount.Visible, "Clan officers can select a withdrawal quantity"); await Capture(2, "clan-quantity");
        Descendants(clanAmount).OfType<MoneyEdit>().Single().Value = 5; clanAmount.Confirm(); DetailCall(world, "ClanWarehouse.OnResult", (byte)WarehouseSubOpcode.Output, (byte)ClanWarehouseResult.Succeeded);
        Require(inventory[Inventory.GridStart].Count == 69 && clan[1].Count == 60, "Clan partial withdrawal merges into the carried stack");
        Require(!clanStatus.Text.StartsWith("Only the chief"), "A valid clan transfer replaces an obsolete permission warning");
        var clanCells = Descendants(panels[2]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToDictionary(c => c.Index);
        {
            var clanBag = Descendants(panels[2]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
            clanCells[1]._DropData(Vector2.Zero, Picked(clanBag[Inventory.GridStart], 3));
            Require(!clanAmount.Visible && (bool)DetailField(world, "ClanWarehouse.inFlight")! && PendingCount("ClanWarehouse.pending") == 3, "Clan bank: a part picked with the mouse wheel is stored at once");
            DetailCall(world, "ClanWarehouse.OnResult", (byte)WarehouseSubOpcode.Input, (byte)ClanWarehouseResult.Failed);
            clanBag[Inventory.GridStart + 3]._DropData(Vector2.Zero, Picked(clanCells[1], 60));
            Require(clanAmount.Visible && Descendants(clanAmount).OfType<MoneyEdit>().Single().Value == 60 && !(bool)DetailField(world, "ClanWarehouse.inFlight")!, "Clan bank: a whole stack dragged out asks the count");
            clanAmount.Close();
            Require(inventory[Inventory.GridStart].Count == 69 && clan[1].Count == 60, "Clan bank: refused and cancelled transfers keep both stacks");
            ((StatusLabel)clanStatus).ResetStatus();
        }
        Variant clanDrag = clanCells[0].DragOut!(clanCells[0]);
        Require(clanCells[5]._CanDropData(Vector2.Zero, clanDrag), "Clan officers may rearrange the clan bank");
        clanCells[5]._DropData(Vector2.Zero, clanDrag); DetailCall(world, "ClanWarehouse.OnResult", (byte)WarehouseSubOpcode.Move, (byte)ClanWarehouseResult.Succeeded);
        Require(clan[0].IsEmpty && clan[5].ItemId == weapon, "Clan rearrangement preserves the item");
        await Capture(2, "clan-rearranged");
        clanDeposit.EmitSignal(Button.SignalName.Pressed);
        Require(clanAmount.Visible, "The clan coin button opens the client's gold prompt"); await Capture(2, "clan-coins"); clanAmount.Close();
        storages[2].TurnPage(7); storages[2].TurnPage(1);
        Require(storages[2].Page == 7 && (int)DetailField(world, "ClanWarehouse.page")! == 7, "Classic clan wheel navigation follows the client's page and clamps at the last page"); await Capture(2, "clan-last-page");
        var carriedCells = Descendants(panels[2]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
        Variant carriedDrag = new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart + 1 } };
        Require(carriedCells[Inventory.GridStart + 10]._CanDropData(Vector2.Zero, carriedDrag), "The embedded inventory permits ordinary bag rearrangement");
        carriedCells[Inventory.GridStart + 10]._DropData(Vector2.Zero, carriedDrag);
        Require((bool)DetailField(world, "InventoryWindow.moveInFlight")!, "Bag rearrangement uses the existing inventory move protocol");
        DetailCall(world, "DeliverItemMoveResult", true);
        Require(inventory[Inventory.GridStart + 1].IsEmpty && inventory[Inventory.GridStart + 10].ItemId == weapon, "Inventory acknowledgement refreshes the embedded bag cells");
        await Capture(2, "clan-inventory-rearranged");
        Set("ClanWarehouse.shown", false); Set("VipWarehouse.shown", true);
        var vipStatus = (Label)DetailField(world, "VipWarehouse.Status")!;
        inventory[Inventory.GridStart + 5] = new NativeSlot { ItemId = ItemData.NoTradeIdFirst, Count = 1, Durability = 1 };
        DetailCall(world, "VipWarehouse.Refresh"); for (int i = 0; i < 3; i++) windows[i].Visible = i == 1; await Frames(2);
        var vipBag5 = Descendants(panels[1]).OfType<ItemSlotView>().Single(c => c.Name == "storage_bag_5");
        Require(vipBag5.Look == SlotLook.Unfit, "The embedded bag marks items the vault refuses, as the client's bag does");
        RightClick(vipBag5);
        Require(!vipAmount.Visible && !(bool)DetailField(world, "VipWarehouse.inFlight")! && vipStatus.Text.Length > 0, "VIP storage refuses a non-storable item with the client's text");
        inventory[Inventory.GridStart + 5] = default;
        Set("VipWarehouse.open", false); Set("VipWarehouse.expiresAt", now - 60u); DetailCall(world, "VipWarehouse.Refresh");
        RightClick(Descendants(panels[1]).OfType<ItemSlotView>().Single(c => c.Name == "storage_bag_0"));
        Require(!vipAmount.Visible && !(bool)DetailField(world, "VipWarehouse.inFlight")! && vipStatus.Text == ItemData.Text(1673, "You cannot use it due to expiration."),
            "An expired VIP vault refuses transfers with the expiration text");
        await Capture(1, "vip-expired");
        Require(vipCells.Values.All(c => c.Locked == !c.Item.IsEmpty) && ((Label)DetailField(world, "VipWarehouse.expiryLbl")!).Text == "expired", "An expired VIP vault locks its stored items and shows the expiry");
        Require(vipCells[0].DragOut!(vipCells[0]).VariantType == Variant.Type.Nil, "Locked VIP cells cannot be dragged");
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-verification.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"Storage audit passed: {checks.Count} checks, {screens.Count} screenshots, nation {nation}");
        world.Free(); net.Free();
        static void RequireDefinition(int id) { if (ItemData.Get(id) == null) throw new Exception("Missing item " + id); }
    }
}
