using KnightOnlineUiClassic.Windows;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic;

public sealed class Plugin : IPlugin
{
    public static UiKit Kit { get; private set; } = null!;

    public void Initialize(PluginContext context)
    {
        Kit = new UiKit(context);
        var ui = context.Ui;
        ui.ReplaceHud(HudPart.StatusBars, () => new NationHud(() => new StatusHud()));
        ui.ReplaceHud(HudPart.TargetFrame, () => new NationHud(() => new TargetFrame()));
        ui.ReplaceHud(HudPart.Hotbar, () => new NationHud(() => new HotkeyBar()));
        ui.ReplaceHud(HudPart.Chat, () => new NationHud(() => new ChatWindow()));
        ui.ReplaceHud(HudPart.CombatLog, () => new NationHud(() => new LogWindow()));
        ui.StyleWhispers(ClassicWhisperSkin.Apply, ClassicWhisperSkin.Line);
        ui.HideHud(HudPart.MiniMap);
        ui.HideHud(HudPart.Launcher);
        ui.HideHud(HudPart.ExpBar);
        ui.HideHud(HudPart.MailIcon);
        ui.HideHud(HudPart.AchievementsIcon);
        ui.HideHud(HudPart.AttendanceIcon);
        ui.HideHud(HudPart.PowerUpStoreIcon);
        ui.ReplaceWindow("inventory", host => new InventoryWindow(host));
        ui.ReplaceWindow("character_info", host => new CharacterWindow(host));
        ui.ReplaceWindow("skills", host => new SkillWindow(host));
        // Keep native modern windows functional and apply the classic frame theme.
        foreach(var id in ui.WindowIds)
            if(id is not ("inventory" or "character_info" or "skills" or "vendor" or "party" or "seek_party" or "exchange" or "anvil" or "vipwarehouse_pin") && !ClassicStorageSkin.WindowIds.Contains(id) && !ClassicMerchantSkin.WindowIds.Contains(id) && !CharacterDetailsSkin.WindowIds.Contains(id))
                ui.ExtendWindow(id, ClassicSkin.ExtendWindow);
        ui.ExtendWindow("vipwarehouse_pin", ClassicStoragePin.Extend);
        foreach(var id in ClassicStorageSkin.WindowIds)ui.ExtendWindow(id,ClassicStorageSkin.Extend);
        foreach(var id in ClassicMerchantSkin.WindowIds)ui.ExtendWindow(id,ClassicMerchantSkin.Extend);
        ui.ExtendWindow("vendor", ClassicVendorSkin.Extend);
        ui.ExtendWindow("exchange", ClassicExchangeSkin.Extend);
        ui.ExtendWindow("anvil", ClassicAnvilSkin.Extend);
        ui.ExtendWindow("anvil_choice", ClassicAnvilChoice.Extend);
        ui.ExtendWindow("party", ClassicPartySkin.Extend);
        ui.ExtendWindow("seek_party", ClassicPartySkin.Extend);
        foreach (var id in CharacterDetailsSkin.WindowIds)
            ui.ExtendWindow(id, CharacterDetailsSkin.Extend);
        ui.ReplaceDialogs(request=>request.Title=="Magic Anvil"?new ClassicAnvilNotice(request):request.Title=="Merchant"?new ClassicMerchantNotice(request):new MessageBox(request));
        ui.AddHud(() => new NationHud(() => new Taskbar()));
        ui.AddHud(() => new NationHud(() => new ClassicMerchantSigns()));
        context.Log.Info($"{Kit.LayoutCount} layouts, {Kit.TextureCount} textures");
        if (Godot.OS.GetCmdlineUserArgs().Contains("classic-check"))
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.CallDeferred(Godot.Node.MethodName.AddChild, new ClassicCheck());
    }

    public void Shutdown()
    {
        NpcPortraitCache.Clear();
    }
}
