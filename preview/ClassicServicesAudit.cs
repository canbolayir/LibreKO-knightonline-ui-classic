using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureClassicServices(PluginGame game, int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net, new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Service Preview", Gear = new int[8], Inventory = new ItemSlot[InventoryConstants.InventoryTotal] });
        ItemData.EnsureLoaded();
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("SERVICE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(World w, string field, object value) => typeof(World).GetField(field, flags)!.SetValue(w, value);
        Inventory InventoryOf(World w) => (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(w)!;
        async Task Capture(HudWindow window, string state)
        {
            window.Visible = true; await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window fits viewport");
            var panel = window.GetChildren().OfType<ClassicServicePanel>().Single();
            Require(window.Size == panel.CustomMinimumSize, state + " fixed service proportions");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && c.HasMeta("service_expected_rect")))
            {
                Require(control.GetRect() == control.GetMeta("service_expected_rect").AsRect2(), state + " measured bounds " + control.Name + " actual=" + control.GetRect());
                Require(new Rect2(Vector2.Zero, panel.Size).Encloses(control.GetRect()), state + " content stays inside frame " + control.Name);
            }
            foreach (var cell in Descendants(panel).OfType<ItemSlotView>().Where(c => c.IsVisibleInTree() && c.HasMeta("service_expected_rect")))
            {
                var icon = Descendants(cell).OfType<TextureRect>().Single();
                Require(icon.GetGlobalRect() == new Rect2(cell.GlobalPosition + new Vector2(2, 2), cell.Size - new Vector2(4, 4)), state + " inventory-aligned icon " + cell.Name);
                Require(cell.CountLabel.GetGlobalRect() == new Rect2(cell.GlobalPosition, cell.Size - new Vector2(2, 2)), state + " inventory-aligned count " + cell.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file,
                labels = Descendants(panel).OfType<Label>().Where(c => c.IsVisibleInTree()).Select(c => new { text = c.Text, rect = c.GetRect().ToString(), parent = c.GetParent().Name.ToString() }).ToArray() });
        }
        async Task<(World, HudWindow)> Build(string method, params object[] args)
        {
            var world = new World(); var window = (HudWindow)DetailCall(world, method, args)!;
            AddChild(window); NativeServices.Prepare(window, world); ClassicServiceSkin.Apply(window.Body); await Frames(); return (world, window);
        }
        Control PieceDropTarget(World world) => Descendants((Node)DetailField(world, "_pieceSocket")!).OfType<ServiceDropTarget>().Single();
        void Dispose(World world, HudWindow window)
        {
            if (DetailField(world, "_invContent") is Control orphan && GodotObject.IsInstanceValid(orphan) && orphan.GetParent() == null) orphan.Free();
            window.Free(); world.Free();
        }

        foreach (bool blocked in new[] { false, true })
        {
            var (world, window) = await Build("BuildWarpUiPreview", blocked);
            await Capture(window, blocked ? "warp-blocked" : "warp-ready");
            Require(((Button)DetailField(world, "_warpTravel")!).Disabled == blocked, "Warp retains the server fee / level gate " + blocked);
            var rows = (List<PanelContainer>)DetailField(world, "_warpRows")!;
            Require(rows.All(r => r.Size.Y <= 19), "Warp uses original dense single-line text rows");
            var oldPositions = rows.Select(r => r.Position).ToArray();
            DetailCall(world, "SelectWarpRow", 1); await Frames();
            Require(rows.Select(r => r.Position).SequenceEqual(oldPositions), "Changing warp selection does not shift list rows");
            DetailCall(world, "SelectWarpRow", blocked ? 6 : 2); await Frames();
            if (!blocked)
            {
                var description = Descendants(window).OfType<Label>().Single(l => l.Name == "warp_description");
                description.Text = string.Join("\n\n", Enumerable.Repeat("El Morad Castle is the capital of the Human nation. Visit the town for merchants, quests and the magic anvil. Travel requires the displayed fee and the destination's level range.", 6));
                await Capture(window, "warp-description");
                var scroll = (ScrollContainer)description.GetParent().GetParent();
                Require(scroll.Size == new Vector2(290, 135) && scroll.GetVScrollBar().Visible, "Long warp descriptions scroll inside their fixed original panel");
                Require(description.Size.X <= scroll.Size.X - scroll.GetVScrollBar().Size.X, "Warp text wraps before the scrollbar instead of clipping under it");
            }
            DetailCall(world, "CloseWarp"); Require(!window.Visible, "Warp cancel closes the native window");
            Dispose(world, window); await Frames();
        }
        var sealMode = typeof(World).GetNestedType("SealMode", BindingFlags.NonPublic | BindingFlags.Public)!;
        foreach (bool bind in new[] { false, true })
        {
            var (world, window) = await Build("BuildSealUiPreview", Enum.ToObject(sealMode, bind ? 1 : 0), true, ItemFlag.Unsealed, false);
            await Capture(window, bind ? "seal-bind" : "seal-secret");
            Require(((Button)DetailField(world, "_sealConfirm")!).Disabled == !bind, "Secret sealing requires eight digits; binding does not");
            if (!bind)
            {
                DetailCall(world, "ShowSealKeypadUiPreview"); await Capture(window, "seal-keypad");
                for (int i = 0; i < 3; i++) DetailCall(world, "PushSealDigit", i);
                Require(!((Button)DetailField(world, "_sealConfirm")!).Disabled, "Eight secret digits enable the confirmation");
                DetailCall(world, "AskSealConfirm");
                var notice = (Notice)DetailField(world, "_sealNotice")!; notice.Reparent(this);
                await Frames();
                string file = (nation == 1 ? "karus" : "human") + "-seal-approval.png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state = "seal-approval", file });
                var dialog = notice.GetChildren().OfType<ClassicServiceNotice>().Single();
                dialog._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                Require(DetailField(world, "_sealNotice") == null && !(bool)DetailField(world, "_sealWaiting")!, "Seal Escape cancels its actual native modal without sending");
                await Frames();
                DetailCall(world, "AskSealConfirm");
                var changedNotice = (Notice)DetailField(world, "_sealNotice")!; changedNotice.Reparent(this); await Frames();
                int selectedSlot = (int)DetailField(world, "_sealSlot")!;
                var changedItem = InventoryOf(world)[selectedSlot]; changedItem.Durability++;
                InventoryOf(world)[selectedSlot] = changedItem;
                changedNotice.GetChildren().OfType<ClassicServiceNotice>().Single()._Input(new InputEventKey { Keycode = Key.Enter, Pressed = true });
                Require(!(bool)DetailField(world, "_sealWaiting")!, "Seal confirmation refuses an inventory item changed after the question opened");
                await Frames();
            }
            DetailCall(world, "CloseSealWindow"); Require(!window.Visible, "Seal close clears the native socket and keypad");
            Dispose(world, window); await Frames();
        }
        var pieceWorld = new World(); InventoryOf(pieceWorld).EnsureLength(InventoryConstants.InventoryTotal);
        DetailCall(pieceWorld, "BuildInventoryPanel"); DetailCall(pieceWorld, "BuildPiecePanel");
        var piece = (HudWindow)DetailField(pieceWorld, "_piecePanel")!; piece.Reparent(this); NativeServices.Prepare(piece, pieceWorld); ClassicServiceSkin.Apply(piece.Body);
        Set(pieceWorld, "_pieceShown", true); Set(pieceWorld, "_pieceNpcId", 300); DetailCall(pieceWorld, "RefreshPieceBackpack"); await Capture(piece, "generator-empty");
        Require(Descendants(piece).Count(c => c.GetType().Name == "UpgradeBackpackCell") == 28, "Classic Generator shows all 28 carried slots with the inventory pitch");
        var rejectedDrop = new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart } };
        Require(!PieceDropTarget(pieceWorld)._CanDropData(Vector2.Zero, rejectedDrop), "Generator rejects an empty / invalid piece drop");
        var rewards = (Dictionary<int, int[]>)typeof(ItemData).GetField("_pieces", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        int validPiece = rewards.Keys.First(id => ItemData.Get(id) != null);
        InventoryOf(pieceWorld)[Inventory.GridStart] = new ItemSlot { ItemId = validPiece, Count = 10, Durability = 1 };
        DetailCall(pieceWorld, "PlacePiece", Inventory.GridStart);
        Require((int)DetailField(pieceWorld, "_pieceItemId")! == validPiece, "Generator stages an actual exported exchange piece");
        var socket = PieceDropTarget(pieceWorld);
        Require(socket._CanDropData(Vector2.Zero, rejectedDrop), "Generator accepts a valid native piece drag");
        await Frames();
        var sourcePiece = Descendants(piece).OfType<Control>().First(c => c.GetType().Name == "UpgradeBackpackCell" && c.GetChildren().OfType<Control>().Any(o => Descendants(o).OfType<TextureRect>().Any(t => t.Texture != null)));
        sourcePiece._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = new Vector2(10, 20) });
        Require(!sourcePiece.IsQueuedForDeletion(), "Generator mouse-down preserves the source until a click or drag completes");
        sourcePiece.ForceDrag(rejectedDrop, new Control());
        Variant pieceDrag = sourcePiece._GetDragData(new Vector2(10, 20));
        Require(socket._CanDropData(Vector2.Zero, pieceDrag), "Generator drag data uses the normal inventory protocol");
        socket._DropData(Vector2.Zero, pieceDrag);
        Require((int)DetailField(pieceWorld, "_pieceItemId")! == validPiece, "Dropping an already-staged piece does not toggle it out");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(-100, -100), GlobalPosition = new Vector2(-100, -100) });
        await Frames();
        DetailCall(pieceWorld, "RefreshPieceActions"); DetailCall(pieceWorld, "StartPieceSpin"); DetailCall(pieceWorld, "PieceChangeTick", .1);
        Require((bool)DetailField(pieceWorld, "_pieceSpinning")!, "Generator keeps its native reward-spin lifecycle");
        await Capture(piece, "generator-spinning"); DetailCall(pieceWorld, "StopPieceSpin");
        Require((bool)DetailField(pieceWorld, "_pieceBusy")! && !(bool)DetailField(pieceWorld, "_pieceSpinning")!, "Generator Stop freezes animation and sends only once");
        await Capture(piece, "generator-pending"); Dispose(pieceWorld, piece); await Frames();

        var (combineWorld, combine) = await Build("BuildItemCombineUiPreview"); await Capture(combine, "combination-staged");
        var materials = (ItemSlotView[])DetailField(combineWorld, "_itemCombineSlots")!;
        Require(materials.Length == 11 && materials.All(c => c.IsVisibleInTree()), "All ten combination materials and the separate Shadow Piece remain visible");
        materials[0]._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
        Require(materials[0].Item.IsEmpty, "Combination right-click returns a staged material");
        DetailCall(combineWorld, "BuildCombineAmountPrompt");
        var amount = (CanvasLayer)DetailField(combineWorld, "_amountLayer")!; amount.Reparent(this); ClassicMerchantAmount.Apply(amount);
        DetailCall(combineWorld, "AskCombineCount", 0, Inventory.GridStart); await Frames();
        var amountPanel = amount.GetChildren().OfType<ClassicMerchantAmountPanel>().Single();
        string quantityFile = (nation == 1 ? "karus" : "human") + "-combination-quantity.png";
        GetViewport().GetTexture().GetImage().SavePng(output + "/" + quantityFile); screens.Add(new { state = "combination-quantity", file = quantityFile });
        amountPanel._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!amount.Visible && materials[0].Item.IsEmpty, "Combination quantity Escape preserves the unstaged material");
        DetailCall(combineWorld, "AskCombineCount", 0, Inventory.GridStart); await Frames();
        amountPanel._Input(new InputEventKey { Pressed = true, Keycode = Key.Enter });
        Require(!amount.Visible && materials[0].Item.Count == 12, "Combination quantity Enter stages the full stack without a misleading sale approval");
        amount.Free();
        materials[0]._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
        await Capture(combine, "combination-cleared"); Dispose(combineWorld, combine); await Frames();
        foreach (bool selected in new[] { false, true })
        {
            var (world, window) = await Build("BuildCombineRecipeBookUiPreview", selected);
            await Capture(window, selected ? "recipes-selected" : "recipes-categories");
            Require(Descendants(window).OfType<ScrollContainer>().Count(s => s.IsVisibleInTree()) == 3, "Recipe book keeps independently scrollable categories, recipes and ingredients " + selected);
            if (selected)
                foreach (var label in Descendants(window).OfType<Label>().Where(l => l.IsVisibleInTree() && l.GetParent() is HBoxContainer && l.Text.Length > 0))
                    Require(label.Size.Y >= 17 && label.Size.X > 15, "Recipe ingredient name / quantity has a readable measured area: " + label.Text);
            Dispose(world, window); await Frames();
        }
        foreach (bool stat in new[] { false, true })
        {
            bool confirmed = false, cancelled = false;
            var request = (DialogRequest)Create(typeof(DialogRequest), stat ? "Redistribute stats" : "Redistribute mastery", stat ? "Every one of your stat points goes back into the pool, and it costs 1,200,000 gold.\n\nUnequip every item first." : "Every one of your mastery points goes back into the pool, and it costs 1,200,000 gold.", "Redistribute", "Cancel", true, (Action)(() => confirmed = true), (Action)(() => cancelled = true), () => { });
            var dialog = new ClassicServiceNotice(request); AddChild(dialog); await Frames();
            string file = (nation == 1 ? "karus" : "human") + (stat ? "-redistribute-stats.png" : "-redistribute-mastery.png");
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state = stat ? "redistribute-stats" : "redistribute-mastery", file });
            dialog._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true }); Require(cancelled && !confirmed, "Redistribution Escape cancels without spending gold");
            dialog._Input(new InputEventKey { Keycode = Key.Enter, Pressed = true }); Require(confirmed, "Redistribution Enter invokes its live confirmation callback"); dialog.Free();
        }
        var redistributionWorld = new World(); DetailCall(redistributionWorld, "BuildClassChangePanel");
        var redistribution = (HudWindow)DetailField(redistributionWorld, "_classChangePanel")!; redistribution.Reparent(this); NativeServices.Prepare(redistribution, redistributionWorld); ClassicServiceSkin.Apply(redistribution.Body);
        await Capture(redistribution, "redistribute-menu");
        Descendants(redistribution).OfType<Button>().Single(b => b.Text == "Redistribute stat points").EmitSignal(BaseButton.SignalName.Pressed);
        Require(NativeServices.RedistributionKind(redistributionWorld) == Net.ResetKindStat, "Original redistribution menu queries the stat reset price before confirming");
        Descendants(redistribution).OfType<Button>().Single(b => b.Text == "Redistribute mastery points").EmitSignal(BaseButton.SignalName.Pressed);
        Require(NativeServices.RedistributionKind(redistributionWorld) == Net.ResetKindStat, "A pending reset price cannot be reassigned by a second menu click");
        await Frames();
        Require(Descendants(redistribution).OfType<Button>().Where(b => b.Text.StartsWith("Redistribute ")).All(b => b.Disabled), "Redistribution choices are disabled while the server price or reset is pending");
        DetailCall(redistributionWorld, "CancelReset"); await Frames();
        Descendants(redistribution).OfType<Button>().Single(b => b.Text == "Redistribute mastery points").EmitSignal(BaseButton.SignalName.Pressed);
        Require(NativeServices.RedistributionKind(redistributionWorld) == Net.ResetKindSkill, "Original redistribution menu queries the mastery reset price before confirming");
        Dispose(redistributionWorld, redistribution); await Frames();
        var (repairWorld, repair) = await Build("BuildRepairUiPreview");
        var audio = new LibreKO.Audio(); AddChild(audio);
        SoundCatalog.EnsureLoaded();
        Require(SoundCatalog.TryGet(Sfx.UiRepair, out var repairSound), "Original repair sound ID 2001 is present in the loaded content");
        Require(Godot.FileAccess.FileExists(SoundCatalog.Dir + repairSound.File) || ResourceLoader.Exists(SoundCatalog.Dir + repairSound.File), "Repair sound resolves to an existing packaged audio asset");
        GameCursor.SetNation(nation); GameCursor.Enable();
        foreach (string stem in new[] { "repair", "repair_alt" })
        {
            var cursor = (Texture2D?)typeof(GameCursor).GetMethod("Peek", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { stem });
            Require(cursor != null, "Original cursor artwork is available: " + stem);
            cursor!.GetImage().SavePng(output + "/" + stem + "-cursor.png");
        }
        GameCursorKind CursorKind() => (GameCursorKind)typeof(GameCursor).GetField("_kind", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (string pack in new[] { "characters", "armor", "weapons" })
            ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/" + pack + ".pck"), false);
        DetailCall(repairWorld, "CloseRepair");
        net.Sheet.SetMaxWeight(17_100);
        var inventory = InventoryOf(repairWorld); inventory.EnsureLength(InventoryConstants.InventoryTotal);
        inventory[InventoryConstants.RightHand] = inventory[Inventory.GridStart];
        var bridge = Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge", BindingFlags.NonPublic)!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { repairWorld }, null)!;
        var attach = typeof(PluginGame).GetMethod("Attach", flags)!;
        attach.Invoke(game, Enumerable.Repeat(bridge, attach.GetParameters().Length).ToArray());
        var inventoryShell = new HudWindow("inventory", "Inventory") { Visible = false };
        inventoryShell.Body.AddChild((Control)DetailField(repairWorld, "_invContent")!);
        foreach (var child in inventoryShell.GetChildren().OfType<Control>()) child.Visible = false;
        inventoryShell.AddThemeStyleboxOverride("panel", new StyleBoxEmpty()); AddChild(inventoryShell);
        var windows = (Dictionary<string, HudWindow>)DetailField(repairWorld, "_mainWindows")!; windows["Inventory"] = inventoryShell;
        var host = (WindowHost)Create(typeof(WindowHost), "inventory", "Inventory", inventoryShell, (Action)(() => DetailCall(repairWorld, "HideMainWindow", "Inventory")));
        var actual = new InventoryWindow(host); inventoryShell.AddChild(actual); inventoryShell.ResetSize();
        DetailCall(repairWorld, "OpenRepair"); await Frames();
        inventoryShell.Position = new Vector2(317, 61);
        Require(!repair.Visible && inventoryShell.Visible, "Classic repair opens the actual inventory without a separate catalogue");
        Require(inventoryShell.GetMeta("classic_repair_mode").AsBool(), "Inventory enters the native repair mode");
        Require(CursorKind() == GameCursorKind.Repair, "Repair opens with the original pre-repair hammer cursor");
        var cells = Descendants(actual).OfType<KnightOnlineUiClassic.Layout.ItemSlot>().Where(c => c.Slot < Inventory.GridStart + Inventory.GridCount).ToArray();
        Require(cells.Length == 42, "Repair preserves fourteen equipment and twenty-eight inventory slots");
        var tip = Descendants(actual).OfType<ClassicInventoryRepair>().Single();
        async Task RepairCapture(string state)
        {
            await Frames();
            Require(GetViewportRect().Encloses(actual.GetGlobalRect()), state + " inventory fits viewport");
            foreach (var cell in cells)
            {
                var icon = Descendants(cell).OfType<TextureRect>().Last();
                Require(icon.GetGlobalRect() == new Rect2(cell.GlobalPosition + new Vector2(2, 2), cell.Size - new Vector2(4, 4)), state + " unchanged inventory icon alignment " + cell.Slot);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        Input.WarpMouse(new Vector2(380, 422)); tip.ShowTip(Inventory.GridStart);
        await RepairCapture("repair-selected");
        Require(Descendants(tip).OfType<Label>().Any(l => l.Text == "Repair Cost"), "Original repair tooltip shows price and durability");
        var equipment = cells.Single(c => c.Slot == InventoryConstants.RightHand);
        equipment._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right });
        Require(!(bool)DetailField(repairWorld, "_repairInFlight")!, "Repair mode suppresses right-click equip and use");
        Require(equipment._GetDragData(Vector2.One).VariantType == Variant.Type.Nil, "Repair mode prevents item dragging");
        equipment._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
        equipment._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left, Position = new Vector2(-10, -10) });
        Require(!(bool)DetailField(repairWorld, "_repairInFlight")!, "Releasing outside the item does not repair it");
        equipment._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left });
        equipment._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left });
        Require((bool)DetailField(repairWorld, "_repairInFlight")!, "Left-click repairs equipped gear through the native callback");
        Require(inventoryShell.GetMeta("classic_repair_pending").AsBool(), "Repair serializes requests while waiting for the server");
        Require(CursorKind() == GameCursorKind.RepairAlt, "A submitted repair switches to the original working hammer cursor");
        await RepairCapture("repair-pending");
        DetailCall(repairWorld, "DeliverRepairResult", true, 0);
        Require(CursorKind() == GameCursorKind.Repair, "The server result restores the pre-repair cursor");
        Require(Descendants(audio).OfType<AudioStreamPlayer>().Any(p => p.Bus == LibreKO.Audio.BusUi && p.Stream != null && p.GetMeta("snd", 0).AsInt32() == Sfx.UiRepair), "Successful server repair plays the original UI repair sound");
        Require(inventory[InventoryConstants.RightHand].Durability == ItemData.MaxDurabilityOf(inventory[InventoryConstants.RightHand].ItemId), "Successful repair updates equipment durability");
        net.Sheet.SeedWealth(0, 0);
        var bagCell = cells.Single(c => c.Slot == Inventory.GridStart);
        bagCell.OnClick!(bagCell.Slot);
        Require(!(bool)DetailField(repairWorld, "_repairInFlight")!, "Insufficient Noahs prevents repair submission");
        net.Sheet.SeedWealth(1180000, 2450);
        bagCell.OnClick!(bagCell.Slot);
        Require((bool)DetailField(repairWorld, "_repairInFlight")!, "Left-click repairs carried gear without picking it up");
        Require((int)typeof(InventoryWindow).GetField("_carried", flags)!.GetValue(actual)! == -1, "Repair never enters inventory carry mode");
        DetailCall(repairWorld, "DeliverRepairResult", false, 0);
        Descendants(actual).OfType<Button>().Single(b => b.Text == "Repair All").EmitSignal(BaseButton.SignalName.Pressed);
        Require((bool)DetailField(repairWorld, "_repairInFlight")! && ((Queue<int>)DetailField(repairWorld, "_repairQueue")!).Count > 0,
            "Repair All remains available through the inventory and serializes damaged gear");
        while ((bool)DetailField(repairWorld, "_repairInFlight")!) DetailCall(repairWorld, "DeliverRepairResult", true, 0);
        inventory.SetDurability(Inventory.GridStart, 1100); bagCell.OnClick!(bagCell.Slot);
        inventory[Inventory.GridStart] = inventory[Inventory.GridStart + 1];
        inventory.SetDurability(Inventory.GridStart, 500);
        DetailCall(repairWorld, "DeliverRepairResult", true, 0);
        Require(inventory[Inventory.GridStart].Durability == 500, "A delayed repair result cannot alter a different item placed in the original slot");
        tip.HideTip();
        for (int i = 0; i < inventory.Length; i++) inventory[i] = default;
        typeof(InventoryWindow).GetMethod("Refresh", flags)!.Invoke(actual, null);
        await RepairCapture("repair-empty");
        DetailCall(repairWorld, "HideMainWindow", "Inventory");
        Require(!(bool)DetailField(repairWorld, "_repairShown")! && !inventoryShell.GetMeta("classic_repair_mode").AsBool(), "Closing inventory exits repair mode and restores ordinary interactions");
        Require(CursorKind() == GameCursorKind.Arrow, "Closing repair restores the normal nation cursor");
        audio.Free(); GameCursor.Disable();
        inventoryShell.Free(); Dispose(repairWorld, repair); await Frames();
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-verification.json", JsonSerializer.Serialize(new { screens, checks }, new JsonSerializerOptions { WriteIndented = true }));
        net.Free(); GD.Print("CLASSIC_SERVICES_AUDIT_OK " + checks.Count);
    }
}
