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
        typeof(Net).GetProperty("LastEnter")!.SetValue(net, new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Storage Preview", Gear = new int[8], Inventory = new NativeSlot[InventoryConstants.InventoryTotal] });
        net.Sheet.SeedWealth(1_234_567, 50_000); net.Sheet.SetMaxWeight(17100);
        ItemData.EnsureLoaded();
        var world = new World();
        var inventory = (Inventory)typeof(World).GetProperty("Inv", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        int potion = 389010000, weapon = 156210008;
        RequireDefinition(potion); RequireDefinition(weapon);
        inventory[Inventory.GridStart] = new NativeSlot { ItemId = potion, Count = 100, Durability = 1 };
        inventory[Inventory.GridStart + 1] = new NativeSlot { ItemId = weapon, Count = 1, Durability = 7000 };
        inventory[Inventory.GridStart + 2] = new NativeSlot { ItemId = 379021000, Count = 1, Durability = 1 };
        DetailCall(world, "BuildInventoryPanel");
        var orphan = (Control)DetailField(world, "_invContent")!; orphan.Visible = false; world.AddChild(orphan);
        DetailCall(world, "BuildWarehousePanel"); DetailCall(world, "BuildVipWarehousePanel"); DetailCall(world, "BuildClanWarehousePanel");
        var bridge = Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge", BindingFlags.NonPublic)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { world }, null)!;
        var attach = typeof(PluginGame).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!;
        attach.Invoke(game, Enumerable.Repeat(bridge, attach.GetParameters().Length).ToArray());
        foreach (string field in new[] { "_whLayer", "_vipWhLayer", "_clanWhLayer", "_itemTipLayer", "_whAmount", "_vipWhAmount", "_clanWhAmount", "_vipWhPinDlg" })
            ((Node)DetailField(world, field)!).Reparent(this);
        var windows = new[] { "_whPanel", "_vipWhPanel", "_clanWhPanel" }.Select(f => (HudWindow)DetailField(world, f)!).ToArray();
        var panels = windows.Select(w => ClassicStorageSkin.Apply(w.Body)!).ToArray();
        var pin = (VipVaultPinPrompt)DetailField(world, "_vipWhPinDlg")!; ClassicStoragePin.Apply(pin.Window.Body);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string message) { if (!valid) throw new Exception("STORAGE_AUDIT: " + message); checks.Add(message); }
        void Set(string field, object value) => typeof(World).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, value);
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Capture(int index, string name)
        {
            for (int i = 0; i < 3; i++) windows[i].Visible = i == index;
            await Frames(); windows[index].Position = new Vector2(216, 42); await Frames(2);
            var controls = Descendants(panels[index]).OfType<Control>().Where(c => c.IsVisibleInTree()).ToArray();
            foreach (var c in controls.Where(c => c.HasMeta("storage_expected_rect")))
                Require(c.GetRect() == c.GetMeta("storage_expected_rect").AsRect2(), "Measured control bounds: " + c.Name + " / " + name + " actual=" + c.GetRect() + " expected=" + c.GetMeta("storage_expected_rect").AsRect2());
            Require(windows[index].Size == new Vector2(366, 553), "Original storage window proportions / " + name);
            Require(GetViewportRect().Encloses(windows[index].GetGlobalRect()), "Complete storage window fits viewport / " + name);
            var bag = controls.OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToArray();
            Require(bag.Length == 28, "All 28 inventory slots visible / " + name);
            Require(!controls.OfType<LineEdit>().Any(), "Classic storage has no search input / " + name);
            Require(!controls.OfType<Label>().Any(c => c.Text.StartsWith("Right-click") || c.Text.StartsWith("Slots used") || c.Text.Contains("slots ·") || c.Text.StartsWith("Members may")), "Redundant storage instructions and capacity text are hidden / " + name);
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
                var icon = Descendants(cell).OfType<TextureRect>().Single();
                Require(icon.GetGlobalRect() == new Rect2(cell.GlobalPosition + new Vector2(2, 2), cell.Size - new Vector2(4, 4)), "Inventory-aligned icon inset / " + name);
                Require(cell.CountLabel.GetGlobalRect() == new Rect2(cell.GlobalPosition, cell.Size - new Vector2(2, 2)), "Inventory-aligned bottom-right count / " + name);
                Require(cell.CountLabel.GetThemeConstant("outline_size") == 3 && cell.CountLabel.GetThemeFontSize("font_size") == 11, "Shared inventory count typography / " + name);
                Require(icon.GetParent().GetIndex() < cell.GetChildren().OfType<UpgradeBadge>().Single().GetIndex(), "Item icon remains below its upgrade badge / " + name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + name + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { name, file, controls = controls.Where(c => c.HasMeta("storage_expected_rect")).Select(c => new { name = c.Name.ToString(), rect = c.GetGlobalRect().ToString() }).ToArray() });
        }
        var warehouse = (NativeSlot[])DetailField(world, "_warehouse")!;
        Set("_whShown", true); Set("_whMoney", 4_820_000);
        DetailCall(world, "RefreshWarehouse"); await Capture(0, "warehouse-empty");
        warehouse[0] = new NativeSlot { ItemId = weapon, Count = 1, Durability = 6200 };
        warehouse[1] = new NativeSlot { ItemId = potion, Count = 40, Durability = 1 };
        warehouse[2] = new NativeSlot { ItemId = 379021000, Count = 1, Durability = 1 };
        warehouse[30] = warehouse[0]; warehouse[191] = warehouse[2];
        DetailCall(world, "RefreshWarehouse"); await Capture(0, "warehouse-ready");
        var normalBag = Descendants(panels[0]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
        var normalCells = Descendants(panels[0]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_") && c.Visible).ToDictionary(c => c.Index);
        void RightClick(ItemSlotView cell) => cell._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
        normalBag[Inventory.GridStart]._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
        normalBag[Inventory.GridStart]._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left });
        Require(!(bool)DetailField(world, "_whInFlight")!, "A plain left click does not transfer an item");
        RightClick(normalBag[Inventory.GridStart + 2]);
        Require((bool)DetailField(world, "_whInFlight")! && !((QuantityPrompt)DetailField(world, "_whAmount")!).Visible, "A single countable unit transfers directly after right-click");
        DetailCall(world, "OnWarehouseResult", (byte)2, false);
        Require(inventory[Inventory.GridStart + 2].Count == 1, "A refused single-unit transfer preserves the source");
        ((StatusLabel)DetailField(world, "_whStatus")!).ResetStatus();
        Require(Descendants(panels[0]).OfType<ItemSlotView>().Count(c => c.Name.ToString().StartsWith("storage_cell_") && c.IsVisibleInTree()) == 24, "Classic storage has 24 slots per page");
        for (int i = 0; i < 9; i++) DetailCall(world, "TurnWhPage", 1);
        Require((int)DetailField(world, "_whPage")! == 7, "Last of eight pages is reachable and clamps at the end");
        await Capture(0, "warehouse-last-page");
        DetailCall(world, "TurnWhPage", -7);
        var normalNext = Descendants(panels[0]).OfType<Button>().Single(c => c.Name == "storage_next");
        var normalPrev = Descendants(panels[0]).OfType<Button>().Single(c => c.Name == "storage_prev");
        normalNext.EmitSignal(Button.SignalName.Pressed);
        Require((int)DetailField(world, "_whPage")! == 1, "Next page button changes the visible storage page");
        await Capture(0, "warehouse-pagination"); normalPrev.EmitSignal(Button.SignalName.Pressed);
        Require((int)DetailField(world, "_whPage")! == 0, "Previous page button restores the first page");
        var qty = (QuantityPrompt)DetailField(world, "_whAmount")!;
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
        Require((bool)DetailField(world, "_whInFlight")!, "Quantity confirmation starts one transfer");
        DetailCall(world, "OnWarehouseResult", (byte)3, true); Require((bool)DetailField(world, "_whInFlight")!, "Unrelated acknowledgement cannot complete a pending transfer");
        DetailCall(world, "OnWarehouseResult", (byte)2, true);
        Require(inventory[Inventory.GridStart].Count == 75 && warehouse[1].Count == 65, "Partial deposit merges into the existing stack after acknowledgement");
        await Capture(0, "warehouse-partial-merged");
        RightClick(normalCells[1]);
        Require(qty.Visible && Descendants(qty).OfType<MoneyEdit>().Single().Value == 65, "Right-click withdrawal starts with the complete stored count");
        Descendants(qty).OfType<Button>().Single(c => c.Name == "classic_trade_cancel").EmitSignal(Button.SignalName.Pressed);
        Require(!qty.Visible && !(bool)DetailField(world, "_whInFlight")! && warehouse[1].Count == 65, "Cancel closes quantity without moving the stored item");
        DetailCall(world, "DepositSlot", Inventory.GridStart, -1); var changed = inventory[Inventory.GridStart]; changed.Count = 74; inventory[Inventory.GridStart] = changed; qty.Confirm();
        Require(!(bool)DetailField(world, "_whInFlight")!, "Changing the source stack while a prompt is open invalidates its confirmation");
        DetailCall(world, "AskGoldTransfer", true); Require(qty.Visible, "Clicking the coin opens a gold quantity prompt"); await Capture(0, "warehouse-coins"); qty.Close();
        var vip = (NativeSlot[])DetailField(world, "_vipWh")!; vip[0] = warehouse[0]; vip[1] = warehouse[1]; vip[47] = warehouse[2];
        Set("_whShown", false); Set("_vipWhShown", true); Set("_vipWhExpirySec", 172800); DetailCall(world, "RefreshVipWarehouse");
        await Capture(1, "vip-ready");
        var vipCells = Descendants(panels[1]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToDictionary(c => c.Index);
        Variant vipDrag = vipCells[0].DragOut!(vipCells[0]);
        Require(vipCells[5]._CanDropData(Vector2.Zero, vipDrag), "VIP accepts a same-page move into an empty slot");
        vipCells[5]._DropData(Vector2.Zero, vipDrag); DetailCall(world, "OnVipWarehouseResult", (byte)4, true);
        Require(vip[0].IsEmpty && vip[5].ItemId == weapon, "VIP rearrangement commits only after acknowledgement");
        await Capture(1, "vip-rearranged");
        RightClick(Descendants(panels[1]).OfType<ItemSlotView>().Single(c => c.Name == "storage_bag_0"));
        var vipAmount = (QuantityPrompt)DetailField(world, "_vipWhAmount")!;
        Require(vipAmount.Visible && Descendants(vipAmount).OfType<MoneyEdit>().Single().Value == 74, "VIP storage asks quantity rather than moving the entire stack");
        await Capture(1, "vip-quantity"); Descendants(vipAmount).OfType<MoneyEdit>().Single().Value = 10; vipAmount.Confirm(); DetailCall(world, "OnVipWarehouseResult", (byte)2, true);
        Require(inventory[Inventory.GridStart].Count == 64 && vip[1].Count == 75, "VIP partial deposit preserves the remainder and merges");
        DetailCall(world, "ChangeVipWhPage", 3); DetailCall(world, "ChangeVipWhPage", 1);
        Require((int)DetailField(world, "_vipWhPage")! == 3, "Classic VIP wheel navigation clamps at the last page"); await Capture(1, "vip-last-page"); DetailCall(world, "ChangeVipWhPage", -3);
        DetailCall(world, "ShowVipPinDialog", Net.VipWhSetPinSub, "Choose a new 4-digit PIN:"); await Capture(1, "vip-pin");
        pin.Input.Text = "12ab"; DetailCall(world, "SubmitVipPin");
        Require(pin.Visible && pin.Message.Text.Contains("exactly 4 digits"), "PIN rejects non-digits without sending"); await Capture(1, "vip-invalid-pin"); pin.Hide();
        var clan = (NativeSlot[])DetailField(world, "_clanWh")!; clan[0] = warehouse[0]; clan[1] = warehouse[1]; clan[191] = warehouse[2];
        Set("_vipWhShown", false); Set("_clanWhShown", true); Set("_clanWhLoaded", true); Set("_clanWhMoney", 9_000_000);
        typeof(Net).GetProperty("MyClan")!.SetValue(net, new MyClanInfo { InClan = true, Fame = ClanRanks.Trainee });
        DetailCall(world, "RefreshClanWarehouse"); await Capture(2, "clan-member");
        DetailCall(world, "ClanWhWithdrawSlot", 1); Require(!((QuantityPrompt)DetailField(world, "_clanWhAmount")!).Visible && !(bool)DetailField(world, "_clanWhInFlight")!, "Clan members cannot initiate withdrawal");
        await Capture(2, "clan-member-refused");
        typeof(Net).GetProperty("MyClan")!.SetValue(net, new MyClanInfo { InClan = true, Fame = ClanRanks.Chief });
        RightClick(Descendants(panels[2]).OfType<ItemSlotView>().Single(c => c.Name == "storage_cell_1"));
        var clanAmount = (QuantityPrompt)DetailField(world, "_clanWhAmount")!;
        Require(clanAmount.Visible, "Clan officers can select a withdrawal quantity"); await Capture(2, "clan-quantity");
        Descendants(clanAmount).OfType<MoneyEdit>().Single().Value = 5; clanAmount.Confirm(); DetailCall(world, "OnClanWhResult", (byte)3, true);
        Require(inventory[Inventory.GridStart].Count == 69 && clan[1].Count == 60, "Clan partial withdrawal merges into the carried stack");
        Require(((Label)DetailField(world, "_clanWhStatus")!).Text == "", "A valid clan transfer clears an obsolete permission warning");
        var clanCells = Descendants(panels[2]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_cell_")).ToDictionary(c => c.Index);
        Variant clanDrag = clanCells[0].DragOut!(clanCells[0]);
        Require(clanCells[5]._CanDropData(Vector2.Zero, clanDrag), "Clan officers may rearrange the current page");
        clanCells[5]._DropData(Vector2.Zero, clanDrag); DetailCall(world, "OnClanWhResult", (byte)4, true);
        Require(clan[0].IsEmpty && clan[5].ItemId == weapon, "Clan rearrangement preserves the item");
        await Capture(2, "clan-rearranged");
        DetailCall(world, "ChangeClanWhPage", 7); DetailCall(world, "ChangeClanWhPage", 1);
        Require((int)DetailField(world, "_clanWhPage")! == 7, "Classic clan wheel navigation clamps at the last page"); await Capture(2, "clan-last-page");
        var carriedCells = Descendants(panels[2]).OfType<ItemSlotView>().Where(c => c.Name.ToString().StartsWith("storage_bag_")).ToDictionary(c => c.Index);
        Variant carriedDrag = new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart + 1 } };
        Require(carriedCells[Inventory.GridStart + 10]._CanDropData(Vector2.Zero, carriedDrag), "The embedded inventory permits ordinary bag rearrangement");
        carriedCells[Inventory.GridStart + 10]._DropData(Vector2.Zero, carriedDrag);
        Require((bool)DetailField(world, "_moveInFlight")!, "Bag rearrangement uses the existing inventory move protocol");
        DetailCall(world, "OnItemMoveResult", true);
        Require(inventory[Inventory.GridStart + 1].IsEmpty && inventory[Inventory.GridStart + 10].ItemId == weapon, "Inventory acknowledgement refreshes the embedded bag cells");
        await Capture(2, "clan-inventory-rearranged");
        Set("_vipWhExpirySec", 0); DetailCall(world, "RefreshVipWarehouse");
        Require((int)DetailField(world, "_vipWhExpirySec")! == 0, "VIP expiry invalidates the displayed rental time");
        Set("_vipWhShown", true); DetailCall(world, "VipDepositSlot", Inventory.GridStart);
        Require(!vipAmount.Visible && !(bool)DetailField(world, "_vipWhInFlight")!, "An expired VIP vault cannot initiate another transfer");
        await Capture(1, "vip-expired");
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-verification.json", JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"Storage audit passed: {checks.Count} checks, {screens.Count} screenshots, nation {nation}");
        world.Free(); net.Free();
        static void RequireDefinition(int id) { if (ItemData.Get(id) == null) throw new Exception("Missing item " + id); }
    }
}
