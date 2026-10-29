using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Collections;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    /// <summary>What an opening row must reach in the client after it is pressed.</summary>
    private sealed record OpeningProbe(Action Before, Func<bool> Opened, Action After);

    /// <summary>One NPC whose reply the client shows in its shared NPC dialogue and the Classic skin on an original window.</summary>
    private sealed record OpeningCase(string Kind, string State, ClassicNpcOpeningLayout Layout, string NpcName, int NpcId, int NpcType,
        Action Open, Action Raise, string DisabledCaption, Dictionary<string, OpeningProbe> Probes);

    /// <summary>The Inn Hostess's opening window only (the mode that introduced it).</summary>
    private Task CaptureInnAudit(int nation) => CaptureNpcOpeningAudit(nation, innOnly: true);

    /// <summary>
    /// The NPC opening windows on their original art: the Inn Hostess (co_inn_us) and the Blacksmith and other
    /// repairing merchants (co_smith_us), both opened through the client's own replies, and the stat/skill point
    /// redistribution menu (co_change_us). Each is captured normal, hovered, pressed and with a disabled choice,
    /// its bounds are checked against the declared layout, and every choice is driven with real pointer input
    /// into the client's own service.
    /// </summary>
    private async Task CaptureNpcOpeningAudit(int nation, bool innOnly = false)
    {
        string mode = innOnly ? "inn-audit" : "npc-opening-audit", tag = innOnly ? "INN_AUDIT" : "NPC_OPENING_AUDIT";
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/" + mode);
        System.IO.Directory.CreateDirectory(output);
        foreach (string pack in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck", "build/client/source-content/content/npcs.pck", "build/client/source-content/content/weapons.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../" + pack), false)) throw new Exception("Missing audit pack " + pack);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        var backdrop = new Color("252822");
        AddChild(new ColorRect { Color = backdrop, Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Opening Preview", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new LibreKO.Domain.ItemSlot[InventoryConstants.InventoryTotal]);
        Game.Session.Self.Sheet.SeedWealth(12_000_000, 0); Game.Session.Self.Sheet.SeedProgress(60, 0, 1);
        ItemData.EnsureLoaded();
        var world = PreviewFixtures.Prepared(new WorldRoot());
        ((Inventory)Game.Session.Self.Inventory).EnsureLength(InventoryConstants.InventoryTotal);
        DetailCall(world, "InventoryWindow.BuildPanel");
        var orphan = (Control)DetailField(world, "InventoryWindow.InvContent")!; orphan.Visible = false; world.AddChild(orphan);
        DetailCall(world, "ItemTooltip.Build");
        DetailCall(world, "NpcDialogWindow.BuildNpcDialog");
        DetailCall(world, "Warehouse.Build"); DetailCall(world, "VipWarehouse.BuildPanel"); DetailCall(world, "Seal.BuildWindow");
        var sealLayer = new CanvasLayer(); world.AddChild(sealLayer); Native.Set(world, "CharacterSealWindow.Layer", sealLayer); DetailCall(world, "CharacterSealWindow.BuildWindow");
        DetailCall(world, "ClanCreate.Build");
        // The Blacksmith's services and the client's own network subscription (RepairInit), the merchant window and the redistribution menu.
        DetailCall(world, "Repair.Build"); DetailCall(world, "Vendor.BuildPanel"); DetailCall(world, "ClassChange.BuildPanel");
        var npcLayer = (Node)DetailField(world, "NpcDialogWindow.layer")!; npcLayer.Reparent(this);
        // Clan naming focuses its field, which needs the scene tree; the window itself stays out of the captures.
        var clanLayer = (Node)DetailField(world, "ClanCreate.layer")!; clanLayer.Reparent(this);
        var window = (HudWindow)DetailField(world, "NpcDialogWindow.Panel")!;

        var actorType = Native.ClientType("LibreKO.Presentation.EntityView")!;
        var actor = Activator.CreateInstance(actorType, nonPublic: true)!;
        ((EntityRegistry)DetailField(world, "EntitySystem.Ents")!)[701] = (EntityView)actor;
        void Npc(string name, int id, int type)
        {
            foreach (var (field, value) in new (string, object)[] { ("NpcId", id), ("ModelId", id), ("Level", 50), ("NpcType", type), ("Name", name), ("IsNpc", true), ("Gear", new int[8]) })
                actorType.GetField(field)!.SetValue(actor, value);
            Native.Set(Game.Session, "NpcQuest.LastNpcId", 701); Native.Set(Game.Session, "NpcQuest.TalkingNpcId", -1);
            Native.Set(Game.Session, "NpcQuest.LastNpcName", name);
        }
        var portrait = new NpcPortraitProbe(world); _windowData!.NpcPortraitSource = () => portrait.NpcPortrait;

        var dialogue = (ClassicNpcDialogue)CharacterDetailsSkin.Apply(window.Body)!;
        var view = window.GetChildren().OfType<ClassicNpcOpeningChoice>().Single();
        // Quest and talk menus show on the bundled 2.x quest menu (the default) or, in the fallback, the Classic dialogue.
        var myko = window.GetChildren().OfType<MykoQuestDialogue>().Single();
        bool art = MykoQuestLayout.Current != null;
        if (art == ClassicFallback) throw new Exception("NPC opening audit: the 2.x quest menu art is " + (art ? "present" : "missing") + " at " + Plugin.Kit.Local.Root);
        Control Shared() => art ? myko : dialogue;
        float SharedWidth() => art ? myko.Layout!.MenuWidth : ClassicNpcLayout.Width;
        NativeNpcOpening.Prepare(window, world);
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string message) { if (!valid) throw new Exception(tag + ": " + message); checks.Add(message); }
        async Task Frames(int count = 8) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Pointer(Vector2 point, bool? press = null)
        {
            if (press == null) Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
            else Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = press.Value, Position = point, GlobalPosition = point });
        }
        async Task Click(Vector2 at) { Pointer(at); await Frames(2); Pointer(at, true); await Frames(2); Pointer(at, false); await Frames(3); }
        Image Shot() => GetViewport().GetTexture().GetImage();
        int Count(Image image, Rect2 rect, Color ink, float tolerance = .1f)
        {
            int found = 0;
            for (int y = (int)rect.Position.Y; y < (int)rect.End.Y; y++)
                for (int x = (int)rect.Position.X; x < (int)rect.End.X; x++)
                {
                    var c = image.GetPixel(x, y);
                    if (Mathf.Abs(c.R - ink.R) <= tolerance && Mathf.Abs(c.G - ink.G) <= tolerance && Mathf.Abs(c.B - ink.B) <= tolerance) found++;
                }
            return found;
        }
        void Save(Image image, Rect2 rect, string state, IEnumerable<Button>? rows)
        {
            using var crop = image.GetRegion(new Rect2I((int)rect.Position.X - 10, (int)rect.Position.Y - 10, (int)rect.Size.X + 20, (int)rect.Size.Y + 20));
            string file = prefix + "-" + state + ".png"; crop.SavePng(System.IO.Path.Combine(output, file));
            screens.Add(new { state, file, window = rect.ToString(),
                rows = rows?.Select(r => new { caption = r.Text, rect = r.GetGlobalRect().ToString(), mode = r.GetDrawMode().ToString() }).ToArray() });
        }
        Button[] Choices() => Descendants((Node)DetailField(world, "NpcDialogWindow.menuBox")!).OfType<Button>().ToArray();
        var viewport = new Rect2(Vector2.Zero, GetViewportRect().Size);

        // Shared checks for an opening window drawn with `layout` at `frame` (global), greeting and rows given.
        void Geometry(string name, ClassicNpcOpeningLayout layout, Rect2 frame, Label greeting, IReadOnlyList<Button> rows, int shift)
        {
            var art = Plugin.Kit.Layout(layout.Art);
            Require(layout.Height(layout.OriginalRows) == art.H && ClassicNpcOpeningLayout.Width == art.W && ClassicNpcOpeningLayout.BaseHeight == art.H,
                $"{name}: the original row count gives the original {art.W} × {art.H} {layout.Art} window");
            Require(frame.Size == new Vector2(ClassicNpcOpeningLayout.Width, layout.Height(rows.Count, shift)),
                $"{name}: original width, height changed by whole rows and greeting lines only: {frame.Size} for {rows.Count} rows, shift {shift}");
            Require(viewport.Encloses(frame), $"{name}: window inside the viewport {frame}");
            var title = art.Find("Text_Title")!;
            Require(greeting.GetRect() == layout.GreetingRect(shift), $"{name}: greeting sits in the original Text_Title rectangle {greeting.GetRect()} (min {greeting.GetCombinedMinimumSize()}, custom {greeting.CustomMinimumSize}, parent {greeting.GetParent().Name}, autowrap {greeting.AutowrapMode})");
            int lines = greeting.GetLineCount();
            float shaped = lines * greeting.GetLineHeight() + (lines - 1) * greeting.GetThemeConstant("line_spacing");
            Require(lines <= greeting.GetVisibleLineCount() && greeting.Text.Length > 0 && shaped <= greeting.Size.Y + .5f && Mathf.Abs(shaped - ClassicNpcOpening.TextHeight(greeting)) < 1,
                $"{name}: every greeting line is visible and the measured height matches the shaped label ({lines} lines, {shaped:0.#} px in {greeting.Size.Y} px)");
            Require(shift == 0 || ClassicNpcOpening.TextHeight(greeting) > layout.Greeting.Size.Y && greeting.Size.Y - ClassicNpcOpening.TextHeight(greeting) < 1,
                $"{name}: rows move down only by the lines the client's greeting needs beyond the original string ({shift} px)");
            Require(greeting.GetThemeFontSize("font_size") == UiKit.FontSize(title) && greeting.GetThemeColor("font_color") == title.Color && greeting.GetThemeFont("font") == Plugin.Kit.FontFor(title),
                $"{name}: greeting uses the original white bold Arial {title.Size} pt");
            var ink = art.Find(layout.RowInk)!;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                Require(row.GetRect() == layout.Choice(i, shift), $"{name}: row {i} actual bounds {row.GetRect()} match the declared row");
                Require(frame.Encloses(row.GetGlobalRect()), $"{name}: row {i} inside the frame");
                Require(i == 0 || row.Position.Y - rows[i - 1].GetRect().End.Y >= 5, $"{name}: row {i} keeps the original gap to the row above");
                float width = row.GetThemeFont("font").GetStringSize(row.Text, HorizontalAlignment.Left, -1, row.GetThemeFontSize("font_size")).X;
                Require(ClassicNpcOpening.Fits(row, layout) && row.TooltipText.Length == 0, $"{name}: caption '{row.Text}' is not cut ({width:0} px)");
                Require(row.GetThemeFontSize("font_size") == UiKit.FontSize(ink) && row.GetThemeColor("font_color") == ink.Color && row.GetThemeFont("font") == Plugin.Kit.FontFor(ink),
                    $"{name}: row {i} uses the original yellow Arial {ink.Size} pt{(ink.Bold ? " bold" : "")}");
            }
            Require(rows[0].Position.Y - greeting.GetRect().End.Y >= layout.FirstRow - layout.Greeting.End.Y, $"{name}: first row keeps the original distance to the greeting");
            Require(frame.End.Y - rows[^1].GetGlobalRect().End.Y == layout.BottomMargin, $"{name}: original space under the last row ({layout.BottomMargin} px)");
            Require(rows[0].GetGlobalRect().Position.X - frame.Position.X == frame.End.X - rows[0].GetGlobalRect().End.X, $"{name}: rows have equal side margins");
            var pieces = art.Children.Where(n => n.IsImage && n.Texture != null).Select(n => ClassicNpcOpeningLayout.Piece(n, (int)frame.Size.Y)).ToArray();
            Require(pieces.All(p => new Rect2(Vector2.Zero, frame.Size).Encloses(p)) && pieces.Sum(r => r.Area) == frame.Size.X * frame.Size.Y,
                $"{name}: the {pieces.Length} art pieces cover the whole window without gaps or overlaps");
        }

        // States under real pointer input: normal yellow, hover white, pressed frame gold (nothing fires before release).
        async Task<Button> States(string name, string state, Rect2 frame, IReadOnlyList<Button> rows, ClassicNpcOpeningLayout layout, Func<int> fired)
        {
            var ink = Plugin.Kit.Layout(layout.Art).Find(layout.RowInk)!;
            using (var normal = Shot())
            {
                Save(normal, frame, state + "-open", rows);
                Require(Count(normal, new Rect2(frame.Position.X, frame.GetCenter().Y, 3, 1), backdrop, .02f) == 0, $"{name}: frame art drawn at the left edge");
                Require(rows.All(r => r.Disabled || Count(normal, r.GetGlobalRect(), ink.Color) >= 12), $"{name}: every available row shows the original yellow caption");
            }
            var first = rows[0]; var point = first.GetGlobalRect().Position + new Vector2(layout.CaptionInset + 14, 10);
            Pointer(point); await Frames(3);
            using (var hover = Shot())
            {
                Save(hover, frame, state + "-hover", rows);
                Require(first.GetDrawMode() == BaseButton.DrawMode.Hover && Count(hover, first.GetGlobalRect(), Colors.White) >= 12 && Count(hover, first.GetGlobalRect(), ink.Color) == 0,
                    $"{name}: hover turns the caption white");
            }
            Pointer(point, true); await Frames(3);
            using (var pressed = Shot())
            {
                Save(pressed, frame, state + "-pressed", rows);
                Require(first.GetDrawMode() == BaseButton.DrawMode.Pressed && Count(pressed, first.GetGlobalRect(), ClassicNpcOpeningLayout.Pressed) >= 12,
                    $"{name}: pressed caption turns frame gold and drops one pixel");
                Require(fired() == 0, $"{name}: nothing fires before the button is released");
            }
            Pointer(point, false); await Frames(3);
            return first;
        }

        async Task<object> Opening(OpeningCase c)
        {
            async Task Open() { DetailCall(world, "NpcDialogWindow.Close"); Npc(c.NpcName, c.NpcId, c.NpcType); c.Open(); await Frames(); }
            string name = c.State;

            // The previous presentation: the shared NPC dialogue (the 2.x quest menu, or the Classic dialogue) with the client's choices.
            if (art)
            {
                // The 2.x menu stands aside while the view still holds the previous opening: let a plain menu clear it first.
                DetailCall(world, "NpcDialogWindow.Close");
                DetailCall(world, "NpcDialogWindow.Begin", c.NpcName, "How can I help you?");
                DetailCall(world, "AddNpcMenuButton", "1.   Close", (Action)(() => { }));
                DetailCall(world, "NpcDialogWindow.End", 1); await Frames(4);
                Require(!view.Active, $"{name}: a plain menu clears the opening view");
            }
            view.ProcessMode = ProcessModeEnum.Disabled; view.Visible = false; if (!art) dialogue.Visible = true;
            await Open();
            for (int i = 0; i < 100 && !art && dialogue.Portrait.Visible && !dialogue.Portrait.IsReady; i++) await Frames(1);
            await Frames(4);
            using (var before = Shot()) Save(before, window.GetGlobalRect(), c.State + "-shared-dialogue", null);
            Require(Shared().Visible && !view.Visible && (!art || !dialogue.Visible), $"{name}: without the opening view the client's choices still show in the shared NPC dialogue ({(art ? "2.x quest menu" : "Classic")})");
            if (art) Require(myko.Options.Count == Choices().Length, $"{name}: one 2.x menu row per client choice ({myko.Options.Count})");
            view.ProcessMode = ProcessModeEnum.Inherit;

            await Open();
            var native = Choices();
            Require(view.Active && view.Kind == c.Kind && view.Layout == c.Layout && view.Visible && !dialogue.Visible && !myko.Visible,
                $"{name}: the client's reply shows the {c.Layout.Art} window instead of the shared NPC dialogue");
            Require(view.Rows.Count == native.Length && native.Length >= 2, $"{name}: one original text row per client choice ({native.Length})");
            Require(view.Rows.Select(r => r.Text).SequenceEqual(native.Select(b => ClassicNpcOpeningLayout.Caption(b.Text))), $"{name}: rows keep the client's captions and order: " + string.Join(" | ", native.Select(b => b.Text)));
            Require(native.Last().Text == "Close" && view.Rows[^1].Text == ClassicNpcOpeningLayout.LeaveCaption, $"{name}: the client's Close choice is the last row and reads as the original leave row");
            var frame = window.GetGlobalRect();
            Require(view.Size == frame.Size && Mathf.Abs(frame.GetCenter().X - viewport.GetCenter().X) <= 1 && Mathf.Abs(frame.GetCenter().Y - viewport.GetCenter().Y) <= 1, $"{name}: window opens centred: {frame}");
            Require(view.Greeting.Text == ((RichTextLabel)Descendants(window.Body).OfType<RichTextLabel>().First()).GetParsedText().Trim(), $"{name}: the greeting is the client's own text");
            Geometry(name, c.Layout, frame, view.Greeting, view.Rows, view.Shift);
            Require(NpcPortraitCache.ActiveRenderers == 0, $"{name}: no NPC portrait is rendered for the opening window");

            int[] fired = new int[native.Length];
            for (int i = 0; i < native.Length; i++) { int index = i; native[i].Pressed += () => fired[index]++; }
            c.Probes.TryGetValue(native[0].Text, out var firstProbe);
            firstProbe?.Before();
            await States(name, c.State, frame, view.Rows, c.Layout, () => fired.Sum());
            Require(fired[0] == 1 && fired.Sum() == 1 && !window.Visible, $"{name}: releasing the first row presses the client's own first choice once and closes the window");
            var verified = new List<string>();
            if (firstProbe != null) { Require(firstProbe.Opened(), $"{name}: row '{native[0].Text}' reaches the client's service"); verified.Add(native[0].Text); firstProbe.After(); }
            Pointer(Vector2.Zero); await Frames(2);

            // Each remaining choice through real pointer input, checked on the client's own state.
            for (int i = 1; i < native.Length; i++)
            {
                await Open();
                native = Choices();
                int calls = 0; native[i].Pressed += () => calls++;
                string caption = native[i].Text;
                c.Probes.TryGetValue(caption, out var probe);
                probe?.Before();
                await Click(view.Rows[i].GetGlobalRect().GetCenter());
                Require(calls == 1 && !window.Visible, $"{name}: row '{caption}' presses the client's own choice once and the window closes");
                if (probe != null) { Require(probe.Opened(), $"{name}: row '{caption}' reaches the client's service"); verified.Add(caption); probe.After(); }
                Pointer(Vector2.Zero); await Frames(1);
            }
            Require(verified.Count == native.Length, $"{name}: every choice was verified on the client's state ({verified.Count}/{native.Length})");

            // A choice the client disables is shown greyed out and cannot be pressed.
            await Open();
            native = Choices();
            int blockedIndex = Array.FindIndex(native, b => b.Text == c.DisabledCaption); if (blockedIndex < 0) blockedIndex = 1;
            native[blockedIndex].Disabled = true; int disabledCalls = 0; native[blockedIndex].Pressed += () => disabledCalls++;
            await Frames(2);
            var blocked = view.Rows[blockedIndex]; var blockedAt = blocked.GetGlobalRect().GetCenter();
            var rowInk = Plugin.Kit.Layout(c.Layout.Art).Find(c.Layout.RowInk)!;
            Pointer(blockedAt); await Frames(3);
            using (var disabled = Shot())
            {
                Save(disabled, window.GetGlobalRect(), c.State + "-disabled", view.Rows);
                Require(blocked.Disabled && blocked.GetDrawMode() == BaseButton.DrawMode.Disabled && Count(disabled, blocked.GetGlobalRect(), ClassicNpcOpeningLayout.Disabled, .08f) >= 12
                    && Count(disabled, blocked.GetGlobalRect(), rowInk.Color) == 0, $"{name}: a disabled client choice is grey, also under the pointer");
            }
            Pointer(blockedAt, true); await Frames(2); Pointer(blockedAt, false); await Frames(2);
            Require(disabledCalls == 0 && window.Visible, $"{name}: a disabled row does not press the client's choice");
            Pointer(Vector2.Zero); await Frames(2);

            // Another NPC's menu afterwards returns to the shared NPC dialogue.
            DetailCall(world, "NpcDialogWindow.Close");
            DetailCall(world, "NpcDialogWindow.Begin", c.NpcName, "How can I help you? Select a service below.");
            DetailCall(world, "AddNpcMenuButton", "1.   Tell me more...", (Action)(() => { }));
            DetailCall(world, "AddNpcMenuButton", "2.   Close", (Action)(() => DetailCall(world, "NpcDialogWindow.Close")));
            DetailCall(world, "NpcDialogWindow.End", 2); await Frames(10);
            Require(!view.Visible && Shared().Visible && Mathf.Abs(window.GetGlobalRect().Size.X - SharedWidth()) < .5f, $"{name}: a quest or talk menu afterwards shows the shared NPC dialogue again ({window.GetGlobalRect().Size.X})");
            // The running game's path: the reply raises the client's network event, the client (subscribed in its
            // Init) builds the menu and the plugin's handler marks it once that menu exists.
            c.Raise();
            await Frames(4);
            Require(view.Visible && view.Kind == c.Kind && !dialogue.Visible && !myko.Visible && window.GetGlobalRect().Size == frame.Size && view.Rows.Count == native.Length,
                $"{name}: the client's network event after another menu opens the {c.Layout.Art} window again");
            using (var network = Shot()) Save(network, window.GetGlobalRect(), c.State + "-network-reopen", view.Rows);
            DetailCall(world, "NpcDialogWindow.Close"); await Frames(2);
            return new { npc = c.NpcName, art = c.Layout.Art, window = frame.ToString(), shift = view.Shift, choices = view.Rows.Select(r => r.Text).ToArray(), servicesVerified = verified };
        }

        var results = new List<object>();
        const int sellingGroup = 255000;
        results.Add(await Opening(new OpeningCase(NativeNpcOpening.Inn, "inn", ClassicNpcOpeningLayout.Inn, "[Inn Hostess] Lined", 12000, 31,
            () => DetailCall(world, "OnWarehouseNpc"),
            () => ((Action)Native.Get<object>(Game.Session.Warehouse, "WarehouseNpcEvent")!)(),
            "Create a Clan", new()
            {
                ["Use storage"] = new(() => { }, () => (bool)DetailField(world, "Warehouse.shown")!, () => DetailCall(world, "Warehouse.Close")),
                ["Use [VIP] storage"] = new(() => Native.Set(world, "VipWarehouse.inFlight", true), () => !(bool)DetailField(world, "VipWarehouse.inFlight")!, () => { }),
                ["Create a Clan"] = new(() => { }, () => (bool)DetailField(world, "ClanCreate.shown")!, () => DetailCall(world, "ClanCreate.Close")),
                ["Seal / Cancel (anti-theft)"] = new(() => { }, () => (bool)DetailField(world, "Seal.shown")! && DetailField(world, "Seal.mode")!.ToString() == "Secret", () => DetailCall(world, "Seal.Close")),
                ["Seal / Cancel"] = new(() => { }, () => (bool)DetailField(world, "Seal.shown")! && DetailField(world, "Seal.mode")!.ToString() == "Bind", () => DetailCall(world, "Seal.Close")),
                ["Character Seal/Unseal"] = new(() => { }, () => ((HudWindow)DetailField(world, "CharacterSealWindow.CsWindow")!).Visible, () => DetailCall(world, "CharacterSealWindow.Close")),
                ["Close"] = new(() => { }, () => !(bool)DetailField(world, "NpcDialogWindow.shown")!, () => { }),
            })));
        if (!innOnly)
        {
            results.Add(await Opening(new OpeningCase(NativeNpcOpening.Smith, "smith", ClassicNpcOpeningLayout.Smith, "[Blacksmith]", 12209, 22,
                () => PreviewFixtures.OnRepairNpc(world, sellingGroup),
                () => ((Action<int>)Native.Get<object>(Game.Session.Repair, "RepairNpcEvent")!)(sellingGroup),
                "Repair", new()
                {
                    ["Buy / Sell"] = new(() => { }, () => (bool)DetailField(world, "Vendor.shown")! && (int)DetailField(world, "Vendor.group")! == sellingGroup, () => DetailCall(world, "Vendor.Close")),
                    ["Repair"] = new(() => { }, () => (bool)DetailField(world, "Repair.shown")!, () => DetailCall(world, "Repair.Close")),
                    ["Close"] = new(() => { }, () => !(bool)DetailField(world, "NpcDialogWindow.shown")!, () => { }),
                })));
            results.Add(await Redistribution());
        }

        // The redistribution menu: the client's own window (class_change) drawn as co_change_us.
        async Task<object> Redistribution()
        {
            const string name = "change";
            var layout = ClassicNpcOpeningLayout.Change;
            var layer = (Node)DetailField(world, "ClassChange.layer")!; layer.Reparent(this);
            var menu = (HudWindow)DetailField(world, "ClassChange.Panel")!;
            NativeServices.Prepare(menu, world);
            var panel = ClassicServiceSkin.Apply(menu.Body)!;
            Npc("[Redistribution]", 35000, 35);
            DetailCall(world, "ClassChange.OnNpc"); await Frames(4);
            menu.Position = ((viewport.Size - panel.Size) / 2).Floor(); await Frames(4);
            Require(menu.Visible && (bool)DetailField(world, "ClassChange.shown")!, "change: the client's redistribution NPC reply opens its menu");
            var greeting = Descendants(panel).OfType<Label>().Single(l => l.Name == "redistribution_description");
            var rows = new[] { "Redistribute stat points", "Redistribute mastery points", ClassicNpcOpeningLayout.LeaveCaption }
                .Select(text => Descendants(panel).OfType<Button>().Single(b => b.Text == text && b.Visible)).ToArray();
            int shift = (int)(greeting.Size.Y - layout.Greeting.Size.Y);
            var frame = panel.GetGlobalRect();
            var art = panel.GetChild(0) as ClassicNpcOpeningArt;
            Require(art != null && art.Layout == layout && art.Size == panel.Size && !Descendants(panel).OfType<ClassicFrame>().Any(),
                "change: the menu is drawn on the co_change_us message art instead of the plain frame");
            Require(menu.GetGlobalRect().Encloses(frame) && (menu.Header is not { } header || !header.IsVisibleInTree()), $"change: no title bar; the window is the art alone (window {menu.GetGlobalRect()}, art {frame})");
            Geometry(name, layout, frame, greeting, rows, shift);
            using (var probe = Shot())
                Require(Count(probe, greeting.GetGlobalRect(), Colors.White, .05f) >= 20, "change: the greeting is drawn white");

            int Kind() => NativeServices.RedistributionKind(world);
            var first = await States(name, name, frame, rows, layout, Kind);
            Require((ResetKind)Kind() == ResetKind.Stat && menu.Visible, "change: 'Redistribute stat points' asks the client for the stat reset price");
            await Frames(3);
            // The client's pending price query disables both choices; the leave row stays available.
            Pointer(first.GetGlobalRect().GetCenter()); await Frames(3);
            using (var disabled = Shot())
            {
                Save(disabled, frame, name + "-disabled", rows);
                var ink = Plugin.Kit.Layout(layout.Art).Find(layout.RowInk)!;
                Require(rows[0].Disabled && rows[1].Disabled && !rows[2].Disabled && rows[0].GetDrawMode() == BaseButton.DrawMode.Disabled
                    && Count(disabled, rows[0].GetGlobalRect(), ClassicNpcOpeningLayout.Disabled, .08f) >= 12 && Count(disabled, rows[0].GetGlobalRect(), ink.Color) == 0
                    && Count(disabled, rows[2].GetGlobalRect(), ink.Color) >= 12, "change: while the price is pending both choices are grey (also under the pointer), Walk away stays yellow");
            }
            await Click(rows[1].GetGlobalRect().GetCenter());
            Require((ResetKind)Kind() == ResetKind.Stat, "change: a disabled row does not reach the client");
            DetailCall(world, "ResetConfirm.CancelReset"); await Frames(3);
            await Click(rows[1].GetGlobalRect().GetCenter());
            Require((ResetKind)Kind() == ResetKind.Skill, "change: 'Redistribute mastery points' asks the client for the mastery reset price");
            DetailCall(world, "ResetConfirm.CancelReset"); await Frames(3);
            await Click(rows[2].GetGlobalRect().GetCenter());
            Require(!(bool)DetailField(world, "ClassChange.shown")! && !menu.Visible, "change: Walk away closes the client's redistribution menu");
            Pointer(Vector2.Zero); await Frames(2);
            var captions = rows.Select(r => r.Text).ToArray();
            layer.Free();
            return new { npc = "[Redistribution] (type 35)", art = layout.Art, window = frame.ToString(), shift, choices = captions,
                servicesVerified = new[] { "Stat points", "Mastery points", "Close" } };
        }

        DetailCall(world, "Warehouse.Dispose"); DetailCall(world, "Repair.Dispose");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + (innOnly ? "-inn.json" : "-npc-opening.json")), JsonSerializer.Serialize(new
        {
            nation = prefix, clientHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/.godot/mono/temp/bin/ExportRelease/LibreKO.dll")))),
            openings = results, checks, screens
        }, new JsonSerializerOptions { WriteIndented = true }));
        _windowData.NpcPortraitSource = null; NpcPortraitCache.Clear();
        npcLayer.Free(); clanLayer.Free(); world.Free(); net.Free();
        GD.Print($"{tag}_OK {prefix} {checks.Count} checks");
    }
}
