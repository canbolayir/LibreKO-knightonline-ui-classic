using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Native;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using Spec = KnightOnlineUiClassic.Layout.ClassicContextMenuLayout;

// context-menu-audit: every right-click menu uses the one Classic context menu. The loot item rule menu (captured
// first in the client's look), the party member, clan member, player and Character Report page menus: title row,
// row geometry and fonts, hover and disabled states with rendered pixels, real mouse clicks running the client's
// actions, Escape and outside-click closing, and the menu staying inside the viewport near the screen edges.
public partial class Preview
{
    private async Task CaptureContextMenuAudit(PluginGame game, int nation)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/context-menus");
        System.IO.Directory.CreateDirectory(output);
        string prefix = nation == 1 ? "karus" : "human";
        var window = GetWindow(); window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled; window.Size = new Vector2I(1280, 800);
        // The audit pushes mouse events and never moves the system mouse; menus take the cursor from those events.
        var stage = GetViewport();
        AddChild(new ColorRect { Color = new Color("2f332a"), Size = new Vector2(1280, 800), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        foreach (string pack in new[] { "build/client/LibreKO.pck", "build/client/source-content/knightonline.pck" })
            if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../" + pack), false)) throw new Exception("Missing context menu audit pack: " + pack);
        ItemData.EnsureLoaded();
        PluginHost.Ui.ReplacePlayerMenu(request => new ClassicPlayerMenu(request));
        var catalog = ItemData.All().Where(i => i.Slot < 100 && !string.IsNullOrWhiteSpace(i.Name)).OrderBy(i => i.Id).ToArray();
        int plain = catalog.Where(i => i.Countable == 0).Select(i => i.Id).First();
        int stackable = catalog.Where(i => i.Countable > 0).OrderByDescending(i => i.Name.Contains("Potion")).First().Id;
        int longest = catalog.Where(i => i.Countable == 0).OrderByDescending(i => ItemData.DisplayName(i.Id).Trim().Length).First().Id;
        int graded = catalog.Select(i => i.Id).FirstOrDefault(id => ItemData.ExtFor(id)?.MagicOrRare is ItemData.Rarity.Magic or ItemData.Rarity.Rare or ItemData.Rarity.Unique);

        // A world in the tree (without its game loop or input), so the client's popups find their viewport and
        // the loot cells and menus under it receive the mouse as in the game.
        var world = new ContextMenuAuditWorld(); AddChild(world);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)Native.Get<object>(world, name)!;
        void SetField(string name, object? value) => Native.Set(world, name, value);
        object? Call(string method, params object?[] args) => DetailCall(world, method, args);
        SetField("myId", 300);
        Call("Loot.BuildWindow"); Call("Party.BuildHud");
        string clanError = "";
        try { Call("Clan.BuildPage"); } catch (Exception e) { clanError = e.InnerException?.Message ?? e.Message; }

        var checks = new List<string>(); var failures = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (valid) checks.Add(text); else { failures.Add(text); GD.PushError("CONTEXT_MENU_AUDIT: " + text); } }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var viewport = GetViewportRect();
        Image? last = null;
        async Task Shot(string state, Control? subject = null)
        {
            await Frames();
            string file = prefix + "-" + state + ".png";
            last = stage.GetTexture().GetImage();
            last.SavePng(System.IO.Path.Combine(output, file));
            var rect = subject?.GetGlobalRect() ?? new Rect2();
            screens.Add(new { file, state, bounds = new { x = rect.Position.X, y = rect.Position.Y, w = rect.Size.X, h = rect.Size.Y } });
        }
        void Move(Vector2 at) => stage.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        async Task Click(Vector2 at, MouseButton button = MouseButton.Left)
        {
            Move(at); await Frames(1);
            var mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
            stage.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, ButtonMask = mask, Pressed = true });
            stage.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = false });
            await Frames(3);
        }
        ClassicContextMenu? OpenMenu() => ClassicContextMenu.OpenLayer?.GetChildren().OfType<ClassicContextMenu>().FirstOrDefault();
        Button Row(string caption) => Descendants(OpenMenu()!).OfType<Button>().Single(b => b.Text == caption);
        bool Near(Color a, Color b) => Mathf.Abs(a.R - b.R) < .03f && Mathf.Abs(a.G - b.G) < .03f && Mathf.Abs(a.B - b.B) < .03f;
        bool Dark(Color c) => c.R < .03f && c.G < .03f && c.B < .03f;
        Color Pixel(Control control) => last!.GetPixelv((Vector2I)(control.GetGlobalRect().Position + new Vector2(2, control.Size.Y / 2)).Floor());

        // One look: panel, title row, rule, rows, sizes and placement at the cursor.
        Control? CheckMenu(string what, string name, string? title, Color? ink, string[] captions, Vector2 cursor, bool[]? enabled = null)
        {
            var menu = OpenMenu();
            Require(menu != null && menu.Name == "classic_" + name, $"{what}: the Classic context menu opens");
            if (menu == null) return null;
            var panel = (PanelContainer)menu.FindChild(name + "_panel", true, false)!;
            var box = panel.GetThemeStylebox("panel") as StyleBoxFlat;
            Require(box != null && box.BgColor == Colors.Black && box.BorderColor == ClassicDarkFrame.InputBox().BorderColor && box.BorderWidthTop > 0
                && box.ContentMarginLeft == Spec.Padding && box.ContentMarginTop == Spec.Padding, $"{what}: the Classic menu panel (opaque black, gold line, 4 px padding)");
            var rows = Descendants(panel).OfType<Button>().ToArray();
            Require(rows.Select(r => r.Text).SequenceEqual(captions), $"{what}: the client's rows in order ({string.Join(", ", rows.Select(r => r.Text))})");
            Require(rows.All(r => r.GetThemeFont("font") == Plugin.Kit.Bold && r.GetThemeFontSize("font_size") == Spec.FontSize
                && r.GetThemeColor("font_color") == ClassicDesign.Text && r.GetThemeColor("font_hover_color") == ClassicDarkFrame.Heading
                && r.GetThemeColor("font_disabled_color") == ClassicDesign.Muted), $"{what}: Arial Bold 12 rows with the Classic inks");
            var label = title == null ? null : menu.FindChild(name + "_name", true, false) as Label;
            float widest = rows.Select(r => Plugin.Kit.Bold.GetStringSize(r.Text, HorizontalAlignment.Left, -1, Spec.FontSize).X)
                .Append(title == null ? 0 : Plugin.Kit.Bold.GetStringSize(title, HorizontalAlignment.Left, -1, Spec.TitleSize).X).Max();
            float width = Spec.Width(widest);
            Require(rows.All(r => r.Size == new Vector2(width, Spec.Row)), $"{what}: every row {width} x {Spec.Row} ({string.Join(", ", rows.Select(r => r.Size))})");
            if (title != null)
            {
                Require(label != null && label.Text == title && label.Size == new Vector2(width, Spec.Row) && label.GetThemeFont("font") == Plugin.Kit.Bold
                    && label.GetThemeFontSize("font_size") == Spec.TitleSize, $"{what}: title row '{title}' in Arial Bold 13 ({label?.Size})");
                Require(label != null && label.GetThemeColor("font_color") == (ink ?? ClassicDarkFrame.Heading), $"{what}: title ink {(ink ?? ClassicDarkFrame.Heading).ToHtml(false)}");
                var rule = menu.FindChild(name + "_rule", true, false) as Control;
                Require(rule != null && rule.Size.Y == Spec.RuleHeight && label != null && rule.Position.Y == label.Position.Y + Spec.Row, $"{what}: gold rule under the title");
            }
            int separators = Descendants(panel).OfType<HSeparator>().Count() - (title == null ? 0 : 1);
            var size = new Vector2(width + Spec.Padding * 2, Spec.Padding * 2 + (title == null ? 0 : Spec.Row + Spec.RuleHeight) + rows.Length * Spec.Row + separators * Spec.SeparatorHeight);
            Require(panel.Size == size, $"{what}: rendered panel {panel.Size} matches the declared {size}");
            var place = Spec.Place(cursor, size, viewport.Size);
            Require(panel.Position == place, $"{what}: opens at the cursor {cursor} -> {panel.Position} (declared {place})");
            Require(viewport.Encloses(panel.GetGlobalRect()), $"{what}: inside the viewport {panel.GetGlobalRect()}");
            Require(rows.All(r => panel.GetGlobalRect().Encloses(r.GetGlobalRect()) && r.GetGlobalRect().Position == r.GetGlobalRect().Position.Round()), $"{what}: rows inside the panel on whole pixels");
            for (int i = 1; i < rows.Length; i++)
                Require(rows[i].GetGlobalRect().Position.Y >= rows[i - 1].GetGlobalRect().End.Y, $"{what}: row {i} below row {i - 1}");
            if (enabled != null)
                Require(rows.Select(r => !r.Disabled).SequenceEqual(enabled), $"{what}: enabled rows {string.Join("", enabled.Select(e => e ? "1" : "0"))}");
            return panel;
        }

        // ---- Loot box (client look first) -----------------------------------------------------------------
        var lootLayer = Field<CanvasLayer>("Loot.layer"); var lootPanel = Field<PanelContainer>("Loot.Panel");
        var lootCells = Field<ItemSlotView[]>("Loot.cells");
        var entries = new List<LootEntry> { new(LootWire.GoldItemId, 1520), new(plain, 1), new(stackable, 25), new(longest, 1) };
        if (graded > 0) entries.Add(new(graded, 1));
        void ShowLoot()
        {
            var list = Field<List<LootEntry>>("Loot.Entries"); list.Clear(); list.AddRange(entries);
            SetField("Loot.openBundleId", 7);
            Call("Loot.RefreshWindow"); lootLayer.Visible = true;
        }
        ShowLoot(); lootPanel.Position = new Vector2(560, 260);
        await Frames();
        ItemSlotView Cell(int item) => lootCells.First(c => c.Item.ItemId == item);
        string[] lootRows = { "Always auto-loot this item", "Never auto-loot this item", "Clear the rule for this item" };
        await Click(Cell(plain).GetGlobalRect().GetCenter(), MouseButton.Right);
        var nativeMenu = Field<PopupMenu>("LootFilterWindow.ruleMenu");
        Require(nativeMenu != null && nativeMenu.Visible && OpenMenu() == null, "client look: the loot rule menu is the client's plain popup");
        if (nativeMenu != null)
        {
            Require(Enumerable.Range(0, nativeMenu.ItemCount).Select(nativeMenu.GetItemText).SequenceEqual(lootRows), "client look: the client's three rule rows");
            await Shot("native-loot-menu", lootPanel);
            nativeMenu.Hide(); world.RemoveChild(nativeMenu); nativeMenu.Free();
        }
        // The client builds its menu again on the next right click, as on a fresh world.
        SetField("LootFilterWindow.ruleMenu", null);

        // ---- Classic --------------------------------------------------------------------------------------
        NativeOverlays.Prepare(world);
        await Frames(3);
        var filter = Field<LootFilter>("LootFilterWindow.lootFilter");
        async Task<Control?> OpenLoot(int item, string what)
        {
            var at = Cell(item).GetGlobalRect().GetCenter();
            await Click(at, MouseButton.Right);
            var panel = CheckMenu(what, "loot_menu", ItemData.DisplayName(item).Trim(), ItemGrade.Tint(item), lootRows, at);
            var native = Field<PopupMenu>("LootFilterWindow.ruleMenu");
            Require(native != null && !native.Visible, $"{what}: the client's popup is hidden behind the Classic menu");
            return panel;
        }
        var lootMenu = await OpenLoot(plain, "loot menu");
        await Shot("loot-menu", lootMenu);
        var always = Row(lootRows[0]);
        Move(always.GetGlobalRect().GetCenter()); await Frames(3);
        await Shot("loot-menu-hover", lootMenu);
        Require(always.IsHovered() && Near(Pixel(always), ClassicDarkFrame.SelectedRow), "loot menu: the hovered row shows the selected-row colour");
        Require(Dark(Pixel(Row(lootRows[1]))), "loot menu: other rows stay on the black panel " + Pixel(Row(lootRows[1])).ToHtml());
        await Click(always.GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && filter.Always.Contains(plain) && !filter.Never.Contains(plain), "loot menu: Always runs the client's rule (always auto-loot) and closes");
        await OpenLoot(plain, "loot menu again");
        await Click(Row(lootRows[1]).GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && filter.Never.Contains(plain) && !filter.Always.Contains(plain), "loot menu: Never runs the client's rule (never auto-loot)");
        await OpenLoot(plain, "loot menu third");
        await Click(Row(lootRows[2]).GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && !filter.Never.Contains(plain) && !filter.Always.Contains(plain), "loot menu: Clear removes the client's rule");
        await OpenLoot(plain, "loot menu escape");
        stage.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }); await Frames(3);
        Require(OpenMenu() == null && !filter.Always.Contains(plain) && !filter.Never.Contains(plain), "loot menu: Escape closes without a rule");
        await OpenLoot(plain, "loot menu outside");
        await Click(new Vector2(40, 40));
        Require(OpenMenu() == null && !filter.Always.Contains(plain) && !filter.Never.Contains(plain), "loot menu: a click outside closes without a rule");
        await Click(Cell(LootWire.GoldItemId).GetGlobalRect().GetCenter(), MouseButton.Right);
        Require(OpenMenu() == null, "loot menu: coins have no rule menu, as in the client");
        var longMenu = await OpenLoot(longest, "loot menu long name");
        var longTitle = (Label?)OpenMenu()?.FindChild("loot_menu_name", true, false);
        Require(longMenu != null && longMenu.Size.X <= Spec.MaxWidth + Spec.Padding * 2 && longTitle != null
            && (longTitle.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis), $"loot menu: a long name widens the menu up to {Spec.MaxWidth} px, then ends in an ellipsis");
        await Shot("loot-menu-long", longMenu);
        ClassicContextMenu.Close(); await Frames(2);
        if (graded > 0)
        {
            var gradeMenu = await OpenLoot(graded, "loot menu graded item");
            Require(ItemGrade.Tint(graded) != ItemGrade.Tint(plain), $"loot menu: the graded item's title uses its grade colour {ItemGrade.Tint(graded).ToHtml(false)}");
            await Shot("loot-menu-grade", gradeMenu);
            ClassicContextMenu.Close(); await Frames(2);
        }
        else GD.Print("CONTEXT_MENU_AUDIT: the audit pack has no magic, rare or unique item; the grade colour is checked on regular items only");
        // Near the bottom-right corner the menu opens on the other side of the cursor and stays on screen.
        lootPanel.Position = viewport.Size - lootPanel.Size - new Vector2(2, 2); await Frames(3);
        var edgeMenu = await OpenLoot(entries[^1].ItemId, "loot menu at the screen edge");
        await Shot("loot-menu-edge", edgeMenu);
        ClassicContextMenu.Close(); Call("Loot.Close"); await Frames(2);

        // ---- Player menu (the client's seam) --------------------------------------------------------------
        var playerAt = new Vector2(420, 260);
        Move(playerAt); await Frames(1);
        Call("PlayerMenu.ShowByName", "Rikka", playerAt); await Frames(3);
        var playerLayer = Field<CanvasLayer>("PlayerMenu.skinLayer");
        var playerMenu = playerLayer?.GetChildren().OfType<ClassicPlayerMenu>().FirstOrDefault();
        Require(playerMenu != null, "player menu: the client's seam builds the Classic player menu");
        if (playerMenu != null)
        {
            var panel = (Control)playerMenu.FindChild("player_menu_panel", true, false)!;
            var rows = Descendants(panel).OfType<Button>().ToArray();
            float width = Spec.Width(rows.Select(r => Plugin.Kit.Bold.GetStringSize(r.Text, HorizontalAlignment.Left, -1, Spec.FontSize).X)
                .Append(Plugin.Kit.Bold.GetStringSize("Rikka", HorizontalAlignment.Left, -1, Spec.TitleSize).X).Max());
            Require(rows.Length == 7 && rows.All(r => r.Size == new Vector2(width, Spec.Row) && r.GetThemeFont("font") == Plugin.Kit.Bold), "player menu: the same rows as every Classic context menu");
            Require(panel.Position == Spec.Place(playerAt, panel.Size, viewport.Size) && viewport.Encloses(panel.GetGlobalRect()), "player menu: at the cursor, inside the viewport");
            await Shot("player-menu", panel);
            rows.Single(r => r.Text == "Copy name").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Require(Field<CanvasLayer?>("PlayerMenu.skinLayer") == null, "player menu: a row runs the client's action and closes");
        }

        // ---- Clan member menu -----------------------------------------------------------------------------
        Require(clanError.Length == 0, "clan: the client's clan page builds " + clanError);
        if (clanError.Length == 0)
        {
            NativeContextMenus.PrepareLater(world);
            var clanAt = new Vector2(300, 520);
            Move(clanAt); await Frames(1);
            Call("Clan.OpenMemberMenu", "Guardian"); await Frames(3);
            var clanNative = Field<PopupMenu>("Clan.memberMenu");
            var clanMenu = CheckMenu("clan member menu", "clan_menu", "Guardian", null, new[] { "Whisper", "Invite to party", "User information" }, clanAt);
            Require(!clanNative.Visible, "clan member menu: the client's popup is hidden; its '— name —' row became the title");
            await Shot("clan-menu", clanMenu);
            long clanPressed = -1; clanNative.IdPressed += id => clanPressed = id;
            await Click(Row("Invite to party").GetGlobalRect().GetCenter());
            var log = Field<TabbedLog>("Log.log").Lines(LogTab.All).ToArray();
            Require(clanPressed == 2 && OpenMenu() == null && log.Any(l => l!.Contains("Inviting Guardian")), "clan member menu: Invite to party runs the client's invite");
        }

        // ---- Party member menu ----------------------------------------------------------------------------
        var members = Enumerable.Range(0, 4).Select(i => new PartyMember(300 + i, 1, new[] { "Knight", "Priest", "Rogue", "Mage" }[i], 3400, 3000, 70 + i, nation * 100 + 1 + i, 2800, 2600)).ToArray();
        Game.Session.Party.SeedPreview(members);
        var partyAt = new Vector2(1180, 120);
        Move(partyAt); await Frames(1);
        NativeParty.PrepareMemberMenu(world, "Priest");
        Require((bool)Call("Party.ShowMemberMenu", 301)!, "party member menu: the client opens its member menu for the leader");
        await Frames(3);
        var partyNative = Field<PopupMenu>("Party.hudMenu");
        var partyMenu = CheckMenu("party member menu", "party_menu", "Priest", null, new[] { "Make leader", "Kick out", "Make commander" }, partyAt);
        Require(!partyNative.Visible, "party member menu: the client's popup is hidden");
        await Shot("party-menu", partyMenu);
        long partyPressed = -1; partyNative.IdPressed += id => partyPressed = id;
        var before = world.GetChildren().ToHashSet();
        await Click(Row("Kick out").GetGlobalRect().GetCenter());
        int kick = (int)Native.Get<object>(Native.StaticOwner("LibreKO.Features.Hud.PartyFeature"), "MenuKick")!;
        Require(partyPressed == kick && Field<int>("Party.hudMenuMember") == -1 && OpenMenu() == null, "party member menu: Kick out runs the client's kick confirmation");
        foreach (var added in world.GetChildren().Where(c => !before.Contains(c)).ToArray()) added.QueueFree();
        await Frames(2);

        // ---- Character Report pages (the plugin's own lists) ---------------------------------------------
        var fixture = (CharacterAuditData)game.Windows.CharacterPanel!;
        var shell = new Control(); AddChild(shell);
        var host = (WindowHost)Create(typeof(WindowHost), "character_info", "Character", shell, (Action)(() => { }));
        var character = new CharacterWindow(host) { Position = new Vector2(32, 46) }; AddChild(character);
        var lists = (Dictionary<string, CharacterList>)typeof(CharacterWindow).GetField("_lists", Flags)!.GetValue(character)!;
        async Task<Control?> PageMenu(string page, int index, string what, string title, string[] captions, bool[] enabled)
        {
            fixture.SelectPage(page);
            typeof(CharacterWindow).GetMethod("Refresh", Flags)!.Invoke(character, null);
            await Settled();
            var button = lists[page].GetChildren().OfType<Button>().ElementAt(index);
            var at = button.GetGlobalRect().GetCenter();
            await Click(at, MouseButton.Right);
            return CheckMenu(what, "character_menu", title, null, captions, at, enabled);
        }
        var friendsMenu = await PageMenu("friends", 2, "friends menu (offline friend)", "Magician", new[] { "Private", "Invite to party", "Delete" }, new[] { false, false, true });
        var privateRow = Row("Private");
        Move(privateRow.GetGlobalRect().GetCenter()); await Frames(3);
        await Shot("character-friends-menu-disabled", friendsMenu);
        Require(Dark(Pixel(privateRow)) && privateRow.GetThemeColor("font_disabled_color") == ClassicDesign.Muted, "friends menu: a disabled row is muted and shows no hover");
        await Click(privateRow.GetGlobalRect().GetCenter());
        Require(OpenMenu() != null && fixture.LastAct == null, "friends menu: a disabled row does nothing and the menu stays open");
        await Click(Row("Delete").GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && fixture.LastAct == ("friend_remove", "Magician"), "friends menu: Delete runs the page's action for that friend");
        var questMenu = await PageMenu("quest", 1, "quest menu", "Defeat the Monsters of Moradon", new[] { "Details", "Track / Untrack", "Abandon", "Turn in" }, new[] { true, true, true, true });
        await Shot("character-quest-menu", questMenu);
        await Click(Row("Turn in").GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && fixture.LastAct == ("quest_complete", "2"), "quest menu: Turn in runs the page's action for that quest");
        await PageMenu("quest", 0, "quest menu (not ready)", "A New Beginning", new[] { "Details", "Track / Untrack", "Abandon", "Turn in" }, new[] { true, true, true, false });
        ClassicContextMenu.Close(); await Frames(2);
        bool chief = fixture.Clan.IsChief;
        var clanRows = chief ? new[] { "Private", "Invite to party", "User information", "Appoint vice-chief", "Hand over leadership", "Expel from clan" } : new[] { "Private", "Invite to party", "User information" };
        var clanPage = await PageMenu("clan", 2, "clan page menu (offline member)", "Guardian", clanRows, clanRows.Select(r => r != "Invite to party").ToArray());
        await Shot("character-clan-menu", clanPage);
        await Click(Row("User information").GetGlobalRect().GetCenter());
        Require(OpenMenu() == null && fixture.LastAct == ("clan_info", "Guardian"), "clan page menu: User information runs the page's action");

        System.IO.File.WriteAllText(System.IO.Path.Combine(output, prefix + "-context-menus.json"),
            JsonSerializer.Serialize(new { checks, failures, screenshots = screens, gradedItem = graded, liveTransactions = false }, new JsonSerializerOptions { WriteIndented = true }));
        ClassicContextMenu.Close();
        character.QueueFree(); shell.QueueFree(); world.QueueFree();
        await Frames(2);
        net.Free();
        if (failures.Count > 0) throw new Exception("CONTEXT_MENU_AUDIT failed: " + string.Join("; ", failures));
        GD.Print("CONTEXT_MENU_AUDIT_OK " + checks.Count);
    }
}

/// <summary>A client world without its game loop and world input, for the context-menu audit.</summary>
public partial class ContextMenuAuditWorld : WorldRoot
{
    public override void _Ready() { }
    public override void _ExitTree() { }
    public override void _Process(double delta) { }
    public override void _Input(InputEvent ev) { }
    public override void _UnhandledInput(InputEvent ev) { }
}
