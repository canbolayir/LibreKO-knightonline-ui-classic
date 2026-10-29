using Godot;
using System.Reflection;
using System.Text.Json;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

// Classic HUD pieces that the other HUD modes do not capture: the target frame (with the client's quest-item rows),
// message boxes, a long status name, the first-frame chat/Info scroll rails, the Info log tabs, and the minimap's
// NPC finder (list and gold diamonds).
public partial class Preview
{
    private async Task CaptureHudQaAudit(int nation)
    {
        var output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        var window = GetWindow(); window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.Size = new Vector2I(1120, 660); window.ContentScaleSize = Vector2I.Zero;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1110, 650), GlobalPosition = new Vector2(1110, 650) });
        AddChild(new ColorRect { Color = new Color("34362b"), Size = new Vector2(1120, 660), MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new ColorRect { Color = new Color("afa995"), Position = new Vector2(0, 0), Size = new Vector2(560, 300), MouseFilter = MouseFilterEnum.Ignore });
        var checks = new List<string>(); var screens = new List<string>(); var bounds = new Dictionary<string, object>();
        var failures = new List<string>();
        // Collect every failed check so one run reports all visible defects.
        void Require(bool value, string text) { if (!value) { failures.Add(text); GD.PushError("HUD_QA_AUDIT: " + text); } else checks.Add(text); }
        async Task Frames(int count = 4) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        async Task Shot(string state)
        {
            await Frames();
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, prefix + "-" + state + ".png"));
            screens.Add(prefix + "-" + state + ".png");
        }
        object Rect(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y };
        bool Inside(Control outer, Control inner) => outer.GetGlobalRect().Grow(0.01f).Encloses(inner.GetGlobalRect());
        var target = _proxies.First(p => p is IGameTarget);
        var character = _proxies.First(p => p is IGameCharacter);
        var map = _proxies.First(p => p is IGameMap);
        var log = _proxies.First(p => p is IGameLog);
        // The item names and icons of the quest-item rows come from the client's source content.
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        ItemData.EnsureLoaded();
        Rect2 Screen(Control c) { var t = c.GetGlobalTransformWithCanvas(); return new Rect2(t.Origin, c.Size * t.Scale); }
        float TextWidth(Control c, string text) => c.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, c.GetThemeFontSize("font_size")).X;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        // Status bar with a long name, chat and Info at their first frame (no resize after construction).
        character.Values = name => name == "get_Name" ? "Extraordinarylongname" : name == "get_Level" ? 83 : null;
        var status = new StatusHud(); AddChild(status);
        var chat = new ChatWindow(); AddChild(chat);
        var info = new LogWindow(); AddChild(info); info.Visible = true;
        var hotbar = new HotkeyBar(); AddChild(hotbar);
        await Frames(6);
        chat.Position = new Vector2(20, 424); info.Position = new Vector2(660, 409);
        hotbar.Position = new Vector2(480, 8);
        var identity = Descendants(status).OfType<Label>().Single(l => l.Name == "Text_Position");
        var zoomIn = Descendants(status).OfType<Control>().Single(c => c.Name == "Btn_ZoomIn");
        Require(identity.GetGlobalRect().End.X <= zoomIn.GetGlobalRect().Position.X - 2, "Status name field ends before the minimap zoom button");
        Require(identity.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && identity.ClipText, "Long status names are trimmed with an ellipsis");
        bounds["statusName"] = Rect(identity.GetGlobalRect()); bounds["zoomIn"] = Rect(zoomIn.GetGlobalRect());
        foreach (var (name, control) in new (string, Control)[] { ("chat", chat), ("info", info) })
        {
            var rail = (Control)control.GetType().GetField("_rail", flags)!.GetValue(control)!;
            var arrows = rail.GetChildren().OfType<TextureButton>().ToArray();
            Require(arrows.Length == 2 && arrows[0].Position.Y == 0 && arrows[1].GetRect().End.Y == rail.Size.Y && rail.Size.Y > 60,
                $"{name} scroll arrows sit at both ends of the rail on the first frame (rail {rail.Size.Y})");
            bounds[name + "Rail"] = new { rail = Rect(rail.GetGlobalRect()), up = Rect(arrows[0].GetGlobalRect()), down = Rect(arrows[1].GetGlobalRect()) };
        }
        var keys = hotbar.FindChildren("key_*", "Label", true, false).Cast<Label>().ToArray();
        var ink = keys[0].GetThemeColor("font_color");
        Require(keys.Length == 10 && keys.All(k => k.GetThemeColor("font_color") == ink), "Hotbar numerals share one ink");
        Require(nation == 1 ? ink.Luminance < 0.3f && keys[0].GetThemeConstant("outline_size") == 0 : ink.Luminance > 0.6f,
            nation == 1 ? "Karus hotbar numerals use dark ink on the light rail" : "El Morad hotbar numerals keep light ink on the dark rail");
        var defaultAt = (Vector2)typeof(HotkeyBar).GetMethod("DefaultPosition", flags)!.Invoke(hotbar, null)!;
        var hotbarDefault = new Rect2(defaultAt, hotbar.Size);
        var mailButton = Descendants(status).OfType<ClassicStatusShortcut>().Single(b => b.Name == "btn_mail").GetGlobalRect();
        Require(!hotbarDefault.Intersects(StatusHud.DefaultBounds) && !hotbarDefault.Intersects(mailButton) && GetViewportRect().Encloses(hotbarDefault),
            $"Default vertical hotbar {hotbarDefault} clears the status minimap and shortcuts");
        bounds["hotbarDefault"] = Rect(hotbarDefault); bounds["statusDefault"] = Rect(StatusHud.DefaultBounds);
        hotbar.Position = defaultAt;
        await Shot("status-chat");
        var image = GetViewport().GetTexture().GetImage();
        foreach (var (id, bar) in new[] { ("Text_HP", "Progress_HP"), ("Text_MSP", "Progress_MSP"), ("Text_ExpP", "Progress_ExpP") })
        {
            var text = Descendants(status).OfType<Label>().Single(l => l.Name == id).GetGlobalRect();
            var meter = Descendants(status).OfType<Control>().Single(c => c.Name == bar).GetGlobalRect();
            // Rendered white digit rows inside the label rectangle.
            var rows = Enumerable.Range((int)text.Position.Y - 2, (int)text.Size.Y + 4).Where(y => Enumerable.Range((int)text.Position.X, (int)text.Size.X)
                .Any(x => image.GetPixel(x, y) is var c && c.R > 0.85f && c.G > 0.85f && c.B > 0.85f)).ToArray();
            Require(rows.Length > 0, id + " digits are rendered");
            float digits = (rows.First() + rows.Last() + 1) / 2f, centre = meter.GetCenter().Y;
            bounds[id + "Rows"] = new { top = rows.First(), bottom = rows.Last(), digits, meter = centre };
            // Recorded only: the native UIF places these text rows on the lower edge of each bar.
        }
        // ---- Info log tabs (3277df5 TabbedLog): All / Status / Item / System as Classic chat tabs before the Info plate.
        log.Values = name => name == "get_History" ? (object)new List<GameLogLine>
        {
            new(GameLogKind.Status, "You recovered 42 MP.", new Color("6565ff")),
            new(GameLogKind.Item, "Water of Ibex succeeded.", new Color("ffff00")),
            new(GameLogKind.System, "Beginning attack on Worm.", Colors.White),
            new(GameLogKind.Item, "You received 3 Bandicoot Fang.", new Color("ffff00")),
        } : null;
        foreach (var width in new[] { 420f, 290f })
        {
            info.Size = new Vector2(width, info.Size.Y); await Frames(2);
            var tabs = Descendants(info).OfType<Button>().Where(b => b.Name.ToString().StartsWith("tab_")).OrderBy(b => b.GetGlobalRect().Position.X).ToArray();
            var plate = (Control)typeof(LogWindow).GetField("_title", flags)!.GetValue(info)!;
            Require(tabs.Select(b => b.Name.ToString()).SequenceEqual(new[] { "tab_all", "tab_status", "tab_item", "tab_system" }), $"Info {width}: the client's four log tabs in order");
            Require(tabs.All(b => b.IsVisibleInTree() && Inside(info, b) && b.GetGlobalRect().End.X <= plate.GetGlobalRect().Position.X + .01f && Mathf.Abs(b.GetGlobalRect().GetCenter().Y - plate.GetGlobalRect().GetCenter().Y) < 1),
                $"Info {width}: the tabs sit on the footer row, inside the window and left of the Info plate");
            for (int i = 1; i < tabs.Length; i++) Require(!tabs[i - 1].GetGlobalRect().Intersects(tabs[i].GetGlobalRect()), $"Info {width}: tabs do not overlap");
            foreach (var tab in tabs)
            {
                var caption = Descendants(tab).OfType<Label>().FirstOrDefault();
                string text = caption?.Text ?? tab.Text;
                Control measured = caption != null ? caption : tab;
                Require(TextWidth(measured, text) <= tab.Size.X - 2, $"Info {width}: tab caption '{text}' fits inside the tab art ({TextWidth(measured, text):0} in {tab.Size.X})");
            }
            bounds["infoTabs" + width] = tabs.Select(b => Rect(b.GetGlobalRect())).ToArray();
        }
        info.Size = new Vector2(420, info.Size.Y); await Frames(2);
        var logText = (Control)typeof(LogWindow).GetField("_log", flags)!.GetValue(info)!;
        int Shown() => ((System.Collections.ICollection)typeof(LogText).GetField("_lines", flags)!.GetValue(logText)!).Count;
        var infoTabs = Descendants(info).OfType<Button>().Where(b => b.Name.ToString().StartsWith("tab_")).ToDictionary(b => b.Name.ToString());
        info.Show(null); await Frames(2);
        Require(Shown() == 4 && infoTabs["tab_all"].ButtonPressed, "Info All shows every line");
        foreach (var (tab, kind, count) in new[] { ("tab_item", GameLogKind.Item, 2), ("tab_status", GameLogKind.Status, 1), ("tab_system", GameLogKind.System, 1) })
        {
            infoTabs[tab].EmitSignal(BaseButton.SignalName.Pressed); infoTabs[tab].ButtonPressed = true; await Frames(2);
            Require(infoTabs.Values.Count(b => b.ButtonPressed) == 1, $"Info {tab}: one tab is selected");
            Require(info.Shown == kind && Shown() == count, $"Info {tab} shows only its {count} line(s) ({Shown()})");
        }
        await Shot("info-tabs");
        infoTabs["tab_all"].EmitSignal(BaseButton.SignalName.Pressed); infoTabs["tab_all"].ButtonPressed = true; await Frames(2);
        Require(info.Shown == null && Shown() == 4, "Info All again shows every line");
        log.Values = null;

        // ---- Minimap NPC finder (3277df5 MiniMap.NpcList): "N" on the map, the list over the map, gold diamonds.
        int npcKind = 2, picked = 0;
        var absent = new[] { 9, 11, 12 };
        var spots = new[]
        {
            new MiniMap.Blip(764.8f + 30f, 416.8f + 20f, UiTheme.GoldVivid, 4.5f, false, Diamond: true),
            new MiniMap.Blip(764.8f + 2000f, 416.8f, UiTheme.GoldVivid, 4.5f, false, Diamond: true),
        };
        map.Values = name => name switch { "get_NpcKind" => npcKind, "get_Blips" => (IReadOnlyList<MiniMap.Blip>)spots, _ => null };
        map.Calls = (name, args) =>
        {
            if (name == "NpcKindPresent") return !absent.Contains((int)args![0]!);
            if (name == "PickNpcKind") { picked = (int)args![0]!; npcKind = picked == npcKind ? NpcFinder.NoKind : picked; }
            return null;
        };
        var npcList = status.NpcList;
        var mapCanvas = Descendants(status).OfType<MapCanvas>().Single();
        npcList.Refresh(); mapCanvas.QueueRedraw(); await Frames(2);
        var mapRect = Screen(mapCanvas);
        var npcButton = npcList.Button;
        var glyph = Descendants(npcButton).OfType<Label>().Single();
        Require(Screen(npcButton).Encloses(Screen(glyph)) && glyph.Text == ClassicNpcList.Glyph && TextWidth(glyph, glyph.Text) <= glyph.Size.X, $"The N glyph sits inside its button ({Screen(glyph)} in {Screen(npcButton)})");
        Require(npcButton.IsVisibleInTree() && Screen(npcButton).Size == ClassicNpcList.ButtonSize && mapRect.Encloses(Screen(npcButton)) && npcButton.TooltipText == "NPC List: Potion shop",
            $"The N button sits in the map's lower left corner ({Screen(npcButton)} in {mapRect}; '{npcButton.TooltipText}')");
        Require(!npcList.Visible, "The NPC list starts closed");
        npcButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
        var listRect = Screen(npcList);
        Require(npcList.IsVisibleInTree() && mapRect.Grow(.01f).Encloses(listRect) && !listRect.Intersects(Screen(npcButton)),
            $"N opens the list over the map, clear of the button ({listRect} in {mapRect})");
        var listTitle = Descendants(npcList).OfType<Label>().Single(l => l.Name == "npc_list_title");
        Require(listTitle.Text == ClassicNpcList.Title && listRect.Encloses(Screen(listTitle)) && TextWidth(listTitle, listTitle.Text) <= listTitle.Size.X, "The list title fits inside the list");
        var kindRows = Enumerable.Range(1, NpcFinder.KindCount).Select(npcList.Row).ToArray();
        foreach (var (row, i) in kindRows.Select((r, i) => (r, i)))
        {
            int kind = i + 1;
            var r = Screen(row);
            Require(listRect.Encloses(r) && r.Size.Y >= 18, $"NPC row {row.Text} lies inside the list ({r})");
            Require(TextWidth(row, row.Text) <= r.Size.X - 2 * ClassicNpcList.RowInset, $"NPC row '{row.Text}' fits its column ({TextWidth(row, row.Text):0} in {r.Size.X - 2 * ClassicNpcList.RowInset})");
            Require(row.Disabled == absent.Contains(kind) && row.ButtonPressed == (kind == npcKind), $"NPC row {row.Text}: muted when the zone lacks it, selected when picked");
        }
        for (int i = 0; i < kindRows.Length; i++)
            for (int j = i + 1; j < kindRows.Length; j++)
                Require(!Screen(kindRows[i]).Intersects(Screen(kindRows[j])), $"NPC rows {kindRows[i].Text} and {kindRows[j].Text} do not overlap");
        bounds["npcList"] = new { map = Rect(mapRect), list = Rect(listRect), button = Rect(Screen(npcButton)), rows = kindRows.Select(r => Rect(Screen(r))).ToArray() };
        await Shot("npc-list");
        kindRows[3].EmitSignal(Control.SignalName.MouseEntered);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = Screen(kindRows[3]).GetCenter(), GlobalPosition = Screen(kindRows[3]).GetCenter() });
        await Shot("npc-list-hover");
        kindRows[3].EmitSignal(BaseButton.SignalName.Pressed); await Frames(3);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1110, 650), GlobalPosition = new Vector2(1110, 650) });
        Require(picked == 4 && npcKind == 4 && !npcList.Visible && npcList.Row(4).ButtonPressed && !npcList.Row(2).ButtonPressed,
            "Picking Repair asks the client for that kind and closes the list so the diamonds show");
        mapCanvas.QueueRedraw(); await Frames(3);
        using (var mapShot = GetViewport().GetTexture().GetImage())
        {
            bool Gold(Vector2 at)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (mapShot.GetPixel((int)at.X + dx, (int)at.Y + dy) is var c && c.R > .8f && c.G > .6f && c.B < .45f) return true;
                return false;
            }
            float ppw = mapCanvas.Size.X * .5f / 140f;
            var mid = mapRect.GetCenter();
            var near = mid + new Vector2(30f * ppw, -20f * ppw);
            var edge = new Vector2(mapRect.End.X - MapCanvas.DiamondInset, mid.Y);
            Require(Gold(near), $"A picked kind's spot in range is a gold diamond on the map ({near})");
            Require(Gold(edge), $"A spot out of range stays as a gold diamond on the map's edge ({edge})");
            bounds["npcDiamonds"] = new { near = new { near.X, near.Y }, edge = new { edge.X, edge.Y } };
        }
        await Shot("npc-diamonds");
        npcButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
        npcList.Row(4).EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
        Require(picked == 4 && npcKind == NpcFinder.NoKind && !npcList.Row(4).ButtonPressed, "Picking the shown kind again clears it (the client's rule)");
        map.Values = null; map.Calls = null;

        status.QueueFree(); chat.QueueFree(); info.QueueFree(); hotbar.QueueFree();
        character.Values = null;

        // Target frame: monster with level, long name at low HP and a player without a level.
        var states = new (string Name, int Level, int Hp, int MaxHp, bool Player)[] {
            ("Death Knight", 72, 6100, 10000, false),
            ("Ancient Blood Seeker of the Abyss", 83, 1500, 10000, false),
            ("Guardian", 0, 10000, 10000, true),
        };
        var frames = new List<TargetFrame>();
        for (int i = 0; i < states.Length; i++)
        {
            var s = states[i];
            target.Values = name => name switch {
                "get_Has" => true, "get_Name" => s.Name, "get_Level" => s.Level, "get_Hp" => s.Hp, "get_MaxHp" => s.MaxHp,
                "get_IsPlayer" => s.Player, "get_Hostile" => !s.Player, _ => null };
            var frame = new TargetFrame(); AddChild(frame); await Frames(1);
            typeof(TargetFrame).GetMethod("Refresh", flags)!.Invoke(frame, null);
            frame.Position = new Vector2(Mathf.Round((1120 - frame.Size.X) / 2), 8 + i * 84);
            frames.Add(frame);
            await Frames(2);
            Require(frame.Visible, $"Target frame {i} is visible for an available target");
            var label = Descendants(frame).OfType<Label>().Single(l => l.Name == "text_target");
            var hp = Descendants(frame).OfType<Control>().Single(c => c.Name == "pro_target");
            Require(Inside(frame, label) && Inside(frame, hp), $"Target frame {i} name and HP bar stay inside the frame");
            var font = label.GetThemeFont("font"); int size = label.GetThemeFontSize("font_size");
            float width = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X;
            Require(width <= label.Size.X || label.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming || label.AutowrapMode != TextServer.AutowrapMode.Off,
                $"Target frame {i} name fits or is trimmed ({width:0} of {label.Size.X:0})");
            Require(!label.GetGlobalRect().Intersects(hp.GetGlobalRect()), $"Target frame {i} name does not overlap the HP bar");
            bounds["target" + i] = new { frame = Rect(frame.GetGlobalRect()), name = Rect(label.GetGlobalRect()), hp = Rect(hp.GetGlobalRect()), text = label.Text, textWidth = width };
        }
        // A monster with the client's quest-item rows (icon and "name: have/need"), one with a long item name.
        // Quest items the audit pack knows (the client's fixture ids first; the pack may lack them).
        var known = new[] { 379273000, 379274000, 379154000, 379159000, 379091000, 379021000, 379008000 }
            .Where(id => !ItemData.DisplayName(id).StartsWith("Item ")).Take(2).ToArray();
        int first = known.Length > 0 ? known[0] : 379273000, second = known.Length > 1 ? known[1] : 379274000;
        var questDrops = new List<GameTargetDrop>
        {
            new(ItemIcons.Get(first), new TargetQuestDrop(first, 2, 3).Line(ItemData.DisplayName(first))),
            new(ItemIcons.Get(second), new TargetQuestDrop(second, 0, 3).Line(ItemData.DisplayName(second))),
            new(ItemIcons.Get(first), new TargetQuestDrop(first, 1, 10).Line("Ancient Blood Seeker Heart of the Lower Abyss Caves")),
        };
        target.Values = name => name switch {
            "get_Has" => true, "get_Name" => "Bandicoot", "get_Level" => 14, "get_Hp" => 380, "get_MaxHp" => 1000,
            "get_IsPlayer" => false, "get_Hostile" => true, "get_QuestDrops" => (IReadOnlyList<GameTargetDrop>)questDrops, _ => null };
        var dropFrame = new TargetFrame(); AddChild(dropFrame); await Frames(1);
        void RefreshFrame() => typeof(TargetFrame).GetMethod("Refresh", flags)!.Invoke(dropFrame, null);
        RefreshFrame();
        dropFrame.Position = new Vector2(Mathf.Round((1120 - dropFrame.Size.X) / 2), 8 + 3 * 84);
        await Frames(2);
        var dropColumn = dropFrame.GetNode<VBoxContainer>(TargetFrame.DropRows);
        var dropRows = dropColumn.GetChildren().OfType<HBoxContainer>().Where(r => !r.IsQueuedForDeletion()).ToArray();
        float expected = dropFrame.ArtHeight + TargetFrame.DropsTop + questDrops.Count * (TargetFrame.DropIcon + TargetFrame.DropGap) - TargetFrame.DropGap;
        Require(dropColumn.Visible && dropRows.Length == 3 && dropFrame.Size.Y == expected, $"Target frame grows by the client's {questDrops.Count} quest-item rows ({dropFrame.Size.Y}, expected {expected})");
        var barHp = Descendants(dropFrame).OfType<Control>().Single(c => c.Name == "pro_target").GetGlobalRect();
        var dropBounds = new List<object>();
        for (int i = 0; i < dropRows.Length; i++)
        {
            var icon = dropRows[i].GetChildren().OfType<TextureRect>().Single();
            var line = dropRows[i].GetChildren().OfType<Label>().Single();
            var ir = icon.GetGlobalRect(); var lr = line.GetGlobalRect();
            Require(ir.Size == new Vector2(TargetFrame.DropIcon, TargetFrame.DropIcon) && icon.Texture != null, $"Quest row {i}: a 20 px item icon");
            Require(Inside(dropFrame, icon) && Inside(dropFrame, line) && ir.Position.Y >= dropFrame.GetGlobalRect().Position.Y + dropFrame.ArtHeight && ir.Position.Y > barHp.End.Y,
                $"Quest row {i}: icon and text lie inside the frame under the bar art ({ir} / {lr})");
            Require(line.Text == questDrops[i].Line && line.GetThemeFont("font") == Plugin.Kit.Bold && line.GetThemeFontSize("font_size") == TargetFrame.DropFont && line.GetThemeColor("font_color") == Colors.White,
                $"Quest row {i}: the client's line '{line.Text}' in white Arial Bold");
            float width = TextWidth(line, line.Text);
            Require(width <= lr.Size.X || line.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis && line.ClipText, $"Quest row {i}: text fits or is trimmed ({width:0} in {lr.Size.X:0})");
            Require(Mathf.Abs(ir.Merge(lr).GetCenter().X - dropFrame.GetGlobalRect().GetCenter().X) <= 1, $"Quest row {i}: icon and text are centred under the bar");
            Require(Mathf.Abs(ir.GetCenter().Y - lr.GetCenter().Y) <= .5f, $"Quest row {i}: text centred on its icon");
            if (i > 0) Require(ir.Position.Y - dropRows[i - 1].GetChildren().OfType<TextureRect>().Single().GetGlobalRect().End.Y == TargetFrame.DropGap, $"Quest row {i}: the client's 2 px row gap");
            dropBounds.Add(new { icon = Rect(ir), line = Rect(lr), text = line.Text, textWidth = width });
        }
        bounds["targetDrops"] = new { frame = Rect(dropFrame.GetGlobalRect()), rows = dropBounds };
        await Shot("target");
        // The rows follow the client: fewer rows shrink the frame, a target without quest items has none.
        questDrops.RemoveRange(1, 2); RefreshFrame(); await Frames(2);
        Require(dropColumn.GetChildren().OfType<HBoxContainer>().Count(r => !r.IsQueuedForDeletion()) == 1 && dropFrame.Size.Y == dropFrame.ArtHeight + TargetFrame.DropsTop + TargetFrame.DropIcon,
            "Target frame follows the client's rows when they change");
        questDrops.Clear(); RefreshFrame(); await Frames(2);
        Require(!dropColumn.Visible && dropFrame.Size.Y == dropFrame.ArtHeight, "Target frame without quest items is the bar art alone");
        target.Values = null;
        dropFrame.QueueFree();
        foreach (var frame in frames) frame.QueueFree();

        // Message boxes: OK, OK/Cancel, a long message and a non-dismissable notice.
        var boxes = new (string State, string Title, string Message, string Confirm, string? Cancel, bool Dismissable)[] {
            ("msgbox-ok", "Notice", "You do not have enough gold.", "OK", null, true),
            ("msgbox-okcancel", "Party", "Guardian invites you to join a party. Do you accept?", "Accept", "Decline", true),
            ("msgbox-long", "Warning", "This item will be destroyed permanently and cannot be recovered. All upgrades, stats and bound attributes are lost. Are you sure that you want to continue?", "OK", "Cancel", true),
            ("msgbox-wait", "Connecting", "Please wait while the server answers your request...", "OK", null, false),
        };
        foreach (var b in boxes)
        {
            int confirmed = 0, cancelled = 0, dismissed = 0;
            var request = (DialogRequest)Create(typeof(DialogRequest), b.Title, b.Message, b.Confirm, b.Cancel, b.Dismissable,
                (Action)(() => confirmed++), (Action)(() => cancelled++), (Action)(() => dismissed++));
            var box = new MessageBox(request); AddChild(box);
            await Frames(3);
            var view = box.GetChildren().OfType<LayoutView>().Single();
            Require(Mathf.Abs(view.GetGlobalRect().GetCenter().X - 560) <= 1 && Mathf.Abs(view.GetGlobalRect().GetCenter().Y - 330) <= 1, b.State + " frame is centred in the viewport");
            var message = Descendants(view).OfType<Label>().Single(l => l.Name == "Text_Message");
            var title = Descendants(view).OfType<Label>().Single(l => l.Name == "Text_title");
            Require(Inside(view, message) && Inside(view, title), b.State + " title and message stay inside the frame");
            int lines = message.GetLineCount(); float lineHeight = message.GetLineHeight();
            Require(lines * lineHeight <= message.Size.Y + 0.5f, $"{b.State} message fits its field ({lines} lines x {lineHeight} in {message.Size.Y})");
            var buttons0 = Descendants(view).OfType<BaseButton>().Where(c => c.IsVisibleInTree()).Select(c => c.GetGlobalRect()).ToArray();
            Require(buttons0.All(r => message.GetGlobalRect().End.Y <= r.Position.Y) && title.GetGlobalRect().Position.Y < message.GetGlobalRect().Position.Y,
                b.State + " message stays between the title and the buttons");
            var buttons = Descendants(view).OfType<BaseButton>().Where(c => c.IsVisibleInTree() && (c.Name == "btn_ok" || c.Name == "btn_cancel")).OrderBy(c => c.Position.X).ToArray();
            Require(buttons.Length == (b.Dismissable ? (b.Cancel != null ? 2 : 1) : 0), b.State + " shows the expected buttons");
            foreach (var button in buttons)
            {
                Require(Inside(view, button), b.State + " button inside frame " + button.Name);
                var caption = Descendants(button).OfType<Label>().Where(l => l.Visible).ToArray();
                Require(caption.Length == 1 && Inside(button, caption[0]), b.State + " one caption inside " + button.Name);
                var f = caption[0].GetThemeFont("font"); int fs = caption[0].GetThemeFontSize("font_size");
                Require(f.GetStringSize(caption[0].Text, HorizontalAlignment.Left, -1, fs).X <= caption[0].Size.X, b.State + " caption fits " + caption[0].Text);
            }
            // Content-sized box: the shared rhythm (title band, message lines, buttons) with no empty band.
            float spacing = message.GetThemeConstant("line_spacing");
            Require(message.Position.Y == KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.TextTop
                && Mathf.Abs(message.Size.Y - (lines * lineHeight + (lines - 1) * spacing)) < 1f, $"{b.State} message rectangle is its {lines} wrapped lines at the common top ({message.GetRect()})");
            foreach (var button in buttons)
                Require(button.Position.Y == message.GetRect().End.Y + KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.ActionGap
                    && view.Size.Y - button.GetRect().End.Y == KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.ButtonBottom, $"{b.State} {button.Name} sits the common gap under the message ({button.GetRect()} in {view.Size})");
            if (buttons.Length == 0)
                Require(view.Size.Y - message.GetRect().End.Y == KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.TextBottom, $"{b.State} frame closes the common gap under the message ({view.Size})");
            var art = view.GetNode<Control>("message_art");
            Require(art.Size == view.Size && art.Position == Vector2.Zero, $"{b.State} frame art covers the fitted box");
            if (buttons.Length == 1) Require(Mathf.Abs(buttons[0].GetGlobalRect().GetCenter().X - view.GetGlobalRect().GetCenter().X) <= 1, b.State + " single button is centred");
            if (buttons.Length == 2)
            {
                float left = buttons[0].GetGlobalRect().Position.X - view.GetGlobalRect().Position.X;
                float right = view.GetGlobalRect().End.X - buttons[1].GetGlobalRect().End.X;
                Require(Mathf.Abs(left - right) <= 1 && buttons[0].Size == buttons[1].Size && buttons[0].Position.Y == buttons[1].Position.Y, b.State + " button pair is symmetric");
            }
            bounds[b.State] = new { frame = Rect(view.GetGlobalRect()), title = Rect(title.GetGlobalRect()), message = Rect(message.GetGlobalRect()), lines,
                buttons = buttons.Select(x => new { name = x.Name.ToString(), rect = Rect(x.GetGlobalRect()) }).ToArray() };
            await Shot(b.State);
            if (b.Dismissable)
            {
                buttons[0].EmitSignal(BaseButton.SignalName.Pressed);
                if (b.Cancel != null) { buttons[1].EmitSignal(BaseButton.SignalName.Pressed); Require(confirmed == 1 && cancelled == 1, b.State + " buttons fire the native confirm/cancel callbacks"); }
                else Require(dismissed == 1, b.State + " OK fires the native dismiss callback");
            }
            box.QueueFree(); await Frames(1);
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-hud-qa.json"), JsonSerializer.Serialize(new { nation, checks, failures, screens, bounds }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count > 0) throw new Exception($"HUD_QA_AUDIT: {failures.Count} failed checks");
        GD.Print($"HUD_QA_AUDIT_OK: {checks.Count} checks, {screens.Count} screens");
    }
}
