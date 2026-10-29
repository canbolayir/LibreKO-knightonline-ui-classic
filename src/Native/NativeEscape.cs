using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static class NativeEscape
{
    private static PluginUi? _ui;

    public static void Attach(PluginUi ui) => _ui = ui;

    public static void CloseOnEscape(WorldRoot world, Func<bool> isOpen, Action close)
    {
        if (_ui?.CloseOnEscape(isOpen, close) is { } entry) NativeHooks.Hold("Escape entry", entry.Dispose, world);
    }
}
