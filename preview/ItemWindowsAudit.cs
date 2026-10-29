using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using ItemSlot = LibreKO.Domain.ItemSlot;

/// <summary>
/// Audits of the Classic item windows added for the 2619 review. "item-windows-audit": Disassemble, the
/// Character Seal family (help, slot list, code keypad, scroll / ring transfer, Cypher Ring sheet) and the EXP
/// Seal. "item-lists-audit": the Temporary Item List, the Loot Filter and the exit menu with and without
/// temporary items. Both nations; actual rendered bounds, contained text and the native actions.
/// </summary>
public partial class Preview
{
    private async Task CaptureItemWindowsAudit(PluginGame game, int nation, bool lists)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR");
        if (output.Length == 0) output = ProjectSettings.GlobalizePath("res://../../research/item-windows-audit");
        System.IO.Directory.CreateDirectory(output);
        ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1000, 700), MouseFilter = MouseFilterEnum.Ignore });
        var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        Native.Set(Game.Session.Self, "LastEnter", new MyInfo { Nation = nation, Class = nation == 1 ? 105 : 205, Race = nation == 1 ? 1 : 11,
            Name = "Item Preview", Gear = new int[8] }); Game.Session.Self.Inventory.Reset(new ItemSlot[InventoryConstants.InventoryTotal]);
        ItemData.EnsureLoaded();
        PluginHost.Ui.ReplaceDialogs(request => new ClassicServiceNotice(request));
        var checks = new List<string>(); var screens = new List<object>();
        string prefix = nation == 1 ? "karus" : "human";
        void Require(bool valid, string text) { if (!valid) throw new Exception("ITEM_WINDOWS_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int n = 8) { for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Set(object owner, string field, object? value) => Native.Set(owner, field, value);
        Inventory InventoryOf(WorldRoot w) => (Inventory)Game.Session.Self.Inventory;

        void Bounds(Control panel, string state)
        {
            foreach (var control in Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree()))
            {
                foreach (string meta in new[] { ClassicItemsPanel.ExpectedRect, "service_expected_rect" })
                    if (control.HasMeta(meta))
                    {
                        var expected = control.GetMeta(meta).AsRect2();
                        Require(control.GetRect() == expected, $"{state}: measured bounds {control.Name} actual={control.GetRect()} expected={expected} min={control.GetCombinedMinimumSize()} ls={control.GetThemeConstant("line_spacing")} so={control.GetThemeConstant("shadow_offset_y")} fh={control.GetThemeFont("font").GetHeight(control.GetThemeFontSize("font_size"))}");
                        Require(new Rect2(Vector2.Zero, panel.Size).Encloses(control.GetRect()), $"{state}: {control.Name} stays inside the frame");
                    }
                // Single-line text fits its rendered rectangle (no clipped captions or values).
                if (control is Label { AutowrapMode: TextServer.AutowrapMode.Off } label && label.Text.Length > 0 && label.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming)
                {
                    var font = label.GetThemeFont("font"); int size = label.GetThemeFontSize("font_size");
                    float width = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, size).X;
                    Require(width <= label.Size.X + 1, $"{state}: text fits {label.Name} '{label.Text}' {width:0}>{label.Size.X}");
                }
                if (control is Button { ClipText: true } button && button.Text.Length > 0 && button.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming)
                {
                    var font = button.GetThemeFont("font"); int size = button.GetThemeFontSize("font_size");
                    Require(font.GetStringSize(button.Text, HorizontalAlignment.Left, -1, size).X <= button.Size.X - 4, $"{state}: caption fits {button.Name} '{button.Text}'");
                }
            }
            foreach (var cell in Descendants(panel).OfType<ItemSlotView>().Where(c => c.IsVisibleInTree() && !c.Item.IsEmpty))
            {
                var icon = cell.GetChild(0).GetChildren().OfType<TextureRect>().FirstOrDefault() ?? Descendants(cell).OfType<TextureRect>().First();
                Require(icon.GetGlobalRect() == new Rect2(cell.GlobalPosition + new Vector2(2, 2), cell.Size - new Vector2(4, 4)), $"{state}: inventory-aligned icon {cell.Name}");
            }
        }
        async Task Capture(Control window, string state, Control? panel = null)
        {
            window.Visible = true; await Frames(); window.Position = ((GetViewportRect().Size - window.Size) / 2).Round(); await Frames(2);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + ": complete window fits viewport");
            if (panel != null)
            {
                Require(window.Size == panel.CustomMinimumSize && panel.Size == panel.CustomMinimumSize, state + ": fixed Classic proportions " + window.Size);
                Bounds(panel, state);
            }
            string file = prefix + "-" + state + ".png";
            GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
            screens.Add(new { state, file, size = window.Size.ToString(),
                controls = panel == null ? Array.Empty<object>() : Descendants(panel).OfType<Control>().Where(c => c.IsVisibleInTree() && (c.HasMeta(ClassicItemsPanel.ExpectedRect) || c.HasMeta("service_expected_rect")))
                    .Select(c => (object)new { name = c.Name.ToString(), rect = c.GetGlobalRect().ToString() }).ToArray() });
        }
        async Task<(WorldRoot, HudWindow, Control)> Build(string method, params object[] args)
        {
            var world = PreviewFixtures.Prepared(new WorldRoot()); InventoryOf(world).EnsureLength(InventoryConstants.InventoryTotal);
            var window = (HudWindow)DetailCall(world, method, args)!;
            AddChild(window);
            Control panel;
            if (ClassicServiceSkin.WindowIds.Contains(window.Id))
            {
                NativeServices.Prepare(window, world); panel = ClassicServiceSkin.Apply(window.Body)!;
            }
            else
            {
                NativeItems.Prepare(window, world); panel = ClassicItemsSkin.Apply(window.Body)!;
            }
            Require(panel != null, window.Id + " receives its Classic skin");
            await Frames(); return (world, window, panel!);
        }
        void Dispose(WorldRoot world, Control window) { if (GodotObject.IsInstanceValid(window)) window.Free(); world.Free(); }
        T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(n => n.Name == name);

        if (!lists)
        {
            // Disassemble ------------------------------------------------------------------------
            foreach (bool result in new[] { false, true })
            {
                var (world, window, panel) = await Build("Preview.BuildItemSmash", result);
                await Capture(window, result ? "itemsmash-result" : "itemsmash-staged", panel);
                Require(Descendants(panel).OfType<ItemSlotView>().Count(c => c.Name.ToString().StartsWith("smash_bag_") && c.IsVisibleInTree()) == 28,
                    "Disassemble keeps the full Classic bag (28 cells) instead of the docked inventory " + result);
                var results = Descendants(panel).OfType<Control>().Where(c => c.Name.ToString().StartsWith("smash_result_")).ToArray();
                Require(results.Length == 9 && results.All(r => r.IsVisibleInTree()), "Disassemble shows the nine piece sockets");
                var staged = Named<ItemSlotView>(panel, "smash_bag_0");
                if (!result)
                {
                    Require(staged.Look == SlotLook.Selected && !((Button)DetailField(world, "ItemSmashWindow.startBtn")!).Disabled, "The staged item is marked in the bag and Start is offered");
                    var drop = Descendants((Node)DetailField(world, "ItemSmashWindow.socket")!).OfType<KnightOnlineUiClassic.Native.ServiceDropTarget>().Single();
                    Require(drop._CanDropData(Vector2.Zero, new Godot.Collections.Dictionary { { "invFrom", Inventory.GridStart } }), "A carried item can be dragged onto the disassembly socket");
                    Named<Button>(panel, "smash_cancel").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
                    Require((int)DetailField(world, "ItemSmashWindow.slot")! == -1 && staged.Look == SlotLook.Normal, "Cancel clears the bench through the native action");
                    staged._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right }); await Frames(2);
                    Require((int)DetailField(world, "ItemSmashWindow.slot")! == Inventory.GridStart, "Right-click on a bag cell stages it through the client's bag action");
                    await Capture(window, "itemsmash-restaged", panel);
                }
                else Require(results.Count(r => r is Control c && Descendants(c).OfType<TextureRect>().Any(t => t.Texture != null)) == 3, "The disassembly result shows the received pieces");
                Dispose(world, window); await Frames();
            }

            // Character Seal ---------------------------------------------------------------------
            foreach (string page in new[] { "help", "select", "code" })
            {
                var (world, window, panel) = await Build("Preview.BuildCharacterSeal", page == "help" ? "sealing" : page);
                await Frames(4);
                Require(window.GetMeta("items_page").AsString() == page, "Character Seal follows the native page " + page);
                await Capture(window, "char-seal-" + page, panel);
                if (page == "help")
                {
                    // The help page is as tall as its texts: section gap, button gap and bottom inset are the family's.
                    var scrolls = new[] { Named<ScrollContainer>(panel, "cs_help_scroll_0"), Named<ScrollContainer>(panel, "cs_help_scroll_1") };
                    var titles = new[] { Named<Label>(panel, "cs_help_title_0"), Named<Label>(panel, "cs_help_title_1") };
                    var seal = Named<Button>(panel, "cs_seal");
                    Require(titles[1].Position.Y - scrolls[0].GetRect().End.Y == ClassicItemsLayout.SealHelpSectionGap
                        && seal.Position.Y - scrolls[1].GetRect().End.Y == ClassicItemsLayout.SealHelpButtonGap
                        && panel.Size.Y - seal.GetRect().End.Y == ClassicItemsLayout.Inset,
                        $"Character Seal help sizes to its content ({titles[1].Position.Y - scrolls[0].GetRect().End.Y}, {seal.Position.Y - scrolls[1].GetRect().End.Y}, {panel.Size.Y - seal.GetRect().End.Y})");
                    foreach (var scroll in scrolls)
                        Require(scroll.GetChild<Label>(0).Size.Y <= scroll.Size.Y + .5f || scroll.Size.Y >= ClassicItemsLayout.SealHelpTextMax, "Character Seal help text is not clipped");
                }
                else Require(panel.Size == ClassicItemsLayout.Size("char_seal", page), "Character Seal page keeps its Classic size " + page);
                if (page == "select")
                {
                    var slots = Descendants(panel).OfType<Button>().Where(b => b.Name.ToString().StartsWith("cs_slot_")).ToArray();
                    Require(slots.Length == 4 && slots.All(s => s.IsVisibleInTree()), "Four character slots");
                    Require(slots[1].GetThemeColor("font_color") == ClassicDesign.FrameTitle, "The picked slot is highlighted");
                    var free = slots.First(s => !s.Disabled && s != slots[1]);
                    free.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
                    Require((int)DetailField(world, "CharacterSealWindow.Picked")! == Array.IndexOf(slots, free) && free.GetThemeColor("font_color") == ClassicDesign.FrameTitle,
                        "A slot click picks it natively and moves the highlight");
                    Require(slots.Any(s => s.Disabled), "Unpickable slots stay visible but disabled");
                    await Capture(window, "char-seal-select-picked", panel);
                }
                if (page == "code")
                {
                    var keys = Named<GridContainer>(panel, "cs_keys").GetChildren().OfType<Button>().ToArray();
                    Require(keys.Length == 10 && keys.All(k => k.Size == new Vector2(ClassicItemsLayout.SealKey, ClassicItemsLayout.SealKey)), "Ten 40 px keypad keys");
                    keys[0].EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
                    Require(Named<Label>(panel, "cs_code_stars").Text == "*****", "A key press adds a digit natively");
                    Named<Button>(panel, "cs_shuffle").EmitSignal(BaseButton.SignalName.Pressed); await Frames(4);
                    keys = Named<GridContainer>(panel, "cs_keys").GetChildren().OfType<Button>().Where(k => !k.IsQueuedForDeletion()).ToArray();
                    Require(keys.Length == 10 && keys.All(k => k.GetThemeStylebox("normal") is StyleBoxTexture), "Shuffled keys are rebuilt and restyled Classic");
                    await Capture(window, "char-seal-code-shuffled", panel);
                }
                if (page == "help")
                {
                    DetailCall(world, "CharacterSealWindow.BuildTrans");
                    Named<Button>(panel, "cs_seal").EmitSignal(BaseButton.SignalName.Pressed);
                    Require(!window.Visible && ((Control)DetailField(world, "CharacterSealWindow.CsTrans")!).Visible, "Seal opens the native scroll transfer window");
                }
                Dispose(world, window); await Frames();
            }
            {
                var (world, window, panel) = await Build("Preview.BuildCharacterSeal", "trans");
                var inventory = InventoryOf(world);
                inventory[Inventory.GridStart + 2] = new ItemSlot { ItemId = 800111000, Count = 1 };
                Set(world, "CharacterSealWindow.csTransShown", true);
                await Capture(window, "char-seal-trans", panel);
                Require(Descendants(panel).OfType<ItemSlotView>().Count(c => c.Name.ToString().StartsWith("cstrans_bag_") && c.IsVisibleInTree()) == 28, "The scroll transfer keeps the Classic bag");
                Named<ItemSlotView>(panel, "cstrans_bag_2")._GuiInput(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Right }); await Frames(2);
                Require((int)DetailField(world, "CharacterSealWindow.csBagSlot")! == Inventory.GridStart + 2 && Named<ItemSlotView>(panel, "cstrans_bag_2").Look == SlotLook.Selected,
                    "Right-click places the scroll through the client's bag action and marks it");
                await Capture(window, "char-seal-trans-staged", panel);
                Dispose(world, window); await Frames();
            }
            foreach (bool achievements in new[] { false, true })
            {
                var (world, window, panel) = await Build("Preview.BuildCharacterSeal", "sheet");
                if (achievements) { DetailCall(world, "CharacterSealWindow.OnSealedAchievements", new[] { 1, 2, 3 }); await Frames(4); }
                await Capture(window, achievements ? "char-seal-sheet-achievements" : "char-seal-sheet", panel);
                Require(Descendants(panel).OfType<ItemSlotView>().Count(c => c.Name.ToString().StartsWith("sheet_cell_") && c.IsVisibleInTree()) == 42, "The Cypher Ring sheet shows 14 gear and 28 bag cells");
                Require(panel.Size == ClassicItemsLayout.Size("char_seal_sheet", achievements ? "achievements" : ""), "The sheet grows only for the achievements line " + achievements);
                Dispose(world, window); await Frames();
            }

            // EXP Seal ---------------------------------------------------------------------------
            foreach (bool on in new[] { false, true })
            {
                var (world, window, panel) = await Build("Preview.BuildExpSeal");
                Native.Set(Game.Session.ExpSeal, "ExpSealState", on ? ExpSealState.On : ExpSealState.Off);
                await Frames(2);
                await Capture(window, on ? "exp-seal-on" : "exp-seal-off", panel);
                var onButton = Named<Button>(panel, "exp_seal_on"); var offButton = Named<Button>(panel, "exp_seal_off");
                Require(onButton.GetThemeStylebox("normal") == onButton.GetThemeStylebox("pressed") == on
                    && offButton.GetThemeStylebox("normal") == offButton.GetThemeStylebox("pressed") == !on && onButton.Modulate == Colors.White,
                    "The active seal switch shows pressed with the Classic ink " + on);
                Require(Named<TextureRect>(panel, "exp_seal_jar").Texture != null, "The placed jar shows in its inventory socket");
                Dispose(world, window); await Frames();
            }
        }
        else
        {
            // Temporary Item List ----------------------------------------------------------------
            {
                var (world, window, panel) = await Build("Preview.BuildTimedItems");
                await Capture(window, "timed-items", panel);
                var bar = Named<VScrollBar>(panel, "timed_scroll");
                Require(bar.IsVisibleInTree() && bar.MaxValue == 2, "Four temporary items page by three rows with the Classic scroll bar");
                Require(Descendants(window).OfType<Control>().Count(c => c.Name.ToString().StartsWith("timed_row_") && !c.Visible) == 3, "The client's row panels give way to Classic separators");
                Require(Named<Label>(panel, "timed_time_0").Text.Length > 0, "Rows show the remaining time");
                var colours = Enumerable.Range(0, 3).Select(i => Named<Label>(panel, "timed_time_" + i).GetThemeColor("font_color")).ToList();
                bar.Value = 1; await Frames(2);
                Require(Named<ItemSlotView>(panel, "timed_cell_0").IsVisibleInTree() && !Named<ItemSlotView>(panel, "timed_cell_1").IsVisibleInTree(), "The second page shows the last item only");
                colours.Add(Named<Label>(panel, "timed_time_0").GetThemeColor("font_color"));
                Require(colours.Contains(new Color("dc143c")), "An item about to expire keeps the client's urgent colour");
                await Capture(window, "timed-items-page2", panel);
                Dispose(world, window); await Frames();
            }

            // Loot Filter ------------------------------------------------------------------------
            {
                var (world, window, panel) = await Build("Preview.BuildLootFilter");
                await Capture(window, "loot-filter", panel);
                var checksShown = Descendants(panel).OfType<CheckBox>().Where(c => c.IsVisibleInTree()).ToArray();
                Require(checksShown.Length == 11 && checksShown.All(c => c.GetThemeIcon("checked") is AtlasTexture), "Eleven Classic check boxes (switch, four groups, six grades)");
                var rules = Named<ScrollContainer>(panel, "loot_rules_scroll");
                var rows = Descendants(rules).OfType<HBoxContainer>().ToArray();
                Require(rows.Length == 2 && rows.All(r => r.GetChildren().OfType<Button>().Single().Text == "Remove"), "Both item rules list with their Remove action");
                Require(rows.All(r => rules.GetGlobalRect().Encloses(r.GetGlobalRect())), "Rule rows stay inside the list");
                Dispose(world, window); await Frames();
            }

            // Exit menu --------------------------------------------------------------------------
            PluginHost.Ui.ReplaceExitMenu(request => new ClassicExitMenu(request));
            foreach (bool timed in new[] { false, true })
            {
                var world = PreviewFixtures.Prepared(new WorldRoot()); InventoryOf(world).EnsureLength(InventoryConstants.InventoryTotal);
                var esc = (CanvasLayer)DetailCall(world, "Preview.ExitMenu", timed)!;
                var skin = (CanvasLayer)DetailField(world, "GameMenu.exitSkinLayer")!; skin.Reparent(this);
                await Frames();
                var menu = Descendants(skin).OfType<ClassicExitMenu>().Single();
                var view = menu.Menu;
                Require(view.Size == new Vector2(209, 227) && GetViewportRect().Encloses(view.GetGlobalRect()), "The original exit menu art at its original size " + timed);
                foreach (string id in new[] { "btn_chr", "btn_exit", "btn_option", "btn_cancel" })
                    Require(view.Get<BaseButton>(id) is { } b && b.IsVisibleInTree() && b.Size == new Vector2(161, 25), "Original exit button " + id);
                var captions = Descendants(view).OfType<Label>().Where(l => l.Text.Length > 0).Select(l => l.Text).Distinct().ToArray();
                Require(captions.Length == 4, "Each exit button carries the client's caption: " + string.Join(", ", captions));
                Require((menu.Timed != null) == timed, "Temporary items show above the menu only when there are some " + timed);
                if (menu.Timed is { } list)
                {
                    Require(list.GetGlobalRect().End.Y + ClassicExitMenu.Gap == view.GlobalPosition.Y && GetViewportRect().Encloses(list.GetGlobalRect()), "The temporary item list sits above the menu");
                    Require(Descendants(list).OfType<Label>().Any(l => l.Text.Contains("temporary")), "The list carries the client's notice");
                    Bounds(list, "exit-menu-timed");
                }
                string file = prefix + (timed ? "-exit-menu-timed.png" : "-exit-menu.png");
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file); screens.Add(new { state = timed ? "exit-menu-timed" : "exit-menu", file });
                view.Get<BaseButton>("btn_cancel")!.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
                Require(!(bool)DetailField(world, "GameMenu.shown")! && (!GodotObject.IsInstanceValid(skin) || skin.IsQueuedForDeletion()), "Cancel runs the client's row and closes the menu");
                esc.Free(); world.Free(); await Frames();
            }
        }
        System.IO.File.WriteAllText(output + "/" + prefix + (lists ? "-item-lists.json" : "-item-windows.json"),
            JsonSerializer.Serialize(new { nation, checks, screens }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print((lists ? "ITEM_LISTS_AUDIT_OK " : "ITEM_WINDOWS_AUDIT_OK ") + checks.Count + " checks / " + screens.Count + " screens");
        net.Free();
    }
}
