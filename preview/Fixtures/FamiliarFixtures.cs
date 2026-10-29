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
    private static readonly Dictionary<WorldRoot, Delegate> PetSkillObservers = new();

    public static void EnablePetKeyboardUiPreview(WorldRoot world)
    {
        var chatType = Native.ClientType("LibreKO.Features.Social.ChatSystem")!;
        var chat = Activator.CreateInstance(chatType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { world }, null)!;
        Native.Set(world, "Chat", chat); Native.Call(chat, "Build"); Native.Call(chat, "DetachNetwork");
        Native.Set(world, "WorldReady", true);
        world.SetProcess(false); world.SetPhysicsProcess(false); world.SetProcessUnhandledInput(false);
        world.SetProcessInput(true);
    }

    public static bool PetKeyboardChatActive(WorldRoot world) => Native.Get<bool>(Native.Get<object>(world, "Chat")!, "IsActive");

    public static void SetPetKeyboardChat(WorldRoot world, bool active)
    {
        if (Native.Get<object>(world, "Chat") is { } chat) Native.Call(chat, active ? "Open" : "Close");
    }

    public static CanvasLayer BuildPetClassicUiPreview(WorldRoot world, int nation) => BuildPetControlUiPreview(world, nation, false);

    public static CanvasLayer BuildPetEquipmentResponseUiPreview(WorldRoot world, int nation) => BuildPetControlUiPreview(world, nation, true);

    private static CanvasLayer BuildPetControlUiPreview(WorldRoot world, int nation, bool observeInventory)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded();
        var enter = Game.Session.Self.LastEnter; enter.Nation = nation; enter.Name = "FamiliarTester";
        Native.Call(Game.Session.Self, "SeedPreviewEnter", enter);
        var ents = Field<EntityRegistry>(world, "EntitySystem.Ents");
        ents[1] = Entity(new() { ["Id"] = 1, ["Name"] = "Kauly", ["IsNpc"] = true, ["NpcType"] = NpcTypes.Pet, ["ModelId"] = 25500, ["Level"] = 12 });
        SetField(world, "Avatar.CharId", 42); SetField(world, "Selection.SelectedId", 2);
        ents[2] = Entity(new() { ["Id"] = 2, ["Name"] = "Gavolt", ["IsNpc"] = true, ["IsMonster"] = true, ["Attackable"] = true, ["Level"] = 10 });
        if (observeInventory)
        {
            Call(world, "InventoryWindow.Build");
            var content = Field<Control>(world, "InventoryWindow.InvContent"); content.Visible = false; world.AddChild(content);
            Call(world, "Pet.Build");
        }
        else Call(world, "Pet.BuildPanel");
        ObservePetSkills(world);
        if (!observeInventory) Call(world, "ItemTooltip.Build");
        var tooltip = Field<CanvasLayer>(world, "ItemTooltip.tipLayer"); var layer = Field<CanvasLayer>(world, "Pet.layer");
        world.RemoveChild(tooltip); layer.AddChild(tooltip);
        var sheet = new PetSheet { Index = 1, Name = "Kauly", Class = 101, Level = 12,
            Hp = 131, MaxHp = 168, Mp = 170, MaxMp = 190, ExpPercent = 4375,
            Satisfaction = 7240, Attack = 51, Defence = 110, Mode = PetMode.Attack };
        for (int i = 0; i < sheet.Resists.Length; i++) sheet.Resists[i] = 20 + i * 5;
        sheet.Items[0] = new ItemSlot { ItemId = PreviewAutomaticLooting, Count = 1, Durability = 1 };
        Native.Set(Game.Session.Pet, "Pet", sheet); Call(world, "Pet.Open");
        if (!observeInventory) world.RemoveChild(layer);
        return layer;
    }

    private static EntityView Entity(Dictionary<string, object> values)
    {
        var entity = Activator.CreateInstance(Native.ClientType("LibreKO.Presentation.EntityView")!, true)!;
        foreach (var (name, value) in values) Native.Set(entity, name, value);
        return (EntityView)entity;
    }

    /// <summary>The client observes familiar skill refusals once its familiar bar exists; the preview has no bar yet.</summary>
    private static void ObservePetSkills(WorldRoot world)
    {
        var handler = Delegate.CreateDelegate(typeof(Action<MagicReply>), Native.Get<object>(world, "PetSkillBar")!, "OnPetSkillMagic");
        Game.Session.Combat.MagicEvent += (Action<MagicReply>)handler;
        PetSkillObservers[world] = handler;
    }

    public static void PetSkillObserveDispose(WorldRoot world)
    {
        if (PetSkillObservers.Remove(world, out var handler)) Game.Session.Combat.MagicEvent -= (Action<MagicReply>)handler;
        SetField(world, "PetSkillBar.petSkillGeneration", Field<int>(world, "PetSkillBar.petSkillGeneration") + 1);
        Field<IDictionary>(world, "PetSkillBar.petSkillCasts").Clear();
    }

    public static void DisposePetEquipmentUiPreview(WorldRoot world)
    {
        Unsubscribe(world.GetViewport(), "SizeChanged", Native.Get<object>(world, "InventoryWindow")!, "RefreshUI");
        Call(world, "InventoryWindow.Dispose");
        Call(world, "Pet.Dispose"); PetSkillObserveDispose(world);
    }

    private static void Unsubscribe(object source, string evt, object target, string method)
    {
        var info = source.GetType().GetEvent(evt, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        info.RemoveEventHandler(source, Delegate.CreateDelegate(info.EventHandlerType!, target, method));
    }

    public static CanvasLayer BuildPetHatchClassicUiPreview(WorldRoot world, int nation, bool transform) => BuildPetHatchUiPreview(world, nation, transform, false);

    public static CanvasLayer BuildPetHatchResponseUiPreview(WorldRoot world, int nation, bool transform) => BuildPetHatchUiPreview(world, nation, transform, true);

    private static CanvasLayer BuildPetHatchUiPreview(WorldRoot world, int nation, bool transform, bool observeReplies)
    {
        ItemData.EnsureLoaded();
        var enter = Game.Session.Self.LastEnter; enter.Nation = nation;
        Native.Call(Game.Session.Self, "SeedPreviewEnter", enter);
        if (observeReplies) { Call(world, "PetHatch.Build"); Call(world, "ItemTooltip.Build"); }
        else Call(world, "PetHatch.BuildPanel");
        SeedFamiliarHatchPreview(world, transform); Call(world, "PetHatch.Open", PreviewTrainerNpc);
        // The client selects the first eligible items; the preview states them explicitly.
        SetField(world, "PetHatch.slot", transform ? -1 : Inventory.GridStart + 2);
        SetField(world, "PetHatch.petTransformSlot", transform ? Inventory.GridStart + 1 : -1);
        SetField(world, "PetHatch.petScrollSlot", transform ? Inventory.GridStart + 9 : -1);
        Call(world, "PetHatch.RefreshPetPickLooks");
        Field<LineEdit>(world, "PetHatch.NameInput").Text = "Kauly"; Call(world, "PetHatch.RefreshUI");
        var layer = Field<CanvasLayer>(world, "PetHatch.layer");
        world.RemoveChild(layer); return layer;
    }

    private static void SeedFamiliarHatchPreview(WorldRoot world, bool transform)
    {
        var inventory = Field<Inventory>(world, "Session.Self.Inventory");
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        if (transform)
        {
            Native.Call(Game.Session.Pet, "Remember", new PetItemInfo(PreviewPetIndex, "Kauly", 101, 12, 4200, 7300));
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

    public static CanvasLayer BuildPetBarClassicUiPreview(WorldRoot world)
    {
        var layer = new CanvasLayer { Layer = 64 };
        SetField(world, "PetSkillBar.PetBarLayer", layer);
        Call(world, "PetSkillBar.BuildPetBar"); Call(world, "PetSkillBar.OnPetBarSummoned", Game.Session.Pet.Pet!);
        return layer;
    }

    /// <summary>The client's familiar plate on its own layer, outside the (process-disabled) audit world.</summary>
    public static CanvasLayer BuildPetPlateClassicUiPreview(WorldRoot world)
    {
        Call(world, "Pet.PlateInit");
        var layer = Field<CanvasLayer>(world, "Pet.plateLayer");
        layer.GetParent()?.RemoveChild(layer);
        return layer;
    }

    /// <summary>The bar's effective default position, including a familiar bar extender's placement.</summary>
    public static Vector2 PetBarDefaultPosition(WorldRoot world)
    {
        var bar = Field<PanelContainer>(world, "PetSkillBar.PetBar");
        var layout = bar.GetChildren().OfType<HudLayout>().Single();
        return Native.Get<Func<Vector2>>(layout, "defaultPosition")!();
    }

    public static void RefreshPetUI(WorldRoot world)
    {
        Call(world, "Pet.RefreshUI");
        FamiliarWindow.Of(world)?.Refresh();
    }

    public static void ShowPetSheet(WorldRoot world, PetSheet? sheet)
    {
        Call(world, "Pet.ShowSheet", sheet);
        FamiliarWindow.Of(world)?.Refresh(sheet);
    }

    public static void PetBarTick(WorldRoot world, double now)
    {
        Call(world, "PetSkillBar.PetBarTick", now);
        if (Field<bool>(world, "Pet.shown") && FamiliarWindow.Of(world) is { } window) { window.RefreshPortrait(); window.RefreshCooldowns(now); }
    }

    public static void RefreshPetPortrait(WorldRoot world) => FamiliarWindow.Of(world)?.RefreshPortrait();

    public static void RebuildPetPicks(WorldRoot world)
    {
        Call(world, "PetHatch.RebuildPetPicks");
        if (FamiliarTrainer.Of(world) is { } trainer) { trainer.Rebuild(); trainer.Refresh(); }
    }

    public static void SelectPetHatchItem(WorldRoot world, int abs) => FamiliarTrainer.Of(world)!.Select(abs);

    public static void OnPetHatchPressed(WorldRoot world) => FamiliarTrainer.Of(world)!.RequestConfirmation();

    /// <summary>The trainer's open confirmation, formerly the client's <c>_petHatchNotice</c>.</summary>
    public static Notice? PetHatchNotice(WorldRoot world) => FamiliarTrainer.Of(world)?.Notice;
}
