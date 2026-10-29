using KnightOnlineUiClassic.Windows;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic;

public sealed class Plugin : IPlugin
{
    public static UiKit Kit { get; private set; } = null!;

    /// <summary>
    /// Whether a plugin instance is running. The client keeps the assembly loaded when the theme is switched off in
    /// Settings > Plugins, so deferred calls and nodes that outlive <see cref="Shutdown"/> check this first.
    /// </summary>
    public static bool Active => Kit != null;

    private PluginContext? _context;

    public void Initialize(PluginContext context)
    {
        _context = context;
        Kit = new UiKit(context);
        NativeSetup.Register(context);
        var ui = context.Ui;
        ui.ReplaceHud(HudPart.StatusBars, () => new NationHud(() => new StatusHud()));
        ui.ReplaceHud(HudPart.TargetFrame, () => new NationHud(() => new TargetFrame()));
        ui.ReplaceHud(HudPart.Hotbar, () => new NationHud(() => new HotkeyBar()));
        ui.ReplaceHud(HudPart.Chat, () => new NationHud(() => new ChatWindow()));
        ui.ReplaceHud(HudPart.CombatLog, () => new NationHud(() => new LogWindow()));
        ui.ReplaceHud(HudPart.Party, () => new NationHud(() => new ClassicPartyHud(Kit.Game.Party, () => NativeHudChat.World)));
        ui.ReplaceHud(HudPart.WarBoard, () => new NationHud(() => new ClassicWarBoard(Kit.Game.Party)));
        NativeHud.StyleWhispers(ClassicWhisperSkin.Apply, ClassicWhisperSkin.Line);
        ui.HideHud(HudPart.MiniMap);
        ui.HideHud(HudPart.Launcher);
        ui.HideHud(HudPart.ExpBar);
        NativeHud.Hide(NativeHudPart.MailIcon);
        NativeHud.Hide(NativeHudPart.AchievementsIcon);
        NativeHud.Hide(NativeHudPart.AttendanceIcon);
        NativeHud.Hide(NativeHudPart.PowerUpStoreIcon);
        NativeHud.Hide(NativeHudPart.GenieIcon);
        NativeHud.Hide(NativeHudPart.EventsIcon);
        NativeHud.Hide(NativeHudPart.DailyRankIcon);
        ui.ReplaceWindow("inventory", host => new InventoryWindow(host));
        ui.ReplaceWindow("character_info", host => new CharacterWindow(host));
        ui.ReplaceWindow("skills", host => new SkillWindow(host));
        // Keep native modern windows functional and apply the classic frame theme.
        foreach(var id in ui.WindowIds)
            if(id is not ("inventory" or "character_info" or "skills" or "vendor" or "party" or "seek_party" or "exchange" or "anvil" or "vipwarehouse_pin" or "chat_colors" or "merchantsearch" or "marketprice" or "shoppingmall" or "specialauction" or "equipview" or "genderchange" or "changehair" or "nationtransfer" or "rebirth" or "cape" or "pet" or "pethatch" or "chat_room" or "user_vote" or "user_vote_ask" or "user_vote_notice" or "repurchase") && !ClassicCommunicationSkin.WindowIds.Contains(id) && !ClassicMailSkin.WindowIds.Contains(id) && !ClassicCouncilRate.WindowIds.Contains(id) && !ClassicSiegeSkin.WindowIds.Contains(id) && !ClassicIdentitySkin.WindowIds.Contains(id) && !ClassicServiceSkin.WindowIds.Contains(id) && !ClassicStorageSkin.WindowIds.Contains(id) && !ClassicMerchantSkin.WindowIds.Contains(id) && !CharacterDetailsSkin.WindowIds.Contains(id) && !ClassicKingSkin.WindowIds.Contains(id) && !ClassicEventsSkin.WindowIds.Contains(id) && !ClassicProgressionSkin.WindowIds.Contains(id) && !ClassicMapFortuneSkin.WindowIds.Contains(id) && !ClassicBoardSkin.WindowIds.Contains(id) && !ClassicCommandListSkin.WindowIds.Contains(id) && id != ClassicQuestNotice.WindowId)
                ui.ExtendWindow(id, ClassicSkin.ExtendWindow);
        ui.ExtendWindow("chat_colors", ClassicChatColoursSkin.Extend);
        ui.ExtendWindow("merchantsearch", ClassicMerchantSearchSkin.Extend);
        ui.ExtendWindow("marketprice", ClassicMarketPriceSkin.Extend);
        ui.ExtendWindow("shoppingmall", ClassicStoreSkin.Extend);
        ui.ExtendWindow("specialauction", ClassicAuctionSkin.Extend);
        ui.ExtendWindow("equipview", ClassicEquipViewSkin.Extend);
        ui.ExtendWindow("genderchange", ClassicGenderSkin.Extend);
        ui.ExtendWindow("changehair", ClassicGenderSkin.Extend);
        ui.ExtendWindow(ClassicHairPaletteSkin.WindowId, ClassicHairPaletteSkin.Extend);
        ui.ExtendWindow("nationtransfer", ClassicGenderSkin.Extend);
        ui.ExtendWindow("rebirth", ClassicRebirthSkin.Extend);
        ui.ExtendWindow("cape", ClassicCapeSkin.Extend);
        ui.ExtendWindow("pet", ClassicPetSkin.Extend);
        NativeHud.Extend(NativeHudPart.FamiliarBar, ClassicPetBarSkin.Apply);
        NativeHud.Extend(NativeHudPart.FamiliarPlate, ClassicPetPlateSkin.Apply);
        ClassicWorldOverlays.Register();
        ui.ExtendWindow("pethatch", ClassicPetHatchSkin.Extend);
        ui.ExtendWindow(ClassicQuestNotice.WindowId, ClassicQuestNotice.Extend);
        ClassicQuestTracker.Register();
        ClassicPremiumChip.Register();
        foreach(var id in ClassicCommunicationSkin.WindowIds)ui.ExtendWindow(id,ClassicCommunicationSkin.Extend);
        ClassicSocialSkin.Register(ui);
        ClassicChatRoomSkin.Register(ui);
        ClassicCommandListSkin.Register(ui);
        ui.ReplacePlayerMenu(request => new ClassicPlayerMenu(request));
        ui.ReplaceTextInput(request => new ClassicTextPrompt(request));
        foreach(var id in ClassicMailSkin.WindowIds)ui.ExtendWindow(id,ClassicMailSkin.Extend);
        foreach(var id in ClassicCouncilRate.WindowIds)ui.ExtendWindow(id,ClassicCouncilRate.Extend);
        foreach(var id in ClassicSiegeSkin.WindowIds)ui.ExtendWindow(id,ClassicSiegeSkin.Extend);
        foreach(var id in ClassicKingSkin.WindowIds)ui.ExtendWindow(id,ClassicKingSkin.Extend);
        foreach(var id in ClassicMapFortuneSkin.WindowIds)ui.ExtendWindow(id,ClassicMapFortuneSkin.Extend);
        foreach(var id in ClassicBoardSkin.WindowIds)ui.ExtendWindow(id,ClassicBoardSkin.Extend);
        foreach(var id in ClassicIdentitySkin.WindowIds)ui.ExtendWindow(id,ClassicIdentitySkin.Extend);
        foreach(var id in ClassicServiceSkin.WindowIds)ui.ExtendWindow(id,ClassicServiceSkin.Extend);
        foreach(var id in ClassicEventsSkin.WindowIds)ui.ExtendWindow(id,ClassicEventsSkin.Extend);
        foreach(var id in ClassicItemsSkin.WindowIds)ui.ExtendWindow(id,ClassicItemsSkin.Extend);
        ui.ReplaceExitMenu(request=>new ClassicExitMenu(request));
        foreach(var id in ClassicEventFamily.WindowIds)ui.ExtendWindow(id,ClassicEventFamily.Extend);
        ui.ExtendWindow("vipwarehouse_pin", ClassicStoragePin.Extend);
        foreach(var id in ClassicStorageSkin.WindowIds)ui.ExtendWindow(id,ClassicStorageSkin.Extend);
        foreach(var id in ClassicMerchantSkin.WindowIds)ui.ExtendWindow(id,ClassicMerchantSkin.Extend);
        ui.ExtendWindow("vendor", ClassicVendorSkin.Extend);
        ui.ExtendWindow("repurchase", ClassicRepurchaseSkin.Extend);
        ui.ExtendWindow("exchange", ClassicExchangeSkin.Extend);
        ui.ExtendWindow("anvil", ClassicAnvilSkin.Extend);
        ui.ExtendWindow("anvil_choice", ClassicAnvilChoice.Extend);
        ui.ExtendWindow("seek_party", ClassicPartySkin.Extend);
        foreach(var id in ClassicProgressionSkin.WindowIds)ui.ExtendWindow(id,ClassicProgressionSkin.Extend);
        foreach (var id in CharacterDetailsSkin.WindowIds)
            ui.ExtendWindow(id, CharacterDetailsSkin.Extend);
        foreach (var id in ClassicUserVoteSkin.WindowIds) ui.ExtendWindow(id, ClassicUserVoteSkin.Extend);
        ui.ReplaceDialogs(request=>request.Title=="Magic Anvil"?new ClassicAnvilNotice(request):request.Title=="Merchant"?new ClassicMerchantNotice(request):request.Title is "Item Seal" or "Redistribute stats" or "Redistribute mastery" or "Create a Clan" or "Transformation" or "Akara's Altar" or "Nation Transfer" or "Rebirth" or "Clan Cape" or "Familiar Hatching" or "Familiar Transformation"?new ClassicServiceNotice(request):new MessageBox(request));
        ui.AddHud(() => new NationHud(() => new Taskbar()));
        ui.AddHud(() => new NationHud(() => new ClassicMerchantSigns()));
        context.Log.Info($"{Kit.LayoutCount} layouts, {Kit.TextureCount} textures");
        if (Godot.OS.GetCmdlineUserArgs().Contains("classic-check"))
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.CallDeferred(Godot.Node.MethodName.AddChild, new ClassicCheck());
    }

    /// <summary>
    /// Runs when the client switches the theme off (and on exit). The client then removes the UI registrations and
    /// rebuilds the World, which frees every Classic node inside it; this undoes everything else.
    /// </summary>
    public void Shutdown()
    {
        if (_context == null) return;
        NativeSetup.Unregister(_context.Game);
        FreeOutsideWorld();
        NpcPortraitCache.Clear();
        ClassicStraightArt.Clear();
        ClassicChatRoomSkin.ClearKnobs();
        Layout.MykoQuestLayout.Clear();
        CharacterPageRouter.Reset();
        Layout.ClassicNpcLayout.Reset();
        Kit.Release();
        _context = null;
        Kit = null!;
    }

    /// <summary>Frees the plugin's nodes that live outside the World (developer check, dialogs on the root).</summary>
    private static void FreeOutsideWorld()
    {
        if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree) return;
        var plugin = typeof(Plugin).Assembly;
        var found = new List<Godot.Node>();
        void Visit(Godot.Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is LibreKO.Scene.WorldRoot) continue;
                if (child.GetType().Assembly == plugin) found.Add(child);
                else Visit(child);
            }
        }
        Visit(tree.Root);
        foreach (var node in found)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
    }
}
