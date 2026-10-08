using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Attaches the client's window layout behaviour to a plugin window and adds the Classic resize grip
/// and edge snapping on top of it.
/// </summary>
public static class NativeLayout
{
    public static HudLayout Attach(
        Control target,
        string id,
        Control? dragHandle,
        Func<Vector2>? defaultPosition,
        bool resizable = false,
        Vector2 defaultSize = default,
        Vector2 minimumSize = default,
        bool persist = true,
        HudLayout.Corner resizeCorner = HudLayout.Corner.BottomRight,
        HudLayout.Corner moveCorner = HudLayout.Corner.TopLeft,
        bool moveGripAlwaysVisible = false,
        Vector2 resizeGripOffset = default,
        Action<float>? backgroundOpacityChanged = null,
        bool legacyResizeGrip = false,
        string? resizeSnapPeerId = null)
    {
        return HudLayout.Attach(target, id, dragHandle, defaultPosition, resizable, defaultSize, minimumSize, persist,
            resizeCorner, moveCorner, moveGripAlwaysVisible, resizeGripOffset, backgroundOpacityChanged);
    }
}
