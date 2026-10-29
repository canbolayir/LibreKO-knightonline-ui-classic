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
    /// <summary>
    /// The quest NPC dialogue on the MYKO reference client's quest windows, bundled as 2.x art in assets/myko
    /// (re_quest_npc_menu, re_quest_viewer). First with the art turned off (the Classic fallback dialogue must stay),
    /// then with them: the talk menu and the quest viewer are captured in every state, their rendered bounds are
    /// compared with the local layouts, and every row, cell and button is driven with real pointer input into the
    /// client's own callbacks. The art comes from <c>LIBREKO_AUDIT_LOCAL_ASSETS</c> or, by default, the
    /// plugin's bundled <c>assets/myko</c>.
    /// </summary>
    private async Task CaptureMykoNpcAudit(int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/myko-npc-audit");
        System.IO.Directory.CreateDirectory(output);
        string assets = OS.GetEnvironment("LIBREKO_AUDIT_LOCAL_ASSETS");
        if (assets.Length == 0) assets = ProjectSettings.GlobalizePath("res://../" + LocalAssets.Bundled);
        assets = System.IO.Path.GetFullPath(assets);
        foreach (string name in new[] { MykoQuestLayout.MenuLayout, MykoQuestLayout.ViewerLayout })
            if (!System.IO.File.Exists(System.IO.Path.Combine(assets, "layouts", name + ".json"))) throw new Exception($"2.x art missing ({name}) in {assets}: run tools/install_local_assets.py --bundle");
        foreach (string path in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck", "build/client/source-content/content/npcs.pck", "build/client/source-content/content/weapons.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../" + path), false)) throw new Exception("Missing live audit pack: " + path);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        var backdrop = new Color("252822");
        var ground = new ColorRect { Color = backdrop, Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(ground);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string prefix = nation == 1 ? "karus" : "human";
        var checks = new List<string>();
        var screens = new List<object>();
        var translucency = new List<object>();
        void Check(bool ok, string what) { if (!ok) throw new Exception("MYKO_NPC_AUDIT_FAIL " + prefix + ": " + what); checks.Add(what); }

        // Local assets are read once per kit; the audit switches them by resetting the kit's cache.
        var localField = typeof(UiKit).GetField("_local", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void UseLocal(string? root)
        {
            System.Environment.SetEnvironmentVariable(LocalAssets.RootVariable, root ?? System.IO.Path.Combine(output, "no-local-assets"));
            localField.SetValue(Plugin.Kit, null);
        }
        UseLocal(null);

        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net); var world = PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(world, "NpcDialogWindow.BuildNpcDialog");
        var layer = (CanvasLayer)DetailField(world, "NpcDialogWindow.layer")!; layer.Reparent(this);
        DetailCall(world, "ItemTooltip.Build");
        var tooltipLayer = (CanvasLayer)DetailField(world, "ItemTooltip.tipLayer")!; tooltipLayer.Reparent(this);
        var window = (HudWindow)DetailField(world, "NpcDialogWindow.Panel")!;
        var actorType = Native.ClientType("LibreKO.Presentation.EntityView")!;
        var actor = Activator.CreateInstance(actorType, nonPublic: true)!;
        ((EntityRegistry)DetailField(world, "EntitySystem.Ents")!)[701] = (EntityView)actor;
        using var seed = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/Npcs.json")));
        var prototypes = seed.RootElement.EnumerateArray().GroupBy(p => p.GetProperty("Id").GetInt32()).ToDictionary(g => g.Key, g => g.Last());
        void SetNpc(int id)
        {
            var data = prototypes[id];
            foreach (var field in new[] { "NpcId", "ModelId", "Level", "NpcType", "Name" })
            {
                object value = field == "Name" ? data.GetProperty(field).GetString()! : field == "NpcId" ? id : data.GetProperty(field).GetInt32();
                actorType.GetField(field)!.SetValue(actor, value);
            }
            actorType.GetField("IsNpc")!.SetValue(actor, true); actorType.GetField("Gear")!.SetValue(actor, new int[8]);
            Native.Set(Game.Session, "NpcQuest.LastNpcId", 701);
            Native.Set(Game.Session, "NpcQuest.TalkingNpcId", -1);
            Native.Set(Game.Session, "NpcQuest.LastNpcName", data.GetProperty("Name").GetString()!);
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var classic = (ClassicNpcDialogue)(CharacterDetailsSkin.Apply(window.Body) ?? window.GetChildren().OfType<ClassicNpcDialogue>().Single());
        KnightOnlineUiClassic.Native.NativeNpcOpening.Prepare(window, world);
        var myko = window.GetChildren().OfType<MykoQuestDialogue>().Single();
        var opening = window.GetChildren().OfType<ClassicNpcOpeningChoice>().Single();
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Close() => DetailCall(world, "NpcDialogWindow.Close");
        void Conversation(int npc, string story, string[] topics, Action<int>? pressed = null)
        {
            Close(); SetNpc(npc); DetailCall(world, "NpcDialogWindow.Begin", prototypes[npc].GetProperty("Name").GetString()!, story);
            for (int i = 0; i < topics.Length; i++) { int index = i; DetailCall(world, "AddNpcMenuButton", $"{i + 1}.   {topics[i]}", (Action)(() => pressed?.Invoke(index))); }
            DetailCall(world, "NpcDialogWindow.End", topics.Length);
        }
        void Offer(int npc, QuestView view) { Close(); SetNpc(npc); DetailCall(world, "ShowQuestView", view); }
        void Pointer(Vector2 point) => Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        async Task Click(Control control, bool release = true)
        {
            var point = control.GetGlobalRect().GetCenter();
            Pointer(point); await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point }); await Frames(2);
            if (release) { Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }); await Frames(2); }
        }
        async Task Release(Control control)
        {
            var point = control.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point }); await Frames(2);
        }
        Image Shot() => GetViewport().GetTexture().GetImage();
        int Count(Image image, Rect2 rect, Func<Color, bool> match)
        {
            int n = 0;
            for (int y = (int)rect.Position.Y; y < (int)rect.End.Y; y++)
                for (int x = (int)rect.Position.X; x < (int)rect.End.X; x++)
                    if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight() && match(image.GetPixel(x, y))) n++;
            return n;
        }
        bool Near(Color a, Color b, float tolerance = .09f) => Math.Abs(a.R - b.R) < tolerance && Math.Abs(a.G - b.G) < tolerance && Math.Abs(a.B - b.B) < tolerance;

        async Task Save(string name, Rect2? area = null)
        {
            await Frames(1);
            var rect = (area ?? window.GetGlobalRect()).Grow(10);
            using var image = Shot();
            var clip = new Rect2I(Math.Max(0, (int)rect.Position.X), Math.Max(0, (int)rect.Position.Y), 0, 0);
            clip.Size = new Vector2I(Math.Min(image.GetWidth() - clip.Position.X, (int)rect.Size.X), Math.Min(image.GetHeight() - clip.Position.Y, (int)rect.Size.Y));
            using var crop = image.GetRegion(clip);
            crop.SavePng(System.IO.Path.Combine(output, prefix + "-" + name + ".png"));
        }

        // ---- Shared bounds checks ---------------------------------------------------------------------------
        async Task Verify(string name, MykoQuestLayout layout)
        {
            await Frames(2);
            var frame = window.GetGlobalRect(); var viewport = new Rect2(Vector2.Zero, GetViewportRect().Size);
            Check(myko.Visible && myko.Layout != null && !classic.Visible && !opening.Visible, name + ": local quest window shown instead of the bundled dialogue");
            Check(myko.Modulate.A >= 1, name + ": settled");
            Check(frame.Size == myko.Size && viewport.Encloses(frame), name + ": window equals the composition and stays inside the viewport " + frame);
            int width = myko.Offer ? layout.ViewerWidth : layout.MenuWidth;
            Check((int)frame.Size.X == width, name + $": width {frame.Size.X} = original {width}");
            // The art covers the whole window height without gaps (head, rows or blocks, footer).
            float covered = 0;
            foreach (var (_, rect) in myko.Pieces.Where(p => p.Rect.Size.X >= width - 8).OrderBy(p => p.Rect.Position.Y))
            {
                Check(rect.Position.Y <= covered + .5f, name + $": art gap at y {covered}..{rect.Position.Y}");
                covered = Math.Max(covered, rect.End.Y);
            }
            Check(Math.Abs(covered - frame.Size.Y) < .5f, name + $": art ends at {covered}, window {frame.Size.Y}");
            foreach (var control in Descendants(myko).OfType<Control>().Where(c => c.IsVisibleInTree() && c is not ScrollContainer))
            {
                bool clipped = false;
                for (Node? parent = control.GetParent(); parent != null && parent != myko; parent = parent.GetParent()) if (parent is Control c && c.ClipContents) clipped = true;
                if (!clipped) Check(frame.Grow(.5f).Encloses(control.GetGlobalRect()), name + ": " + control.Name + " inside the window " + control.GetGlobalRect());
            }
            var title = myko.TitleLabel; var titleInk = myko.Offer ? layout.ViewerTitle : layout.MenuTitle;
            Check(title.GetThemeFontSize("font_size") == UiKit.FontSize(titleInk) && Near(title.GetThemeColor("font_color"), new Color(titleInk.Color, 1)), name + ": title in the original ink");
            Check(title.GetThemeFont("font").GetStringSize(title.Text, HorizontalAlignment.Left, -1, title.GetThemeFontSize("font_size")).X <= title.Size.X, name + ": title fits");
            Check(new Rect2(frame.Position + titleInk.Position, titleInk.SizeVec).Grow(.5f).Encloses(title.GetGlobalRect()), name + ": title inside the original title band");
            var header = classic.Header;
            Check(header.GetParent() == myko && header.Modulate.A == 0 && title.GetGlobalRect().Grow(.5f).Encloses(header.GetGlobalRect()), name + ": the client's title bar is the drag handle over the title");
            var close = myko.CloseButton!; var closeNode = myko.Offer ? layout.ViewerClose : layout.MenuClose;
            Check(close.GetGlobalRect() == new Rect2(frame.Position + closeNode.Position, closeNode.SizeVec), name + ": close button at the original place");
            var scroll = myko.Offer ? layout.ViewerScroll : layout.MenuScroll; var text = myko.Offer ? layout.ViewerNote : layout.MenuTalk;
            Check(myko.Rail!.GetGlobalRect() == new Rect2(frame.Position + scroll.Position, scroll.SizeVec), name + ": scroll bar at the original place");
            var box = myko.TextScroll.GetGlobalRect(); float line = (text.Bold ? Plugin.Kit.Bold : Plugin.Kit.Regular).GetHeight(UiKit.FontSize(text));
            Check(box.Position == frame.Position + text.Position && box.Size.X == text.W && box.Size.Y <= text.H && box.Size.Y > text.H - line && Math.Abs(box.Size.Y / line - Mathf.Round(box.Size.Y / line)) < .01f,
                name + ": text box at the original place, whole lines only " + box);
            Check(myko.Captions.Select(c => c.GetGlobalRect().Position.X).Distinct().Count() <= 1, name + ": captions aligned");
            using var image = Shot();
            // Frame art: the left and right border columns are drawn (not the backdrop) at mid height.
            float mid = frame.Position.Y + Mathf.Floor(frame.Size.Y / 2);
            Check(Count(image, new Rect2(frame.Position.X, mid - 4, 4, 8), c => !Near(c, backdrop, .03f)) >= 8 && Count(image, new Rect2(frame.End.X - 5, mid - 4, 4, 8), c => !Near(c, backdrop, .03f)) >= 8, name + ": frame border drawn on both sides");
            // The window body is translucent black: darker than the backdrop, not opaque.
            var empty = myko.Offer ? new Vector2(40, frame.Size.Y - layout.ViewerFooter + 3) : new Vector2(40, layout.FirstRow + 1);
            var inner = image.GetPixel((int)(frame.Position.X + empty.X), (int)(frame.Position.Y + empty.Y));
            Check(inner.R < backdrop.R && inner.R > 0.005f, name + $": translucent original body ({inner})");
            screens.Add(new
            {
                name, frame = frame.ToString(), offer = myko.Offer, page = myko.Page, pages = myko.PageCount, title = title.Text, titleRect = title.GetGlobalRect().ToString(),
                text = myko.Text.GetParsedText(), textRect = myko.TextScroll.GetGlobalRect().ToString(), textScroll = myko.TextScroll.ScrollVertical,
                options = myko.Options.Select(o => new { caption = o.Caption.Text, state = o.State.Text, rect = o.GetGlobalRect().ToString(), captionRect = o.Caption.GetGlobalRect().ToString(), disabled = o.Disabled }).ToArray(),
                cells = myko.Cells.Select(c => new { first = c.First.Text, second = c.Second.Text, rect = c.GetGlobalRect().ToString(), selected = c.Selected }).ToArray(),
                captions = myko.Captions.Select(c => new { text = c.Text, rect = c.GetGlobalRect().ToString() }).ToArray(),
                status = myko.Status?.Text, statusRect = myko.Status?.GetGlobalRect().ToString(),
                actions = myko.Actions.Select(a => new { caption = a.Caption.Text, rect = a.GetGlobalRect().ToString(), disabled = a.Disabled }).ToArray(),
                pager = myko.PageLabel?.Text, close = close.GetGlobalRect().ToString(), rail = myko.Rail!.GetGlobalRect().ToString(),
            });
            await Save(name);
        }

        // Translucency: the same state over black and over white gives each pixel's coverage (1 - (white - black)).
        // The dark ground keeps GroundAlpha of the art's alpha: body 187 -> 150, cells 102 -> 82 over the body.
        float Thinned(int alpha) => Mathf.Round(alpha * MykoQuestLayout.GroundAlpha) / 255f;
        async Task Translucency(string name, (string Part, Vector2 At, float Expected)[] samples)
        {
            Pointer(Vector2.Zero); await Frames(2);
            var frame = window.GetGlobalRect();
            ground.Color = Colors.Black; await Frames(2); using var black = Shot(); await Save(name + "-on-black");
            ground.Color = Colors.White; await Frames(2); using var white = Shot(); await Save(name + "-on-white");
            ground.Color = backdrop; await Frames(2);
            foreach (var (part, at, expected) in samples)
            {
                var p = (Vector2I)(frame.Position + at).Floor();
                float alpha = 1 - (white.GetPixel(p.X, p.Y).R - black.GetPixel(p.X, p.Y).R);
                Check(Math.Abs(alpha - expected) < .02f, $"{name}: {part} coverage {alpha:0.000}, expected {expected:0.000}");
                Check(black.GetPixel(p.X, p.Y).R < .03f, $"{name}: {part} ground is black");
                translucency.Add(new { screen = name, part, at = at.ToString(), alpha, expected });
            }
        }

        // ---- 1. Without local assets: the bundled Classic dialogue ------------------------------------------
        string[] hepa = { "Armor of Destruction - top", "Goblin armor - top", "Talia armor" };
        Conversation(14301, "Which one is tempting?", hepa);
        await Frames(10);
        Check(MykoQuestLayout.Current == null && !myko.Visible && myko.Layout == null && classic.Visible, "without local assets the bundled Classic dialogue stays");
        Check((int)window.GetGlobalRect().Size.X == ClassicNpcLayout.Width && classic.Header.GetParent() == classic, "without local assets the window keeps the bundled width and title bar");
        await Save("fallback-menu");
        ItemData.EnsureLoaded();
        var items = (Dictionary<int, ItemData.Item>)typeof(ItemData).GetField("items", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        int Item(string name, int fallback) => items.Values.FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && i.Icon > 0)?.Id ?? fallback;
        int[] helmets = new[] { "Half Plate Helmet", "Rogue Helmet", "Mage Linen Cap", "Priest Helmet" }.Select(name => items.Values.First(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && i.Icon > 0).Id + 5).ToArray();
        var gavolt = new QuestView(501, 13013, 21, true, true, false, false, QuestViewState.Available, 0, "Subdual of Gavolt", "Hunt five Gavolts.",
            "Gavolts raided our mill and took the wheat. Hunt five Gavolts to help the farmers supply Moradon again.",
            new QuestObjectives(501, false, [new(5, [1], "Gavolt", true)]), [0], [new(false, 2, 0, 6250, 0)], []) { Options = helmets.Select(id => new QuestTransfer(false, 0, id, 1, 0)).ToArray() };
        Offer(13013, gavolt); await Frames(10);
        Check(!myko.Visible && classic.Visible, "without local assets the quest offer keeps the bundled dialogue");
        await Save("fallback-offer");

        // ---- 2. With local assets ---------------------------------------------------------------------------
        UseLocal(assets);
        var layout = MykoQuestLayout.Current ?? throw new Exception("Local quest layouts did not load from " + assets);
        Check(layout.MenuWidth == 363 && layout.ViewerWidth == 359 && layout.RowPitch == 40 && layout.FirstRow == 177 && layout.MaxContent == 516 && layout.ViewerFooter == 85,
            $"local layouts read: menu {layout.MenuWidth} wide, rows from {layout.FirstRow} every {layout.RowPitch}; viewer {layout.ViewerWidth} wide, {layout.MaxContent} content, {layout.ViewerFooter} footer");

        // Talk menu: the owner's [Blacksmith] Hepa screenshot (three quests).
        int chosen = -1;
        Conversation(14301, "Which one is tempting?", hepa, index => chosen = index);
        await Frames(10);
        await Verify("menu-hepa", layout);
        var frameRect = window.GetGlobalRect();
        Check(frameRect.Size == new Vector2(363, layout.MenuHeight(3, false)) && frameRect.Size.Y == 353, $"menu cut to three rows: {frameRect.Size}");
        Check(myko.TitleLabel.Text == "[Blacksmith] Hepa" && myko.Text.GetParsedText().Trim() == "Which one is tempting?", "menu title and talk text are the client's");
        Check(myko.Options.Count == 3, "one row per client choice");
        for (int i = 0; i < myko.Options.Count; i++)
        {
            var option = myko.Options[i];
            Check(option.GetGlobalRect() == new Rect2(frameRect.Position + layout.RowPart(layout.RowButton, i).Position, layout.RowButton.SizeVec), $"row {i} at the original button rectangle");
            Check(option.Caption.Text == hepa[i] && !option.State.Visible, $"row {i} caption without the client's number");
            Check(Math.Abs(option.Caption.GetGlobalRect().GetCenter().X - option.GetGlobalRect().GetCenter().X) <= .5f, $"row {i} caption centred");
            Check(option.Caption.GetThemeFontSize("font_size") == UiKit.FontSize(layout.RowCaption) && Near(option.Caption.GetThemeColor("font_color"), new Color(layout.RowCaption.Color, 1)), $"row {i} caption in the original ink");
            Check(myko.Pieces.Any(p => p.Image == layout.RowPlate && p.Rect == layout.RowPart(layout.RowPlate, i)), $"row {i} plate drawn");
        }
        Check(Math.Abs((frameRect.GetCenter() - GetViewportRect().Size / 2).Length()) <= 1, "menu opens centred");
        await Translucency("menu-hepa", new[] { ("body", new Vector2(40, layout.FirstRow + 1), Thinned(187)), ("talk box", new Vector2(300, 140), Thinned(187)) });
        // States by real pointer input: hover (tag 2), pressed (tag 1), then the client's own callback.
        var first = myko.Options[0];
        Pointer(first.GetGlobalRect().GetCenter()); await Frames(3);
        Check(first.ArtState == 2, "row hover state"); await Save("menu-hover");
        await Click(first, release: false);
        Check(first.ArtState == 1 && chosen == -1, "row pressed state, nothing fires before release"); await Save("menu-pressed");
        await Release(first);
        Check(chosen == 0, "row press reaches the client's menu callback");
        Pointer(Vector2.Zero); await Frames(2);
        // Disabled row: the client's disabled choice shows grey and presses nothing.
        var nativeRows = classic.MenuSource.GetChildren().OfType<Button>().ToArray();
        nativeRows[1].Disabled = true; await Frames(3);
        Check(myko.Options[1].Disabled && myko.Options[1].ArtState == 3 && Near(myko.Options[1].Caption.GetThemeColor("font_color"), ClassicNpcOpeningLayout.Disabled), "disabled row mirrors the client");
        chosen = -1; await Click(myko.Options[1]);
        Check(chosen == -1, "disabled row presses nothing");
        Pointer(Vector2.Zero); await Frames(2); await Save("menu-disabled");
        // Quest states and paging: the guard's seventeen quests.
        string[] guardTopics = { "[In progress] Orc Watcher hunting", "Patrick's trust", "[In progress] Bandicoot hunt", "[Ready] Kecoon hunting", "Bulcan hunting", "Wild bulcan hunting", "Kekoon warrior hunt", "Subdual of Gavolt", "Kekoon Captain hunt", "Subdual of Vulture", "Giant bulcan hunting", "Werewolf elimination", "Subdual of Silan", "Giant Gavolt hunting", "Werewolf skin", "Glyptodont hunt", "Gloomwing hunt" };
        Conversation(13013, "What mission are you going to undertake? Help keep Moradon safe.", guardTopics, index => chosen = index);
        await Frames(10); await Verify("menu-guard", layout);
        var guardFrame = window.GetGlobalRect();
        Check(myko.PageCount == 2 && myko.Options.Count == 10 && myko.Previous!.Disabled && !myko.Next!.Disabled && myko.PageLabel!.Text == "1/2", "seventeen quests on two pages of ten rows");
        Check(guardFrame.Size.Y == layout.MenuHeight(10, true), "paged menu height: ten rows, pager strip, bottom");
        var states = myko.Options.Select(o => o.State.Visible ? o.State.Text : "").ToArray();
        Check(states[0] == "In progress" && states[3] == "Ready" && states[1] == "" && myko.Options[0].Caption.Text == "Orc Watcher hunting", "client quest states at the right of the rows");
        foreach (var option in myko.Options.Where(o => o.State.Visible))
            Check(option.Caption.GetGlobalRect().End.X <= option.State.GetGlobalRect().Position.X, "caption and state do not overlap");
        await Click(myko.Next!); Pointer(Vector2.Zero); await Frames(3); await Verify("menu-guard-page-2", layout);
        Check(myko.Page == 1 && myko.Options.Count == 7 && window.GetGlobalRect() == guardFrame && myko.Next!.Disabled && myko.PageLabel!.Text == "2/2", "page two keeps the window fixed");
        await Click(myko.Options.Last()); Check(chosen == 16, "last quest reaches its native callback");
        Pointer(Vector2.Zero);
        // Long talk text scrolls with the wheel and the original arrows.
        Conversation(31508, "How are you? I am the Enchanter dispatched to Moradon Castle. I offer buffs to help you on your journey. There are three types of buffs that you can receive: attack power, defensive power and enhancement of physical strength, and they can be stacked! Possible payment means are Noah, and, for those people between level 36 and 60, can pay with the [sign of victory] too.",
            new[] { "[Buff]Noah/Symbol of Victory", "Spirit Aisles' of Mounting", "Settlement support free of charge buff", "Close" });
        await Frames(10); await Verify("menu-long-talk", layout);
        var talkFrame = window.GetGlobalRect(); var bar = myko.TextScroll.GetVScrollBar();
        Check(bar.MaxValue > bar.Page, "long talk overflows its box");
        var target = myko.TextScroll.GetGlobalRect().GetCenter(); Pointer(target); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = target, GlobalPosition = target });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = target, GlobalPosition = target }); await Frames(2);
        Check(bar.Value > 0 && window.GetGlobalRect() == talkFrame, "wheel scrolls the talk text inside the fixed window");
        double wheel = bar.Value; await Click(myko.Rail!.Down);
        Check(bar.Value > wheel, "down arrow scrolls the talk text");
        await Click(myko.Rail!.Up); Check(bar.Value < bar.MaxValue, "up arrow scrolls back");
        bar.Value = bar.MaxValue; Pointer(Vector2.Zero); await Frames(3); await Verify("menu-long-talk-end", layout);
        Check(Math.Abs(myko.Rail!.ThumbTop + 17 - (layout.MenuScroll.Children.First(n => n.Type == "trackbar").Y - layout.MenuScroll.Y + layout.MenuScroll.Children.First(n => n.Type == "trackbar").H)) <= 1, "thumb at the end of the track");
        Check(myko.Options.Last().Caption.Text == "Close", "the client's Close choice stays a row");
        // Close knob: hover, pressed, then the client's close callback.
        var knob = myko.CloseButton!;
        Pointer(knob.GetGlobalRect().GetCenter()); await Frames(3); Check(knob.ArtState == 2, "close hover"); await Save("close-hover");
        await Click(knob, release: false); Check(knob.ArtState == 1 && window.Visible, "close pressed"); await Save("close-pressed");
        await Release(knob);
        Check(!window.Visible && !(bool)DetailField(world, "NpcDialogWindow.shown")!, "close knob reaches the client's close");
        Pointer(Vector2.Zero);

        // Quest viewer: the owner's "Armor of Destruction - top" offer (Collect, Reward, Accept / Reject).
        int talisman = Item("Talisman of Destruction", helmets[0]), iron = Item("Iron Bar", helmets[1]), pauldron = Item("Pauldron of Destruction", helmets[2]);
        var armor = new QuestView(610, 14301, 21, true, true, false, false, QuestViewState.Available, 0, "Armor of Destruction - top", "Bring the materials to the Blacksmith.",
            "To make Body Armor of Destruction top, you must take 7 Amulets of Destruction, 15 iron bar, and 500,000 Noahs to [Blacksmith] Heppa.",
            new QuestObjectives(610, false, []), [], [new(true, 0, talisman, 7, 0), new(true, 0, iron, 15, 0), new(true, 1, 0, 500000, 0), new(false, 0, pauldron, 1, 0)], []);
        Offer(14301, armor); await Frames(10); await Verify("viewer-armor", layout);
        var armorFrame = window.GetGlobalRect();
        int content = layout.CaptionHeight(true) + 2 * layout.CellRowHeight + layout.CaptionHeight(false) + layout.CellRowHeight;
        Check(armorFrame.Size == new Vector2(359, layout.ViewerHeight(content)), $"viewer cut to its groups: {armorFrame.Size} (content {content})");
        Check(myko.TitleLabel.Text == "Armor of Destruction - top" && myko.Text.GetParsedText().StartsWith("To make Body Armor"), "viewer title is the quest, note is the client's text");
        Check(myko.Captions.Select(c => c.Text).SequenceEqual(new[] { "Collect", "Rewards" }) && myko.Cells.Count == 4, "Collect and Reward groups with one cell per item");
        Check(myko.Status?.Text.Length > 0 && myko.Captions[0].GetGlobalRect().End.X < myko.Status!.GetGlobalRect().Position.X, "quest state beside the first caption");
        for (int i = 0; i < myko.Cells.Count; i++)
        {
            var cell = myko.Cells[i]; var anchor = i == 1 ? layout.RightCell : layout.LeftCell;
            Check(cell.GetGlobalRect().Size == anchor.SizeVec && cell.GetGlobalRect().Position.X == armorFrame.Position.X + anchor.X, $"cell {i} at the original column and size");
            Check(cell.Icon.Texture != null && cell.First.Text.Length > 0 && cell.Second.Text.Length > 0, $"cell {i} has icon, name and amount");
        }
        Check(myko.Cells[3].GetGlobalRect().Position.Y - myko.Cells[2].GetGlobalRect().Position.Y == layout.CellRowHeight + layout.CaptionHeight(false), "reward group follows its caption");
        var actions = myko.Actions;
        Check(actions.Count == 2 && actions[0].Caption.Text == "Accept" && actions[1].Caption.Text == "Reject", "Accept and Reject from the client");
        Check(actions[0].GetGlobalRect() == new Rect2(armorFrame.Position + new Vector2(layout.LeftAction.X, armorFrame.Size.Y - layout.ViewerFooter + layout.LeftAction.Y - layout.PagerStrip.Y), layout.LeftAction.SizeVec)
            && actions[1].GetGlobalRect().Position.X == armorFrame.Position.X + layout.RightAction.X, "actions in the original two slots");
        Check(actions.All(a => Near(a.Caption.GetThemeColor("font_color"), layout.ActionInk) && Near(layout.ActionInk, new Color(layout.RowCaption.Color, 1), .001f)), "action captions in the neutral stone-button ink");
        using (var image = Shot())
            foreach (var action in actions)
                Check(Count(image, action.Caption.GetGlobalRect(), c => Near(c, layout.ActionInk, .1f)) > 20 && Count(image, action.Caption.GetGlobalRect(), c => c.G > c.R + .2f || c.R > c.G + .3f) == 0,
                    action.Caption.Text + " caption neutral on screen (no green or red ink)");
        var cellAt = myko.Cells[0].GetGlobalRect().Position - armorFrame.Position + new Vector2(42, 23);
        await Translucency("viewer-armor", new[] { ("body", new Vector2(40, armorFrame.Size.Y - layout.ViewerFooter + 3), Thinned(187)), ("note box", new Vector2(300, 140), Thinned(187)),
            ("cell", cellAt, 1 - (1 - Thinned(187)) * (1 - Thinned(102))) });
        Check(myko.PageLabel?.Text == "1/1" && myko.Previous!.Disabled && myko.Next!.Disabled, "pager 1/1 with both arrows at their ends");
        // Item tooltip through the client's row, then Accept by real pointer input.
        var itemCell = myko.Cells[0];
        Pointer(itemCell.GetGlobalRect().Position + new Vector2(20, 20)); await Frames(3);
        Check(world.ItemTooltipVisible, "item cell hover opens the client's item tooltip");
        var tooltip = (Control)DetailField(world, "ItemTooltip.TipPanel")!;
        tooltip.Position = new Vector2(Math.Min(armorFrame.End.X + 12, GetViewportRect().Size.X - tooltip.Size.X - 8), armorFrame.Position.Y); await Frames(2);
        await Save("viewer-item-tooltip", armorFrame.Merge(tooltip.GetGlobalRect()));
        Pointer(Vector2.Zero); await Frames(3); Check(!world.ItemTooltipVisible, "tooltip closes on exit");
        int accepts = 0; Descendants(window.Body).OfType<Button>().Single(b => b.Text == "Accept").Pressed += () => accepts++;
        Pointer(actions[0].GetGlobalRect().GetCenter()); await Frames(3); Check(actions[0].ArtState == 2, "action hover state"); await Save("viewer-accept-hover");
        await Click(actions[0]);
        Check(accepts == 1 && !window.Visible, "Accept reaches the client's accept and closes");
        Pointer(Vector2.Zero);

        // Hunt offer with rewards and a reward choice list.
        Offer(13013, gavolt); await Frames(10); await Verify("viewer-gavolt-offer", layout);
        Check(myko.Captions.Select(c => c.Text).SequenceEqual(new[] { "Hunt", "Rewards", "Choose one reward" }) && myko.Cells.Count == 6, "hunt, rewards and the reward choices");
        Check(myko.Cells[0].First.Text == "Gavolt" && myko.Cells[0].Second.Text == "0 / 5", "hunt cell shows the client's count");
        Check(myko.Status?.Text == "Available", "offer state");
        // Ready: confirm needs a reward choice; choose one by real pointer input.
        Offer(13013, gavolt with { CanAccept = false, CanClaim = true, State = QuestViewState.Claimable, Counts = [5] }); await Frames(10);
        await Verify("viewer-ready-disabled", layout);
        var confirm = myko.Actions.Single(a => a.Caption.Text == "Confirm");
        Check(confirm.Disabled && confirm.ArtState == 3 && Near(confirm.Caption.GetThemeColor("font_color"), ClassicNpcOpeningLayout.Disabled), "Confirm disabled until a reward is chosen");
        using (var image = Shot()) Check(Count(image, confirm.Caption.GetGlobalRect(), c => Near(c, ClassicNpcOpeningLayout.Disabled, .12f)) > 10, "disabled Confirm caption is grey on screen");
        var choices = myko.Cells.Where(c => c.Choice).ToArray();
        Check(choices.Length == 4 && myko.Cells[0].Second.Text == "5 / 5", "four reward choices; complete hunt");
        await Click(choices[2]); Pointer(Vector2.Zero); await Frames(3);
        Check((int)DetailField(world, "NpcDialogWindow.questRewardChoice")! == 2 && choices[2].Selected && !choices[0].Selected && !confirm.Disabled, "reward choice reaches the client and enables Confirm");
        using (var image = Shot()) Check(Count(image, new Rect2(choices[2].GetGlobalRect().Position, new Vector2(choices[2].Size.X, 3)), c => Near(c, new Color("e4c174"), .1f)) > 40, "chosen reward framed in gold");
        await Verify("viewer-ready-selected", layout);
        int confirms = 0; Descendants(window.Body).OfType<Button>().Single(b => b.Text == "Confirm").Pressed += () => confirms++;
        await Click(confirm); Check(confirms == 1 && !window.Visible, "Confirm reaches the client's turn-in");
        Pointer(Vector2.Zero);
        // In progress: Abandon and Close (the red Close of the original).
        Offer(13013, gavolt with { CanAccept = false, State = QuestViewState.InProgress, Counts = [2] }); await Frames(10); await Verify("viewer-in-progress", layout);
        Check(myko.Actions.Select(a => a.Caption.Text).SequenceEqual(new[] { "Abandon", "Close" }) && myko.Actions.All(a => Near(a.Caption.GetThemeColor("font_color"), layout.ActionInk)), "Abandon and Close in the neutral caption ink");
        await Click(myko.Actions[1]); Check(!window.Visible, "Close reaches the client's close");
        Pointer(Vector2.Zero);
        // Many rewards: whole rows per page, fixed window, pager.
        Offer(13013, gavolt with { CanAccept = false, CanClaim = true, State = QuestViewState.Claimable, Counts = [5], Options = Enumerable.Range(0, 28).Select(i => new QuestTransfer(false, 0, helmets[i % 4], i + 1, 0)).ToArray() });
        await Frames(10); await Verify("viewer-many-rewards", layout);
        var manyFrame = window.GetGlobalRect(); int pages = myko.PageCount;
        Check(pages > 1 && !myko.Next!.Disabled && manyFrame.Size.Y <= layout.ViewerHeight(layout.MaxContent), $"long reward list paged ({pages} pages) within the original height");
        int seen = myko.Cells.Count(c => c.Choice);
        for (int p = 1; p < pages; p++)
        {
            await Click(myko.Next!); Pointer(Vector2.Zero); await Frames(3);
            await Verify("viewer-many-rewards-page-" + (p + 1), layout);
            Check(myko.Page == p && window.GetGlobalRect() == manyFrame && myko.Captions.Count > 0, $"page {p + 1} keeps the window and repeats its caption");
            seen += myko.Cells.Count(c => c.Choice);
        }
        Check(seen == 28 && myko.Next!.Disabled, "every reward choice reachable through the pager");
        await Click(myko.Cells.Last(c => c.Choice)); Pointer(Vector2.Zero); await Frames(3);
        Check((int)DetailField(world, "NpcDialogWindow.questRewardChoice")! == 27, "last reward chosen on the last page");
        // Small viewport: fewer rows per page, still inside the screen.
        GetWindow().Size = new Vector2I(1000, 540); await Frames(8);
        Offer(13013, gavolt with { CanAccept = false, CanClaim = true, State = QuestViewState.Claimable, Counts = [5], Options = Enumerable.Range(0, 28).Select(i => new QuestTransfer(false, 0, helmets[i % 4], i + 1, 0)).ToArray() });
        await Frames(10); await Verify("viewer-small-viewport", layout);
        Conversation(13013, "What mission are you going to undertake?", guardTopics); await Frames(10); await Verify("menu-small-viewport", layout);
        GetWindow().Size = new Vector2I(1000, 700); await Frames(8);

        // Openings keep their own original windows; the quest menu returns after them.
        Conversation(14301, "A good blacksmith like me is hard to find.", new[] { "Buy / Sell", "Repair", "Close" });
        KnightOnlineUiClassic.Native.NativeNpcOpening.Opened(world, KnightOnlineUiClassic.Native.NativeNpcOpening.Smith); await Frames(10);
        Check(opening.Active && opening.Visible && !myko.Visible && !classic.Visible, "the Blacksmith opening keeps its co_smith_us window");
        await Save("opening-smith");
        Conversation(14301, "Which one is tempting?", hepa); await Frames(10);
        Check(!opening.Active && myko.Visible && !classic.Visible, "the quest menu returns after the opening");

        // Local assets removed again: the bundled dialogue returns at once.
        UseLocal(null); await Frames(6);
        Check(!myko.Visible && classic.Visible && classic.Header.GetParent() == classic, "removing the local assets restores the bundled dialogue");
        Conversation(14301, "Which one is tempting?", hepa); await Frames(10);
        Check((int)window.GetGlobalRect().Size.X == ClassicNpcLayout.Width, "bundled width after removal");
        await Save("fallback-after-removal");
        Close();

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-verification.json"), JsonSerializer.Serialize(new { nation = prefix, localAssets = assets, groundAlpha = MykoQuestLayout.GroundAlpha, checks = checks.Count, checkList = checks, translucency, screens },
            new JsonSerializerOptions { WriteIndented = true }));
        NpcPortraitCache.Clear(); tooltipLayer.Free(); layer.Free(); world.Free(); net.Free();
        GD.Print($"MYKO_NPC_AUDIT_OK: {prefix}, {checks.Count} checks");
    }
}
