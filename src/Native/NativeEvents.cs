using System.Runtime.CompilerServices;
using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// The temple event leave confirmation as a plugin dialog: one prompt at a time, only inside an
/// event zone, dismissed when the zone changes, and confirmed only for the zone it was asked in.
/// The client's own confirmation dialog stays the trigger and is never shown.
/// </summary>
public static class NativeEvents
{
    private sealed class LeaveState
    {
        public Notice? Prompt;
        public int Zone;
    }

    private static readonly ConditionalWeakTable<World, LeaveState> _leave = new();

    /// <summary>The open leave confirmation of a world, if any.</summary>
    public static Notice? LeavePrompt(World world) =>
        _leave.TryGetValue(world, out var state) && state.Prompt is { } prompt && GodotObject.IsInstanceValid(prompt) && !prompt.IsQueuedForDeletion() ? prompt : null;

    /// <summary>Takes over the client's event leave confirmation once its in-zone UI exists.</summary>
    public static void Prepare(World world)
    {
        if (Native.Get<ConfirmationDialog>(world, "_inZoneLeaveAsk") is not { } dialog || dialog.HasMeta("classic_leave_prompt")) return;
        dialog.SetMeta("classic_leave_prompt", true);
        dialog.AboutToPopup += () =>
        {
            Callable.From(() => { if (GodotObject.IsInstanceValid(dialog)) dialog.Hide(); }).CallDeferred();
            if (GodotObject.IsInstanceValid(world)) Ask(world);
        };
        var watcher = new NativeEventWatcher(() => { if (GodotObject.IsInstanceValid(world)) Refresh(world); }) { Name = "classic_leave_watch" };
        (dialog.GetParent() ?? (Node)world).AddChild(watcher);
    }

    private static int Zone(World world) => Convert.ToInt32(Native.Get<object>(world, "_zone") ?? 0);
    private static bool InEvent(World world) => Native.Call(typeof(World), "IsTempleEventZone", Zone(world)) is true;

    private static void Ask(World world)
    {
        if (!InEvent(world) || LeavePrompt(world) != null) return;
        var state = _leave.GetOrCreateValue(world);
        state.Zone = Zone(world);
        state.Prompt = Notice.Confirm(world, $"Do you want to leave {ZoneCatalog.Name(state.Zone)} and return to Moradon?",
            "Leave", "Cancel", () => Confirmed(world), () => state.Prompt = null, title: "Leave Event");
    }

    private static void Confirmed(World world)
    {
        if (!_leave.TryGetValue(world, out var state)) return;
        state.Prompt = null;
        if (!GodotObject.IsInstanceValid(world) || Zone(world) != state.Zone || !InEvent(world)) return;
        Native.Call(world, "OnInZoneLeaveConfirmed");
    }

    /// <summary>Dismisses a confirmation that no longer matches the current zone.</summary>
    public static void Refresh(World world)
    {
        if (!_leave.TryGetValue(world, out var state) || LeavePrompt(world) is not { } prompt) return;
        if (Zone(world) == state.Zone && InEvent(world)) return;
        prompt.Close();
        state.Prompt = null;
    }
}

public partial class NativeEventWatcher : Node
{
    private readonly Action _tick;
    public NativeEventWatcher() => _tick = () => { };
    public NativeEventWatcher(Action tick) => _tick = tick;
    public override void _Process(double delta) => _tick();
}
