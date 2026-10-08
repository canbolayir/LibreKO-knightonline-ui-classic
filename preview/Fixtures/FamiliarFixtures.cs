using System.Collections;
using System.Reflection;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>
/// Familiar window, trainer and skill bar fixtures, formerly <c>World.UiPreview.PetClassic</c>. The native
/// familiar controls are completed by the plugin (<see cref="FamiliarWindow"/>, <see cref="FamiliarTrainer"/>,
/// <see cref="FamiliarBar"/>); the overrides below run the plugin's part of a native refresh in the same
/// call, as the client did when the familiar pages lived in core, so audits can check the result
/// without waiting for the next frame.
/// </summary>
public static partial class PreviewFixtures
{
    private const int PreviewEggItem = 600001000;
    private const int PreviewKaulItem = 610001000;
    private const int PreviewPetIndex = 1;
    private const int PreviewImageChange = 700017000;
    private const int PreviewEtarothScroll = 700019001;
    private const int PreviewAutomaticLooting = 700012000;
    private const int PreviewTrainerNpc = 13016;
    private static readonly Dictionary<World, Delegate> PetSkillObservers = new();

    public static void EnablePetKeyboardUiPreview(World world)
    {
        var chatType = Native.ClientType("LibreKO.ChatSystem")!;
        var chat = Activator.CreateInstance(chatType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { world }, null)!;
        Native.Set(world, "Chat", chat); Native.Call(chat, "Build"); Native.Call(chat, "DetachNetwork");
        Native.Set(world, "_worldReady", true);
        world.SetProcess(false); world.SetPhysicsProcess(false); world.SetProcessUnhandledInput(false);
        world.SetProcessInput(true);
    }

    public static bool PetKeyboardChatActive(World world) => Native.Get<bool>(Native.Get<object>(world, "Chat")!, "IsActive");

    public static void SetPetKeyboardChat(World world, bool active)
    {
        if (Native.Get<object>(world, "Chat") is { } chat) Native.Call(chat, active ? "Open" : "Close");
    }

    public static CanvasLayer BuildPetClassicUiPreview(World world, int nation) => BuildPetControlUiPreview(world, nation, false);

    public static CanvasLayer BuildPetEquipmentResponseUiPreview(World world, int nation) => BuildPetControlUiPreview(world, nation, true);

    private static CanvasLayer BuildPetControlUiPreview(World world, int nation, bool observeInventory)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded();
        var enter = Net.I.LastEnter; enter.Nation = nation; enter.Name = "FamiliarTester";
        Native.Call(Net.I, "SeedPreviewEnter", enter);
        var ents = Field<IDictionary>(world, "_ents");
        ents[1] = Entity(new() { ["Id"] = 1, ["Name"] = "Kauly", ["IsNpc"] = true, ["NpcType"] = NpcTypes.Pet, ["ModelId"] = 25500, ["Level"] = 12 });
        SetField(world, "_myId", 42); SetField(world, "_selectedId", 2);
        ents[2] = Entity(new() { ["Id"] = 2, ["Name"] = "Gavolt", ["IsNpc"] = true, ["IsMonster"] = true, ["Attackable"] = true, ["Level"] = 10 });
        if (observeInventory)
        {
            Call(world, "InventoryInit");
            var content = Field<Control>(world, "_invContent"); content.Visible = false; world.AddChild(content);
            Call(world, "PetInit");
        }
        else Call(world, "BuildPetPanel");
        ObservePetSkills(world);
        if (!observeInventory) Call(world, "BuildItemTooltip");
        var tooltip = Field<CanvasLayer>(world, "_itemTipLayer"); var layer = Field<CanvasLayer>(world, "_petLayer");
        world.RemoveChild(tooltip); layer.AddChild(tooltip);
        var sheet = new PetSheet { Index = 1, Name = "Kauly", Class = 101, Level = 12,
            Hp = 131, MaxHp = 168, Mp = 170, MaxMp = 190, ExpPercent = 4375,
            Satisfaction = 7240, Attack = 51, Defence = 110, Mode = PetSheet.ModeAttack };
        for (int i = 0; i < sheet.Resists.Length; i++) sheet.Resists[i] = 20 + i * 5;
        sheet.Items[0] = new ItemSlot { ItemId = PreviewAutomaticLooting, Count = 1, Durability = 1 };
        Native.Call(Net.I, "SeedPreviewPet", sheet); Call(world, "OpenPet");
        if (!observeInventory) world.RemoveChild(layer);
        return layer;
    }

    private static object Entity(Dictionary<string, object> values)
    {
        var entity = Activator.CreateInstance(Native.ClientType("LibreKO.World+Ent")!, true)!;
        foreach (var (name, value) in values) Native.Set(entity, name, value);
        return entity;
    }

    /// <summary>The client observes familiar skill refusals once its familiar bar exists; the preview has no bar yet.</summary>
    private static void ObservePetSkills(World world)
    {
        var handler = Delegate.CreateDelegate(typeof(Action<int, int, int, int, short[]>), world, "OnPetSkillMagic");
        Net.I.MagicEvent += (Action<int, int, int, int, short[]>)handler;
        PetSkillObservers[world] = handler;
    }

    public static void PetSkillObserveDispose(World world)
    {
        if (PetSkillObservers.Remove(world, out var handler)) Net.I.MagicEvent -= (Action<int, int, int, int, short[]>)handler;
        SetField(world, "_petSkillGeneration", Field<int>(world, "_petSkillGeneration") + 1);
        Field<IDictionary>(world, "_petSkillCasts").Clear();
    }

    public static void DisposePetEquipmentUiPreview(World world)
    {
        Unsubscribe(world.GetViewport(), "SizeChanged", world, "RefreshInventoryUI");
        foreach (var (evt, method) in new[] { ("ItemMoveResultEvent", "OnItemMoveResult"), ("ItemRemoveResultEvent", "OnItemRemoveResult"),
                     ("InventorySlotEvent", "OnInventorySlotUpdate"), ("ItemGainedEvent", "OnItemGained"),
                     ("InventoryGridRefreshEvent", "OnInventoryGridRefresh"), ("GoldChangeEvent", "OnInventoryGoldChange") })
            Unsubscribe(Net.I, evt, world, method);
        Call(world, "PetDispose"); PetSkillObserveDispose(world);
    }

    private static void Unsubscribe(object source, string evt, World world, string method)
    {
        var info = source.GetType().GetEvent(evt, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        info.RemoveEventHandler(source, Delegate.CreateDelegate(info.EventHandlerType!, world, method));
    }

    public static CanvasLayer BuildPetHatchClassicUiPreview(World world, int nation, bool transform) => BuildPetHatchUiPreview(world, nation, transform, false);

    public static CanvasLayer BuildPetHatchResponseUiPreview(World world, int nation, bool transform) => BuildPetHatchUiPreview(world, nation, transform, true);

    private static CanvasLayer BuildPetHatchUiPreview(World world, int nation, bool transform, bool observeReplies)
    {
        ItemData.EnsureLoaded();
        var enter = Net.I.LastEnter; enter.Nation = nation;
        Native.Call(Net.I, "SeedPreviewEnter", enter);
        if (observeReplies) { Call(world, "PetHatchInit"); Call(world, "BuildItemTooltip"); }
        else Call(world, "BuildPetHatchPanel");
        SeedFamiliarHatchPreview(world, transform); Call(world, "OpenPetHatch", PreviewTrainerNpc);
        // The client selects the first eligible items; the preview states them explicitly.
        SetField(world, "_petHatchSlot", transform ? -1 : Inventory.GridStart + 2);
        SetField(world, "_petTransformSlot", transform ? Inventory.GridStart + 1 : -1);
        SetField(world, "_petScrollSlot", transform ? Inventory.GridStart + 9 : -1);
        Call(world, "RefreshPetPickLooks");
        Field<LineEdit>(world, "_petHatchName").Text = "Kauly"; Call(world, "RefreshPetHatchUI");
        var layer = Field<CanvasLayer>(world, "_petHatchLayer");
        world.RemoveChild(layer); return layer;
    }

    private static void SeedFamiliarHatchPreview(World world, bool transform)
    {
        var inventory = Field<Inventory>(world, "Inv");
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        if (transform)
        {
            Net.I.PetItems[PreviewPetIndex] = new PetItemInfo(PreviewPetIndex, "Kauly", 101, 12, 4200, 7300);
            inventory.ApplySlotUpdate(Inventory.GridStart + 1, new ItemSlot { ItemId = PreviewKaulItem, Count = 1, Durability = 1, UniqueId = PreviewPetIndex });
            inventory.ApplySlotUpdate(Inventory.GridStart + 4, new ItemSlot { ItemId = PreviewImageChange, Count = 1, Durability = 1 });
            inventory.ApplySlotUpdate(Inventory.GridStart + 9, new ItemSlot { ItemId = PreviewEtarothScroll, Count = 1, Durability = 1 });
        }
        else
        {
            inventory.ApplySlotUpdate(Inventory.GridStart + 2, new ItemSlot { ItemId = PreviewEggItem, Count = 1, Durability = 1 });
            inventory.ApplySlotUpdate(Inventory.GridStart + 6, new ItemSlot { ItemId = PreviewEggItem, Count = 1, Durability = 1 });
        }
    }

    public static CanvasLayer BuildPetBarClassicUiPreview(World world)
    {
        var layer = new CanvasLayer { Layer = 64 };
        SetField(world, "_petBarLayer", layer);
        Call(world, "BuildPetBar"); Call(world, "OnPetBarSummoned", Net.I.Pet!);
        return layer;
    }

    /// <summary>The bar's effective default position, including a familiar bar extender's placement.</summary>
    public static Vector2 PetBarDefaultPosition(World world)
    {
        var bar = Field<PanelContainer>(world, "_petBar");
        var layout = bar.GetChildren().OfType<HudLayout>().Single();
        return Native.Get<Func<Vector2>>(layout, "_defaultPosition")!();
    }

    public static void RefreshPetUI(World world)
    {
        Call(world, "RefreshPetUI");
        FamiliarWindow.Of(world)?.Refresh();
    }

    public static void ShowPetSheet(World world, PetSheet? sheet)
    {
        Call(world, "ShowPetSheet", sheet);
        FamiliarWindow.Of(world)?.Refresh(sheet);
    }

    public static void PetBarTick(World world, double now)
    {
        Call(world, "PetBarTick", now);
        if (Field<bool>(world, "_petShown") && FamiliarWindow.Of(world) is { } window) { window.RefreshPortrait(); window.RefreshCooldowns(now); }
    }

    public static void RefreshPetPortrait(World world) => FamiliarWindow.Of(world)?.RefreshPortrait();

    public static void RebuildPetPicks(World world)
    {
        Call(world, "RebuildPetPicks");
        if (FamiliarTrainer.Of(world) is { } trainer) { trainer.Rebuild(); trainer.Refresh(); }
    }

    public static void SelectPetHatchItem(World world, int abs) => FamiliarTrainer.Of(world)!.Select(abs);

    public static void OnPetHatchPressed(World world) => FamiliarTrainer.Of(world)!.RequestConfirmation();

    /// <summary>The trainer's open confirmation, formerly the client's <c>_petHatchNotice</c>.</summary>
    public static Notice? PetHatchNotice(World world) => FamiliarTrainer.Of(world)?.Notice;
}
