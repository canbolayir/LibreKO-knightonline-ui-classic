using System.Runtime.CompilerServices;
using Godot;
using LibreKO;
using LibreKO.Domain;

namespace KnightOnlineUiClassic.Native;

/// <summary>Native UI members the Classic skins use that the client keeps private.</summary>
public static class NativeUi
{
    private sealed class WindowState { public Action<bool>? AttentionStyler; }
    private sealed class MoneyState { public bool GroupDigits = true; }

    private static readonly ConditionalWeakTable<HudWindow, WindowState> _windows = new();
    private static readonly ConditionalWeakTable<MoneyEdit, MoneyState> _money = new();

    /// <summary>Count text for an item slot: stackable items show "1" as well as larger counts.</summary>
    public static string CountBadge(ItemData.Item? def, int shown) =>
        shown > 1 || shown == 1 && def is { Countable: > 0 } ? shown.ToString() : "";

    extension(HudWindow window)
    {
        public PanelContainer? Header => Native.Get<PanelContainer>(window, "_header");

        public Action<bool>? AttentionStyler
        {
            get => _windows.GetOrCreateValue(window).AttentionStyler;
            set => _windows.GetOrCreateValue(window).AttentionStyler = value;
        }
    }

    extension(ItemSlotView view)
    {
        public Label CountLabel => Native.Get<Label>(view, "_count")!;
    }

    extension(MoneyEdit edit)
    {
        public bool GroupDigits
        {
            get => _money.GetOrCreateValue(edit).GroupDigits;
            set => _money.GetOrCreateValue(edit).GroupDigits = value;
        }
    }

    extension(QuantityPrompt prompt)
    {
        public void Confirm() => Native.Call(prompt, "Confirm");
    }
}
