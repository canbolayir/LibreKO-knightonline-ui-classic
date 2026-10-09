using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterRebirth(PluginContext context) => NativeWindows.Prepare("rebirth", NativeRebirth.Prepare);
}

/// <summary>
/// The Classic rebirth window: the original Stat/Current/Add/Total columns, availability texts, and an
/// allocation confirmation that freezes the chosen points before the client sends them. Requirements,
/// the request and its result stay with the client.
/// </summary>
public static class NativeRebirth
{
    public const int QualificationItem = 900579000;

    private sealed class State
    {
        public HudWindow Window = null!;
        public Label[] Totals = Array.Empty<Label>();
        public Notice? Notice;
        public int Revision;
        public string? Warning;
    }

    private static readonly ConditionalWeakTable<World, State> _states = new();

    public static Notice? Confirmation(World world) => _states.TryGetValue(world, out var state) ? state.Notice : null;

    public static void Prepare(HudWindow window, World world)
    {
        if (!NativeIdentity.Begin(window, "native_rebirth")) return;
        var bonus = Native.Get<Label[]>(world, "_rebirthBonusLbls");
        var picked = Native.Get<Label[]>(world, "_rebirthPickLbls");
        var add = Native.Get<Button[]>(world, "_rebirthAddBtns");
        var remove = Native.Get<Button[]>(world, "_rebirthRemoveBtns");
        var accept = Native.Get<Button>(world, "_rebirthBtn");
        var status = Native.Get<Label>(world, "_rebirthStatus");
        var level = Native.Get<Label>(world, "_rebirthLevelLbl");
        var points = Native.Get<Label>(world, "_rebirthPointsLbl");
        var cancel = accept?.GetParent()?.GetChildren().OfType<Button>().FirstOrDefault(b => b != accept);
        if (bonus == null || picked == null || add == null || remove == null || accept == null || status == null
            || level == null || points == null || cancel == null || bonus.Length != RebirthPick.StatCount) return;

        var state = new State { Window = window, Totals = new Label[RebirthPick.StatCount] };
        level.Name = "rebirth_level"; points.Name = "rebirth_points";
        accept.Name = "rebirth_accept"; cancel.Name = "rebirth_cancel"; status.Name = "rebirth_status";
        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            int index = row;
            var line = bonus[row].GetParent();
            if (line.GetChild(0) is Label stat) stat.Name = "rebirth_stat_" + row;
            bonus[row].Name = "rebirth_current_" + row;
            picked[row].Name = "rebirth_picked_" + row;
            remove[row].Name = "rebirth_remove_" + row;
            add[row].Name = "rebirth_add_" + row;
            var total = NativeIdentity.HudLabel(13, HorizontalAlignment.Center);
            total.Name = "rebirth_total_" + row;
            line.AddChild(total);
            state.Totals[row] = total;
            NativeIdentity.Rewire(remove[row], () => EditPoint(world, index, false));
            NativeIdentity.Rewire(add[row], () => EditPoint(world, index, true));
        }
        NativeIdentity.Rewire(accept, () => Pressed(world));
        _states.AddOrUpdate(world, state);
        window.VisibilityChanged += () =>
        {
            if (window.Visible) Refresh(world);
            else Dismiss(state);
        };
        window.SetMeta("classic_rebirth_controls", 1);
        NativeWindows.Sync(window, () => Refresh(world));
    }

    private static CharacterSheet? Sheet(World world) => Native.Get<CharacterSheet>(world, "Sheet");

    private static bool Unavailable(World world, CharacterSheet sheet) =>
        Native.Get<bool>(world, "_selfDead") || sheet.Level < CharacterSheet.MaxLevel || sheet.RebirthLevel >= RebirthPick.MaxRebirthLevel;

    /// <summary>The fork's RefreshRebirthUI: columns, availability and the lock while a confirmation is open.</summary>
    public static void Refresh(World world)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "_rebirthPick") is not { } pick) return;
        int level = sheet.RebirthLevel;
        bool capped = level >= RebirthPick.MaxRebirthLevel;
        bool locked = Native.Get<bool>(world, "_rebirthInFlight") || state.Notice != null || Native.Get<bool>(world, "_selfDead")
            || capped || sheet.Level < CharacterSheet.MaxLevel;
        var labels = Native.Get<Label[]>(world, "_rebirthBonusLbls")!;
        var picks = Native.Get<Label[]>(world, "_rebirthPickLbls")!;
        var add = Native.Get<Button[]>(world, "_rebirthAddBtns")!;
        var remove = Native.Get<Button[]>(world, "_rebirthRemoveBtns")!;
        Native.Get<Label>(world, "_rebirthLevelLbl")!.Text = capped ? $"Rebirth Lv {level} / {RebirthPick.MaxRebirthLevel}" : $"Rebirth Lv {level}  →  Lv {level + 1}";
        Native.Get<Label>(world, "_rebirthPointsLbl")!.Text = capped ? "No further bonus points are available."
            : $"Bonus points: {RebirthPick.PointsPerRebirth}   Remaining: {pick.Remaining}";
        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            int current = sheet.RebirthBonusAtRow(row), placed = capped ? 0 : pick.PickedAt(row);
            labels[row].Text = $"+{current}";
            picks[row].Text = $"+{placed}";
            state.Totals[row].Text = $"+{current + placed}";
            add[row].Disabled = locked || !pick.CanAdd(row);
            remove[row].Disabled = locked || !pick.CanRemove(row);
        }
        Native.Get<Button>(world, "_rebirthBtn")!.Disabled = locked || !pick.Complete;
        string availability = capped ? "The maximum rebirth level has been reached."
            : sheet.Level < CharacterSheet.MaxLevel ? $"Level {CharacterSheet.MaxLevel} is required."
            : Native.Get<bool>(world, "_selfDead") ? "You cannot rebirth while dead." : "";
        var status = Native.Get<Label>(world, "_rebirthStatus")!;
        if (availability.Length > 0)
        {
            if (status.Text != availability) Native.Call(world, "SetRebirthStatus", availability, true);
            state.Warning = availability;
        }
        else if (state.Warning != null)
        {
            if (status.Text == state.Warning) Native.Call(world, "SetRebirthStatus", "", false);
            state.Warning = null;
        }
    }

    /// <summary>Adds or removes a bonus point unless the allocation is frozen.</summary>
    public static void EditPoint(World world, int row, bool add)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "_rebirthPick") is not { } pick) return;
        if (!Native.Get<bool>(world, "_rebirthShown") || Native.Get<bool>(world, "_rebirthInFlight") || state.Notice != null
            || Unavailable(world, sheet)) return;
        if (add) pick.Add(row); else pick.Remove(row);
        Native.Call(world, "SetRebirthStatus", "", false);
        state.Warning = null;
        Native.Call(world, "RefreshRebirthUI");
        Refresh(world);
    }

    /// <summary>Opens the allocation confirmation; nothing is sent until it is accepted.</summary>
    public static void Pressed(World world)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "_rebirthPick") is not { } pick) return;
        if (!Native.Get<bool>(world, "_rebirthShown") || Native.Get<bool>(world, "_rebirthInFlight") || state.Notice != null
            || Unavailable(world, sheet)) return;
        if (!pick.Complete)
        {
            Native.Call(world, "SetRebirthStatus", $"Place all {RebirthPick.PointsPerRebirth} points first.", true);
            state.Warning = null;
            return;
        }
        int revision = ++state.Revision;
        var picks = pick.Payload();
        var layer = Native.Get<Node>(world, "_rebirthLayer") ?? world;
        state.Notice = Notice.Confirm(layer,
            $"Your {ItemData.DisplayName(QualificationItem).Trim()} will be used.\nApply {RebirthPick.PointsPerRebirth} bonus points?\nRebirth Lv {sheet.RebirthLevel} → Lv {sheet.RebirthLevel + 1}",
            "Rebirth", "Cancel", () => Send(world, revision, picks), () => Cancel(world, revision), title: "Rebirth");
        Refresh(world);
    }

    private static void Send(World world, int revision, byte[] picks)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || !Native.Get<bool>(world, "_rebirthShown")
            || Native.Get<bool>(world, "_rebirthInFlight") || state.Notice == null) return;
        state.Notice = null;
        if (Sheet(world) is not { } sheet || Unavailable(world, sheet)) { Refresh(world); return; }
        var sent = (byte[])picks.Clone();
        Native.Set(world, "_rebirthInFlight", true);
        Native.Set(world, "_rebirthSent", sent);
        Native.Call(world, "SetRebirthStatus", "Reincarnating…", false);
        state.Warning = null;
        Native.Call(world, "RefreshRebirthUI");
        Refresh(world);
        // The fix branch's sender reports a refused send; upstream's returns nothing and always sends.
        if (Net.I is { } net && Native.Call(net, "SendRebirthStatChange", sent) is false)
            Native.Call(world, "OnRebirthStatResult", (int)Net.ClassChangeRebirthStat, LocalRefusal);
    }

    /// <summary>
    /// The result a refused local send reports: the signed busy refusal where the reply carries an i16
    /// result, and the legacy refusal byte otherwise.
    /// </summary>
    private static int LocalRefusal =>
        Native.ClientType("LibreKO.Network.RebirthWire") is { } wire && Native.TryGet<short>(wire, "Busy", out short busy) ? busy : 0;

    private static void Cancel(World world, int revision)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || Native.Get<bool>(world, "_rebirthInFlight")
            || state.Notice == null) return;
        state.Notice = null;
        Refresh(world);
    }

    private static void Dismiss(State state)
    {
        state.Revision++;
        if (state.Notice is { } notice && GodotObject.IsInstanceValid(notice)) notice.Close();
        state.Notice = null;
    }
}
