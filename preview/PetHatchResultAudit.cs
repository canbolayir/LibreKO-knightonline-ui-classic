using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task AuditPetHatchResults(int nation, string output, Action<bool, string> require)
    {
        var originalNet = Net.I;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var screens = new List<object>();
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task KeyPress(Key key)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            await Frames();
        }
        async Task Click(Control control)
        {
            var at = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true); await Frames(2);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            await Frames();
        }
        foreach (bool transform in new[] { false, true })
        {
            var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
            var world = new PetAuditWorld { ProcessMode = ProcessModeEnum.Disabled };
            var inventory = (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(world)!;
            // Mirror authoritative slot events into the same domain inventory before trainer observers run.
            Action<int, LibreKO.Domain.ItemSlot> mirror = inventory.ApplySlotUpdate;
            net.InventorySlotEvent += mirror;
            var layer = (CanvasLayer)DetailCall(world, "BuildPetHatchResponseUiPreview", nation, transform)!;
            AddChild(world); AddChild(layer); await Frames();
            var panel = layer.GetChildren().OfType<HudWindow>().Single(); panel.Position = new Vector2(217, 50);
            var classic = panel.GetChildren().OfType<ClassicPetHatchPanel>().Single();
            T Field<T>(string name) => (T)typeof(World).GetField(name, flags)!.GetValue(world)!;
            T Find<T>(string name) where T : Control => Descendants(classic).OfType<T>().Single(c => c.Name == name);
            string service = transform ? "transform" : "hatch";
            int slot = Field<int>(transform ? "_petTransformSlot" : "_petHatchSlot");
            int scrollSlot = Field<int>("_petScrollSlot");
            if (transform) { var material = inventory[scrollSlot]; material.Count = 2; inventory.ApplySlotUpdate(scrollSlot, material); DetailCall(world, "RebuildPetPicks"); }
            for (int i = 0; i < inventory.Length; i++) net.MirrorInventorySlot(i, inventory[i]);
            var name = Field<LineEdit>("_petHatchName");
            var status = Field<Label>("_petHatchStatus");
            var action = Field<Button>("_petHatchBtn");
            int mutations = 0; net.InventorySlotEvent += (_, _) => mutations++;
            async Task Capture(string state)
            {
                await Frames(); require(panel.Size == ClassicPetHatchLayout.Size && GetViewportRect().Encloses(panel.GetGlobalRect()), "Received " + service + " UI keeps its fixed contained composition / " + state);
                foreach (var control in Descendants(classic).OfType<Control>().Where(c => c.HasMeta("pet_hatch_expected_rect") && c.IsVisibleInTree()))
                {
                    var rect = control.GetMeta("pet_hatch_expected_rect").AsRect2();
                    require(control.Position == rect.Position && control.Size == rect.Size && panel.GetGlobalRect().Encloses(control.GetGlobalRect()), "Received " + service + " UI actual geometry / " + state + " / " + control.Name);
                    if (control is Label label && label.Text.Length > 0) require(label.GetVisibleLineCount() == label.GetLineCount(), "Received " + service + " text is fully visible / " + state + " / " + control.Name);
                }
                string file = (nation == 1 ? "karus" : "human") + "-" + service + "-reply-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { service, state, file });
            }
            var connection = (KoConn)typeof(Net).GetField("_conn", flags)!.GetValue(net)!;
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var timeout = new CancellationTokenSource(30000);
            try
            {
                var stage = Find<ItemSlotView>("pet_hatch_stage_" + (transform ? 1 : 0));
                var hover = stage.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                require(world.ItemTooltipVisible, "Actual " + service + " staged item hover opens the native item tooltip");
                var tooltip = Field<PanelContainer>("_itemTipPanel");
                require(GetViewportRect().Encloses(tooltip.GetGlobalRect()) && Descendants(tooltip).OfType<Label>().Any(l => l.Text == ItemData.DisplayName(stage.Item.ItemId)), "Actual " + service + " tooltip is contained and identifies the staged item");
                await Capture("tooltip");
                var gridCell = Find<ItemSlotView>("pet_hatch_inventory_" + (slot - Inventory.GridStart));
                hover = gridCell.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover }, true); await Frames();
                require(world.ItemTooltipVisible && Descendants(tooltip).OfType<Label>().Any(l => l.Text == ItemData.DisplayName(gridCell.Item.ItemId)), "Actual " + service + " inventory hover keeps the native tooltip item identity");
                GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(20, 20), GlobalPosition = new Vector2(20, 20) }, true); await Frames();
                require(!world.ItemTooltipVisible, "Leaving " + service + " items hides the native tooltip");
                connection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
                using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
                for (int i = 0; i < 120 && !net.Connected; i++) await Frames(1);
                require(net.Connected, "Actual " + service + " UI uses an isolated native loopback connection");
                using var stream = receiver.GetStream();
                async Task Submit()
                {
                    if (!transform) await Click(name);
                    else GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                    await KeyPress(Key.Enter);
                    require(PreviewFixtures.PetHatchNotice(world) != null && !Field<bool>("_petHatchInFlight"), "Actual " + service + " Enter opens confirmation before a request");
                    await KeyPress(Key.Enter);
                    require(Field<bool>("_petHatchInFlight") && action.Disabled && (transform || !name.Editable), "Actual " + service + " confirmation locks the pending draft");
                    var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)); var body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
                    require(body[0] == (byte)GameOpcodes.GS_ITEM_UPGRADE && body[1] == (transform ? 10 : 6)
                        && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2)) == 13016 && body[10] == slot - Inventory.GridStart,
                        "Actual " + service + " UI submits the original operation, trainer and inventory slot");
                }
                void Receive(Packet packet)
                {
                    packet.ResetOffset(); typeof(Net).GetMethod("HandleItemUpgrade", flags)!.Invoke(net, new object[] { packet });
                }
                Packet Refused()
                {
                    var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteByte((byte)(transform ? 10 : 6));
                    if (transform) { packet.WriteByte(0); packet.WriteByte(1); }
                    else packet.WriteByte(PetWire.HatchNameTaken);
                    return packet;
                }
                Packet Success()
                {
                    var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE); packet.WriteByte((byte)(transform ? 10 : 6)); packet.WriteByte(1);
                    packet.WriteInt(transform ? 610015000 : 610001000); packet.WriteByte((byte)(slot - Inventory.GridStart)); packet.WriteInt(transform ? inventory[slot].UniqueId : 109); packet.WriteString("Kauly");
                    packet.WriteByte(101); packet.WriteByte(12); packet.WriteUShort(4200); packet.WriteShort(7300);
                    if (transform) { packet.WriteByte(0); packet.WriteInt(inventory[scrollSlot].ItemId); packet.WriteByte((byte)(scrollSlot - Inventory.GridStart)); }
                    return packet;
                }
                var before = inventory[slot]; var beforeMaterial = transform ? inventory[scrollSlot] : default;
                await Submit(); await Capture("pending");
                var truncated = new Packet(GameOpcodes.GS_ITEM_UPGRADE); truncated.WriteByte((byte)(transform ? 10 : 6)); truncated.WriteByte(1); Receive(truncated);
                require(Field<bool>("_petHatchInFlight") && action.Disabled && mutations == 0, "Truncated " + service + " response leaves the actual UI locked without partial item mutation");
                Receive(Refused()); await Frames();
                require(!Field<bool>("_petHatchInFlight") && !action.Disabled && inventory[slot].Equals(before) && mutations == 0 && status.Text.Length > 0,
                    "Received " + service + " refusal restores the same editable draft without spending items");
                require(status.GetThemeColor("font_color") == UiTheme.Bad && (transform || name.Editable && name.Text == "Kauly"), "Received " + service + " refusal retains the native warning color and name");
                if (!transform) require(status.Text.Contains("name is already in use"), "Native name-taken response presents the complete retry explanation");
                await Capture("refusal");
                await Submit(); DetailCall(world, "ClosePetHatch"); await Frames();
                require(!panel.Visible && Field<bool>("_petHatchInFlight"), "Closing pending " + service + " does not discard its native request");
                DetailCall(world, "OpenPetHatch", 99999); await Frames();
                require(panel.Visible && Field<int>("_petHatchNpc") == 13016 && action.Disabled, "Reopening pending " + service + " keeps the original trainer and frozen operation");
                var success = Success(); Receive(success); await Frames();
                require(!Field<bool>("_petHatchInFlight") && inventory[slot].ItemId == (transform ? 610015000 : 610001000)
                    && (transform ? inventory[scrollSlot].Count == beforeMaterial.Count - 1 : inventory[slot].UniqueId == 109), "Received " + service + " success updates the actual trainer item views from native authority");
                require(status.Text.StartsWith("Kauly " + (transform ? "transformed into " : "hatched.")) && status.GetThemeColor("font_color") == UiTheme.TextHi,
                    "Received " + service + " success presents the native result text and normal color");
                require(transform ? !action.Disabled && Find<ItemSlotView>("pet_hatch_stage_2").Item.Count == 1 : name.Text.Length == 0 && action.Disabled && stage.Item.IsEmpty,
                    "Received " + service + " success restores correct next-operation eligibility");
                var lines = Field<Queue<string>>("_combatLogLines"); int logCount = lines.Count, mutationCount = mutations;
                require(logCount == 1 && lines.Last().Contains(transform ? "transformed into" : "hatched from the egg"), "Received " + service + " success reaches the native Info log once");
                Receive(success); await Frames(); require(mutations == mutationCount && lines.Count == logCount && status.Text.Length > 0, "Duplicate " + service + " success cannot alter the trainer or repeat its Info notification");
                await Capture("success");
                if (!transform) { slot = Inventory.GridStart + 6; DetailCall(world, "SelectPetHatchItem", slot); name.Text = "Kauly"; }
                await Submit(); typeof(Net).GetMethod("ResetPet", flags)!.Invoke(net, null); await Frames();
                require(!panel.Visible && !Field<bool>("_petHatchInFlight") && PreviewFixtures.PetHatchNotice(world) == null, "Native connection reset closes and unlocks the actual " + service + " trainer");
                Receive(success); await Frames(); require(!panel.Visible && mutations == mutationCount && lines.Count == logCount, "Delayed " + service + " success after reset cannot reopen the trainer or mutate inventory");
            }
            finally
            {
                connection.Close(); DetailCall(world, "PetHatchDispose"); net.InventorySlotEvent -= mirror;
                layer.Free(); world.Free(); net.Free(); typeof(Net).GetProperty("I")!.SetValue(null, originalNet);
            }
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-results.json", JsonSerializer.Serialize(new { screens }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
