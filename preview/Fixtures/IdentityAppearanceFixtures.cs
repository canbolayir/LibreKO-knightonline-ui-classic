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
    private static void Seed(World world, MyInfo me) => Native.Call(Net.I, "SeedPreviewEnter", me);

    private static T Detach<T>(World world, string field) where T : Node
    {
        var node = Field<T>(world, field);
        node.GetParent().RemoveChild(node);
        return node;
    }

    public static Control BuildCouncilRateUiPreview(World world, string view)
    {
        ItemData.EnsureLoaded();
        if (view == "nation")
        {
            var king = new CanvasLayer(); world.AddChild(king); SetField(world, "_kingLayer", king);
            Call(world, "BuildNationTaxRate"); Call(world, "OnKingTariffRead", new KingTariff(KingElection.Success, 3));
            var nation = Detach<HudWindow>(world, "_nationTaxRatePanel");
            NativeIdentity.Prepare(nation, world);
            return nation;
        }
        var siege = new CanvasLayer(); world.AddChild(siege); SetField(world, "_siegeLayer", siege);
        Call(world, "BuildSiegeTaxRate"); SetField(world, "_siegeRates", new SiegeTaxRates(SiegeWarfare.Success, 3, 4, SiegeWarfare.FeeLimit));
        Call(world, "OpenSiegeTaxRate", view == "fee" ? SiegeRateKind.DungeonFee : SiegeRateKind.Moradon);
        var window = Detach<HudWindow>(world, "_siegeTaxRatePanel");
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static void StepNationTaxRate(World world, int delta) => NativeIdentity.StepNationTaxRate(world, delta);

    public static Control BuildClanCreateUiPreview(World world)
    {
        Call(world, "ClanCreateInit");
        var window = Field<HudWindow>(world, "_clanCreatePanel");
        window.Ready += () => Callable.From(() => { Call(world, "OpenClanCreate", $"Name your clan. Founding it costs {ClanTypes.CreationCoins:n0} gold and makes you its chief."); }).CallDeferred();
        window.GetParent().RemoveChild(window);
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static Control BuildNameChangeUiPreview(World world)
    {
        Call(world, "BuildNameChangePanel");
        var window = Field<HudWindow>(world, "_nameChangePanel");
        window.Ready += () => Callable.From(() => { Call(world, "OpenNameChange"); }).CallDeferred();
        window.GetParent().RemoveChild(window);
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static Control BuildDisguiseUiPreview(World world, bool empty)
    {
        Call(world, "BuildDisguisePanel");
        SetField(world, "_disguiseGroups", empty ? Array.Empty<DisguiseGroup>() : new[]
        {
            new DisguiseGroup(30, Enumerable.Range(0, 18).Select(i => new DisguiseForm(i + 1,
                i == 0 ? "Kecoon" : i == 1 ? "Orc Watcher" : "Transformation creature " + (i + 1), 30, 600001 + i, 379091000, 1,
                i == 1 ? "Increase maximum health while transformed." : "", 0)).ToArray()),
            new DisguiseGroup(50, new[] { new DisguiseForm(19, "Giant Golem", 50, 600020, 379091000, 1, "", 0) }),
            new DisguiseGroup(70, new[] { new DisguiseForm(20, "Dark Mare", 70, 600021, 379091000, 1, "", 0) })
        });
        Field<CharacterSheet>(world, "Sheet").ApplyLevel(55, 0, 100, 1000);
        Call(world, "PickDisguiseGroup", 0); SetField(world, "_disguiseShown", true);
        var window = Detach<HudWindow>(world, "_disguisePanel");
        window.Visible = true;
        NativeIdentity.Prepare(window, world);
        return window;
    }

    public static CanvasLayer BuildEquipViewClassicUiPreview(World world)
    {
        ItemData.EnsureLoaded(); Call(world, "BuildItemTooltip"); Call(world, "EquipViewInit");
        var inventory = Field<Inventory>(world, "Inv");
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        inventory[InventoryConstants.InventoryStart] = new ItemSlot { ItemId = 389015000, Count = 100, Durability = 1 };
        var layer = Field<CanvasLayer>(world, "_equipViewLayer");
        Field<CanvasLayer>(world, "_itemTipLayer").Reparent(layer, false);
        world.RemoveChild(layer);
        NativeEquipView.Prepare(Field<HudWindow>(world, "_equipViewPanel"), world);
        return layer;
    }

    public static void RequestEquipmentView(World world, string name) => NativeEquipView.Request(world, name);

    /// <summary>A reply reaches the client's handler and then the plugin's, as the network event does.</summary>
    public static void OnEquipmentView(World world, Net.EquipmentViewResult result, Net.EquipmentView view)
    {
        Call(world, "OnEquipmentView", result, view);
        NativeEquipView.Received(world, result, view);
    }

    /// <summary>The fork placed the tooltip in its own viewport; the audit world itself is not in the tree.</summary>
    public static void UpdateInventoryTooltip(World world)
    {
        var panel = Field<Control>(world, "_itemTipPanel");
        if (panel == null || !panel.Visible || panel.GetViewport() is not { } viewport) return;
        var visible = viewport.GetVisibleRect().Size;
        var p = viewport.GetMousePosition() + new Vector2(16, 16);
        var size = panel.Size;
        if (size.X <= 1 || size.Y <= 1) size = panel.GetCombinedMinimumSize();
        p.X = Mathf.Clamp(p.X, 8, Mathf.Max(8, visible.X - size.X - 8));
        p.Y = Mathf.Clamp(p.Y, 8, Mathf.Max(8, visible.Y - size.Y - 8));
        panel.Position = p;
    }

    public static Net.EquipmentView EquipViewPreviewSnapshot(string name, int nation, bool empty = false, bool maximum = false)
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
        return new Net.EquipmentView(name, nation == 1 ? 101 : 201, nation == 1 ? 1 : 11, 0, 0, 83, 5, nation,
            maximum ? 32767 : 7836, maximum ? 32767 : 7696, 255, maximum ? 255 : 13, 255, 10, 60, 8, 50, 0, 50, 0,
            maximum ? 32767 : 2103, maximum ? 32767 : 238, 120, 90, 140, 50, 130, 170, worn);
    }

    public static CanvasLayer BuildGenderClassicUiPreview(World world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "Tester10"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 1; me.Hair = (2 << 24) | 0x5A3820;
        Seed(world, me);
        Call(world, "BuildGenderPanel"); Call(world, "OpenGenderChange");
        var layer = Detach<CanvasLayer>(world, "_genderLayer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "_genderPanel"), world);
        return layer;
    }

    public static CanvasLayer BuildBeautyClassicUiPreview(World world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "AppearanceTester2026"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 0; me.Hair = 0x5A3820;
        Seed(world, me);
        SetField(world, "_selfRace", me.Race); SetField(world, "_selfFace", me.Face); SetField(world, "_selfHair", me.Hair);
        Call(world, "ChangeHairInit"); Call(world, "OpenChangeHair");
        var layer = Detach<CanvasLayer>(world, "_changeHairLayer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "_changeHairPanel"), world);
        return layer;
    }

    public static void ShowChangeHairLook(World world) => NativeAppearance.ShowBeautyLook(world);

    /// <summary>The fork's boolean pending flag became the fix branch's request tracker.</summary>
    public static bool ChangeHairPending(Net net) =>
        Native.Get<object>(net, "_changeHairRequest") is { } request ? Native.Get<bool>(request, "Pending") : Native.Get<bool>(net, "_changeHairPending");

    public static CanvasLayer BuildNationTransferClassicUiPreview(World world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "AppearanceTester2026"; me.Nation = nation; me.Class = nation == 1 ? 112 : 206;
        me.Race = nation == 1 ? 2 : 12; me.Face = 0; me.Hair = 0x5A3820;
        Seed(world, me);
        Call(world, "NationTransferInit");
        var layer = Detach<CanvasLayer>(world, "_transferLayer");
        NativeAppearance.Prepare(Field<HudWindow>(world, "_transferPanel"), world);
        return layer;
    }

    public static CanvasLayer BuildRebirthClassicUiPreview(World world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "RebirthTester2026"; me.Nation = nation;
        Seed(world, me);
        Net.I.Sheet.SeedProgress(83, 0, 1);
        Net.I.Sheet.SeedRebirth(4, 3, 1, 2, 1, 1);
        Call(world, "RebirthInit"); Call(world, "OpenRebirthPicker");
        var layer = Detach<CanvasLayer>(world, "_rebirthLayer");
        NativeRebirth.Prepare(Field<HudWindow>(world, "_rebirthPanel"), world);
        return layer;
    }

    public static void RefreshRebirthUI(World world)
    {
        Native.Call(world, "RefreshRebirthUI");
        NativeRebirth.Refresh(world);
    }

    public static void OnRebirthPressed(World world) => NativeRebirth.Pressed(world);

    public static void EditRebirthPoint(World world, int row, bool add) => NativeRebirth.EditPoint(world, row, add);

    public static CanvasLayer BuildCapeClassicUiPreview(World world, int nation)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "CapeTester2026"; me.Nation = nation; me.Race = nation == 1 ? 2 : 12;
        me.Class = nation == 1 ? 112 : 206; me.Hair = 0x5A3820;
        Seed(world, me);
        Native.Call(Net.I, "SeedPreviewClan", Native.Call(typeof(World), "PreviewClan", true, ClanTypes.Royal1));
        Call(world, "CapeInit"); Call(world, "ToggleCape");
        var layer = Detach<CanvasLayer>(world, "_capeLayer");
        NativeCape.Prepare(Field<HudWindow>(world, "_capePanel"), world);
        return layer;
    }

    public static void ToggleCape(World world) => NativeCape.Toggle(world);

    public static void ShowCapePattern(World world, int pattern) => NativeCape.ShowPattern(world, pattern);

    public static void SelectCape(World world, int capeId) => NativeCape.Select(world, capeId);

    public static void UpdateCapeGate(World world)
    {
        Native.Call(world, "UpdateCapeGate");
        NativeCape.Gate(world);
    }
}
