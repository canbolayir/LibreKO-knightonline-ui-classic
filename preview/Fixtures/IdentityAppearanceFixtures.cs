using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using KnightOnlineUiClassic.Native;

/// <summary>
/// Identity, council, equipment view, appearance, rebirth and cape fixtures the fork kept in the client.
/// Each builds the client's own window, then runs this group's preparer as the client's window flow
/// would before the skin is applied.
/// </summary>
public static partial class PreviewFixtures
{
    private static void Seed(WorldRoot world, MyInfo me) => Native.Call(Game.Session.Self, "SeedPreviewEnter", me);

    private static T Detach<T>(WorldRoot world, string field) where T : Node
    {
        var node = Field<T>(world, field);
        node.GetParent().RemoveChild(node);
        return node;
    }

    public static Control BuildCouncilRateUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded();
        if (view == "nation")
        {
            var king = new CanvasLayer(); world.AddChild(king); SetField(world, "KingTreasury.layer", king);
            Call(world, "KingTreasury.BuildNationTaxRate"); Call(world, "KingTreasury.OnTariffRead", new KingTariff(KingWire.Success, 3));
            var nation = Detach<HudWindow>(world, "KingTreasury.NationTaxRatePanel");
            NativeIdentity.Prepare(nation, world);
            return nation;
        }
        var siege = new CanvasLayer(); world.AddChild(siege); SetField(world, "Siege.layer", siege);
        Call(world, "Siege.BuildTaxRate"); SetField(world, "Siege.rates", new SiegeTaxRates(SiegeWire.Success, 3, 4, SiegeWarfare.FeeLimit));
        Call(world, "Siege.OpenTaxRate", view == "fee" ? SiegeRateKind.DungeonFee : SiegeRateKind.Moradon);
        var window = Detach<HudWindow>(world, "Siege.TaxRatePanel");
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static void StepNationTaxRate(WorldRoot world, int delta) => NativeIdentity.StepNationTaxRate(world, delta);

    public static Control BuildClanCreateUiPreview(WorldRoot world)
    {
        Call(world, "ClanCreate.Build");
        var window = Field<HudWindow>(world, "ClanCreate.panel");
        window.Ready += () => Callable.From(() => { Call(world, "ClanCreate.Open", $"Name your clan. Founding it costs {ClanTypes.CreationCoins:n0} gold and makes you its chief."); }).CallDeferred();
        window.GetParent().RemoveChild(window);
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static Control BuildNameChangeUiPreview(WorldRoot world) => BuildNameChangeUiPreview(world, false);

    /// <summary>The clan ID change (opened by the clan rename reply) reuses the rename window.</summary>
    public static Control BuildClanNameChangeUiPreview(WorldRoot world) => BuildNameChangeUiPreview(world, true);

    private static Control BuildNameChangeUiPreview(WorldRoot world, bool clan)
    {
        Call(world, "NameChange.BuildPanel");
        var window = Field<HudWindow>(world, "NameChange.panel");
        window.Ready += () => Callable.From(() => { Call(world, "NameChange.Open", clan); }).CallDeferred();
        window.GetParent().RemoveChild(window);
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static Control BuildDisguiseUiPreview(WorldRoot world, bool empty)
    {
        Call(world, "DisguiseWindow.BuildPanel");
        SetField(world, "DisguiseWindow.groups", empty ? Array.Empty<DisguiseGroup>() : new[]
        {
            new DisguiseGroup(30, Enumerable.Range(0, 18).Select(i => new DisguiseForm(i + 1,
                i == 0 ? "Kecoon" : i == 1 ? "Orc Watcher" : "Transformation creature " + (i + 1), 30, 600001 + i, 379091000, 1,
                i == 1 ? "Increase maximum health while transformed." : "", 0)).ToArray()),
            new DisguiseGroup(50, new[] { new DisguiseForm(19, "Giant Golem", 50, 600020, 379091000, 1, "", 0) }),
            new DisguiseGroup(70, new[] { new DisguiseForm(20, "Dark Mare", 70, 600021, 379091000, 1, "", 0) })
        });
        Field<CharacterSheet>(world, "Session.Self.Sheet").ApplyLevel(55, 0, 100, 1000);
        Call(world, "DisguiseWindow.PickGroup", 0); SetField(world, "DisguiseWindow.shown", true);
        var window = Detach<HudWindow>(world, "DisguiseWindow.Panel");
        window.Visible = true;
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static CanvasLayer BuildEquipViewClassicUiPreview(WorldRoot world)
    {
        ItemData.EnsureLoaded(); Call(world, "ItemTooltip.Build"); Call(world, "EquipView.Build");
        var inventory = Field<Inventory>(world, "Session.Self.Inventory");
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        inventory[InventoryConstants.InventoryStart] = new ItemSlot { ItemId = 389015000, Count = 100, Durability = 1 };
        var layer = Field<CanvasLayer>(world, "EquipView.layer");
        Field<CanvasLayer>(world, "ItemTooltip.tipLayer").Reparent(layer, false);
        world.RemoveChild(layer);
        NativeEquipView.Prepare(Field<HudWindow>(world, "EquipView.Panel"), world);
        return layer;
    }

    public static void RequestEquipmentView(WorldRoot world, string name) => NativeEquipView.Request(world, name);

    /// <summary>A reply reaches the client's handler and then the plugin's, as the network event does.</summary>
    public static void OnEquipmentView(WorldRoot world, EquipmentViewResult result, EquipmentView view)
    {
        Call(world, "EquipView.OnEquipmentView", result, view);
        NativeEquipView.Received(world, result, view);
    }

    /// <summary>The fork placed the tooltip in its own viewport; the audit world itself is not in the tree.</summary>
    public static void UpdateInventoryTooltip(WorldRoot world)
    {
        var panel = Field<Control>(world, "ItemTooltip.TipPanel");
        if (panel == null || !panel.Visible || panel.GetViewport() is not { } viewport) return;
        var visible = viewport.GetVisibleRect().Size;
        var p = viewport.GetMousePosition() + new Vector2(16, 16);
        var size = panel.Size;
        if (size.X <= 1 || size.Y <= 1) size = panel.GetCombinedMinimumSize();
        p.X = Mathf.Clamp(p.X, 8, Mathf.Max(8, visible.X - size.X - 8));
        p.Y = Mathf.Clamp(p.Y, 8, Mathf.Max(8, visible.Y - size.Y - 8));
        panel.Position = p;
    }

    public static EquipmentView EquipViewPreviewSnapshot(string name, int nation, bool empty = false, bool maximum = false)
    {
        var worn = new List<(int Slot, int ItemId, short Durability, byte Flag)>();
        if (!empty)
        {
            int[] gear = { 1310610106, 208003000, 1310610106, 330310000, 208001000, 0, 156210008, 0, 136710000, 320410011, 208002000, 320410011, 208004000, 208005000 };
            for (int i = 0; i < InventoryConstants.SlotMax; i++) worn.Add((i, gear[i], (short)(i == 6 ? 0 : 12000), (byte)(i == 6 ? ItemFlag.Sealed : 0)));
            int[] positions = { 8, 1, 7, 2, 4, 3, 9, 0, 5 };
            int[] codes = { 112, 107, 111, 100, 105, 100, 113, 110, 114 };
            for (int i = 0; i < positions.Length; i++)
            {
                int id = ItemData.All().Where(item => item.Slot == codes[i] && ResourceLoader.Exists($"res://assets/items/icons/{(ItemData.ExtFor(item.Id) is { Icon: > 0 } ext ? ext.Icon : item.Icon)}.png")).OrderBy(item => item.Id).FirstOrDefault()?.Id ?? 0;
                worn.Add((InventoryConstants.CospreStart + positions[i], id, 1, 0));
            }
        }
        return new EquipmentView(name, nation == 1 ? 101 : 201, nation == 1 ? 1 : 11, 0, 0, 83, 5, nation,
            maximum ? 32767 : 7836, maximum ? 32767 : 7696, 255, maximum ? 255 : 13, 255, 10, 60, 8, 50, 0, 50, 0,
            maximum ? 32767 : 2103, maximum ? 32767 : 238, 120, 90, 140, 50, 130, 170, worn);
    }

    public static CanvasLayer BuildGenderClassicUiPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter;
        me.Name = "Tester10"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 1; me.Hair = (2 << 24) | 0x5A3820;
        Seed(world, me);
        Call(world, "GenderChangeWindow.BuildPanel"); Call(world, "GenderChangeWindow.Open");
        var layer = Detach<CanvasLayer>(world, "GenderChangeWindow.layer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "GenderChangeWindow.Panel"), world);
        return layer;
    }

    public static CanvasLayer BuildBeautyClassicUiPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter;
        me.Name = "AppearanceTester2026"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 0; me.Hair = 0x5A3820;
        Seed(world, me);
        SetField(world, "Session.Self.Race", me.Race); SetField(world, "Session.Self.Face", me.Face); SetField(world, "Session.Self.Hair", me.Hair);
        Call(world, "ChangeHair.Build"); Call(world, "ChangeHair.Open");
        var layer = Detach<CanvasLayer>(world, "ChangeHair.layer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "ChangeHair.Panel"), world);
        return layer;
    }

    /// <summary>The client's request tracker: one outstanding Beauty Shop request at a time.</summary>
    public static bool ChangeHairPending(Net net) =>
        Native.Get<object>(Game.Session.ChangeHair, "Request") is { } request && Native.Get<bool>(request, "Pending");

    /// <summary>The hair palette the Beauty Shop built on demand, prepared as the client's window flow would.</summary>
    public static HudWindow? PrepareHairPalette(WorldRoot world)
    {
        if (Native.Get<HudWindow>(world, "ChangeHair.hairPalette") is not { } palette) return null;
        NativeAppearance.Prepare(palette, world);
        return palette;
    }

    public static CanvasLayer BuildNationTransferClassicUiPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter;
        me.Name = "AppearanceTester2026"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 0; me.Hair = 0x5A3820;
        Seed(world, me);
        Call(world, "NationTransfer.Build");
        var layer = Detach<CanvasLayer>(world, "NationTransfer.transferLayer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "NationTransfer.TransferPanel"), world);
        return layer;
    }

    public static CanvasLayer BuildRebirthClassicUiPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter;
        me.Name = "RebirthTester2026"; me.Nation = nation;
        Seed(world, me);
        Game.Session.Self.Sheet.SeedProgress(83, 0, 1);
        Game.Session.Self.Sheet.SeedRebirth(4, 3, 1, 2, 1, 1);
        Call(world, "Rebirth.Build"); Call(world, "Rebirth.OpenPicker");
        var layer = Detach<CanvasLayer>(world, "Rebirth.layer");
        NativeRebirth.Prepare(Field<HudWindow>(world, "Rebirth.panel"), world);
        return layer;
    }

    public static void RefreshRebirthUI(WorldRoot world)
    {
        Native.Call(world, "Rebirth.RefreshUI");
        NativeRebirth.Refresh(world);
    }

    public static void OnRebirthPressed(WorldRoot world) => NativeRebirth.Pressed(world);

    public static void EditRebirthPoint(WorldRoot world, int row, bool add) => NativeRebirth.EditPoint(world, row, add);

    public static CanvasLayer BuildCapeClassicUiPreview(WorldRoot world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter;
        me.Name = "CapeTester2026"; me.Nation = nation; me.Race = nation == 1 ? 2 : 12;
        me.Class = nation == 1 ? 112 : 206; me.Hair = 0x5A3820;
        Seed(world, me);
        Native.Call(Game.Session.Clan, "SeedPreview", Native.Call(Native.StaticOwner("LibreKO.WorldPreview"), "PreviewClan", true, ClanTypes.Royal1));
        Call(world, "CapeWindow.Build"); Call(world, "CapeWindow.Toggle");
        var layer = Detach<CanvasLayer>(world, "CapeWindow.layer");
        NativeCape.Prepare(Field<HudWindow>(world, "CapeWindow.Panel"), world);
        return layer;
    }

    public static void ToggleCape(WorldRoot world) => NativeCape.Toggle(world);

    public static void ShowCapePattern(WorldRoot world, int pattern) => NativeCape.ShowPattern(world, pattern);

    public static void SelectCape(WorldRoot world, int capeId) => NativeCape.Select(world, capeId);

    public static void UpdateCapeGate(WorldRoot world)
    {
        Native.Call(world, "CapeWindow.UpdateGate");
        NativeCape.Gate(world);
    }
}
