using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterIdentity(PluginContext context)
    {
        NativeWindows.Prepare("namechange", NativeIdentity.PrepareNameChange);
        NativeWindows.Prepare("creat_clan", NativeIdentity.PrepareClanCreate);
        NativeWindows.Prepare("disguise", NativeIdentity.PrepareDisguise);
        NativeWindows.Prepare("nationtaxrate", NativeIdentity.PrepareCouncilRate);
        NativeWindows.Prepare("siegetaxrate", NativeIdentity.PrepareCouncilRate);
        NativeWindows.Prepare("equipview", NativeEquipView.Prepare);
    }
}

/// <summary>
/// Name change, clan creation, transformation and council tax rate windows: names the native controls
/// the Classic identity and council skins look for, adds the name change Cancel button, marks the selected
/// transformation rows and mirrors the pending tariff into <c>council_rate_pending</c>.
/// </summary>
public static class NativeIdentity
{
    public const string ClanFeeText = "Creating a clan costs {0:n0} coins. Do you want to create this clan?";

    /// <summary>Runs the preparer of an identity, council or equipment view window built outside the client's window flow.</summary>
    public static void Prepare(HudWindow window, World world)
    {
        switch (window.Id)
        {
            case "namechange": PrepareNameChange(window, world); break;
            case "creat_clan": PrepareClanCreate(window, world); break;
            case "disguise": PrepareDisguise(window, world); break;
            case "nationtaxrate" or "siegetaxrate": PrepareCouncilRate(window, world); break;
            case "equipview": NativeEquipView.Prepare(window, world); break;
        }
    }

    public static void PrepareNameChange(HudWindow window, World world)
    {
        if (!Begin(window, "native_identity")) return;
        var body = window.Body;
        var hint = body.GetChildren().OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Requires a Scroll", StringComparison.Ordinal));
        NativeWindows.Name(hint, "name_change_hint");
        NativeWindows.Name(Native.Get<LineEdit>(world, "_nameChangeEdit"), "name_change_name");
        var accept = Native.Get<Button>(world, "_nameChangeBtn");
        NativeWindows.Name(accept, "name_change_accept");
        var status = Native.Get<Label>(world, "_nameChangeStatus");
        NativeWindows.Name(status, "name_change_status");
        if (accept == null || status == null || hint == null) return;
        var cancel = new Button { Name = "name_change_cancel", Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += () => Native.Call(world, "CloseNameChange");
        body.AddChild(cancel);
        body.MoveChild(cancel, status.GetIndex());
        window.SetMeta("classic_identity_controls", 1);
    }

    public static void PrepareClanCreate(HudWindow window, World world)
    {
        if (!Begin(window, "native_identity")) return;
        NativeWindows.Name(Native.Get<Label>(world, "_clanCreateMessage"), "clan_create_message");
        NativeWindows.Name(Native.Get<LineEdit>(world, "_clanCreateName"), "clan_create_name");
        var buttons = Native.Descendants(window.Body).OfType<Button>().ToArray();
        bool named = NativeWindows.Name(buttons.FirstOrDefault(b => b.Text == "Create"), "clan_create_accept")
            & NativeWindows.Name(buttons.FirstOrDefault(b => b.Text == "Cancel"), "clan_create_cancel");
        if (!named || !Native.Has(world, "_clanCreateMessage") || !Native.Has(world, "_clanCreateName")) return;
        window.SetMeta("classic_identity_controls", 1);
        // The Classic fee question speaks of coins, as the original text table does.
        string fee = string.Format(ClanFeeText, ClanTypes.CreationCoins);
        NativeWindows.Sync(window, () =>
        {
            if (Native.Get<Notice>(world, "_clanCreateNotice") is { } notice && GodotObject.IsInstanceValid(notice)
                && Native.Get<DialogRequest>(notice, "_request") is { } request && request.Message != fee)
                notice.SetMessage(fee);
        });
    }

    public static void PrepareDisguise(HudWindow window, World world)
    {
        if (!Begin(window, "native_identity")) return;
        var groups = Native.Get<VBoxContainer>(world, "_disguiseGroupRows");
        var forms = Native.Get<VBoxContainer>(world, "_disguiseFormRows");
        NativeWindows.Name(groups, "disguise_groups");
        NativeWindows.Name(forms, "disguise_forms");
        NativeWindows.Name(Native.Get<Label>(world, "_disguiseNote"), "disguise_note");
        var buttons = Native.Descendants(window.Body).OfType<Button>().Where(b => b.GetParent() != groups && b.GetParent() != forms).ToArray();
        bool named = NativeWindows.Name(buttons.FirstOrDefault(b => b.Text == "OK"), "disguise_accept")
            & NativeWindows.Name(buttons.FirstOrDefault(b => b.Text == "Close"), "disguise_cancel");
        if (!named || groups == null || forms == null || !Native.Has(world, "_disguiseNote")) return;
        foreach (var rows in new[] { groups, forms })
        {
            foreach (var row in rows.GetChildren()) MarkDisguiseRow(row);
            NativeWindows.OnDescendantAdded(rows, MarkDisguiseRow);
        }
        window.SetMeta("classic_identity_controls", 1);
    }

    /// <summary>Selected rows carry <c>identity_selected</c>; full names stay readable as tooltips.</summary>
    private static void MarkDisguiseRow(Node node)
    {
        if (node is not Button button) return;
        button.TooltipText = button.Text;
        button.SetMeta("identity_selected", button.GetThemeColor("font_color") == UiTheme.GoldBright);
    }

    public static void PrepareCouncilRate(HudWindow window, World world)
    {
        if (!Begin(window, "native_council")) return;
        bool nation = window.Id == "nationtaxrate";
        var body = window.Body;
        var prompt = nation ? body.GetChildren().OfType<Label>().FirstOrDefault() : Native.Get<Label>(world, "_siegeTaxRatePrompt");
        var value = Native.Get<Label>(world, nation ? "_nationTaxRateValue" : "_siegeTaxRateValue");
        var arrows = value?.GetParent()?.GetParent();
        var footer = body.GetChildren().OfType<HBoxContainer>().LastOrDefault(row => row != arrows);
        var steps = arrows?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();
        var actions = footer?.GetChildren().OfType<Button>().ToArray() ?? Array.Empty<Button>();
        if (prompt == null || value == null || steps.Length != 2 || actions.Length != 2) return;
        prompt.Name = "taxrate_prompt"; value.Name = "taxrate_value";
        steps[0].Name = "taxrate_down"; steps[1].Name = "taxrate_up";
        actions[0].Name = "taxrate_accept"; actions[1].Name = "taxrate_cancel";
        window.SetMeta("classic_council_controls", 1);
        window.SetMeta("council_rate_pending", false);
        if (nation) NativeWindows.Sync(window, () => window.SetMeta("council_rate_pending", Native.Get<bool>(world, "_nationTaxRateBusy")));
    }

    /// <summary>Changes the nation tax rate unless a tariff request is pending.</summary>
    public static void StepNationTaxRate(World world, int delta)
    {
        if (Native.Get<bool>(world, "_nationTaxRateBusy")) return;
        Native.Call(world, "StepNationTaxRate", delta);
    }

    internal static bool Begin(HudWindow window, string marker)
    {
        if (window.HasMeta(marker)) return false;
        window.SetMeta(marker, true);
        return true;
    }

    /// <summary>Replaces every native press handler of a button.</summary>
    internal static void Rewire(BaseButton button, Action pressed)
    {
        foreach (var connection in button.GetSignalConnectionList(BaseButton.SignalName.Pressed))
            button.Disconnect(BaseButton.SignalName.Pressed, connection["callable"].AsCallable());
        button.Pressed += pressed;
    }

    /// <summary>A label styled like the client's HUD labels.</summary>
    internal static Label HudLabel(int size, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }
}
