using Godot;
using LibreKO;
using LibreKO.Plugins;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private readonly List<(Control Control, Rect2 Bounds)> _designBounds = new();
    private readonly List<Button> _designMenu = new();
    private readonly List<Control> _designChoices = new();
    private int[] _designHelmetIds = Array.Empty<int>();
    private Texture2D? _designHuntIcon;
    private RichTextLabel? _designSpeech;
    private static readonly string[] DesignTopics = { "Orc Watcher hunting", "Patrick's trust", "Bandicoot hunt", "Kecoon hunting", "Bulcan hunting", "Wild bulcan hunting", "Kekoon warrior hunt", "Subdual of Gavolt" };
    private static readonly string[] DesignStates = { "In progress", "Available", "In progress", "Ready", "Available", "Available", "Available", "Available" };
    private static readonly Color DesignGold = new("dfc184");
    private const string DesignLongDialogue = "Master, have you heard of a monster called the werewolf?\n\nThey're nocturnal but I heard they grew increasingly violent of late.\n\nYou know who to go see about this time as well, don't you?\n\nJeez, [Sentinel] Patrick's just the person who'd be in the know on something like this, right?";

    // Isolated component studies: this entry point does not modify or install the live plugin.
    private async Task CaptureNpcDesigns(int nation)
    {
        bool selectedOnly = OS.GetCmdlineUserArgs().Contains("selected-a-polish");
        string output = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/" + (selectedOnly ? "npc-dialogue-scroll-audit" : "npc-quest-design-options")));
        System.IO.Directory.CreateDirectory(output);
        using (var icon = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://assets/npc-quest/monster-hunt-32.png")))
            _designHuntIcon = ImageTexture.CreateFromImage(icon);
        foreach (string path in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck", "build/client/source-content/content/npcs.pck", "build/client/source-content/content/weapons.pck" })
            if (!ProjectSettings.LoadResourcePack(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../" + path)), false)) throw new Exception("Missing design resource pack: " + path);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size = new Vector2I(1000, 760);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 760), MouseFilter = MouseFilterEnum.Ignore });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var world = new World();
        using var npcs = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/Npcs.json")));
        var patrick = npcs.RootElement.EnumerateArray().First(n => n.GetProperty("Id").GetInt32() == 13013);
        var scene = (PackedScene?)typeof(World).GetMethod("ResolveMobScene", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, new object[] { patrick.GetProperty("ModelId").GetInt32() });
        if (scene == null) throw new Exception("Guard Patrick's source model is unavailable");
        scene.Instantiate<Node3D>().Free();
        var portrait = await NpcPortraitCache.Get(this, new GameNpcPortrait("quest-design-patrick", "[Guard] Patrick", 50, () => scene.Instantiate<Node3D>()));
        if (portrait == null) throw new Exception("The actual NPC portrait was not captured");
        var magpie = npcs.RootElement.EnumerateArray().First(n => n.GetProperty("Id").GetInt32() == 31506);
        var magpieScene = (PackedScene?)typeof(World).GetMethod("ResolveMobScene", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, new object[] { magpie.GetProperty("ModelId").GetInt32() });
        if (magpieScene == null) throw new Exception("Magpie's source model is unavailable");
        var magpiePortrait = await NpcPortraitCache.Get(this, new GameNpcPortrait("quest-design-magpie", "[Lunar Lady] Magpie", 50, () => magpieScene.Instantiate<Node3D>()));
        if (magpiePortrait == null) throw new Exception("The actual multi-objective NPC portrait was not captured");
        ItemData.EnsureLoaded();
        var items = (Dictionary<int, ItemData.Item>)typeof(ItemData).GetField("_items", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        _designHelmetIds = new[] { "Half Plate Helmet", "Rogue Helmet", "Mage Linen Cap", "Priest Helmet" }
            .Select(name => items.Values.First(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && i.Icon > 0).Id + 5).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, (nation == 1 ? "karus" : "human") + "-items.json"), JsonSerializer.Serialize(_designHelmetIds.Select(id => new { id, name = ItemData.Get(id)?.Name, icon = ItemData.Get(id)?.Icon, extensionIcon = ItemData.ExtFor(id)?.Icon })));
        var results = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        foreach (string variant in selectedOnly ? new[] { "a" } : new[] { "a", "b", "c" })
        foreach (bool offer in new[] { false, true })
        foreach (bool multiple in variant == "a" && offer ? new[] { false, true } : new[] { false })
        foreach (bool longSpeech in selectedOnly && !offer ? new[] { false, true } : new[] { false })
        {
            _designBounds.Clear(); _designMenu.Clear(); _designChoices.Clear(); _designSpeech = null;
            var root = BuildNpcDesign(variant, offer, multiple ? magpiePortrait : portrait, multiple, longSpeech);
            root.Position = new Vector2(Mathf.Floor((1000 - root.Size.X) / 2), Mathf.Floor((760 - root.Size.Y) / 2));
            AddChild(root);
            for (int frame = 0; frame < 8; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            // Commit the declared grid after controls resolve their actual inherited font metrics.
            foreach (var pair in _designBounds) { pair.Control.Position = pair.Bounds.Position; pair.Control.Size = pair.Bounds.Size; }
            for (int frame = 0; frame < 2; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string screen = offer ? multiple ? "accept-multiple" : "accept" : longSpeech ? "list-long" : "list";
            var failures = new List<string>();
            var bounds = new List<object>();
            foreach (var pair in _designBounds)
            {
                var actual = pair.Control.GetRect();
                bool exact = (actual.Position - pair.Bounds.Position).Length() < .1f && (actual.Size - pair.Bounds.Size).Length() < .1f;
                var relative = pair.Control.GetGlobalRect(); relative.Position -= root.GlobalPosition;
                bool enclosed = new Rect2(Vector2.Zero, root.Size).Grow(.1f).Encloses(relative);
                bool textFits = pair.Control is not Label label || label.GetMinimumSize().Y <= actual.Size.Y + .1f;
                if (pair.Control is Label single && single.AutowrapMode == TextServer.AutowrapMode.Off)
                    textFits &= single.GetThemeFont("font").GetStringSize(single.Text, HorizontalAlignment.Left, -1, single.GetThemeFontSize("font_size")).X <= actual.Size.X + .1f;
                if (!exact || !enclosed || !textFits) failures.Add(pair.Control.Name + ": " + actual + "; declared " + pair.Bounds + "; minimum " + pair.Control.GetCombinedMinimumSize() + "; textFits=" + textFits);
                bounds.Add(new { name = pair.Control.Name.ToString(), declared = pair.Bounds.ToString(), actual = actual.ToString(), exact, enclosed, textFits });
            }
            if (!new Rect2(Vector2.Zero, new Vector2(1000, 760)).Encloses(root.GetGlobalRect())) failures.Add("Frame exceeds the viewport");
            var speechBar = _designSpeech?.GetVScrollBar();
            bool speechOverflow = speechBar != null && speechBar.MaxValue > speechBar.Page + .5;
            if (variant == "a" && speechOverflow != longSpeech) failures.Add("Speech overflow did not match the fixture");
            async Task Save(string suffix)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                using var crop = image.GetRegion(new Rect2I((int)root.Position.X - 10, (int)root.Position.Y - 10, (int)root.Size.X + 20, (int)root.Size.Y + 20));
                crop.SavePng(System.IO.Path.Combine(output, $"{prefix}-{variant}-{screen}{suffix}.png"));
            }
            await Save("");
            bool scrollInputVerified = false;
            if (longSpeech && _designSpeech != null && speechBar != null)
            {
                var rootBounds = root.GetGlobalRect();
                var speechBounds = _designSpeech.GetGlobalRect();
                var target = speechBounds.GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = target, GlobalPosition = target });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = target, GlobalPosition = target });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = target, GlobalPosition = target });
                for (int frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                scrollInputVerified = speechBar.Value > 0;
                if (!scrollInputVerified) failures.Add("Wheel input inside the speech area did not scroll its contents");
                speechBar.Value = speechBar.MaxValue - speechBar.Page;
                await Save("-scrolled");
                if (root.GetGlobalRect() != rootBounds || _designSpeech.GetGlobalRect() != speechBounds || _designBounds.Any(p => p.Control.GetRect() != p.Bounds))
                    failures.Add("Scrolling changed the frame or declared layout");
                Input.ParseInputEvent(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero });
            }
            else if (!offer)
            {
                var target = _designMenu[1].GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = target, GlobalPosition = target });
                for (int frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await Save("-hover");
                Input.ParseInputEvent(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero });
            }
            else if (_designChoices.Count > 2)
            {
                foreach (var choice in _designChoices) choice.SetMeta("quest_reward_selected", false);
                _designChoices[2].SetMeta("quest_reward_selected", true);
                foreach (var choice in _designChoices) foreach (var mark in choice.GetChildren().OfType<NpcDesignRewardMark>()) mark.QueueRedraw();
                await Save("-selection");
            }
            results.Add(new { variant, screen, width = root.Size.X, height = root.Size.Y, controls = bounds,
                speech = _designSpeech == null ? null : new { fixedHeight = _designSpeech.Size.Y, contentHeight = _designSpeech.GetContentHeight(), overflow = speechOverflow,
                    scrollInputVerified, scrollValue = speechBar!.Value, horizontalScroll = false }, failures });
            root.Free();
            if (failures.Count > 0) throw new Exception($"Design {prefix}/{variant}/{screen}: " + string.Join("; ", failures));
        }
        if (NpcPortraitCache.ActiveRenderers != 0 || NpcPortraitCache.RetainedViewportCount != 0 || NpcPortraitCache.SceneNodeCount != 0) throw new Exception("The static portrait retained 3D rendering resources");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-verification.json"), JsonSerializer.Serialize(new { nation = prefix, prototypeOnly = true, portraitRenders = NpcPortraitCache.RenderRequests, retainedPortraitViewports = NpcPortraitCache.RetainedViewportCount, screens = results }, new JsonSerializerOptions { WriteIndented = true }));
        NpcPortraitCache.Clear(); world.Free();
        GD.Print("NPC_DESIGN_AUDIT_OK: " + prefix + ", " + results.Count + " screens, actual bounds, text fit, hover, selection, cached portraits");
    }

    private Control DesignPlace(Control parent, Control child, string name, float x, float y, float w, float h, bool verify = true)
    {
        child.Name = name; child.Position = new Vector2(x, y); child.Size = new Vector2(w, h);
        parent.AddChild(child);
        if (verify) _designBounds.Add((child, new Rect2(x, y, w, h)));
        return child;
    }
    private Label DesignText(Control parent, string name, string text, float x, float y, float w, float h, bool bold = false, Color? ink = null, bool center = false, bool wrap = false, int size = 13)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontOverride("font", bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", ink ?? ClassicReportDesign.Value);
        label.AddThemeConstantOverride("outline_size", 0);
        label.AddThemeConstantOverride("line_spacing", 0);
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        DesignPlace(parent, label, name, x, y, w, h);
        return label;
    }
    private static StyleBoxTexture DesignBox(string texture, Rect2 source, int margin = 3, bool fill = false)
    {
        var box = new StyleBoxTexture { Texture = Plugin.Kit.Texture(texture), RegionRect = source, DrawCenter = fill };
        foreach (var side in new[] { Side.Left, Side.Right, Side.Top, Side.Bottom }) { box.SetTextureMargin(side, margin); box.SetContentMargin(side, 0); }
        return box;
    }
    private void DesignInset(Control parent, string variant, string name, float x, float y, float w, float h, bool selected = false)
    {
        if (variant == "b")
        {
            DesignPlace(parent, new ColorRect { Color = new Color(ClassicReportDesign.SelectedRow, selected ? .65f : .14f), MouseFilter = MouseFilterEnum.Ignore }, name + "_fill", x + 3, y + 3, w - 6, h - 6);
            DesignPlace(parent, new ClassicReportSection(), name, x, y, w, h);
        }
        else
        {
            var panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            if (variant == "a") { panel.AddThemeStyleboxOverride("panel", DesignBox("ui_quest_us.png", new Rect2(18, 47, 328, 186))); DesignPlace(parent, panel, name, x, y, w, h); }
            else DesignPlace(parent, new NpcDesignMessageInset(), name, x, y, w, h);
        }
    }
    private Button DesignButton(Control parent, string variant, string name, string text, float x, float y, float w, float h, bool tab = false)
    {
        var button = new Button { Text = text, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        if (variant == "a")
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
                button.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 18, ContentMarginRight = 18 });
            button.AddChild(new ClassicNpcOptionPlate(button));
            button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 13);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(state, DesignGold);
            button.AddThemeColorOverride("font_disabled_color", new Color("77746e"));
        }
        else if (variant == "b") ClassicReportDesign.StyleButton(button, tab);
        else
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                int sy = state is "pressed" or "hover_pressed" ? 251 : state == "hover" ? 280 : 222;
                var box = DesignBox("ui_message_us.png", new Rect2(332, sy, 97, 29), 3, true);
                if (ClassicDesign.Karus) box = ClassicDesign.ButtonBox(state, false);
                if (state == "disabled") box.ModulateColor = new Color(.55f, .55f, .55f);
                button.AddThemeStyleboxOverride(state, box);
            }
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 13);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(state, ClassicDesign.Karus ? new Color("201c18") : DesignGold);
            button.AddThemeColorOverride("font_disabled_color", new Color("77746e"));
        }
        DesignPlace(parent, button, name, x, y, w, h);
        return button;
    }
    private void DesignArrow(Control parent, string variant, string name, float x, float y, bool next, bool disabled)
    {
        var node = Plugin.Kit.Layout("{nation}_page_quest_us").Find(next ? "btn_page_up" : "btn_page_down")!;
        var button = new Button { Disabled = disabled };
        ClassicReportDesign.StyleButton(button, false, node);
        DesignPlace(parent, button, name, x, y, 32, 18);
    }
    private TextureRect DesignPortrait(Control parent, Texture2D texture, float x, float y, int size)
    {
        var shader = new Shader { Code = """
            shader_type canvas_item;
            uniform sampler2D frame_art : source_color, filter_nearest;
            void fragment() {
                vec2 offset=UV-vec2(.5);
                float radius=length(offset);
                if(radius>.5) COLOR=vec4(0.);
                else if(radius>.445) {
                    float angle=atan(offset.y,offset.x)/6.2831853+.5;
                    vec2 art=vec2(42.+angle*278.,306.+(radius-.445)/.055*4.)/512.;
                    COLOR=texture(frame_art,art);
                    COLOR.rgb=mix(vec3(.12,.10,.07),COLOR.rgb,COLOR.a);
                    COLOR.a=1.-smoothstep(.493,.5,radius);
                } else {
                    vec4 portrait=texture(TEXTURE,UV);
                    COLOR=vec4(mix(vec3(.055,.045,.03),portrait.rgb,portrait.a),1.);
                }
            }
            """ };
        var material = new ShaderMaterial { Shader = shader }; material.SetShaderParameter("frame_art", Plugin.Kit.Texture("ui_quest_us.png")!);
        var image = new TextureRect { Texture = texture, Material = material, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = TextureFilterEnum.Linear, MouseFilter = MouseFilterEnum.Ignore };
        DesignPlace(parent, image, "npc_portrait", x, y, size, size);
        return image;
    }

    private Control BuildNpcDesign(string variant, bool offer, Texture2D portrait, bool multiple = false, bool longSpeech = false)
    {
        int width = variant == "a" ? 363 : variant == "b" ? 392 : 380;
        int left = variant == "a" ? 18 : variant == "b" ? 12 : 16;
        int inner = width - left * 2;
        int top = variant == "a" ? 80 : variant == "b" ? 88 : 62;
        int introHeight = variant == "b" ? 110 : 120;
        int y = top + introHeight + 8;
        int rowHeight = variant == "a" ? 32 : variant == "b" ? 30 : 40;
        int gap = variant == "a" ? 5 : 4;
        int huntHeight = variant == "a" ? multiple ? 90 : 42 : 34;
        int huntSectionHeight = huntHeight + 20;
        int footer = offer ? y + huntSectionHeight + 9 + 62 + (multiple ? 12 : 9 + 112 + 12) : y + 8 * rowHeight + 7 * gap + 12;
        int height = footer + 32 + 24;
        var root = new Control { Name = "design_" + variant, Size = new Vector2(width, height) };
        if (variant == "a") DesignPlace(root, new ClassicNpcQuestChrome(), "quest_shell", 0, 0, width, height);
        else if (variant == "b")
        {
            DesignPlace(root, new ClassicReportSurface(), "nation_shell", 0, 40, width, height - 40);
            DesignPlace(root, new ClassicReportHeader(false), "nation_header", 0, 38, width, 42);
            var ornaments = CharacterLayout.Frame().Children.Where(n => n.Type == "image" || n.IsButton && n.Id == "btn_close").ToArray();
            var model = new LayoutNode { Type = "base", Id = "native_close_ornament", W = width, H = 80 };
            foreach (var ornament in ornaments) model.Children.Add(ornament);
            DesignPlace(root, new LayoutView(Plugin.Kit, model), "original_close_ornament", 0, 0, width, 80);
            DesignButton(root, variant, "header_plate", "", 44, 42, width - 50, 34, true).MouseFilter = MouseFilterEnum.Ignore;
        }
        else DesignPlace(root, new NpcDesignBraidFrame(), "braided_shell", 0, 0, width, height);
        string title = offer ? multiple ? "Hit and Miss Festival Begins" : "Subdual of Gavolt" : "[Guard] Patrick";
        DesignText(root, "title", title, variant == "b" ? 56 : 36, variant == "a" ? 32 : variant == "b" ? 47 : 18,
            width - (variant == "b" ? 70 : 92), 24, true, variant == "a" || variant == "c" ? DesignGold : ClassicDesign.Karus ? new Color("201c18") : ClassicReportDesign.Value, variant == "c");
        if (variant != "b")
        {
            var closeSource = Plugin.Kit.Layout("co_questmenu_us").Find("btn_close")!;
            var close = new Button(); ClassicReportDesign.StyleButton(close, false, closeSource);
            DesignPlace(root, close, "close", width - 33, variant == "a" ? 27 : 16, 23, 23);
        }
        int portraitSize = variant == "c" ? 96 : 84;
        DesignPortrait(root, portrait, left + 4, top + (variant == "b" ? 5 : 6), portraitSize);
        DesignText(root, "npc_level", "Lv. 50", left + 4, top + portraitSize + 9, portraitSize, 17, false, ClassicReportDesign.Caption, true, size: 12);
        int textX = left + portraitSize + 15;
        DesignInset(root, variant, "speech_frame", textX, top, width - left - textX, introHeight);
        DesignText(root, "npc_identity", multiple ? "[Lunar Lady] Magpie" : offer ? "[Guard] Patrick" : "Moradon's guardian", textX + 8, top + 7, width - left - textX - 16, 19, true,
            ClassicReportDesign.Caption, size: 12);
        string dialogue = longSpeech ? DesignLongDialogue : multiple ? "Please hunt ten of each creature: Paramun, Doom Soldier, Troll Berserker and Giant Golem." : offer ? "Gavolts raided our mill and took the wheat. Hunt five Gavolts to help the farmers supply Moradon again." : "What mission are you going to undertake? Help keep Moradon safe.";
        if (variant == "a") DesignSpeech(root, dialogue, textX + 8, top + 29, width - left - textX - 16, introHeight - 36);
        else DesignText(root, "speech", dialogue, textX + 8, top + 29, width - left - textX - 16, introHeight - 36, wrap: true);
        if (!offer)
        {
            for (int i = 0; i < DesignTopics.Length; i++)
            {
                int rowY = y + i * (rowHeight + gap);
                var button = DesignButton(root, variant, "quest_" + i, "", left, rowY, inner, rowHeight);
                _designMenu.Add(button);
                bool soft = variant == "b" || variant == "c" && ClassicDesign.Karus;
                Color normal = soft ? (ClassicDesign.Karus ? new Color("201c18") : ClassicReportDesign.Value) : DesignGold;
                Color stateInk = DesignStates[i] == "Ready" ? new Color(soft && ClassicDesign.Karus ? "165026" : "a3d790")
                    : DesignStates[i] == "In progress" ? new Color(soft && ClassicDesign.Karus ? "16465e" : "9dd6ec") : normal;
                if (variant == "c")
                {
                    DesignText(button, "topic", DesignTopics[i], 8, 3, inner - 16, 18, true, normal, true, size: 13);
                    DesignText(button, "state", DesignStates[i], 8, 21, inner - 16, 15, false, stateInk, true, size: 11);
                }
                else
                {
                    DesignText(button, "topic", (i + 1) + ".  " + DesignTopics[i], variant == "a" ? 18 : 8, 4, inner - 120, rowHeight - 8, true, normal, size: 12);
                    var state = DesignText(button, "state", DesignStates[i], inner - 100, 4, 82, rowHeight - 8, false, stateInk, size: 11);
                    state.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }
            DesignArrow(root, variant, "previous_page", left + 6, footer + 7, false, true);
            DesignText(root, "page", "1 / 3", left + 43, footer + 4, 60, 24, center: true, size: 12);
            DesignArrow(root, variant, "next_page", left + 107, footer + 7, true, false);
            DesignButton(root, variant, "close_list", "Close", width - left - 130, footer, 130, 32);
        }
        else
        {
            DesignSection(root, variant, "Hunt", left, y, inner);
            if (variant == "a")
            {
                int objectiveWidth = (inner - 6) / 2;
                string[] targets = multiple ? new[] { "Paramun", "Doom Soldier", "Troll Berserker", "Giant Golem" } : new[] { "Gavolt" };
                for (int i = 0; i < targets.Length; i++) DesignMonster(root, "hunt_" + i, targets[i], multiple ? "0 / 10" : "0 / 5",
                    left + i % 2 * (objectiveWidth + 6), y + 20 + i / 2 * 48, i % 2 == 1 ? inner - objectiveWidth - 6 : objectiveWidth);
            }
            else
            {
                DesignInset(root, variant, "hunt_card", left, y + 20, inner, 34);
                DesignText(root, "hunt_symbol", "◆", left + 10, y + 25, 20, 24, false, DesignGold, size: 16);
                DesignText(root, "hunt_name", "Gavolt", left + 34, y + 27, inner - 105, 20);
                DesignText(root, "hunt_count", "0 / 5", width - left - 63, y + 27, 51, 20, true, DesignGold, center: true);
            }
            y += huntSectionHeight + 9;
            DesignSection(root, variant, "Rewards", left, y, inner);
            // This source quest grants experience and one selected helmet; no fictional coin reward is shown.
            DesignReward(root, variant, "experience", "Experience", multiple ? "20,000,000" : "6,250", 900001000, left, y + 20, inner, 42, false);
            y += 71;
            if (!multiple)
            {
            DesignSection(root, variant, "Choose one reward", left, y, inner);
            int cardWidth = (inner - 6) / 2;
            string[] helmets = { "Half Plate Helmet", "Rogue Helmet", "Mage Linen Cap", "Priest Helmet" };
            for (int i = 0; i < 4; i++) DesignReward(root, variant, "helmet_" + i, helmets[i], "+5  ·  1", _designHelmetIds[i], left + i % 2 * (cardWidth + 6), y + 20 + i / 2 * 46, i % 2 == 1 ? inner - cardWidth - 6 : cardWidth, 42, true, i == 0);
            }
            DesignButton(root, variant, "accept", "Accept", left, footer, (inner - 6) / 2, 32);
            DesignButton(root, variant, "reject", "Reject", left + (inner - 6) / 2 + 6, footer, inner - (inner - 6) / 2 - 6, 32);
        }
        return root;
    }
    private void DesignSpeech(Control parent, string text, int x, int y, int width, int height)
    {
        int lineHeight = Mathf.CeilToInt(Plugin.Kit.Regular.GetHeight(13));
        int visibleHeight = height / lineHeight * lineHeight;
        var speech = new RichTextLabel { Text = text, BbcodeEnabled = false, FitContent = false, ScrollActive = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipContents = true, MouseFilter = MouseFilterEnum.Stop };
        foreach (string face in new[] { "normal", "bold", "italics", "bold_italics", "mono" })
            speech.AddThemeFontOverride(face + "_font", face.StartsWith("bold") ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        speech.AddThemeFontSizeOverride("normal_font_size", 13);
        speech.AddThemeFontSizeOverride("bold_font_size", 13);
        speech.AddThemeColorOverride("default_color", ClassicReportDesign.Value);
        speech.AddThemeConstantOverride("line_separation", 0);
        speech.AddThemeConstantOverride("outline_size", 0);
        speech.AddThemeConstantOverride("text_highlight_h_padding", 0);
        speech.AddThemeConstantOverride("scrollbar_separation", 4);
        speech.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        speech.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var bar = speech.GetVScrollBar();
        var track = new StyleBoxFlat { BgColor = new Color("171715"), BorderColor = ClassicReportDesign.Caption };
        track.SetBorderWidthAll(1); track.SetContentMarginAll(0);
        bar.AddThemeStyleboxOverride("scroll", track);
        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            var grab = ClassicDesign.ButtonBox(state == "grabber_highlight" ? "hover" : state == "grabber_pressed" ? "pressed" : "normal");
            grab.SetContentMarginAll(0); bar.AddThemeStyleboxOverride(state, grab);
        }
        bar.CustomMinimumSize = new Vector2(12, 0);
        bar.Step = lineHeight;
        bar.FocusMode = FocusModeEnum.None;
        speech.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true } wheel || wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
            bar.Value += (wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1) * lineHeight * 3;
            speech.AcceptEvent();
        };
        DesignPlace(parent, speech, "speech", x, y, width, visibleHeight);
        _designSpeech = speech;
    }
    private void DesignMonster(Control parent, string name, string target, string progress, int x, int y, int width)
    {
        var card = new Control { MouseFilter = MouseFilterEnum.Ignore, TooltipText = target + " · Monster hunt objective" };
        DesignPlace(parent, card, name, x, y, width, 42);
        DesignInset(card, "a", "card_border", 0, 0, width, 42);
        DesignPlace(card, new TextureRect { Texture = _designHuntIcon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore }, "monster_icon", 6, 5, 32, 32);
        DesignText(card, "monster_name", target, 43, 5, width - 50, 17, size: 11);
        DesignText(card, "progress", progress, 43, 23, width - 50, 14, true, DesignGold, size: 11);
    }
    private void DesignSection(Control parent, string variant, string text, int x, int y, int width)
    {
        if (variant == "b") DesignPlace(parent, new ClassicReportSurface(true), text.Replace(' ', '_') + "_rule", x, y + 18, width, 1);
        DesignText(parent, text.Replace(' ', '_') + "_caption", text, x + 2, y, width - 4, 18, true, ClassicReportDesign.Caption, size: 12);
    }
    private void DesignReward(Control parent, string variant, string name, string text, string count, int item, int x, int y, int width, int height, bool choice, bool selected = false)
    {
        var card = new Control { MouseFilter = choice ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore };
        DesignPlace(parent, card, name, x, y, width, height);
        DesignInset(card, variant, "card_border", 0, 0, width, height, selected);
        DesignPlace(card, new TextureRect { Texture = ItemData.Icon(item), TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore }, "icon", 6, 5, 32, 32);
        int textSize = choice ? 11 : 12;
        DesignText(card, "item_name", text, 43, 5, width - 50, 17, false, null, size: textSize);
        DesignText(card, "amount", count, 43, 23, width - 72, 14, true, DesignGold, size: 11);
        if (choice)
        {
            card.SetMeta("quest_reward_selected", selected);
            var mark = new NpcDesignRewardMark(card); card.AddChild(mark); mark.Position = new Vector2(width - 20, 23); mark.Size = new Vector2(15, 15);
            _designChoices.Add(card);
            card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { foreach (var option in _designChoices) { option.SetMeta("quest_reward_selected", option == card); foreach (var selection in option.GetChildren().OfType<NpcDesignRewardMark>()) selection.QueueRedraw(); } } };
        }
    }
}

// Original message-window braid and clean message-panel rails, assembled as independent components.
public partial class NpcDesignBraidFrame : Control
{
    public NpcDesignBraidFrame() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var message = Plugin.Kit.Texture("ui_message_us.png")!;
        DrawRect(new Rect2(3, 3, Size.X - 6, Size.Y - 6), new Color(.018f, .016f, .012f, 1));
        void Tile(Texture2D atlas, Rect2 dst, Rect2 src, Color? color = null)
        {
            for (float y = 0; y < dst.Size.Y; y += src.Size.Y)
            for (float x = 0; x < dst.Size.X; x += src.Size.X)
            {
                var size = new Vector2(Math.Min(src.Size.X, dst.Size.X - x), Math.Min(src.Size.Y, dst.Size.Y - y));
                DrawTextureRectRegion(atlas, new Rect2(dst.Position + new Vector2(x, y), size), new Rect2(src.Position, size), color ?? Colors.White);
            }
        }
        NpcDesignMessageInset.DrawBorder(this, Size);
        Tile(message, new Rect2(5, 5, Size.X - 10, 9), new Rect2(4, 47, 32, 9));
        Tile(message, new Rect2(5, 44, Size.X - 10, 9), new Rect2(4, 71, 32, 9));
        Tile(message, new Rect2(5, Size.Y - 13, Size.X - 10, 9), new Rect2(4, 71, 32, 9));
    }
}

// Straight atlas samples omit the original compression seam in the middle of each rail.
public partial class NpcDesignMessageInset : Control
{
    public NpcDesignMessageInset() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; Resized += QueueRedraw; }
    public override void _Draw() => DrawBorder(this, Size);
    public static void DrawBorder(Control owner, Vector2 size)
    {
        var atlas = Plugin.Kit.Texture("ui_message_us.png")!;
        void Part(Rect2 dst, Rect2 src) => owner.DrawTextureRectRegion(atlas, dst, src);
        void Tile(Rect2 dst, Rect2 src)
        {
            for (float y = 0; y < dst.Size.Y; y += src.Size.Y)
            for (float x = 0; x < dst.Size.X; x += src.Size.X)
            { var extent = new Vector2(Math.Min(src.Size.X, dst.Size.X - x), Math.Min(src.Size.Y, dst.Size.Y - y)); Part(new Rect2(dst.Position + new Vector2(x, y), extent), new Rect2(src.Position, extent)); }
        }
        Part(new Rect2(0, 0, 4, 4), new Rect2(330, 2, 4, 4));
        Part(new Rect2(size.X - 4, 0, 4, 4), new Rect2(455, 2, 4, 4));
        Part(new Rect2(0, size.Y - 4, 4, 4), new Rect2(330, 127, 4, 4));
        Part(new Rect2(size.X - 4, size.Y - 4, 4, 4), new Rect2(455, 127, 4, 4));
        Tile(new Rect2(4, 0, size.X - 8, 4), new Rect2(342, 2, 16, 4));
        Tile(new Rect2(4, size.Y - 4, size.X - 8, 4), new Rect2(342, 127, 16, 4));
        Tile(new Rect2(0, 4, 4, size.Y - 8), new Rect2(330, 20, 4, 16));
        Tile(new Rect2(size.X - 4, 4, 4, size.Y - 8), new Rect2(455, 20, 4, 16));
    }
}

// The original Help-window check artwork replaces a navigation-arrow selection mark.
public partial class NpcDesignRewardMark : Control
{
    private readonly Control _card;
    private bool _selected;
    public NpcDesignRewardMark(Control card) { _card = card; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Nearest; }
    public override void _Process(double delta)
    {
        bool current = _card.GetMeta("quest_reward_selected").AsBool();
        if (current != _selected) { _selected = current; QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_selected)
        {
            DrawTextureRectRegion(Plugin.Kit.Texture("ui_message_us.png")!, new Rect2(Vector2.Zero, Size), new Rect2(39, 409, 25, 25));
            DrawRect(new Rect2(-Position, _card.Size).Grow(-.5f), new Color("e4c174"), false, 1);
        }
        else { DrawRect(new Rect2(1, 1, 13, 13), new Color("171511")); DrawRect(new Rect2(1, 1, 13, 13), new Color("8f7b56"), false, 1); }
    }
}
