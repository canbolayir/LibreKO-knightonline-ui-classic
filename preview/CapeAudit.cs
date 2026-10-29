using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureCapeAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/characters.pck"), false)) throw new Exception("Missing existing character pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 740);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        PluginHost.Ui.ExtendWindow("cape", ClassicCapeSkin.Extend);
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        var world = PreviewFixtures.Prepared(new WorldRoot()); var layer = (CanvasLayer)DetailCall(world, "BuildCapeClassicUiPreview", nation)!; AddChild(layer);
        bool capesEnabled = Cape.Enabled; Cape.Enabled = true;
        var worldViewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(64, 64), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled }; AddChild(worldViewport);
        var worldBody = CharacterPreview.Build(Game.Session.Self.LastEnter.Race, Game.Session.Self.LastEnter.Face, Array.Empty<int>()) ?? throw new Exception("Missing actual world character mesh"); worldViewport.AddChild(worldBody);
        Native.Set(world, "Avatar.Visual", worldBody);
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        await Frames();
        var window = layer.GetChildren().OfType<HudWindow>().Single(); window.SetMeta("content_open_anchor", true); window.Position = new Vector2(190, 100);
        var panel = window.GetChildren().OfType<ClassicCapePanel>().Single();
        T Find<T>(string name) where T : Control => Descendants(panel).OfType<T>().Single(c => c.Name == name);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("CAPE_AUDIT: " + text); checks.Add(text); }
        bool Busy() => (bool)DetailField(world, "CapeWindow.requestInFlight")!;
        bool NetBusy() => (bool)Native.Get<object>(Game.Session.Cape, "Pending")!;
        Notice? Confirmation() => NativeCape.Confirmation(world);
        async Task KeyInput(Key key)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); await Frames(1);
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); await Frames();
        }
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); var viewport = GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
        }
        void Reply(short result, bool success = false, short? clan = null, bool trailing = false)
        {
            var packet = new Packet(GameOpcodes.GS_CAPE); packet.WriteShort(result);
            if (success) { packet.WriteShort(clan ?? (short)Game.Session.Clan.MyClan.ClanId); packet.WriteShort(0); packet.WriteShort((short)(int)DetailField(world, "CapeWindow.choice")!); packet.WriteInt(0); }
            if (trailing) packet.WriteByte(0);
            PreviewPackets.Receive(packet);
        }
        async Task Capture(string state)
        {
            await Frames(); Require(window.Size == ClassicCapeLayout.Size, state + " fixed original composition plus dye extension");
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window fits viewport");
            foreach (var c in Descendants(panel).OfType<Control>().Where(c => c.HasMeta("cape_expected_rect")))
            {
                var rect = c.GetMeta("cape_expected_rect").AsRect2();
                Require((c.Position - rect.Position).Length() < .1 && (c.Size - rect.Size).Length() < .1, state + " actual bounds " + c.Name);
                Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " contained " + c.Name);
                if (c is not LookPreview) Require(c.GetThemeFont("font") == Plugin.Kit.Bold && c.GetThemeFontSize("font_size") == 13, state + " shared bold font " + c.Name);
                if (c is Label label && label.Text.Length > 0) Require(label.GetVisibleLineCount() == label.GetLineCount(), state + " all text lines visible " + c.Name);
            }
            Require(Find<LookPreview>("cape_preview").ClipContents, state + " model stays inside original preview rectangle");
            var model = typeof(LookPreview).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Find<LookPreview>("cape_preview")) as Node3D;
            Require(model != null && Descendants(model).OfType<MeshInstance3D>().Any(m => m.Visible && m.Mesh != null), state + " actual character meshes rendered");
            var viewport = Find<LookPreview>("cape_preview").GetChild<SubViewport>(0);
            var camera = viewport.GetCamera3D();
            var bodyPoints = Descendants(model!).OfType<MeshInstance3D>().Where(m => m is not Cape && m.Mesh != null && m.IsVisibleInTree()).SelectMany(InventoryPortrait.FramingPoints).Select(camera.UnprojectPosition).ToArray();
            Require(bodyPoints.Length > 0 && bodyPoints.Max(p => p.Y) - bodyPoints.Min(p => p.Y) >= 140, state + " character retains original preview scale on reopen");
            if ((int)DetailField(world, "CapeWindow.choice")! >= 0) Require(model!.GetNodeOrNull<Cape>("Cape")?.Mesh != null, state + " actual selected cape mesh attached");
            string file = (nation == 1 ? "karus" : "human") + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state, file });
        }
        (int Id, Cape.CapeDef Def)? castleRestore = null;
        try
        {
            await Capture("initial"); Require(Find<Button>("cape_buy").Disabled, "No purchase before a cape or valid dye is chosen");
            int worn = Game.Session.Self.LastEnter.CapeId; Reply(1, success: true); Require(Game.Session.Self.LastEnter.CapeId == worn, "Unsolicited reply cannot mutate worn cape");
            Require(Cape.Catalogue.Count > 6, "Audit uses real cape catalogue");
            await Click(Find<Button>("cape_colour_0")); await Capture("selected");
            int choice = (int)DetailField(world, "CapeWindow.choice")!; Require(choice >= 0 && !Find<Button>("cape_buy").Disabled, "Colour click selects actual catalogue id");
            Require(worldBody.GetNodeOrNull<Cape>("Cape")?.Mesh != null, "Actual world body receives live draft cape");
            await KeyInput(Key.Enter); Require(Confirmation() == null && !Busy(), "Main Enter never silently purchases a cape");
            await Click(Find<Button>("cape_buy")); await Capture("confirmation"); Require(Confirmation() != null && !Busy(), "Buy first opens payment confirmation");
            int chosen = (int)DetailField(world, "CapeWindow.choice")!;
            Find<Button>("cape_colour_1").EmitSignal(BaseButton.SignalName.Pressed); Require((int)DetailField(world, "CapeWindow.choice")! == chosen, "Confirmation freezes selection callbacks");
            var oldRequest = (DialogRequest)typeof(Notice).GetField("request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Confirmation())!;
            Require(oldRequest.Message == NativeCape.ConfirmText(world, chosen, false) && oldRequest.Message.Contains("CONT") && oldRequest.Title == NativeCape.Title,
                "The Classic confirmation uses the client's purchase wording with the Clan CONT and Noah lines: " + oldRequest.Message.Replace("\n", " | "));
            await KeyInput(Key.Escape); await Capture("cancelled"); Require(Confirmation() == null && !Busy() && (int)DetailField(world, "CapeWindow.choice")! == chosen, "Modal Escape retains draft without submitting");
            await Click(Find<Button>("cape_buy")); oldRequest.Confirm(); Require(!Busy(), "Stale confirmation callback cannot submit replacement modal");
            await KeyInput(Key.Enter); await Capture("pending"); Require(Busy() && NetBusy() && !Find<HSlider>("cape_dye_R").Editable, "Modal Enter submits once and locks edits");
            Require(!Game.Requests.Cape.Buy(0, choice, 0, 0, 0), "Network prevents second simultaneous cape request");
            Reply(1); Reply(-7, trailing: true); Require(Busy() && NetBusy(), "Truncated and trailing responses do not finish purchase");
            Reply(1, success: true, clan: 99); await Capture("foreign-clan");
            Require(!Busy() && !NetBusy() && window.Visible && !Find<Button>("cape_buy").Disabled && Find<HSlider>("cape_dye_R").Editable
                && (int)DetailField(world, "CapeWindow.choice")! == chosen && Game.Session.Self.LastEnter.CapeId == worn
                && Find<Label>("cape_status").Text == LibreKO.Domain.ItemData.Text(16810, "Try again later"),
                "A reply for another clan releases the purchase without applying it and unlocks the same draft");
            await Click(Find<Button>("cape_buy")); await KeyInput(Key.Enter); Require(Busy() && NetBusy(), "The released draft can be submitted again");
            Reply(-7); await Capture("refused"); Require(!Busy() && !NetBusy() && !Find<Button>("cape_buy").Disabled, "Server refusal unlocks the same draft for retry");
            if (!Find<Button>("cape_colour_next").Disabled)
            { await Click(Find<Button>("cape_colour_next")); await Capture("colour-page-two"); Require(NativeCape.ColourPage(world) == 1, "Colour next arrow opens next six choices"); await Click(Find<Button>("cape_colour_previous")); }
            if (!Find<Button>("cape_pattern_next").Disabled)
            { await Click(Find<Button>("cape_pattern_next")); await Capture("pattern-page-two"); Require(NativeCape.PatternPage(world) == 1, "Pattern next arrow opens next four choices"); await Click(Find<Button>("cape_pattern_0")); await Capture("new-pattern"); Require((int)DetailField(world, "CapeWindow.choice")! == -1, "Changing pattern clears stale purchase selection"); Require(worldBody.GetNodeOrNull<Cape>("Cape") == null, "Clearing selection restores no-cape world state immediately"); }
            DetailCall(world, "ShowCapePattern", 0); await Click(Find<Button>("cape_colour_0"));
            Find<HSlider>("cape_dye_R").Value = 144; Find<HSlider>("cape_dye_G").Value = 85; Find<HSlider>("cape_dye_B").Value = 42;
            await Capture("custom-dye"); Require(Find<Label>("cape_price").Text.Contains("36.000") || Find<Label>("cape_price").Text.Contains("36,000"), "Dye fee shown separately from cape price");
            await Click(Find<Button>("cape_turn_left")); await Capture("rotated"); await Click(Find<Button>("cape_turn_right"));
            var member = Game.Session.Clan.MyClan; member.Fame = ClanRanks.Trainee; Native.Call(Game.Session.Clan, "SeedPreview", member); await Capture("member"); Require(Find<Button>("cape_buy").Disabled, "Only chief can buy");
            Require(!Find<Label>("cape_requirement").Text.StartsWith("Purchase Grade") && Find<Label>("cape_requirement").GetLineCount() <= 2,
                "The client's clan refresh (its own SelectCape) does not replace the Classic two-line grade: " + Find<Label>("cape_requirement").Text.Replace("\n", " | "));
            member.Fame = ClanRanks.Chief; member.Flag = ClanTypes.Training; Native.Call(Game.Session.Clan, "SeedPreview", member); await Capture("unpromoted"); Require(Find<Button>("cape_buy").Disabled, "Unpromoted clan cannot buy");
            member.Flag = ClanTypes.Promoted; member.Grade = 5; Native.Call(Game.Session.Clan, "SeedPreview", member); await Capture("rank-locked"); Require(Find<Button>("cape_buy").Disabled, "Rank requirement prevents purchase while retaining inspection");
            Native.Set(world, "Avatar.Dead", true); DetailCall(world, "UpdateCapeGate"); await Capture("dead"); Require(Find<Button>("cape_buy").Disabled, "Dead character cannot buy cape");
            Native.Set(world, "Avatar.Dead", false);
            member.InClan = false; Native.Call(Game.Session.Clan, "SeedPreview", member); await Capture("no-clan"); Require(Find<Button>("cape_buy").Disabled, "Character without clan cannot buy"); member.InClan = true; member.Grade = 2;
            member.Flag = ClanTypes.Royal1; Native.Call(Game.Session.Clan, "SeedPreview", member); DetailCall(world, "CapeWindow.SetStatus", "This cape cannot be applied while the character is busy. Finish the current action and try again.", true); await Capture("long-status");
            await Click(Find<Button>("cape_colour_1"));
            Find<CheckBox>("cape_ticket").ButtonPressed = true; await Capture("ticket"); Require(Find<Button>("cape_buy").Disabled, "Coin cape cannot be bought using ticket opcode");
            Require(Find<Label>("cape_hint").Text == LibreKO.Domain.ItemData.Text(LibreKO.Domain.CapeShop.VoucherRefusedText, "You cannot purchase this item using exchange voucher"),
                "The client's voucher refusal (text 10322) explains the disabled purchase: " + Find<Label>("cape_hint").Text);
            Find<CheckBox>("cape_ticket").ButtonPressed = false;
            // The client sells only castle capes for the exchange voucher (CapeShop.VoucherAllowed). The extracted cape
            // table has no castle flag, so the audit marks one cape as a castle cape for this step.
            var table = (System.Collections.Generic.Dictionary<int, Cape.CapeDef>)typeof(Cape).GetField("table", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            int ticketId = Cape.Catalogue.FirstOrDefault(entry => entry.Value.Castle && entry.Value.M == 0).Key;
            if (ticketId <= 0) { ticketId = Cape.Catalogue.First(entry => entry.Key > 0 && entry.Value.Price == 0 && entry.Value.Points > 0 && entry.Value.M == 0).Key; castleRestore = (ticketId, table[ticketId]); table[ticketId] = table[ticketId] with { Castle = true }; }
            DetailCall(world, "SelectCape", ticketId); Find<CheckBox>("cape_ticket").ButtonPressed = true; await Capture("ticket-eligible"); Require(!Find<Button>("cape_buy").Disabled && Find<Label>("cape_price").Text == "1 castellan ticket", "A castle cape keeps the voucher payment option");
            await Click(Find<Button>("cape_buy"));
            var voucherRequest = (DialogRequest)typeof(Notice).GetField("request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Confirmation())!;
            Require(voucherRequest.Message == LibreKO.Domain.ItemData.Text(LibreKO.Domain.CapeShop.VoucherConfirmText, "Do you wish to purchase with the exchange voucher?"), "The voucher purchase asks with the client's voucher wording (10321)");
            await KeyInput(Key.Escape); Require(Confirmation() == null && !Busy(), "Cancelling the voucher confirmation sends nothing");
            Find<CheckBox>("cape_ticket").ButtonPressed = false;
            var before = window.Position; var p = panel.GlobalPosition + new Vector2(60, 7); var delta = new Vector2(-22, -18); var vp = GetViewport();
            vp.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frames(1);
            vp.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            vp.PushInput(new InputEventMouseMotion { Position = p + delta, GlobalPosition = p + delta, Relative = delta }, true); await Frames(1);
            vp.PushInput(new InputEventMouseButton { Position = p + delta, GlobalPosition = p + delta, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Require((window.Position - before - delta).Length() < 1, "Original top edge forwards native header drag"); await Capture("dragged");
            await Click(Find<Button>("cape_buy")); await KeyInput(Key.Enter); await KeyInput(Key.Escape); Require(!window.Visible && Busy(), "Closing pending purchase retains operation identity");
            DetailCall(world, "ToggleCape"); Require(!window.Visible && Busy(), "Reopen cannot replace outstanding operation"); Reply(-7); Require(!window.Visible && !Busy(), "Late refusal never reopens service");
            DetailCall(world, "ToggleCape"); await Click(Find<Button>("cape_colour_0")); await Click(Find<Button>("cape_buy")); await KeyInput(Key.Enter); Reply(1, success: true); await Capture("applied");
            bool plain = Find<HSlider>("cape_dye_R").Value == 0 && Find<HSlider>("cape_dye_G").Value == 0 && Find<HSlider>("cape_dye_B").Value == 0;
            Require(!plain || Find<Label>("cape_price").Text == LibreKO.Domain.ItemData.Text(LibreKO.Domain.CapeShop.NowEquippedText, "Now equipped"),
                "The worn cape reads the client's \"Now equipped\" instead of a price: " + Find<Label>("cape_price").Text);
            Require(Game.Session.Self.LastEnter.CapeId == worn && (int)DetailField(world, "CapeWindow.current")! == (int)DetailField(world, "CapeWindow.choice")! && !Busy(),
                "Success applies the chosen cape and leaves the worn record to the server's clan update");
            var update = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS); update.WriteByte(0x24); update.WriteShort((short)Game.Session.Clan.MyClan.ClanId);
            update.WriteByte((byte)Game.Session.Clan.MyClan.Flag); update.WriteShort((short)(int)DetailField(world, "CapeWindow.choice")!);
            update.WriteByte(0); update.WriteByte(0); update.WriteByte(0); update.WriteByte(0);
            PreviewPackets.Receive(update);
            Require(Game.Session.Self.LastEnter.CapeId == (int)DetailField(world, "CapeWindow.choice")!, "The server's clan update records the purchased cape as worn");
            Reply(-7); Require(!Busy() && !NetBusy(), "Duplicate idle response cannot alter completed operation");
            await Click(Find<Button>("cape_buy")); await KeyInput(Key.Enter); net.Disconnect(expected: true); Require(!window.Visible && !Busy() && !NetBusy(), "Disconnect closes service and clears pending request");
            DetailCall(world, "ToggleCape"); GetWindow().Size = new Vector2I(1200, 850); await Capture("wide-viewport");
        }
        finally { if (castleRestore is { } restore) ((System.Collections.Generic.Dictionary<int, Cape.CapeDef>)typeof(Cape).GetField("table", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)[restore.Id] = restore.Def; DetailCall(world, "CapeWindow.Dispose"); layer.Free(); worldViewport.Free(); Cape.Enabled = capesEnabled; world.Free(); net.Free(); }
        System.IO.File.WriteAllText(output + "/" + (nation == 1 ? "karus" : "human") + "-cape.json", JsonSerializer.Serialize(new { nation, checks, screens, liveRequests = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Classic cape audit: " + checks.Count + " checks / " + screens.Count + " screens");
    }
}
