using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>Mail, messenger, chat-room, market, store and auction fixtures for the Classic audits.</summary>
public static partial class PreviewFixtures
{
    private static void PrepareCommunication(CanvasLayer layer, World world)
    {
        foreach (var window in layer.GetChildren().OfType<HudWindow>()) NativeCommunication.Prepare(window, world);
    }

    private static void PrepareMarket(CanvasLayer layer, World world)
    {
        foreach (var window in layer.GetChildren().OfType<HudWindow>()) NativeMarket.Prepare(window, world);
    }

    /// <summary>
    /// Keeps the item tooltip at the pointer every frame, as the running client's World does. A detached
    /// preview World is not processed and has no viewport to measure the pointer with.
    /// </summary>
    private static void TrackTooltip(World world)
    {
        var panel = Field<PanelContainer>(world, "_itemTipPanel");
        var tree = (SceneTree)Engine.GetMainLoop();
        void Place()
        {
            if (!GodotObject.IsInstanceValid(panel)) { tree.ProcessFrame -= Place; return; }
            if (!panel.Visible || panel.GetViewport() is not { } viewport) return;
            var screen = viewport.GetVisibleRect().Size; var at = viewport.GetMousePosition() + new Vector2(16, 16);
            var size = panel.Size; if (size.X <= 1 || size.Y <= 1) size = panel.GetCombinedMinimumSize();
            at.X = Mathf.Clamp(at.X, 8, Mathf.Max(8, screen.X - size.X - 8)); at.Y = Mathf.Clamp(at.Y, 8, Mathf.Max(8, screen.Y - size.Y - 8));
            panel.Position = at;
        }
        tree.ProcessFrame += Place;
    }

    public static CanvasLayer BuildMailClassicUiPreview(World world, string view)
    {
        ItemData.EnsureLoaded(); Call(world, "BuildMailWindow"); Call(world, "BuildMailComposeWindow");
        var layer = Field<CanvasLayer>(world, "_mailLayer");
        PrepareCommunication(layer, world);
        layer.Ready += () => Callable.From(() =>
        {
            var mails = (List<MailEntry>)Native.Call(typeof(World), "MailUiPreviewEntries")!;
            mails.Add((MailEntry)Native.Call(typeof(World), "PusPreviewMail")!);
            for (int i = 0; i < 24; i++) mails.Add(new MailEntry { Id = i + 10, Sender = "LongCharacterName1299", Subject = "A long message subject that should stay in its own column", Read = i % 2 == 0, SentAt = DateTime.UtcNow.AddDays(-1), Attachments = MailAttachmentState.None });
            if (view == "empty") mails.Clear();
            Call(world, "OnMailList", mails); Call(world, "OnMailUnread", mails.Count(m => !m.Read));
            if (view is "inbox" or "empty") { SetField(world, "_mailShown", true); Field<HudWindow>(world, "_mailWindow").Visible = true; }
            else if (view == "read")
            {
                Call(world, "SelectMail", 3); Call(world, "OnMailRead", 3, true, string.Concat(Enumerable.Repeat("You completed the Collection Race in Moradon. Your rewards are attached to this letter.\n\n", 12)));
            }
            else if (view == "store")
            {
                var mail = mails.Last(m => m.Kind == MailKind.Store); Call(world, "SelectMail", mail.Id); Call(world, "OnMailRead", mail.Id, true, "Your Power-Up Store purchase has arrived. Claim individual attachments below or claim all remaining attachments.");
            }
            else
            {
                var inv = Field<Inventory>(world, "Inv");
                inv.EnsureLength(InventoryConstants.InventoryTotal);
                inv[Inventory.GridStart] = new ItemSlot { ItemId = 810418000, Count = 20, Durability = 1 };
                inv[Inventory.GridStart + 1] = new ItemSlot { ItemId = 379154000, Count = 1, Durability = 1 };
                inv[Inventory.GridStart + 2] = new ItemSlot { ItemId = 389018000, Count = 5, Durability = 1 };
                Field<SortedSet<string>>(world, "_mailContacts").UnionWith(new[] { "Rikka", "Zeus", "Ariel", "Marduk", "Ranger", "Guardian", "Magician", "LongCharacterName1299", "Warrior" });
                Net.I.Sheet.SetGold(100_000); Call(world, "OpenMailCompose");
                Field<LineEdit>(world, "_mailTo").Text = "Rikka"; Field<LineEdit>(world, "_mailSubject").Text = "Apples for the raid";
                Field<TextEdit>(world, "_mailBody").Text = "Here are the apples you asked for. Good hunting!"; Field<MoneyEdit>(world, "_mailGold").Value = 25_000;
                Call(world, "OnMailBodyChanged");
                var picks = Field<List<(int Slot, int Count)>>(world, "_mailAttachments");
                picks.Add((Inventory.GridStart, 20)); picks.Add((Inventory.GridStart + 1, 1)); Call(world, "RenderMailAttachments");
            }
        }).CallDeferred();
        world.RemoveChild(layer); return layer;
    }

    public static CanvasLayer BuildCommunicationUiPreview(World world, string id)
    {
        CanvasLayer layer;
        if (id == "messenger")
        {
            Call(world, "MessengerInit"); layer = Field<CanvasLayer>(world, "_msgrLayer"); PrepareCommunication(layer, world);
            SetField(world, "_msgrShown", true); Field<HudWindow>(world, "_msgrPanel").Visible = true;
            OnMessengerList(world, new List<MessengerBuddy>
            {
                new() { CharId = 1, Name = "Rikka", Online = true },
                new() { CharId = 2, Name = "LongCharacterName1299", Online = false },
                new() { CharId = 3, Name = "Ariel", Online = true },
            });
        }
        else
        {
            Call(world, "ChatRoomInit"); layer = Field<CanvasLayer>(world, "_chatRoomLayer"); PrepareCommunication(layer, world);
            SetField(world, "_chatRoomShown", true); Field<HudWindow>(world, "_chatRoomPanel").Visible = true;
            SetField(world, "_chatRoomCurrentId", 7); Call(world, "UpdateChatRoomStatus");
            OnChatRoomList(world, new List<ChatRoomEntry>
            {
                new() { RoomId = 7, Name = "Moradon hunting party", MemberCount = 8 },
                new() { RoomId = 8, Name = "A long room name for the market", MemberCount = 123 },
                new() { RoomId = 9, Name = "Ronark raid", MemberCount = 24 },
            });
            Call(world, "AppendChatRoomLine", "System", "Joined the room.", UiTheme.Gold);
            Call(world, "AppendChatRoomLine", "Rikka", "Meet near the bridge after stocking potions.", UiTheme.TextHi);
            Call(world, "AppendChatRoomLine", "LongCharacterName1299", "This is a long message that should wrap inside the fixed chat viewport. We are looking for a warrior and a priest for the next hunt.", UiTheme.TextHi);
        }
        world.RemoveChild(layer); return layer;
    }

    /// <summary>Delivers a buddy-list reply the way the network does, through the client's reply event.</summary>
    public static void OnMessengerList(World world, List<MessengerBuddy> list) =>
        Native.Get<Action<List<MessengerBuddy>>>(Net.I, "MessengerListEvent")!.Invoke(list);

    /// <summary>Delivers a room-list reply the way the network does, through the client's reply event.</summary>
    public static void OnChatRoomList(World world, List<ChatRoomEntry> list) =>
        Native.Get<Action<List<ChatRoomEntry>>>(Net.I, "ChatRoomListEvent")!.Invoke(list);

    public static CanvasLayer BuildMarketPriceClassicUiPreview(World world)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded(); Call(world, "BuildItemTooltip"); Call(world, "BuildMarketPricePanel");
        var layer = Field<CanvasLayer>(world, "_marketPriceLayer"); PrepareMarket(layer, world);
        var amount = new CanvasLayer { Visible = false }; SetField(world, "_amountLayer", amount); layer.AddChild(amount);
        Field<CanvasLayer>(world, "_itemTipLayer").Reparent(layer, false); TrackTooltip(world);
        world.RemoveChild(layer); return layer;
    }

    public static CanvasLayer BuildMerchantSearchClassicUiPreview(World world)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded(); Call(world, "BuildMerchantSearchPanel"); Call(world, "BuildMarketPricePanel");
        var layer = Field<CanvasLayer>(world, "_merchantSearchLayer"); PrepareMarket(layer, world);
        var marketPrice = Field<CanvasLayer>(world, "_marketPriceLayer"); PrepareMarket(marketPrice, world);
        var whispers = new CanvasLayer { Layer = 79 }; SetField(world, "_whisperLayer", whispers); layer.AddChild(whispers);
        marketPrice.Reparent(layer, false); Call(world, "OnMerchantSearchOpen");
        world.RemoveChild(layer); return layer;
    }

    public static CanvasLayer BuildPowerUpStoreClassicUiPreview(World world, string variant)
    {
        Call(world, "BuildItemTooltip");
        var layer = (CanvasLayer)Call(world, "BuildPowerUpStoreUiPreview", variant)!; PrepareMarket(layer, world);
        Field<CanvasLayer>(world, "_itemTipLayer").Reparent(layer, false); TrackTooltip(world); return layer;
    }

    public static CanvasLayer BuildAuctionClassicUiPreview(World world)
    {
        Call(world, "BuildItemTooltip");
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter; me.Name = "Zeus"; Native.Call(Net.I, "SeedPreviewEnter", me);
        Net.I.Sheet.SetGold(845_300_000);
        var inv = Field<Inventory>(world, "Inv"); int gridStart = Inventory.GridStart;
        inv.EnsureLength(gridStart + Native.Get<int>(typeof(World), "GridCount"));
        inv[gridStart + 3] = (ItemSlot)Native.Call(typeof(World), "PreviewItem", SpecialAuction.MythrilCheck, 1, 1)!;
        inv[gridStart + 7] = (ItemSlot)Native.Call(typeof(World), "PreviewItem", SpecialAuction.MythrilCheck, 1, 1)!;
        Call(world, "EnsureAuctionTable"); Call(world, "BuildSpecialAuctionPanel");
        var layer = Field<CanvasLayer>(world, "_specialAuctionLayer"); PrepareMarket(layer, world);
        Call(world, "ResetSpecialAuction"); SetField(world, "_auctionOpening", true);
        int Const(string name) => Native.Get<int>(typeof(World), name);
        int[] items = [Const("AgilityNecklace"), Const("StrengthNecklace"), Const("ShadowPiece"), Const("ElfMetalEarrings"), Const("PreviewHpPotion"), Const("PreviewMpPotion"), Const("PreviewUpgradedWeapon"), 379021000];
        int previewDay = Const("AuctionPreviewDay"), previewSeconds = Const("AuctionPreviewSeconds");
        Call(world, "OnAuctionToday", new AuctionToday(SpecialAuction.Bidding, 1, previewSeconds, previewDay,
            [new AuctionOffer(0, items[0], 0, ""), new AuctionOffer(1, items[1], 1_012_000_000, "Rikka"), new AuctionOffer(2, items[2], 230_000_000, "Zeus")]));
        Call(world, "SelectAuctionLot", 1); Field<SpinBox>(world, "_auctionMillions").Value = 13; Field<SpinBox>(world, "_auctionCheckInput").Value = 1; Call(world, "RefreshAuctionTotal");
        var table = new List<AuctionScheduleLot>();
        for (int day = 1; day <= SpecialAuction.DaysPerGroup; day++)
            for (int slot = 0; slot < items.Length; slot++)
                table.Add(new AuctionScheduleLot(SpecialAuction.Row(1, day), slot, items[slot], slot == 4 ? 20 : 1, 1_000_000, 1_000_000, day > previewDay && slot == 3));
        SetField(world, "_auctionTable", table.ToArray());
        Call(world, "OnAuctionToday", new AuctionToday(SpecialAuction.Bidding, 1, previewSeconds, previewDay,
            items.Select((item, slot) => new AuctionOffer(slot, item, slot == 1 ? 1_012_000_000 : slot == 2 ? 230_000_000 : 0, slot == 1 ? "Rikka" : slot == 2 ? "Zeus" : "")).ToArray()));
        Call(world, "SelectAuctionLot", 1); Field<SpinBox>(world, "_auctionMillions").Value = 13; Field<SpinBox>(world, "_auctionCheckInput").Value = 1; Call(world, "RefreshAuctionTotal");
        Field<CanvasLayer>(world, "_itemTipLayer").Reparent(layer, false); TrackTooltip(world);
        world.RemoveChild(layer); return layer;
    }
}
