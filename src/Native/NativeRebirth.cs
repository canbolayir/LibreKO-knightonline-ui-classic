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
/// allocation confirmation that freezes the chosen points before the client sends them. The same window
/// redistributes the existing rebirth points (the client's reset mode). Requirements, the request and
/// its result stay with the client.
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

    private static readonly ConditionalWeakTable<WorldRoot, State> _states = new();

    public static Notice? Confirmation(WorldRoot world) => _states.TryGetValue(world, out var state) ? state.Notice : null;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (!NativeIdentity.Begin(window, "native_rebirth")) return;
        var bonus = Native.Get<Label[]>(world, "Rebirth.bonusLbls");
        var picked = Native.Get<Label[]>(world, "Rebirth.pickLbls");
        var add = Native.Get<Button[]>(world, "Rebirth.addBtns");
        var remove = Native.Get<Button[]>(world, "Rebirth.removeBtns");
        var accept = Native.Get<Button>(world, "Rebirth.btn");
        var status = Native.Get<Label>(world, "Rebirth.status");
        var level = Native.Get<Label>(world, "Rebirth.levelLbl");
        var points = Native.Get<Label>(world, "Rebirth.pointsLbl");
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
            RewireStep(remove[row], () => EditPoint(world, index, false));
            RewireStep(add[row], () => EditPoint(world, index, true));
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

    /// <summary>
    /// The client's step buttons add a point on press and repeat while held; the Classic step keeps that
    /// rhythm but goes through the confirmation lock.
    /// </summary>
    private static void RewireStep(BaseButton button, Action step)
    {
        foreach (var signal in new[] { BaseButton.SignalName.Pressed, BaseButton.SignalName.ButtonDown })
            foreach (var connection in button.GetSignalConnectionList(signal))
                button.Disconnect(signal, connection["callable"].AsCallable());
        HoldRepeat.Attach(button, step);
    }

    private static CharacterSheet? Sheet(WorldRoot world) => Game.Session.Self.Sheet;

    /// <summary>The client's reset mode redistributes the points already earned instead of adding a stage.</summary>
    public static bool Redistributing(WorldRoot world) => Native.Get<bool>(world, "Rebirth.resetMode");

    private static bool Unavailable(WorldRoot world, CharacterSheet sheet) =>
        Native.Get<bool>(world, "Avatar.Dead")
        || !Redistributing(world) && (sheet.Level < CharacterSheet.MaxLevel || sheet.RebirthLevel >= RebirthPick.MaxRebirthLevel);

    /// <summary>The fork's RefreshRebirthUI: columns, availability and the lock while a confirmation is open.</summary>
    public static void Refresh(WorldRoot world)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "Rebirth.pick") is not { } pick) return;
        int level = sheet.RebirthLevel;
        bool reset = Redistributing(world);
        bool capped = !reset && level >= RebirthPick.MaxRebirthLevel;
        bool locked = Native.Get<bool>(world, "Rebirth.inFlight") || state.Notice != null || Unavailable(world, sheet);
        var labels = Native.Get<Label[]>(world, "Rebirth.bonusLbls")!;
        var picks = Native.Get<Label[]>(world, "Rebirth.pickLbls")!;
        var add = Native.Get<Button[]>(world, "Rebirth.addBtns")!;
        var remove = Native.Get<Button[]>(world, "Rebirth.removeBtns")!;
        Native.Get<Label>(world, "Rebirth.levelLbl")!.Text = reset ? $"Rebirth Lv {level}  ·  Redistribution"
            : capped ? $"Rebirth Lv {level} / {RebirthPick.MaxRebirthLevel}" : $"Rebirth Lv {level}  →  Lv {level + 1}";
        Native.Get<Label>(world, "Rebirth.pointsLbl")!.Text = capped ? "No further bonus points are available."
            : $"Bonus points: {pick.Capacity}   Remaining: {pick.Remaining}";
        for (int row = 0; row < RebirthPick.StatCount; row++)
        {
            int current = sheet.RebirthBonusAtRow(row), placed = capped ? 0 : pick.PickedAt(row);
            labels[row].Text = $"+{current}";
            picks[row].Text = $"+{placed}";
            // Redistributing replaces the earned points with the new placement.
            state.Totals[row].Text = $"+{(reset ? placed : current + placed)}";
            add[row].Disabled = locked || !pick.CanAdd(row);
            remove[row].Disabled = locked || !pick.CanRemove(row);
        }
        var accept = Native.Get<Button>(world, "Rebirth.btn")!;
        accept.Disabled = locked || !pick.Complete;
        accept.Text = reset ? "Redistribute" : "Rebirth";
        string availability = capped ? "The maximum rebirth level has been reached."
            : sheet.Level < CharacterSheet.MaxLevel ? $"Level {CharacterSheet.MaxLevel} is required."
            : Native.Get<bool>(world, "Avatar.Dead") ? "You cannot rebirth while dead." : "";
        var status = Native.Get<Label>(world, "Rebirth.status")!;
        if (availability.Length > 0)
        {
            if (status.Text != availability) Native.Call(world, "Rebirth.SetStatus", availability, true);
            state.Warning = availability;
        }
        else if (state.Warning != null)
        {
            if (status.Text == state.Warning) Native.Call(world, "Rebirth.SetStatus", "", false);
            state.Warning = null;
        }
    }

    /// <summary>Adds or removes a bonus point unless the allocation is frozen.</summary>
    public static void EditPoint(WorldRoot world, int row, bool add)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "Rebirth.pick") is not { } pick) return;
        if (!Native.Get<bool>(world, "Rebirth.shown") || Native.Get<bool>(world, "Rebirth.inFlight") || state.Notice != null
            || Unavailable(world, sheet)) return;
        if (add) pick.Add(row); else pick.Remove(row);
        Native.Call(world, "Rebirth.SetStatus", "", false);
        state.Warning = null;
        Native.Call(world, "Rebirth.RefreshUI");
        Refresh(world);
    }

    /// <summary>Opens the allocation confirmation; nothing is sent until it is accepted.</summary>
    public static void Pressed(WorldRoot world)
    {
        if (!_states.TryGetValue(world, out var state) || Sheet(world) is not { } sheet
            || Native.Get<RebirthPick>(world, "Rebirth.pick") is not { } pick) return;
        if (!Native.Get<bool>(world, "Rebirth.shown") || Native.Get<bool>(world, "Rebirth.inFlight") || state.Notice != null
            || Unavailable(world, sheet)) return;
        if (!pick.Complete)
        {
            Native.Call(world, "Rebirth.SetStatus", $"Place all {pick.Capacity} points first.", true);
            state.Warning = null;
            return;
        }
        int revision = ++state.Revision;
        var picks = pick.Payload();
        var layer = Native.Get<Node>(world, "Rebirth.layer") ?? world;
        state.Notice = Redistributing(world)
            ? Notice.Confirm(layer, $"Redistribute your {pick.Capacity} rebirth bonus points?\nThe new placement replaces the current one.",
                "Redistribute", "Cancel", () => Send(world, revision, picks), () => Cancel(world, revision), title: "Rebirth")
            : Notice.Confirm(layer,
                $"Your {ItemData.DisplayName(QualificationItem).Trim()} will be used.\nApply {RebirthPick.PointsPerRebirth} bonus points?\nRebirth Lv {sheet.RebirthLevel} → Lv {sheet.RebirthLevel + 1}",
                "Rebirth", "Cancel", () => Send(world, revision, picks), () => Cancel(world, revision), title: "Rebirth");
        Refresh(world);
    }

    private static void Send(WorldRoot world, int revision, byte[] picks)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || !Native.Get<bool>(world, "Rebirth.shown")
            || Native.Get<bool>(world, "Rebirth.inFlight") || state.Notice == null) return;
        state.Notice = null;
        // The picks stayed frozen while the confirmation was open; the client sends them and handles a refused send.
        if (Sheet(world) is { } sheet && !Unavailable(world, sheet)
            && Native.Get<RebirthPick>(world, "Rebirth.pick") is { } pick && pick.Payload().SequenceEqual(picks))
            Native.Call(world, "Rebirth.OnPressed");
        state.Warning = null;
        Refresh(world);
    }

    private static void Cancel(WorldRoot world, int revision)
    {
        if (!_states.TryGetValue(world, out var state) || revision != state.Revision || Native.Get<bool>(world, "Rebirth.inFlight")
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
