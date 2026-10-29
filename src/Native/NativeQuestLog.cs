using System.Text.RegularExpressions;
using Godot;
using LibreKO;
using LibreKO.Domain;
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
    private static IGameConnection? _listening;
    private static NativeHooks.Hook? _receiptHook;
    private static WeakReference<WorldRoot>? _receiptWorld;

    /// <summary>
    /// Remembers what each turn-in granted. The client keeps only the pending choice, so the Classic log
    /// records receipts itself to show "Received rewards" while the quest stays completed. The network
    /// session outlives the World, so the receipts follow the session and refresh the latest World.
    /// </summary>
    private static void ListenForReceipts(WorldRoot world)
    {
        _receiptWorld = new WeakReference<WorldRoot>(world);
        if (Game.Connection is not { } net || ReferenceEquals(net, _listening) && _receiptHook is { Active: true }) return;
        _receiptHook?.Release();
        _listening = net;
        _receipts.Clear();
        Action<QuestReceipt> received = receipt =>
        {
            Record(receipt);
            if (_receiptWorld != null && _receiptWorld.TryGetTarget(out var shown) && GodotObject.IsInstanceValid(shown))
                Callable.From(() => { if (GodotObject.IsInstanceValid(shown)) Native.TryCall(shown, "Quest.RefreshDetail", out _); }).CallDeferred();
        };
        _receiptHook = NativeHooks.Listen("Net.QuestReceiptEvent", () => Game.Session.NpcQuest.QuestReceiptEvent += received, () => Game.Session.NpcQuest.QuestReceiptEvent -= received);
    }

    internal static void Reset()
    {
        _receipts.Clear();
        _listening = null;
        _receiptHook = null;
        _receiptWorld = null;
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

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        ListenForReceipts(world);
        if (Native.Get<Container>(world, "Quest.rewardBox") is not { } tiles || tiles.GetParent() is not Control detail) return;
        if (detail.GetNodeOrNull(RewardBoxName) != null) return;
        var optionTitle = Native.Get<Label>(world, "Quest.rewardOptionTitle");
        var optionTiles = Native.Get<Container>(world, "Quest.rewardOptionBox");
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

    /// <summary>
    /// Keeps the quest shown in Quest Details while the client reloads its quest log. A reload clears every quest
    /// view until the server sends them again; without the legacy quest table the open journal then has no rows
    /// and drops its selection, so the subpage fell back to the empty hint and stayed there after the views
    /// arrived. Until the quest is listed again the subpage keeps its last details (the client leaves them in
    /// place when it has no selection), then the selection comes back. A quest that really left the list
    /// (abandoned, finished, filtered out) is let go.
    /// </summary>
    public static void KeepSelection(HudWindow window, WorldRoot world)
    {
        if (Native.Get<Control>(world, "Quest.detailBody") is not { } body || Native.Get<Control>(world, "Quest.detailEmpty") is not { } empty) return;
        int kept = -1;
        void Keep()
        {
            if (!GodotObject.IsInstanceValid(world) || !GodotObject.IsInstanceValid(body) || !window.IsVisibleInTree()) return;
            int selected = Native.Get<int>(world, "Quest.Selected");
            if (selected >= 0) { kept = selected; return; }
            if (kept < 0) return;
            if (Native.Call(world, "Quest.QuestsInView") is List<(int QuestId, int State)> rows && rows.Any(r => r.QuestId == kept))
            {
                Native.Set(world, "Quest.Selected", kept);
                Native.Call(world, "Quest.RefreshDetail");
                return;
            }
            bool reloading = Game.Session.QuestBook.Entries is { } quests
                && quests.Any(q => q.QuestId == kept && Native.Call(world, "Quest.InFilter", q.State) is true)
                && !Game.Session.QuestBook.Views.ContainsKey(kept);
            if (reloading) { body.Visible = true; empty.Visible = false; return; }
            kept = -1;
            Native.Call(world, "Quest.RefreshDetail");
        }
        window.VisibilityChanged += () => { if (!window.Visible) kept = -1; };
        // The client hides the details in the middle of a frame; answer before that frame is drawn.
        empty.VisibilityChanged += () => { if (empty.Visible && kept >= 0) Callable.From(Keep).CallDeferred(); };
        NativeWindows.Sync(window, Keep);
    }

    private static object Signature(WorldRoot world)
    {
        int questId = Native.Get<int>(world, "Quest.Selected");
        int state = questId >= 0 ? Game.Session.QuestBook.StateOf(questId) : 0;
        var selection = Game.Session.QuestBook.RewardSelection;
        object? receipt = null, pending = null;
        if (selection != null)
        {
            if (TryReceipt(selection, questId, out var granted)) receipt = granted;
            var args = new object?[] { questId, null };
            if (Native.Call(selection, "Chosen", args) is true) pending = args[1];
        }
        var views = Game.Session.QuestBook.Views;
        QuestView? view = null;
        views?.TryGetValue(questId, out view);
        return (questId, state, receipt, pending, view);
    }

    private static void Fill(WorldRoot world, VBoxContainer box)
    {
        foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); }
        var title = Native.Get<Label>(world, "Quest.rewardTitle");
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
        else if (Game.Session.QuestBook.Rewards(questId, Game.Session.Self.LastEnter.Class) is var rewards)
            foreach (var reward in rewards) box.AddChild(Tile(world, reward.ItemId, reward.Count));
        if (title != null) title.Visible = box.GetChildCount() > 0;
    }

    private static Control Tile(WorldRoot world, int itemId, int count)
    {
        var panel = UiTheme.Section();
        var name = LibreKO.UiKit.QuestRewardName(itemId);
        if (Native.Call(Native.StaticOwner("LibreKO.QuestRows"), "ItemRow", Native.Get<object>(world, "ItemTooltip"), itemId, name, count.ToString("n0"), UiTheme.GoldBright) is Control row)
        {
            Unframe(row, itemId);
            row.SetMeta("quest_reward_item_id", itemId);
            row.SetMeta("quest_reward_count", count);
            panel.AddChild(row);
        }
        return panel;
    }

    private static readonly (string Glyph, int ItemId)[] Glyphs =
    {
        ("system/coins", QuestData.CoinItemId), ("system/level", QuestData.ExpItemId), ("system/medal", QuestData.LadderPointItemId),
    };

    /// <summary>
    /// The item a reward row shows: the client frames item icons in a grade slot and draws coins, experience
    /// and ladder points as flat glyphs; the Classic rows show the original item icon for every reward.
    /// </summary>
    public static Texture2D? RewardIcon(Control row)
    {
        var icon = Native.Descendants(row).OfType<TextureRect>().FirstOrDefault();
        if (icon?.Texture is not { } texture) return null;
        foreach (var (glyph, itemId) in Glyphs)
            if (UiIcons.Get(glyph) == texture) return ItemIcons.Get(itemId);
        return texture;
    }

    /// <summary>The client's reward icon slot: the first cell of a quest item row, holding an icon or a glyph.</summary>
    public static bool IsRewardSlot(PanelContainer slot) =>
        slot.GetIndex() == 0 && slot.GetParent() is HBoxContainer && slot.GetChildCount() == 1
        && (slot.GetChild(0) is TextureRect || slot.GetChild(0) is MarginContainer glyph && glyph.GetChildren().OfType<TextureRect>().Any());

    /// <summary>
    /// Shows a reward slot that stays in the client's row (the client repaints chosen slots) as the original bare
    /// item icon: the slot's own frame is not drawn, and a glyph becomes the reward's item icon.
    /// </summary>
    public static void ClassicSlot(PanelContainer slot)
    {
        if (slot.HasMeta("classic_reward_slot")) return;
        slot.SetMeta("classic_reward_slot", true);
        slot.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        slot.SelfModulate = Colors.Transparent;
        if (slot.GetChild(0) is not MarginContainer glyph || RewardIcon(slot) is not { } texture) return;
        slot.RemoveChild(glyph);
        glyph.QueueFree();
        slot.AddChild(new TextureRect
        {
            Texture = texture, CustomMinimumSize = new Vector2(32, 32), MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
    }

    /// <summary>Replaces the client's reward slot (frame or glyph) with the original bare item icon.</summary>
    public static void Unframe(Control row, int itemId)
    {
        if (row.GetChildCount() == 0 || row.GetChild(0) is not PanelContainer frame) return;
        if (Native.Call(Native.StaticOwner("LibreKO.QuestRows"), "ItemIcon", itemId) is not TextureRect icon) return;
        row.RemoveChild(frame);
        frame.QueueFree();
        row.AddChild(icon);
        row.MoveChild(icon, 0);
    }

    /// <summary>Shows planned stat points as allocated and remaining totals.</summary>
    public static void PreparePresets(HudWindow window, WorldRoot world)
    {
        NativeWindows.Sync(window, () =>
        {
            if (Native.Get<Label>(world, "SkillPreset.presetStatPointsLbl") is not { } label) return;
            var plan = Regex.Match(label.Text, @"^Planned (\d+) of (\d+) stat point\(s\)$");
            if (!plan.Success) return;
            int spent = int.Parse(plan.Groups[1].Value), total = int.Parse(plan.Groups[2].Value);
            label.Text = $"Allocated {spent} / {total}    Remaining {total - spent}";
        });
    }
}
