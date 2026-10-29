using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

/// <summary>
/// The repair mode at the Blacksmith: hammer cursor frames and the swing timing, the repair tooltip
/// over repairable, non-repairable and unaffordable items, the exhausted (zero durability) wash before
/// and after a repair, Repair All swings, and the cursor hand-back on close. The hardware cursor is not
/// part of a viewport capture, so the captures overlay the cursor art the repair mode shows, with its
/// top-left corner (the original hotspot) at the audit pointer.
/// </summary>
public partial class Preview
{
    private async Task CaptureRepairAudit(PluginGame game, int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        System.IO.Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        foreach (string pack in new[] { "characters", "armor", "weapons" })
            ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/content/" + pack + ".pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        var background = new Color("252822");
        AddChild(new ColorRect { Color = background, Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Repair Preview", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new ItemSlot[InventoryConstants.InventoryTotal]);
        ItemData.EnsureLoaded();
        var checks = new List<string>(); var screens = new List<object>(); var timeline = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("REPAIR_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int n = 6) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string? Applied() => (string?)typeof(GameCursor).GetField("applied", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        GameCursorKind Kind() => (GameCursorKind)typeof(GameCursor).GetField("kind", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        bool InFlight(WorldRoot w) => (bool)DetailField(w, "Repair.InFlight")!;

        // Native repair world and the actual Classic inventory in repair mode.
        var world = PreviewFixtures.Prepared(new WorldRoot()); var window = (HudWindow)DetailCall(world, "Preview.BuildRepair")!;
        AddChild(window); NativeServices.Prepare(window, world); ClassicServiceSkin.Apply(window.Body); await Frames();
        var audio = new LibreKO.Audio(); AddChild(audio);
        SoundCatalog.EnsureLoaded();
        Require(SoundCatalog.TryGet(Sfx.UiRepair, out _), "Original repair sound ID 2001 (ID_SOUND_ITEM_IN_REPAIR) is in the loaded content");
        GameCursor.SetNation(nation); GameCursor.Enable();
        var hammer = NativeRepair.HammerOf(window);
        Require(hammer != null, "Repair window carries the Classic hammer swing");
        foreach (string stem in new[] { "repair", "repair_alt" })
        {
            var art = (Texture2D?)typeof(GameCursor).GetMethod("Peek", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { stem });
            Require(art != null && art.GetSize() == new Vector2(32, 32), "Original 32x32 cursor art is available: " + stem);
            art!.GetImage().SavePng(output + "/" + stem + "-cursor.png");
        }
        DetailCall(world, "Repair.Close");
        Game.Session.Self.Sheet.SetMaxWeight(17_100);
        var inventory = (Inventory)Game.Session.Self.Inventory; inventory.EnsureLength(InventoryConstants.InventoryTotal);
        // Durability states: right hand exhausted (0), carried weapon worn, top used, boots intact, helmet not repairable.
        inventory[InventoryConstants.RightHand] = inventory[Inventory.GridStart];
        inventory.SetDurability(InventoryConstants.RightHand, 0);
        var bridge = Native.Call(world, "CreatePluginAdapters")!;
        var attach = typeof(PluginGame).GetMethod("Attach", flags)!;
        Native.Call(bridge, "AttachTo", game);
        var shell = new HudWindow("inventory", "Inventory") { Visible = false };
        shell.Body.AddChild((Control)DetailField(world, "InventoryWindow.InvContent")!);
        foreach (var child in shell.GetChildren().OfType<Control>()) child.Visible = false;
        shell.AddThemeStyleboxOverride("panel", new StyleBoxEmpty()); AddChild(shell);
        ((Dictionary<string, HudWindow>)DetailField(world, "MainWindows.Windows")!)["Inventory"] = shell;
        var host = (WindowHost)Create(typeof(WindowHost), "inventory", "Inventory", shell, (Action)(() => DetailCall(world, "MainWindows.Hide", "Inventory")));
        var actual = new InventoryWindow(host); shell.AddChild(actual); shell.ResetSize();
        void Refresh() => typeof(InventoryWindow).GetMethod("Refresh", flags)!.Invoke(actual, null);
        DetailCall(world, "Repair.Open"); await Frames();
        shell.Position = new Vector2(250, 61); Refresh(); await Frames();
        Require(!window.Visible && shell.Visible && shell.GetMeta("classic_repair_mode").AsBool(), "Repair opens the actual inventory in repair mode");
        Require(Kind() == GameCursorKind.Repair && Applied() == "repair", "Repair mode shows the raised hammer (cursorrepair01) everywhere");
        Require(!hammer!.Struck, "The hammer is raised before any repair is sent");

        var cells = Descendants(actual).OfType<KnightOnlineUiClassic.Layout.ItemSlot>().ToDictionary(c => c.Slot);
        var tip = Descendants(actual).OfType<ClassicInventoryRepair>().Single();
        var pointer = Vector2.Zero; tip.Pointer = () => pointer;
        var overlayLayer = new CanvasLayer { Layer = 130 }; AddChild(overlayLayer);
        var cursor = new TextureRect { Size = new Vector2(32, 32), MouseFilter = MouseFilterEnum.Ignore }; overlayLayer.AddChild(cursor);
        Label Cost() => Descendants(tip.Tip).OfType<Label>().Single(l => l.Text.EndsWith(" Noahs"));
        Label Title() => Descendants(tip.Tip).OfType<Label>().Single(l => l.Text is "Repair Cost" or "Cannot repair");
        void Point(int slot) { var cell = cells[slot]; pointer = (cell.GlobalPosition + cell.Size / 2).Round(); }
        void Hover(int slot) { Point(slot); cells[slot].OnHover!(slot, true); }
        void Click(int slot)
        {
            var cell = cells[slot]; Point(slot);
            cell._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = cell.Size / 2 });
            cell._GuiInput(new InputEventMouseButton { Pressed = false, ButtonIndex = MouseButton.Left, Position = cell.Size / 2 });
        }
        async Task Capture(string state, string note)
        {
            cursor.Texture = hammer.Current; cursor.Position = pointer;
            bool struck = hammer.Struck; string? shown = Applied();
            await Frames(2);
            var image = GetViewport().GetTexture().GetImage();
            Require(GetViewportRect().Encloses(actual.GetGlobalRect()), state + ": inventory fits the viewport");
            string file = prefix + "-repair-" + state + ".png"; image.SavePng(output + "/" + file);
            screens.Add(new { state, file, note, pointer = pointer.ToString(), struck, cursor = struck && shown != "repair_alt" ? "repair_alt (Classic swing over " + shown + ")" : shown,
                tip = tip.Tip.Visible ? tip.Tip.GetGlobalRect().ToString() : null });
        }
        IconWash Wash(int slot) => (IconWash)typeof(IconVeilView).GetField("wash", flags)!.GetValue(Descendants(cells[slot]).OfType<IconVeilView>().Single())!;

        // Durability colours before repair.
        Require(Wash(InventoryConstants.RightHand) == IconWash.Exhausted, "A zero-durability item carries the original exhausted wash");
        Require(new[] { Inventory.GridStart, Inventory.GridStart + 1, Inventory.GridStart + 2 }.All(s => Wash(s) == IconWash.None), "Worn but usable items keep their plain icon (no wash above zero durability)");
        Hover(InventoryConstants.RightHand);
        await Capture("durability-before", "Right hand at zero durability: original RGBA(200,20,20,100) wash; tooltip shows the full repair price");
        var washRect = cells[InventoryConstants.RightHand].GetGlobalRect();
        var before = GetViewport().GetTexture().GetImage();

        // Tooltip states.
        Require(tip.Tip.Visible && Title().Text == "Repair Cost", "Hovering a repairable item shows the original repair-cost tooltip");
        Require(tip.Tip.GetGlobalRect() == new Rect2(ClassicInventoryRepair.Place(pointer, new Vector2(223, 98), GetViewportRect().Size), new Vector2(223, 98)),
            "Tooltip follows the original placement (pointer + 26 px right, bottom on the pointer) " + tip.Tip.GetGlobalRect());
        Require(tip.Tip.GetGlobalRect().Position == pointer + new Vector2(26, -98), "Tooltip sits 26 px right of and directly above the pointer");
        var tipPixel = before.GetPixelv((Vector2I)(tip.Tip.GetGlobalRect().Position + new Vector2(6, 26)).Round());
        Require(Math.Abs(tipPixel.R - background.R * (1 - 187 / 255f)) < .03f && tipPixel.R > .01f,
            "Tooltip keeps the original translucent black (alpha 187) background " + tipPixel);
        Require(Cost().GetThemeColor("font_color") == new Color("ffff00"), "Affordable price keeps the UIF yellow");
        Hover(Inventory.GridStart);
        await Capture("hover-repairable", "Worn carried weapon: price, maximum and current durability");
        Hover(Inventory.GridStart + 3);
        await Frames(2);
        Require(tip.Tip.Visible && Title().Text == "Cannot repair" && !Cost().Visible, "A non-repairable item shows only the original cannot-repair line");
        Require(Kind() == GameCursorKind.Repair, "The hammer does not change over a non-repairable item");
        await Capture("hover-not-repairable", "Item that cannot be repaired");
        int gold = game.Character.Gold;
        Game.Session.Self.Sheet.SeedWealth(10, 0); Hover(Inventory.GridStart); await Frames(2);
        Require(Cost().GetThemeColor("font_color") == ClassicInventoryRepair.LackColor, "Unaffordable price turns the original red 0xffdd0000");
        await Capture("hover-not-enough-gold", "Price above the carried Noahs in the original red");
        Click(Inventory.GridStart);
        Require(!InFlight(world) && !hammer.Struck, "An unaffordable repair is not sent and the hammer does not swing");
        Game.Session.Self.Sheet.SeedWealth(1_180_000, 2_450); Refresh();

        // A slow answer: the struck hammer stays down for the whole wait, as in the original.
        Hover(Inventory.GridStart + 1);
        Click(Inventory.GridStart + 1);
        Require(InFlight(world) && Kind() == GameCursorKind.RepairAlt && hammer.Struck,
            "Releasing on the same item sends the repair; the struck hammer (cursorrepair02) shows while waiting");
        await Wait(ClassicRepairHammer.StrikeSeconds + .15);
        Require(InFlight(world) && Applied() == "repair_alt", "While the server has not answered the hammer stays down");
        await Capture("swing-slow-waiting", "Waiting for a slow server: struck hammer for the whole round trip");
        DetailCall(world, "DeliverRepairResult", true, 0); await Frames(2);
        Require(!hammer.Struck && Applied() == "repair", "A late answer raises the hammer at once");
        Require(Descendants(audio).OfType<AudioStreamPlayer>().Any(p => p.Bus == LibreKO.Audio.BusUi && p.GetMeta("snd", 0).AsInt32() == Sfx.UiRepair),
            "The acknowledged repair plays sound 2001");

        // An instant (local) answer: the Classic swing keeps the struck frame visible.
        Hover(InventoryConstants.RightHand);
        await Capture("swing-0-raised", "Before the click: raised hammer");
        Click(InventoryConstants.RightHand);
        ulong sent = Time.GetTicksMsec();
        Require(InFlight(world) && hammer.Struck, "The Classic swing starts with the request");
        DetailCall(world, "DeliverRepairResult", true, 0);
        Require(Kind() == GameCursorKind.Repair && Applied() == "repair", "The server result restores the client's raised hammer state");
        Require(hammer.Struck, "An instant local answer still leaves the hammer down");
        Refresh(); // the preview bridge raises no inventory change; the live client does
        var overlaid = typeof(ClassicRepairHammer).GetField("_overlaid", flags)!;
        bool laidOver = false;
        double releasedAt = -1; bool held = false;
        var crops = new List<Image>(); const int Crop = 96;
        while (Time.GetTicksMsec() - sent < 450)
        {
            bool frameStruck = hammer.Struck;
            cursor.Texture = hammer.Current; cursor.Position = pointer;
            await Frames(1);
            double t = (Time.GetTicksMsec() - sent) / 1000.0;
            if (!hammer.Struck && releasedAt < 0) releasedAt = t;
            if (hammer.Struck && (bool)overlaid.GetValue(hammer)!) laidOver = true;
            if (crops.Count < 24) crops.Add(GetViewport().GetTexture().GetImage().GetRegion(new Rect2I((Vector2I)pointer - new Vector2I(Crop / 2, Crop / 2), new Vector2I(Crop, Crop))));
            timeline.Add(new { phase = "single", t = Math.Round(t, 3), struck = hammer.Struck, frame = crops.Count - 1, shown = frameStruck ? "repair_alt" : "repair" });
            if (!held && hammer.Struck) { held = true; await Capture("swing-1-struck", "Answered at once (sound 2001); struck hammer held, " + Math.Round(t, 2) + " s after the click"); }
        }
        Require(held, "A struck frame was captured after the instant answer");
        Require(laidOver, "The struck art is laid over the client's raised hammer while the swing lasts");
        var strip = Image.CreateEmpty(Crop * crops.Count, Crop, false, crops[0].GetFormat());
        for (int i = 0; i < crops.Count; i++) strip.BlitRect(crops[i], new Rect2I(0, 0, Crop, Crop), new Vector2I(i * Crop, 0));
        strip.SavePng(output + "/" + prefix + "-repair-swing-frames.png");
        Require(releasedAt >= ClassicRepairHammer.StrikeSeconds - .02 && releasedAt <= ClassicRepairHammer.StrikeSeconds + .12,
            "The struck hammer is held for about " + ClassicRepairHammer.StrikeSeconds + " s after the click (released at " + releasedAt + " s)");
        Require(!hammer.Struck, "The hammer rises again after the swing");
        await Capture("swing-2-raised", "Swing finished: raised hammer again");

        // Durability colours after repair.
        Require(inventory[InventoryConstants.RightHand].Durability == ItemData.MaxDurabilityOf(inventory[InventoryConstants.RightHand].ItemId), "Repair restores maximum durability");
        Refresh(); Hover(InventoryConstants.RightHand); await Frames(2);
        Require(Wash(InventoryConstants.RightHand) == IconWash.None, "The exhausted wash is removed after repair");
        await Capture("durability-after", "Right hand repaired: wash gone, price 0");
        var after = GetViewport().GetTexture().GetImage();
        var probe = (Vector2I)(washRect.Position + new Vector2(5, washRect.Size.Y - 6)).Round();
        Color b = before.GetPixelv(probe), a = after.GetPixelv(probe);
        Require(b.R - b.G > a.R - a.G + .08f, "Rendered pixels confirm the red wash before and its removal after (" + b + " -> " + a + ")");

        // Repair All: one swing per item sent.
        inventory.SetDurability(Inventory.GridStart + 1, 500); inventory.SetDurability(Inventory.GridStart + 2, 700); Refresh();
        var all = Descendants(actual).OfType<Button>().Single(x => x.Text == "Repair All");
        Require(!all.Disabled, "Repair All is available while damaged gear remains");
        Point(Inventory.GridStart + 4); tip.HideTip();
        all.EmitSignal(BaseButton.SignalName.Pressed);
        Require(InFlight(world) && hammer.Struck, "Repair All sends the first item and swings");
        int answered = 0;
        while (InFlight(world)) { DetailCall(world, "DeliverRepairResult", true, 0); answered++; }
        Require(answered >= 2, "Repair All serializes several damaged items (" + answered + ")");
        ulong allStart = Time.GetTicksMsec(); bool was = hammer.Struck, shown = false; int swings = was ? 1 : 0;
        double expected = answered * ClassicRepairHammer.StrikeSeconds + (answered - 1) * ClassicRepairHammer.RaiseSeconds;
        while (Time.GetTicksMsec() - allStart < (expected + .4) * 1000)
        {
            await Frames(1);
            if (hammer.Struck && !was) swings++;
            was = hammer.Struck;
            timeline.Add(new { phase = "repair-all", t = Math.Round((Time.GetTicksMsec() - allStart) / 1000.0, 3), struck = hammer.Struck });
            if (!shown && swings == 2 && hammer.Struck) { shown = true; await Capture("repair-all-swing", "Repair All: second swing"); }
        }
        Require(swings == Math.Min(answered, 1 + ClassicRepairHammer.MaxQueued), "Repair All swings once per repaired item (" + swings + " of " + answered + ")");
        Require(!hammer.Struck && Applied() == "repair", "After Repair All the raised hammer returns");

        // Closing hands the cursor back.
        Click(InventoryConstants.RightHand);
        Require(!InFlight(world), "A fully repaired item is not sent again");
        inventory.SetDurability(Inventory.GridStart + 1, 100); Refresh();
        Click(Inventory.GridStart + 1); Require(hammer.Struck, "A new repair swings again");
        DetailCall(world, "MainWindows.Hide", "Inventory"); await Frames(2);
        Require(!shell.GetMeta("classic_repair_mode").AsBool() && !hammer.Struck, "Closing the inventory ends the repair mode and the swing");
        Require(Kind() == GameCursorKind.Arrow && Applied() == (nation == 1 ? "ka_idle" : "el_idle"), "Closing repair restores the nation cursor");
        DetailCall(world, "DeliverRepairResult", true, 0);

        overlayLayer.Free(); audio.Free(); GameCursor.Disable();
        shell.Free(); window.Free(); world.Free(); await Frames();
        System.IO.File.WriteAllText(output + "/" + prefix + "-repair.json", JsonSerializer.Serialize(new
        {
            strikeSeconds = ClassicRepairHammer.StrikeSeconds, raiseSeconds = ClassicRepairHammer.RaiseSeconds, gold,
            screens, timeline, checks,
        }, new JsonSerializerOptions { WriteIndented = true }));
        net.Free(); GD.Print("REPAIR_AUDIT_OK " + checks.Count);
    }
}
