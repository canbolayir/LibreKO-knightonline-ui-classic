using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;

/// <summary>Mail, messenger, market, store and auction fixtures for the Classic audits.</summary>
public static partial class PreviewFixtures
{
    private static void PrepareCommunication(CanvasLayer layer, WorldRoot world)
    {
        foreach (var window in layer.GetChildren().OfType<HudWindow>()) NativeCommunication.Prepare(window, world);
    }

    private static void PrepareMarket(CanvasLayer layer, WorldRoot world)
    {
        foreach (var window in layer.GetChildren().OfType<HudWindow>()) NativeMarket.Prepare(window, world);
    }

    /// <summary>
    /// Keeps the item tooltip at the pointer every frame, as the running client's World does. A detached
    /// preview World is not processed and has no viewport to measure the pointer with.
    /// </summary>
    private static void TrackTooltip(WorldRoot world)
    {
        var panel = Field<PanelContainer>(world, "ItemTooltip.TipPanel");
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

    public static CanvasLayer BuildMailClassicUiPreview(WorldRoot world, string view)
    {
        ItemData.EnsureLoaded(); Call(world, "Mail.BuildWindow"); Call(world, "MailCompose.BuildComposeWindow");
        var layer = Field<CanvasLayer>(world, "Mail.Layer");
        PrepareCommunication(layer, world);
        layer.Ready += () => Callable.From(() =>
        {
            var mails = (List<MailEntry>)Native.Call(Native.StaticOwner("LibreKO.WorldPreview"), "MailEntries")!;
            mails.Add((MailEntry)Native.Call(Native.StaticOwner("LibreKO.WorldPreview"), "PusPreviewMail")!);
            for (int i = 0; i < 24; i++) mails.Add(new MailEntry { Id = i + 10, Sender = "LongCharacterName1299", Subject = "A long message subject that should stay in its own column", Read = i % 2 == 0, SentAt = DateTime.UtcNow.AddDays(-1), Attachments = MailAttachmentState.None });
            if (view == "empty") mails.Clear();
            Call(world, "Mail.OnList", mails); Call(world, "Mail.OnUnread", mails.Count(m => !m.Read));
            if (view is "inbox" or "empty") { SetField(world, "Mail.shown", true); Field<HudWindow>(world, "Mail.Window").Visible = true; }
            else if (view == "read")
            {
                Call(world, "Mail.Select", 3); Call(world, "Mail.OnRead", 3, true, string.Concat(Enumerable.Repeat("You completed the Collection Race in Moradon. Your rewards are attached to this letter.\n\n", 12)));
            }
            else if (view == "store")
            {
                var mail = mails.Last(m => m.Kind == MailKind.Store); Call(world, "Mail.Select", mail.Id); Call(world, "Mail.OnRead", mail.Id, true, "Your Power-Up Store purchase has arrived. Claim individual attachments below or claim all remaining attachments.");
            }
            else
            {
                var inv = Field<Inventory>(world, "Session.Self.Inventory");
                inv.EnsureLength(InventoryConstants.InventoryTotal);
                inv[Inventory.GridStart] = new ItemSlot { ItemId = 810418000, Count = 20, Durability = 1 };
                inv[Inventory.GridStart + 1] = new ItemSlot { ItemId = 379154000, Count = 1, Durability = 1 };
                inv[Inventory.GridStart + 2] = new ItemSlot { ItemId = 389018000, Count = 5, Durability = 1 };
                Field<SortedSet<string>>(world, "MailCompose.Contacts").UnionWith(new[] { "Rikka", "Zeus", "Ariel", "Marduk", "Ranger", "Guardian", "Magician", "LongCharacterName1299", "Warrior" });
                Game.Session.Self.Sheet.SetGold(100_000); Call(world, "MailCompose.OpenCompose");
                Field<LineEdit>(world, "MailCompose.Recipient").Text = "Rikka"; Field<LineEdit>(world, "MailCompose.SubjectInput").Text = "Apples for the raid";
                Field<TextEdit>(world, "MailCompose.BodyInput").Text = "Here are the apples you asked for. Good hunting!"; Field<MoneyEdit>(world, "MailCompose.Gold").Value = 25_000;
                Call(world, "MailCompose.OnBodyChanged");
                var picks = Field<List<(int Slot, int Count)>>(world, "MailCompose.Attachments");
                picks.Add((Inventory.GridStart, 20)); picks.Add((Inventory.GridStart + 1, 1)); Call(world, "MailCompose.RenderAttachments");
            }
        }).CallDeferred();
        world.RemoveChild(layer); return layer;
    }

    public static CanvasLayer BuildCommunicationUiPreview(WorldRoot world, string id)
    {
        CanvasLayer layer;
        if (id == "messenger")
        {
            Call(world, "Messenger.Build"); layer = Field<CanvasLayer>(world, "Messenger.msgrLayer"); PrepareCommunication(layer, world);
            SetField(world, "Messenger.msgrShown", true); Field<HudWindow>(world, "Messenger.msgrPanel").Visible = true;
            OnMessengerList(world, new List<MessengerBuddy>
            {
                new() { CharId = 1, Name = "Rikka", Online = true },
                new() { CharId = 2, Name = "LongCharacterName1299", Online = false },
                new() { CharId = 3, Name = "Ariel", Online = true },
            });
        }
        // The chat rooms are the nation chat windows now; BuildSocialUiPreview builds them.
        else throw new ArgumentException("Unknown communication window " + id);
        world.RemoveChild(layer); return layer;
    }

    /// <summary>Delivers a buddy-list reply the way the network does, through the client's reply event.</summary>
    public static void OnMessengerList(WorldRoot world, List<MessengerBuddy> list) =>
        Native.Get<Action<List<MessengerBuddy>>>(Game.Session.Messenger, "MessengerListEvent")!.Invoke(list);

    public static CanvasLayer BuildMarketPriceClassicUiPreview(WorldRoot world)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded(); Call(world, "ItemTooltip.Build"); Call(world, "MarketPrice.BuildMarketPricePanel");
        var layer = Field<CanvasLayer>(world, "MarketPrice.marketPriceLayer"); PrepareMarket(layer, world);
        var amount = new CanvasLayer { Visible = false }; SetField(world, "Merchant.AmountLayer", amount); layer.AddChild(amount);
        Field<CanvasLayer>(world, "ItemTooltip.tipLayer").Reparent(layer, false); TrackTooltip(world);
        world.RemoveChild(layer); return layer;
    }

    public static CanvasLayer BuildMerchantSearchClassicUiPreview(WorldRoot world)
    {
        ItemData.EnsureLoaded(); SkillData.EnsureLoaded(); Call(world, "MerchantSearch.BuildSearchPanel"); Call(world, "MarketPrice.BuildMarketPricePanel");
        var layer = Field<CanvasLayer>(world, "MerchantSearch.searchLayer"); PrepareMarket(layer, world);
        var marketPrice = Field<CanvasLayer>(world, "MarketPrice.marketPriceLayer"); PrepareMarket(marketPrice, world);
        var whispers = new CanvasLayer { Layer = 79 }; SetField(world, "Whispers.whisperLayer", whispers); layer.AddChild(whispers);
        marketPrice.Reparent(layer, false); Call(world, "MerchantSearch.OnSearchOpen");
        world.RemoveChild(layer); return layer;
    }

    public static void OnStorePurchase(WorldRoot world, PowerUpStoreResult result, int knightCash)
    {
        if (knightCash >= 0) Game.Session.Self.Sheet.SetKnightCash(knightCash);
        Call(world, "ShoppingMall.OnPurchase", result, knightCash);
    }

    public static CanvasLayer BuildPowerUpStoreClassicUiPreview(WorldRoot world, string variant)
    {
        Call(world, "ItemTooltip.Build");
        var layer = (CanvasLayer)Call(world, "Preview.BuildPowerUpStore", variant)!; PrepareMarket(layer, world);
        Field<CanvasLayer>(world, "ItemTooltip.tipLayer").Reparent(layer, false); TrackTooltip(world); return layer;
    }

    public static CanvasLayer BuildAuctionClassicUiPreview(WorldRoot world)
    {
        Call(world, "ItemTooltip.Build");
        ItemData.EnsureLoaded();
        var me = Game.Session.Self.LastEnter; me.Name = "Zeus"; Native.Call(Game.Session.Self, "SeedPreviewEnter", me);
        Game.Session.Self.Sheet.SetGold(845_300_000);
        var inv = Field<Inventory>(world, "Session.Self.Inventory"); int gridStart = Inventory.GridStart;
        inv.EnsureLength(gridStart + Inventory.GridCount);
        inv[gridStart + 3] = (ItemSlot)Native.Call(Native.StaticOwner("LibreKO.WorldPreview"), "PreviewItem", SpecialAuction.MythrilCheck, 1, 1)!;
        inv[gridStart + 7] = (ItemSlot)Native.Call(Native.StaticOwner("LibreKO.WorldPreview"), "PreviewItem", SpecialAuction.MythrilCheck, 1, 1)!;
        Call(world, "SpecialAuctionWindow.EnsureAuctionTable"); Call(world, "SpecialAuctionWindow.BuildPanel");
        var layer = Field<CanvasLayer>(world, "SpecialAuctionWindow.layer"); PrepareMarket(layer, world);
        Call(world, "SpecialAuctionWindow.Reset"); SetField(world, "SpecialAuctionWindow.AuctionOpening", true);
        int Const(string name) => Native.Get<int>(Native.StaticOwner("LibreKO.WorldPreview"), name);
        int[] items = [Const("AgilityNecklace"), Const("StrengthNecklace"), Const("ShadowPiece"), Const("ElfMetalEarrings"), Const("PreviewHpPotion"), Const("PreviewMpPotion"), Const("PreviewUpgradedWeapon"), 379021000];
        int previewDay = Const("AuctionPreviewDay"), previewSeconds = Const("AuctionPreviewSeconds");
        Call(world, "SpecialAuctionWindow.OnAuctionToday", new AuctionToday(SpecialAuction.Bidding, 1, previewSeconds, previewDay,
            [new AuctionOffer(0, items[0], 0, ""), new AuctionOffer(1, items[1], 1_012_000_000, "Rikka"), new AuctionOffer(2, items[2], 230_000_000, "Zeus")]));
        Call(world, "SpecialAuctionWindow.SelectAuctionLot", 1); Field<SpinBox>(world, "SpecialAuctionWindow.AuctionMillions").Value = 13; Field<SpinBox>(world, "SpecialAuctionWindow.AuctionCheckInput").Value = 1; Call(world, "SpecialAuctionWindow.RefreshAuctionTotal");
        var table = new List<AuctionScheduleLot>();
        for (int day = 1; day <= SpecialAuction.DaysPerGroup; day++)
            for (int slot = 0; slot < items.Length; slot++)
                table.Add(new AuctionScheduleLot(SpecialAuction.Row(1, day), slot, items[slot], slot == 4 ? 20 : 1, 1_000_000, 1_000_000, day > previewDay && slot == 3));
        SetField(world, "SpecialAuctionWindow.auctionTable", table.ToArray());
        Call(world, "SpecialAuctionWindow.OnAuctionToday", new AuctionToday(SpecialAuction.Bidding, 1, previewSeconds, previewDay,
            items.Select((item, slot) => new AuctionOffer(slot, item, slot == 1 ? 1_012_000_000 : slot == 2 ? 230_000_000 : 0, slot == 1 ? "Rikka" : slot == 2 ? "Zeus" : "")).ToArray()));
        Call(world, "SpecialAuctionWindow.SelectAuctionLot", 1); Field<SpinBox>(world, "SpecialAuctionWindow.AuctionMillions").Value = 13; Field<SpinBox>(world, "SpecialAuctionWindow.AuctionCheckInput").Value = 1; Call(world, "SpecialAuctionWindow.RefreshAuctionTotal");
        Field<CanvasLayer>(world, "ItemTooltip.tipLayer").Reparent(layer, false); TrackTooltip(world);
        world.RemoveChild(layer); return layer;
    }
}
