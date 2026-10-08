using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>HUD elements the client does not expose as <see cref="LibreKO.Plugins.HudPart"/>.</summary>
public enum NativeHudPart
{
    MailIcon,
    AchievementsIcon,
    AttendanceIcon,
    PowerUpStoreIcon,
    FamiliarBar,
}

/// <summary>Hides or extends native HUD elements that have no public plugin hook.</summary>
public static partial class NativeHud
{
    private static readonly HashSet<NativeHudPart> _hidden = new();
    private static readonly Dictionary<NativeHudPart, List<Action<Control>>> _extenders = new();

    internal static Action<HudWindow>? WhisperStyler { get; private set; }
    internal static Func<string, bool, bool, string, Control>? WhisperLineBuilder { get; private set; }

    public static void Hide(NativeHudPart part) => _hidden.Add(part);

    public static bool IsHidden(NativeHudPart part) => _hidden.Contains(part);

    public static void Extend(NativeHudPart part, Action<Control> extend)
    {
        if (!_extenders.TryGetValue(part, out var list)) _extenders[part] = list = new();
        list.Add(extend);
    }

    public static IReadOnlyList<Action<Control>> Extenders(NativeHudPart part) =>
        _extenders.TryGetValue(part, out var list) ? list : Array.Empty<Action<Control>>();

    public static void StyleWhispers(Action<HudWindow> style, Func<string, bool, bool, string, Control> buildLine)
    {
        WhisperStyler = style;
        WhisperLineBuilder = buildLine;
    }
}
