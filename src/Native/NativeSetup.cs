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
        NativeWindows.Register(context.Ui);
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
}
