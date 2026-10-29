using Godot;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

/// <summary>
/// Registers every native preparer and HUD hook before the skins register theirs. Each window group
/// implements its own partial method in its own file.
/// </summary>
public static partial class NativeSetup
{
    public static void Register(PluginContext context)
    {
        RegisterGame(context);
        RegisterHud(context);
        RegisterChat(context);
        RegisterCharacter(context);
        RegisterNpc(context);
        RegisterParty(context);
        RegisterInventory(context);
        RegisterStorage(context);
        RegisterVendor(context);
        RegisterTrade(context);
        RegisterMerchant(context);
        RegisterAnvil(context);
        RegisterServices(context);
        RegisterCommunication(context);
        RegisterMarket(context);
        RegisterIdentity(context);
        RegisterAppearance(context);
        RegisterCape(context);
        RegisterRebirth(context);
        RegisterPet(context);
        RegisterSiege(context);
        RegisterKing(context);
        RegisterEventsA(context);
        RegisterProgression(context);
        RegisterMapFortune(context);
        RegisterBoards(context);
        RegisterOverlays(context);
        RegisterQuestHud(context);
        NativeWindows.Register(context.Ui);
        NativeEscape.Attach(context.Ui);
    }

    /// <summary>
    /// Undoes <see cref="Register"/> when the client switches the plugin off: releases every recorded hook, removes
    /// the plugin delegates still attached to the client's game API (the Classic HUD and windows of the World that
    /// is about to be rebuilt) and forgets every static registry, so a new plugin instance registers exactly once.
    /// The client removes the <see cref="PluginContext.Ui"/> registrations itself.
    /// </summary>
    public static void Unregister(PluginGame game)
    {
        int released = NativeHooks.ReleaseAll();
        var sweep = new Dictionary<string, int>
        {
            ["game"] = NativeHooks.Sweep(game),
            ["net"] = Game.Connection is { } net ? NativeHooks.Sweep(net) : 0,
            ["tree"] = NativeHooks.SweepTree(),
        };
        NativeHooks.RecordSweep(sweep);
        if (sweep["net"] + sweep["tree"] > 0)
            GD.PushWarning($"[plugin:knightonline-ui-classic] shutdown removed {sweep["net"]} network and {sweep["tree"]} scene-tree handler(s) that were not recorded");
        GD.Print($"[plugin:knightonline-ui-classic] shutdown released {released} hook(s) and {sweep["game"]} game API handler(s)");
        NativeWindows.Reset();
        NativeHud.Reset();
        NativeOverlays.Reset();
        NativeQuestHud.Reset();
        NativeLayout.Reset();
        FamiliarBar.Reset();
        NativeGame.Reset();
        NativeChat.Reset();
        NativeQuestLog.Reset();
        NativeCommunication.Reset();
        NativeHudChat.Reset();
        Native.Reset();
    }

    static partial void RegisterGame(PluginContext context);
    static partial void RegisterHud(PluginContext context);
    static partial void RegisterChat(PluginContext context);
    static partial void RegisterCharacter(PluginContext context);
    static partial void RegisterNpc(PluginContext context);
    static partial void RegisterParty(PluginContext context);
    static partial void RegisterInventory(PluginContext context);
    static partial void RegisterStorage(PluginContext context);
    static partial void RegisterVendor(PluginContext context);
    static partial void RegisterTrade(PluginContext context);
    static partial void RegisterMerchant(PluginContext context);
    static partial void RegisterAnvil(PluginContext context);
    static partial void RegisterServices(PluginContext context);
    static partial void RegisterCommunication(PluginContext context);
    static partial void RegisterMarket(PluginContext context);
    static partial void RegisterIdentity(PluginContext context);
    static partial void RegisterAppearance(PluginContext context);
    static partial void RegisterCape(PluginContext context);
    static partial void RegisterRebirth(PluginContext context);
    static partial void RegisterPet(PluginContext context);
    static partial void RegisterSiege(PluginContext context);
    static partial void RegisterKing(PluginContext context);
    static partial void RegisterEventsA(PluginContext context);
    static partial void RegisterProgression(PluginContext context);
    static partial void RegisterMapFortune(PluginContext context);
    static partial void RegisterBoards(PluginContext context);
    static partial void RegisterOverlays(PluginContext context);
    static partial void RegisterQuestHud(PluginContext context);
}
