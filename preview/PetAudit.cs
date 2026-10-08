using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using System.Reflection;
using System.Text.Json;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using KnightOnlineUiClassic;

public partial class Preview
{
    private async Task CapturePetAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/npcs.pck"), false)) throw new Exception("Missing existing NPC models");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(800, 680);
        GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Size = new Vector2(800, 680), Color = new Color("252822"), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ExtendWindow("pet", ClassicPetSkin.Extend);
        var world = new PetAuditWorld { ProcessMode = ProcessModeEnum.Disabled };
        var layer = (CanvasLayer)DetailCall(world, "BuildPetClassicUiPreview", nation)!; AddChild(world); AddChild(layer);
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        await Frames();
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.Position = new Vector2(240, 70);
        var panel = window.GetChildren().OfType<ClassicPetPanel>().Single();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool condition, string text) { if (!condition) throw new Exception("PET_AUDIT: " + text); checks.Add(text); }
        async Task Click(Control control, MouseButton button = MouseButton.Left)
        {
            var point = control.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true); await Frames(1);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = button, Pressed = pressed }, true);
            await Frames();
        }
        async Task Capture(string state)
        {
            await Frames();
            var status = Find<Label>("pet_status");
            var expectedSize = status.Visible && status.Text.Length > 0 ? ClassicPetLayout.Size : ClassicPetLayout.BaseSize;
            Require(window.Size == expectedSize, state + " original composition with message extension only when needed");
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " window contained");
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("pet_expected_rect")))
            {
                var expected = control.GetMeta("pet_expected_rect").AsRect2();
                Require((control.Position - expected.Position).Length() < .1 && (control.Size - expected.Size).Length() < .1, state + " actual geometry " + control.Name);
                if (control.IsVisibleInTree()) Require(window.GetGlobalRect().Encloses(control.GetGlobalRect()), state + " contained control " + control.Name);
                if (control is Label label && label.IsVisibleInTree() && label.Text.Length > 0)
                    Require(label.GetVisibleLineCount() == label.GetLineCount(), state + " all lines visible " + control.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        Button Tab(string caption) => Descendants(panel).OfType<Button>().Single(b => b.Text == caption);
        var portrait = Find<ClassicFamiliarPortrait>("familiar_portrait_texture");
        for (int i = 0; i < 300 && portrait.Texture == null; i++) await Frames(1);
        Require(portrait.Texture != null, "Actual summoned Kaul model produces a cached portrait");
        Require(NpcPortraitCache.RenderRequests == 1 && NpcPortraitCache.RetainedViewportCount == 0 && NpcPortraitCache.ActiveRenderers == 0,
            "Portrait captures once and retains no live renderer");
        await Capture("ability");
        Require(Find<Label>("pet_detail_0").Text.EndsWith("51") && Find<Label>("pet_detail_7").Text.EndsWith("45"), "Received attack and resistance values are visible");
        Require(Find<Button>("pet_attack").ButtonPressed, "Selected mode follows server state");
        await Click(Tab("Items")); await Capture("items");
        Require(Find<ItemSlotView>("pet_item_0").Item.ItemId != 0, "Native familiar equipment retained");
        await Click(Tab("Skills")); await Capture("skills");
        await AuditPetEquipment(world, net, layer, window, panel, Require, Capture);
        await AuditPetSkills(world, net, panel, Require, Capture);
        var sheet = net.Pet!; sheet.Mode = PetSheet.ModeLooting; sheet.Mp = 0; DetailCall(world, "RefreshPetUI");
        Require(Find<Button>("pet_loot").ButtonPressed && !Find<Button>("pet_attack").ButtonPressed, "Mode switch follows native response");
        await Capture("no-mana");
        typeof(World).GetField("_selfDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, true); DetailCall(world, "RefreshPetUI");
        Require(Find<Button>("pet_feed").Disabled && Find<Button>("pet_attack").Disabled, "Dead owner cannot feed or command");
        await Click(Tab("Items")); await Capture("dead");
        DetailCall(world, "ShowPetSheet", (object?)null);
        Require(portrait.Texture == null, "Unsummoned familiar clears the old portrait immediately");
        Require(Enumerable.Range(0, 4).All(i => Find<ItemSlotView>("pet_item_" + i).Item.IsEmpty), "Unsummoned familiar clears every old item view");
        await Capture("unsummoned");
        DetailCall(world, "SetPetStatus", "Your familiar would not eat this food. Choose another familiar food item and try again.", true);
        await Capture("long-status");
        DetailCall(world, "ShowPetSheet", sheet); await Frames();
        Require(portrait.Texture != null && NpcPortraitCache.RenderRequests == 1 && NpcPortraitCache.CacheHits > 0,
            "Reopening the same familiar reuses its 2D texture");
        window.Visible = false;
        await AuditPetBar(world, net, nation, output, Require);
        await AuditPetIncubationReplies(Require);
        await AuditPetHatchGuards(checks, nation, output);
        await AuditPetHatchResults(nation, output, Require);
        await AuditPetEquipmentResults(nation, output, Require);
        await AuditPetPortraitForms(world, net, window, panel, nation, output, Require);
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet.json", JsonSerializer.Serialize(new { checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PET_AUDIT: " + checks.Count + " checks, " + screens.Count + " states");
        DetailCall(world, "PetSkillObserveDispose");
        world.Free();
        net.Free();
    }

    private async Task AuditPetHatchGuards(List<string> checks, int nation, string output)
    {
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        PluginHost.Ui.ExtendWindow("pethatch", ClassicPetHatchSkin.Extend);
        var screens = new List<object>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Require(bool value, string message) { if (!value) throw new Exception("PET_HATCH_AUDIT: " + message); checks.Add(message); }
        async Task KeyPress(Key key)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
            await Frames();
        }
        foreach (bool transform in new[] { false, true })
        {
            var world = new World();
            var layer = (CanvasLayer)DetailCall(world, "BuildPetHatchClassicUiPreview", nation, transform)!; AddChild(layer);
            var panel = layer.GetChildren().OfType<HudWindow>().Single();
            T Field<T>(string name) => (T)typeof(World).GetField(name, flags)!.GetValue(world)!;
            void Set(string name, object value) => typeof(World).GetField(name, flags)!.SetValue(world, value);
            panel.Position = new Vector2(217, 50);
            await Frames();
            var classic = panel.GetChildren().OfType<ClassicPetHatchPanel>().Single();
            T Find<T>(string id) where T : Control => Descendants(classic).OfType<T>().Single(c => c.Name == id);
            var inventory = (Inventory)typeof(World).GetProperty("Inv", flags)!.GetValue(world)!;
            string service = transform ? "transform" : "hatch";
            async Task Capture(string state)
            {
                await Frames(); Require(panel.Size == ClassicPetHatchLayout.Size, service + "/" + state + " fixed service bounds");
                Require(GetViewportRect().Encloses(panel.GetGlobalRect()), service + "/" + state + " window contained");
                foreach (var control in Descendants(classic).OfType<Control>().Where(c => c.HasMeta("pet_hatch_expected_rect")))
                {
                    var expected = control.GetMeta("pet_hatch_expected_rect").AsRect2();
                    Require((control.Position - expected.Position).Length() < .1 && (control.Size - expected.Size).Length() < .1,
                        service + "/" + state + " native geometry " + control.Name);
                    if (!control.IsVisibleInTree()) continue;
                    Require(panel.GetGlobalRect().Encloses(control.GetGlobalRect()), service + "/" + state + " visible control contained " + control.Name);
                    if (control is Label label && label.Text.Length > 0)
                        Require(label.GetLineCount() == label.GetVisibleLineCount(), service + "/" + state + " complete label " + control.Name);
                }
                var cells = Enumerable.Range(0, 28).Select(i => Find<ItemSlotView>("pet_hatch_inventory_" + i)).ToArray();
                Require(cells.All(c => c.IsVisibleInTree()), "All 28 inventory slots are simultaneously visible");
                for (int i = 0; i < cells.Length; i++)
                {
                    var icon = cells[i].GetNode<Control>("pet_hatch_item_overlay").GetChildren().OfType<TextureRect>().Single();
                    Require(icon.GetGlobalRect().Position == cells[i].GetGlobalRect().Position + new Vector2(2, 2) && icon.Size == new Vector2(41, 41), "Shared icon inset " + service + "/" + i);
                    Require(cells[i].CountLabel.OffsetRight == -2 && cells[i].CountLabel.OffsetBottom == -2, "Shared item count inset " + service + "/" + i);
                }
                string file = (nation == 1 ? "karus" : "human") + "-" + service + "-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { service, state, file });
            }
            async Task Click(Control control, MouseButton button = MouseButton.Left)
            {
                var at = control.GetGlobalRect().GetCenter(); Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2);
                foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed });
                await Frames();
            }
            async Task Drag(ItemSlotView source, ItemSlotView destination)
            {
                var from = source.GetGlobalRect().GetCenter(); var to = destination.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from }); await Frames(2);
                Input.ParseInputEvent(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = from + new Vector2(18, 0), GlobalPosition = from + new Vector2(18, 0), Relative = new Vector2(18, 0), ButtonMask = MouseButtonMask.Left }); await Frames(2);
                Require(GetViewport().GuiIsDragging(), "Actual inventory drag starts in " + service);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left }); await Frames(2);
                Input.ParseInputEvent(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
            }
            var name = Field<LineEdit>("_petHatchName");
            await Capture("ready");
            var headerAt = panel.GetGlobalRect().Position + new Vector2(120, 24); var oldPosition = panel.Position; var shift = new Vector2(35, 18);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = headerAt, GlobalPosition = headerAt }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton { Position = headerAt, GlobalPosition = headerAt, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = headerAt + shift, GlobalPosition = headerAt + shift, Relative = shift, ButtonMask = MouseButtonMask.Left }); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton { Position = headerAt + shift, GlobalPosition = headerAt + shift, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames(2);
            Require(panel.Position.DistanceTo(oldPosition + shift) < 1 && !GetViewport().GuiIsDragging(), "Header dragging moves the native " + service + " window (" + oldPosition + " -> " + panel.Position + ")");
            panel.Position = oldPosition; await Frames();
            int pick = Field<int>(transform ? "_petTransformSlot" : "_petHatchSlot");
            Require(!Field<Button>("_petHatchBtn").Disabled, (transform ? "Transform" : "Hatch") + " valid draft is actionable");
            var stage = Find<ItemSlotView>("pet_hatch_stage_" + (transform ? 1 : 0));
            await Click(stage, MouseButton.Right);
            Require(stage.Item.IsEmpty && Field<Button>("_petHatchBtn").Disabled, "Right-click clears staged " + service + " item");
            await Capture("empty-selection");
            await Click(Find<ItemSlotView>("pet_hatch_inventory_" + (pick - Inventory.GridStart)), MouseButton.Right);
            Require(stage.Item.ItemId == inventory[pick].ItemId && !Field<Button>("_petHatchBtn").Disabled, "Inventory right-click stages a valid " + service + " item");
            await Click(stage, MouseButton.Right); await Drag(Find<ItemSlotView>("pet_hatch_inventory_" + (pick - Inventory.GridStart)), stage);
            Require(stage.Index == pick && !Field<Button>("_petHatchBtn").Disabled, "Pointer drag stages the chosen " + service + " item");
            if (transform)
            {
                var wrong = new Godot.Collections.Dictionary { ["invFrom"] = pick };
                Require(!Find<ItemSlotView>("pet_hatch_stage_2")._CanDropData(Vector2.Zero, wrong), "Familiar cannot occupy the transformation scroll slot");
            }
            else
            {
                name.Text = ""; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text); Require(Field<Button>("_petHatchBtn").Disabled, "Empty familiar name cannot hatch");
                name.Text = "Bad Name"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text); Require(Field<Button>("_petHatchBtn").Disabled, "Whitespace name cannot hatch");
                name.Text = "FamiliarName123"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text); await Capture("long-name"); name.Text = "Kauly"; name.EmitSignal(LineEdit.SignalName.TextChanged, name.Text);
            }
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            await KeyPress(Key.Enter);
            await Capture("confirmation");
            Require(Field<Notice?>("_petHatchNotice") != null && !name.Editable && Field<Button>("_petHatchBtn").Disabled,
                "Confirmation freezes " + (transform ? "transformation" : "hatching") + " draft");
            await KeyPress(Key.Escape);
            Require(Field<Notice?>("_petHatchNotice") == null && name.Text == "Kauly" && Field<int>(transform ? "_petTransformSlot" : "_petHatchSlot") == pick,
                "Escape preserves chosen items and name");
            await Click(Find<Button>("pet_hatch_accept"));
            await KeyPress(Key.Enter);
            Require(!Field<bool>("_petHatchInFlight") && Field<Label>("_petHatchStatus").Text.StartsWith("Not connected"),
                "Enter confirms once and disconnected send releases the operation lock");
            DetailCall(world, "OnPetHatchPressed"); await Frames();
            inventory.ApplySlotUpdate(pick, default);
            await KeyPress(Key.Enter);
            Require(!Field<bool>("_petHatchInFlight") && Field<Label>("_petHatchStatus").Text.StartsWith("Your selection changed"),
                "Changed inventory invalidates the frozen confirmation before send");
            await Capture("changed-selection");
            for (int i = 0; i < 28; i++) inventory.ApplySlotUpdate(Inventory.GridStart + i, new LibreKO.Domain.ItemSlot { ItemId = transform ? 610001000 : 600001000, Count = 1, Durability = 1, UniqueId = transform ? i + 1 : 0 });
            DetailCall(world, "RebuildPetPicks"); name.Text = "Kauly"; DetailCall(world, "SelectPetHatchItem", Inventory.GridStart);
            await Capture("full-inventory");
            var sealedItem = inventory[Inventory.GridStart]; sealedItem.Flag = (byte)ItemFlag.Sealed;
            inventory.ApplySlotUpdate(Inventory.GridStart, sealedItem); DetailCall(world, "RebuildPetPicks");
            Require(stage.Item.IsEmpty && !stage._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { ["invFrom"] = Inventory.GridStart }), "Sealed item cannot be staged");
            DetailCall(world, "OnPetHatchFailed", Net.PetHatchNameTakenCode); await Capture("long-refusal");
            sealedItem.Flag = 0; inventory.ApplySlotUpdate(Inventory.GridStart, sealedItem);
            if (transform) inventory.ApplySlotUpdate(Inventory.GridStart + 27, new LibreKO.Domain.ItemSlot { ItemId = 700019001, Count = 1, Durability = 1 });
            DetailCall(world, "RebuildPetPicks"); DetailCall(world, "SelectPetHatchItem", Inventory.GridStart);
            if (transform) DetailCall(world, "SelectPetHatchItem", Inventory.GridStart + 27);
            Set("_petHatchInFlight", true);
            DetailCall(world, "SetPetHatchStatus", transform ? "Transforming…" : "Hatching…", false);
            DetailCall(world, "ClosePetHatch"); DetailCall(world, "OpenPetHatch", 99999);
            Require(Field<bool>("_petHatchInFlight") && Field<int>("_petHatchNpc") == 13016 && Field<Button>("_petHatchBtn").Disabled,
                "Closing and reopening preserves pending operation and trainer identity");
            await Capture("pending-reopen");
            DetailCall(world, "ResetPetHatch");
            Require(!Field<bool>("_petHatchInFlight") && !panel.Visible && Field<Notice?>("_petHatchNotice") == null,
                "Connection reset clears pending draft and closes the trainer service");
            layer.Free(); world.Free();
        }
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-hatch.json", JsonSerializer.Serialize(new { screens }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
