using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task AuditPetBar(World world, Net net, int nation, string output, Action<bool, string> require)
    {
        typeof(World).GetField("_selfDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, false);
        PluginHost.Ui.ExtendHud(HudPart.FamiliarBar, ClassicPetBarSkin.Apply);
        var layer = (CanvasLayer)DetailCall(world, "BuildPetBarClassicUiPreview")!; AddChild(layer);
        var bar = layer.GetChildren().OfType<PanelContainer>().Single();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(World).GetField(name, flags)!.GetValue(world)!;
        T Find<T>(string name) where T : Control => Descendants(bar).OfType<T>().Single(c => c.Name == name);
        var cells = Enumerable.Range(0, 8).Select(i => Find<PanelContainer>("pet_bar_skill_" + i)).ToArray();
        int SkillId(Control cell) => (int)cell.GetType().GetProperty("SkillId")!.GetValue(cell)!;
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Click(Control control, MouseButton button = MouseButton.Left)
        {
            var at = control.GetGlobalRect().GetCenter(); Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2);
            foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed });
            await Frames();
        }
        var screens = new List<object>();
        async Task Capture(string state)
        {
            await Frames(); require(bar.Size == ClassicPetBarLayout.Size && GetViewportRect().Encloses(bar.GetGlobalRect()), "Familiar HUD fixed contained composition / " + state + " / " + bar.GetGlobalRect());
            foreach (var control in Descendants(bar).OfType<Control>().Where(c => c.HasMeta("pet_bar_expected_rect")))
            {
                var rect = control.GetMeta("pet_bar_expected_rect").AsRect2();
                require(control.Position == rect.Position && control.Size == rect.Size, "Familiar HUD actual integer geometry / " + state + " / " + control.Name + " / actual " + control.GetRect() + " / expected " + rect);
                require(bar.GetGlobalRect().Encloses(control.GetGlobalRect()), "Familiar HUD control stays inside artwork / " + state + " / " + control.Name);
            }
            foreach (var cell in cells.Prepend(Find<PanelContainer>("pet_bar_attack")))
            {
                var icon = cell.GetNode<Control>("pet_bar_slot_overlay/pet_bar_icon");
                var cooldown = cell.GetNode<Control>("pet_bar_slot_overlay/pet_bar_cooldown");
                require(icon.GetGlobalRect() == cell.GetGlobalRect() && cooldown.GetGlobalRect() == icon.GetGlobalRect(), "Familiar HUD native icon and cooldown share original 32 px cell / " + state + " / " + cell.Name);
                require(cell.GetThemeStylebox("panel") is StyleBoxEmpty, "Native refresh preserves the Classic HUD frame / " + state + " / " + cell.Name);
            }
            string file = (nation == 1 ? "karus" : "human") + "-bar-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        await Frames();
        require(Descendants(bar).OfType<HudLayout>().Count() == 1, "Classic familiar HUD retains the single native drag controller");
        require(bar.HasMeta("classic_pet_bar") && Find<HBoxContainer>("pet_bar_native_row").Visible == false, "HUD extender keeps native controls and removes the native row layout");
        var page = Find<Button>("pet_bar_page"); require(page.FocusMode == Control.FocusModeEnum.None, "Familiar HUD page button does not capture Enter");
        require(page.GetThemeFont("font") == KnightOnlineUiClassic.Plugin.Kit.Bold && page.GetThemeFontSize("font_size") == 13, "Familiar HUD page uses shared bold Classic type");
        var defaultAt = (Vector2)DetailCall(world, "PetBarDefaultPosition")!;
        require(bar.Position == defaultAt, "Classic familiar default position uses its actual compact bounds");
        bar.Position = new Vector2(208, 580);
        var skillIds = Field<List<int>>("_petBarSkills");
        require(cells.Select(SkillId).SequenceEqual(skillIds.Take(8)), "Familiar HUD first page retains native table order");
        await Capture("first-page");
        await Click(page);
        require(page.Text == "2" && cells.Take(2).Select(SkillId).SequenceEqual(skillIds.Skip(8)) && cells.Skip(2).All(c => SkillId(c) == 0), "Actual HUD page click reaches all second-page skills and bounded empty slots");
        await Capture("second-page");
        await Click(page); require(page.Text == "1", "Actual HUD page cycle returns to its first page");
        var sheet = net.Pet!; int oldLevel = sheet.Level, oldMp = sheet.Mp;
        sheet.Level = 1; DetailCall(world, "RefreshPetBar"); await Capture("level-locked");
        require(cells.Where(c => SkillId(c) != 0).All(c => c.TooltipText.Contains("Familiar level") || SkillData.Get(SkillId(c))!.Level <= 1), "HUD locked tooltips retain actual required familiar levels");
        sheet.Level = Math.Max(oldLevel, skillIds.Max(id => SkillData.Get(id)!.Level));
        sheet.Mp = 0; DetailCall(world, "RefreshPetBar"); await Capture("no-mana");
        require(cells.Where(c => SkillId(c) != 0 && SkillData.Get(SkillId(c))!.Msp > 0).All(c => c.GetNode<TextureRect>("pet_bar_slot_overlay/pet_bar_icon").Modulate.R < .5f), "Native insufficient MP dims the shared Classic skill icons");
        sheet.Mp = Math.Max(oldMp, skillIds.Max(id => SkillData.Get(id)!.Msp)); DetailCall(world, "RefreshPetBar");
        var cooldowns = Field<Dictionary<int, double>>("_petSkillReadyAt"); cooldowns.Clear();
        var instant = cells.First(c => SkillId(c) != 0 && SkillData.Get(SkillId(c)) is { CastSeconds: 0, RecastSeconds: > 0 });
        int instantId = SkillId(instant);
        var connection = (KoConn)typeof(Net).GetField("_conn", flags)!.GetValue(net)!;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(30000);
        connection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
            for (int i = 0; i < 120 && !net.Connected; i++) await Frames(1);
            require(net.Connected, "HUD skill callback uses the actual native loopback connection");
            using var stream = receiver.GetStream();
            await Click(instant, MouseButton.Right); require(!stream.DataAvailable && cooldowns.Count == 0, "Familiar HUD right-click does not activate a skill");
            await Click(instant);
            var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)); var body = new byte[length + 2]; await stream.ReadExactlyAsync(body, timeout.Token);
            require(length == 39 && body[0] == (byte)GameOpcodes.GS_PET && body[1] == 2 && body[2] == PetSkills.StageEffecting
                && BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(3)) == instantId, "Actual Classic HUD left-click invokes the original native skill packet");
            DetailCall(world, "PetBarTick", Time.GetTicksMsec() / 1000d);
            var shade = instant.GetNode<ColorRect>("pet_bar_slot_overlay/pet_bar_cooldown");
            require(shade.Visible && cooldowns.ContainsKey(instantId), "HUD activation starts the same native cooldown as the detail page");
            await Capture("cooldown");
            double ready = cooldowns[instantId]; await Click(instant);
            require(!stream.DataAvailable && cooldowns[instantId] == ready, "HUD repeated click cannot submit a skill during cooldown");
            await Click(page); await Click(page); DetailCall(world, "PetBarTick", Time.GetTicksMsec() / 1000d);
            require(shade.Visible, "HUD paging does not discard the original skill cooldown");
            cooldowns[instantId] = Time.GetTicksMsec() / 1000d - 1; DetailCall(world, "PetBarTick", Time.GetTicksMsec() / 1000d);
            require(!shade.Visible, "HUD cooldown shading clears on the same native tick as the detail page");
            await Capture("cooldown-expired");
            typeof(World).GetField("_selfDead", flags)!.SetValue(world, true); await Click(instant);
            require(!stream.DataAvailable, "Dead owner cannot activate a skill from the Classic familiar HUD");
            typeof(World).GetField("_selfDead", flags)!.SetValue(world, false);
            foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed });
            await Frames(); require(!stream.DataAvailable && page.Text == "1", "Enter cannot activate a familiar skill or change HUD pages");
            var hover = instant.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = hover + new Vector2(0, 2), GlobalPosition = hover + new Vector2(0, 2), Relative = new Vector2(0, 2) }, true);
            await Frames(2);
            require(GetViewport().GuiGetHoveredControl() == instant, "Familiar HUD tooltip timer starts on the actual skill control");
            await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout); await Frames();
            IEnumerable<Node> AllNodes(Node node)
            {
                yield return node;
                foreach (var child in node.GetChildren(true)) foreach (var descendant in AllNodes(child)) yield return descendant;
            }
            var tooltip = AllNodes(GetTree().Root).OfType<Label>().SingleOrDefault(l => l.Name == "pet_bar_tooltip_text" && l.IsVisibleInTree());
            require(tooltip != null && tooltip.Text == instant.TooltipText, "Actual familiar HUD pointer hover displays the complete native skill tooltip");
            require(tooltip!.GetThemeFont("font") == KnightOnlineUiClassic.Plugin.Kit.Bold && tooltip.GetThemeFontSize("font_size") == 13
                && tooltip.GetVisibleLineCount() == tooltip.GetLineCount(), "Familiar HUD tooltip shares bold Classic type and shows every wrapped line");
            await Capture("tooltip");
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(780, 660), GlobalPosition = new Vector2(780, 660) }); await Frames();
            foreach (bool pressed in new[] { true, false }) Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = pressed });
            await Frames(); require(bar.Visible && !stream.DataAvailable, "Escape does not dismiss a summoned familiar or activate a HUD skill");
        }
        finally { connection.Close(); }
        sheet.Level = oldLevel; sheet.Mp = oldMp; cooldowns.Clear(); DetailCall(world, "RefreshPetBar");
        var grip = Find<Control>("pet_bar_grip"); var before = bar.Position; var from = grip.GetGlobalRect().GetCenter(); var shift = new Vector2(-31, -19);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from }); await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(2);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from + shift, GlobalPosition = from + shift, Relative = shift, ButtonMask = MouseButtonMask.Left }); await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { Position = from + shift, GlobalPosition = from + shift, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
        require(bar.Position == before + shift && !GetViewport().GuiIsDragging(), "Original ornament moves the native familiar bar without dragging a skill icon");
        bar.Position = new Vector2(208, 580); await Capture("dragged");
        DetailCall(world, "HidePetBar"); require(!bar.Visible, "Dismissal hides the Classic familiar HUD");
        DetailCall(world, "OnPetBarSummoned", sheet); require(bar.Visible && page.Text == "1", "Summoning restores the Classic HUD with native first-page identity");
        await AuditPetBarPlacement(world, layer, bar, require, Capture);
        layer.Free();
        File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-pet-bar.json", JsonSerializer.Serialize(new { screens }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
