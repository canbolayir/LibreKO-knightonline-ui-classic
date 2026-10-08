using System.Collections;
using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// NPC dialogue support for the Classic skins: the talking NPC's portrait model factory, the quest
/// state caption at the top of a quest view and the selection marker on reward choices.
/// </summary>
public static class NativeNpc
{
    private const string RewardChoiceTooltip = "Pick this reward";
    private const string CaptionMarker = "classic_quest_caption";

    private sealed class PortraitState
    {
        public GameNpcPortrait? Portrait;
        public object? Actor;
        public int Model, Type, NpcId;
        public int[] Gear = Array.Empty<int>();
    }

    private static readonly ConditionalWeakTable<World, PortraitState> _portraits = new();
    private static readonly ConditionalWeakTable<World, object> _subscribed = new();

    /// <summary>
    /// The portrait of the NPC the open dialogue belongs to. A delayed dialogue reply can outlive the
    /// transient interaction-range target, so the vendor NPC is the fallback.
    /// </summary>
    public static GameNpcPortrait? DialogPortrait(World world)
    {
        bool shown = Native.Get<bool>(world, "_npcDialogShown") || Native.Get<HudWindow>(world, "_npcPanel") is { Visible: true };
        if (!shown) return null;
        int talk = Native.Get<int>(world, "_npcTalkId");
        int target = talk >= 0 ? talk : Native.Get<int>(world, "_vendorNpcId");
        if (Native.Get<IDictionary>(world, "_ents") is not { } ents || !ents.Contains(target) || ents[target] is not { } npc) return null;
        if (!Native.Get<bool>(npc, "IsNpc") || Native.Get<bool>(npc, "Dead")) return null;
        string name = Native.Get<string>(npc, "Name") ?? "";
        int level = Native.Get<int>(npc, "Level"), modelId = Native.Get<int>(npc, "ModelId");
        int npcType = Native.Get<int>(npc, "NpcType"), npcId = Native.Get<int>(npc, "NpcId");
        int[] current = Native.Get<int[]>(npc, "Gear") ?? Array.Empty<int>();
        var state = _portraits.GetOrCreateValue(world);
        if (ReferenceEquals(state.Actor, npc) && state.Portrait != null && state.Portrait.Name == name && state.Portrait.Level == level
            && state.Model == modelId && state.Type == npcType && state.NpcId == npcId && state.Gear.SequenceEqual(current)) return state.Portrait;
        int[] gear = (int[])current.Clone();
        var unarmed = Native.Get<HashSet<int>>(typeof(World), "NoWeaponNpcIds");
        if (npcType == NpcTypes.FixedPose || unarmed?.Contains(npcId) == true) Array.Clear(gear);
        string appearance = $"{modelId}:" + string.Join(',', gear);
        state.Actor = npc; state.Model = modelId; state.Type = npcType; state.NpcId = npcId; state.Gear = (int[])current.Clone();
        var weakWorld = new WeakReference<World>(world);
        state.Portrait = new GameNpcPortrait(appearance, name, level, () =>
        {
            if (!weakWorld.TryGetTarget(out var w) || !GodotObject.IsInstanceValid(w)) return null;
            Node3D? model;
            if (Native.Call(w, "ResolveMobScene", modelId) is PackedScene scene)
            {
                model = scene.Instantiate<Node3D>();
                Native.Call(typeof(World), "ForceDoubleSidedOnce", model, scene.ResourcePath);
            }
            else
            {
                var body = Native.Get<Node3D>(npc, "Body");
                string modelNode = Native.Get<string>(typeof(World), "ModelNodeName") ?? "Model";
                var visual = body != null && GodotObject.IsInstanceValid(body) ? body.GetNodeOrNull<Node3D>(modelNode) : null;
                var source = visual?.GetChildren().OfType<Node3D>().FirstOrDefault();
                model = source?.Duplicate((int)Node.DuplicateFlags.UseInstantiation) as Node3D;
                if (model == null) return null;
                model.Transform = Transform3D.Identity;
            }
            if (gear.Any(id => id > 0)) Native.Call(w, "AttachWeapons", model, gear, npcType, npcId);
            return model;
        });
        return state.Portrait;
    }

    /// <summary>Follows quest views for one world so each NPC quest view gets its state caption.</summary>
    public static void Prepare(HudWindow window, World world)
    {
        if (Net.I == null || _subscribed.TryGetValue(world, out _)) return;
        var weakWorld = new WeakReference<World>(world);
        var net = Net.I;
        Action<QuestView>? handler = null;
        handler = view =>
        {
            if (!weakWorld.TryGetTarget(out var w) || !GodotObject.IsInstanceValid(w)) { net.QuestViewEvent -= handler; return; }
            if (view.Notification || !view.Open || view.Page == QuestPageKind.Conversation) return;
            QuestViewShown(w, view);
            Callable.From(() => { if (GodotObject.IsInstanceValid(w)) QuestViewShown(w, view); }).CallDeferred();
        };
        net.QuestViewEvent += handler;
        _subscribed.Add(world, handler);
    }

    /// <summary>
    /// Completes a quest view the client has just built: a state caption heads the content and every
    /// reward choice row carries whether it is the pending selection.
    /// </summary>
    public static void QuestViewShown(World world, QuestView view)
    {
        if (Native.Get<VBoxContainer>(world, "_npcQuestContent") is not { } content || !GodotObject.IsInstanceValid(content)) return;
        if (content.GetChildCount() == 0 || content.GetChild(0) is not RichTextLabel) return;
        var caption = UiTheme.Text(view.StateLabel, 13, UiTheme.Gold);
        caption.SetMeta("quest_status", (int)view.State);
        caption.SetMeta(CaptionMarker, view.QuestId);
        content.AddChild(caption);
        content.MoveChild(caption, 0);
        var rows = Native.Descendants(content).OfType<Control>().Where(c => c.TooltipText == RewardChoiceTooltip).ToArray();
        if (!view.CanClaim || rows.Length == 0) return;
        void Paint()
        {
            int chosen = Native.Get<int>(world, "_questRewardChoice");
            for (int index = 0; index < rows.Length; index++)
                if (GodotObject.IsInstanceValid(rows[index])) rows[index].SetMeta("quest_reward_selected", index == chosen);
        }
        foreach (var row in rows)
            row.GuiInput += ev => { if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && GodotObject.IsInstanceValid(world)) Paint(); };
        Paint();
    }

    /// <summary>Marks the quest notification's state caption with its quest state.</summary>
    public static void PrepareNotification(HudWindow window, World world)
    {
        var body = window.Body;
        void Mark(Node node)
        {
            if (node is not Label label || label.GetParent() != body || label.HasMeta("quest_status")) return;
            var views = Native.Get<List<QuestView>>(world, "_questNotifications");
            int index = Native.Get<int>(world, "_questNotificationIndex");
            if (views == null || index < 0 || index >= views.Count) return;
            label.SetMeta("quest_status", (int)views[index].State);
            label.AddThemeFontSizeOverride("font_size", 13);
        }
        foreach (var child in body.GetChildren()) Mark(child);
        body.ChildEnteredTree += Mark;
    }
}

public partial class ClientGame
{
    private GameNpcPortrait? NpcPortraitSource => ActiveWorld is { } world ? NativeNpc.DialogPortrait(world) : null;
}
