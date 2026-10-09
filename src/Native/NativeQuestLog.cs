using System.Text.RegularExpressions;
using Godot;
using LibreKO;
using LibreKO.Network;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Quest Details rewards as Classic item rows: fixed (or received) rewards, then either the pending
/// choice or the alternatives with a hint. The client's own reward tiles stay alive in a hidden
/// holder and only signal when the selected quest's rewards were rebuilt.
/// </summary>
public static class NativeQuestLog
{
    public const string RewardBoxName = "classic_quest_rewards";
    private const int QuestStateCompleted = 2;
    private const int RewardSeparation = 5, CaptionSize = 13, HintSize = 12;

    private static readonly Dictionary<int, QuestReceipt> _receipts = new();
    private static Net? _listening;

    /// <summary>
    /// Remembers what each turn-in granted. The client keeps only the pending choice, so the Classic log
    /// records receipts itself to show "Received rewards" while the quest stays completed.
    /// </summary>
    private static void ListenForReceipts(World world)
    {
        if (Net.I is not { } net || ReferenceEquals(net, _listening)) return;
        _listening = net;
        _receipts.Clear();
        net.QuestReceiptEvent += receipt =>
        {
            Record(receipt);
            if (GodotObject.IsInstanceValid(world)) Callable.From(() => Native.TryCall(world, "RefreshQuestDetail", out _)).CallDeferred();
        };
    }

    /// <summary>Records what a turn-in granted; the preview calls this when it feeds receipts directly.</summary>
    public static void Record(QuestReceipt receipt) => _receipts[receipt.QuestId] = receipt;

    private static bool TryReceipt(object selection, int questId, out QuestReceipt? receipt)
    {
        if (Native.HasMethod(selection, "Received"))
        {
            var args = new object?[] { questId, null };
            bool found = Native.Call(selection, "Received", args) is true;
            receipt = args[1] as QuestReceipt;
            return found;
        }
        return _receipts.TryGetValue(questId, out receipt);
    }

    public static void Prepare(HudWindow window, World world)
    {
        ListenForReceipts(world);
        if (Native.Get<HBoxContainer>(world, "_questRewardBox") is not { } tiles || tiles.GetParent() is not Control detail) return;
        if (detail.GetNodeOrNull(RewardBoxName) != null) return;
        var optionTitle = Native.Get<Label>(world, "_questRewardOptionTitle");
        var optionTiles = Native.Get<HBoxContainer>(world, "_questRewardOptionBox");
        var rows = new VBoxContainer { Name = RewardBoxName };
        rows.AddThemeConstantOverride("separation", RewardSeparation);
        int index = tiles.GetIndex();
        var holder = new Control { Name = "native_quest_rewards", Visible = false };
        detail.AddChild(rows);
        detail.MoveChild(rows, index);
        detail.AddChild(holder);
        foreach (var native in new Control?[] { tiles, optionTitle, optionTiles })
            if (native != null && native.GetParent() == detail) native.Reparent(holder);
        object? shown = null;
        void Rebuild(Node _)
        {
            if (!GodotObject.IsInstanceValid(world) || !GodotObject.IsInstanceValid(rows)) return;
            var signature = Signature(world);
            if (Equals(signature, shown)) return;
            shown = signature;
            Fill(world, rows);
        }
        foreach (var box in new Node?[] { tiles, optionTiles })
            if (box != null) { box.ChildEnteredTree += Rebuild; box.ChildExitingTree += Rebuild; }
        Fill(world, rows);
    }

    private static object Signature(World world)
    {
        int questId = Native.Get<int>(world, "_questSelected");
        int state = questId >= 0 ? (int)(Native.Call(world, "QuestStateOf", questId) ?? 0) : 0;
        var selection = Native.Get<object>(world, "_questRewardSelection");
        object? receipt = null, pending = null;
        if (selection != null)
        {
            if (TryReceipt(selection, questId, out var granted)) receipt = granted;
            var args = new object?[] { questId, null };
            if (Native.Call(selection, "Chosen", args) is true) pending = args[1];
        }
        var views = Native.Get<Dictionary<int, QuestView>>(world, "_questViews");
        QuestView? view = null;
        views?.TryGetValue(questId, out view);
        return (questId, state, receipt, pending, view);
    }

    private static void Fill(World world, VBoxContainer box)
    {
        foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); }
        var title = Native.Get<Label>(world, "_questRewardTitle");
        var (questId, state, receiptValue, pendingValue, view) = ((int, int, object?, object?, QuestView?))Signature(world);
        if (questId < 0) return;
        if (title != null) title.Text = "Rewards";
        if (state == QuestStateCompleted && receiptValue is QuestReceipt receipt)
        {
            if (title != null) title.Text = "Received rewards";
            foreach (var reward in receipt.Granted) box.AddChild(Tile(world, reward.ItemId, reward.Count));
        }
        else if (view != null)
        {
            foreach (var reward in view.Transfers.Where(t => !t.Take)) box.AddChild(Tile(world, reward.DisplayItemId, reward.Count));
            if (view.Options.Length > 0)
            {
                if (pendingValue is QuestTransfer selected && view.Options.Contains(selected))
                {
                    box.AddChild(UiTheme.Text("Selected reward", CaptionSize, UiTheme.Gold));
                    box.AddChild(UiTheme.Text("Pending confirmation at the quest NPC.", HintSize, UiTheme.TextLo));
                    box.AddChild(Tile(world, selected.DisplayItemId, selected.Count));
                }
                else
                {
                    box.AddChild(UiTheme.Text("Reward options", CaptionSize, UiTheme.Gold));
                    var hint = UiTheme.Text(state == QuestStateCompleted
                        ? "One option was awarded when this quest was turned in."
                        : "Choose one when turning in this quest.", HintSize, UiTheme.TextLo);
                    hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    hint.CustomMinimumSize = new Vector2(1, 0);
                    box.AddChild(hint);
                    foreach (var option in view.Options) box.AddChild(Tile(world, option.DisplayItemId, option.Count));
                }
            }
        }
        else if (Native.Call(world, "QuestRewards", questId) is IEnumerable<(int ItemId, int Count)> rewards)
            foreach (var reward in rewards) box.AddChild(Tile(world, reward.ItemId, reward.Count));
        if (title != null) title.Visible = box.GetChildCount() > 0;
    }

    private static Control Tile(World world, int itemId, int count)
    {
        var panel = UiTheme.Section();
        var name = Native.Call(world, "QuestRewardName", itemId) as string ?? "";
        if (Native.Call(world, "QuestItemRow", itemId, name, count.ToString("n0"), UiTheme.GoldBright) is Control row)
        {
            row.SetMeta("quest_reward_item_id", itemId);
            row.SetMeta("quest_reward_count", count);
            panel.AddChild(row);
        }
        return panel;
    }

    /// <summary>Shows planned stat points as allocated and remaining totals.</summary>
    public static void PreparePresets(HudWindow window, World world)
    {
        NativeWindows.Sync(window, () =>
        {
            if (Native.Get<Label>(world, "_presetStatPointsLbl") is not { } label) return;
            var plan = Regex.Match(label.Text, @"^Planned (\d+) of (\d+) stat point\(s\)$");
            if (!plan.Success) return;
            int spent = int.Parse(plan.Groups[1].Value), total = int.Parse(plan.Groups[2].Value);
            label.Text = $"Allocated {spent} / {total}    Remaining {total - spent}";
        });
    }
}
