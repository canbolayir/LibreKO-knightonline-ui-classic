using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    /// <summary>
    /// Familiar window, trainer and skill bar. The windows are matched when they enter the tree rather than
    /// through window extenders, so preview harnesses that register skins on another <see cref="PluginUi"/>
    /// still receive the prepared native controls.
    /// </summary>
    static partial void RegisterPet(PluginContext context)
    {
        NativeWindows.PrepareMatching(window => window.Id == "pet", (window, world) => FamiliarWindow.Prepare(window, OwnerOf(window) ?? world));
        NativeWindows.PrepareMatching(window => window.Id == "pethatch", (window, world) => FamiliarTrainer.Prepare(window, OwnerOf(window) ?? world));
        FamiliarBar.Watch();
    }

    /// <summary>The World that built a native window: the target of its close handler.</summary>
    private static World? OwnerOf(HudWindow window) =>
        Native.Get<Action>(window, "Closed")?.GetInvocationList().Select(handler => handler.Target).OfType<World>().FirstOrDefault();
}
