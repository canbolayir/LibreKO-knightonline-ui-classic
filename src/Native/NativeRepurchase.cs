using Godot;
using LibreKO;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Prepares the client's buy-back window (Kaishan, <c>repurchase</c>, 3277df5) for the Classic skin: it names the
/// client's ten row label sets, Buy buttons, scroll bar, empty line, captions and Refresh button. The client keeps
/// every callback: Buy asks with its own confirmation (expired rows get its "period has been expired" notice) and
/// sends the buy-back request, Refresh and the window's close send their requests, and the scroll bar re-renders
/// the rows. The rows' price and time states (too dear, expired) come from the plugin facade
/// <c>IGameRepurchase.Rows</c> and are mirrored onto the row labels for the skin.
/// </summary>
public static class NativeRepurchase
{
    public const string WindowId = "repurchase";
    public const string PreparedMeta = "native_repurchase";
    public const string TooDear = "rb_too_dear", Expired = "rb_expired";
    public const int Rows = 10;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta(PreparedMeta)) return;
        var names = Native.Get<Label[]>(world, "Repurchase.rbNames");
        var prices = Native.Get<Label[]>(world, "Repurchase.rbPrices");
        var times = Native.Get<Label[]>(world, "Repurchase.rbTimes");
        var buys = Native.Get<Button[]>(world, "Repurchase.rbBuy");
        var scroll = Native.Get<VScrollBar>(world, "Repurchase.RbScroll");
        var empty = Native.Get<Label>(world, "Repurchase.rbEmpty");
        if (names == null || prices == null || times == null || buys == null || scroll == null || empty == null
            || names.Length != Rows || prices.Length != Rows || times.Length != Rows || buys.Length != Rows
            || names.Concat(prices).Concat(times).Any(l => l == null) || buys.Any(b => b == null)
            || names[0].GetParent() is not Control canvas) return;
        canvas.Name = "rb_canvas";
        for (int i = 0; i < Rows; i++)
        {
            names[i].Name = "rb_name_" + i; prices[i].Name = "rb_price_" + i; times[i].Name = "rb_time_" + i; buys[i].Name = "rb_buy_" + i;
        }
        scroll.Name = "rb_scroll";
        empty.Name = "rb_empty";
        // The client builds the four captions first, then the refresh button, then the rows.
        int heading = 0;
        foreach (var label in canvas.GetChildren().OfType<Label>().Where(l => !names.Contains(l) && !prices.Contains(l) && !times.Contains(l) && l != empty).Take(4))
            label.Name = "rb_head_" + heading++;
        if (canvas.GetChildren().OfType<Button>().FirstOrDefault(b => !buys.Contains(b)) is { } refresh) refresh.Name = "rb_refresh";
        // The client's header X hides the window before its Closed handler runs, and that handler (CloseRepurchase)
        // then skips the close request for a hidden window. Close through the facade, which hides it and tells the server.
        if (window.Header?.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Button>().FirstOrDefault(b => b.TooltipText == "Close") is { } close)
            NativeVendor.Rewire(close, () => { if (Plugin.Active) Plugin.Kit.Game.Repurchase.Close(); });
        window.SetMeta(PreparedMeta, true);
        NativeWindows.Sync(window, () => Sync(names, prices, times, scroll));
    }

    /// <summary>Marks each shown row with its facade state: the price above the wallet, the deletion time passed.</summary>
    private static void Sync(Label[] names, Label[] prices, Label[] times, VScrollBar scroll)
    {
        if (!Plugin.Active) return;
        var rows = Plugin.Kit.Game.Repurchase.Rows;
        int offset = (int)scroll.Value;
        for (int i = 0; i < Rows; i++)
        {
            int index = offset + i;
            bool shown = index >= 0 && index < rows.Count;
            prices[i].SetMeta(TooDear, shown && rows[index].TooDear);
            times[i].SetMeta(Expired, shown && rows[index].Expired);
            names[i].SetMeta(Expired, shown && rows[index].Expired);
        }
    }
}
