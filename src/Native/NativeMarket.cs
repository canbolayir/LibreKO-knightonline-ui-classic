using System.Collections;
using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Native;

public static partial class NativeSetup
{
    static partial void RegisterMarket(PluginContext context)
    {
        foreach (var id in NativeMarket.WindowIds) NativeWindows.Prepare(id, NativeMarket.Prepare);
    }
}

/// <summary>
/// Prepares the native Market Price, Merchant Search, Power-Up Store and Akara's Altar windows for the
/// Classic skins: names the native controls, mirrors native state into the metas the skins read, and
/// applies the Classic theme styleboxes wherever the client re-applies its own materials.
/// </summary>
public static class NativeMarket
{
    public static readonly string[] WindowIds = { "marketprice", "merchantsearch", "shoppingmall", "specialauction" };

    private const string NoMatchingItems = "No matching items.";
    // The Classic store shows its categories as tabs over the item well; the narrower cart keeps them on one row
    // at the usual 960-pixel store width.
    private const float CompactStoreWidth = 1100;
    private const float CompactCategoryWidth = 180;
    private const float CompactCartWidth = 264;

    public static void Prepare(HudWindow window, WorldRoot world)
    {
        switch (window.Id)
        {
            case "marketprice": PrepareMarketPrice(window, world); break;
            case "merchantsearch": PrepareMerchantSearch(window, world); break;
            case "shoppingmall": PrepareStore(window, world); break;
            case "specialauction": PrepareAuction(window, world); break;
        }
    }

    private static bool NameAll(Node?[] nodes, string[] names)
    {
        if (nodes.Any(n => n == null || !GodotObject.IsInstanceValid(n))) return false;
        for (int i = 0; i < nodes.Length; i++) nodes[i]!.Name = names[i];
        return true;
    }

    // Market Price -------------------------------------------------------------------------------

    private static void PrepareMarketPrice(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_market_price_controls")) return;
        var slot = Native.Get<ItemSlotView>(world, "MarketPrice.marketPriceSlot");
        var name = Native.Get<Label>(world, "MarketPrice.marketPriceName");
        var trades = Native.Get<Label>(world, "MarketPrice.marketPriceTrades");
        var chart = Native.Get<PriceChart>(world, "MarketPrice.marketPriceChart");
        var updated = Native.Get<Label>(world, "MarketPrice.marketPriceUpdated");
        var status = Native.Get<Label>(world, "MarketPrice.marketPriceStatus");
        var search = Native.Get<ItemSearchPanel>(world, "MarketPrice.MarketPriceSearch");
        if (slot == null || name == null || trades == null || chart == null || updated == null || status == null || search == null) return;
        var find = name.GetParent().GetChildren().OfType<Button>().FirstOrDefault();
        var caption = trades.GetParent().GetChildren().OfType<Label>().FirstOrDefault(l => l != trades);
        var latest = updated.GetParent().GetChildren().OfType<Label>().LastOrDefault(l => l != updated);
        if (!PrepareItemSearch(search)) return;
        if (!NameAll(new Node?[] { slot, name, find, caption, trades, chart, updated, latest, status },
            new[] { "market_price_item", "market_price_name", "market_price_search", "market_price_trades_caption", "market_price_trades", "market_price_chart", "market_price_updated", "market_price_latest", "market_price_status" }))
            return;
        if (NativeChartBackground.Supported(chart)) chart.AddChild(new NativeChartBackground(chart));
        int noHistoryId = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.MarketPriceFeature"), "MarketPriceNoHistoryText");
        NativeWindows.Sync(window, () =>
        {
            name.TooltipText = name.Text;
            // A cached "no history" reply is drawn as an empty five-day history; show it as no history instead.
            int item = Native.Get<int>(world, "MarketPrice.marketPriceItem");
            if (item == 0 || updated.Text.Length == 0 || Native.Get<IDictionary>(world, "MarketPrice.marketPriceCache") is not { } cache) return;
            if (cache[item] is not MarketPriceReply { Result: MarketPrice.NoHistory }) return;
            Native.Call(world, "MarketPrice.ClearMarketPriceChart");
            Native.Call(world, "MarketPrice.SetMarketPriceStatus", ItemData.Text(noHistoryId, "There is no trade history for this item"));
        });
        window.SetMeta("classic_market_price_controls", 1);
    }

    private static bool PrepareItemSearch(ItemSearchPanel panel)
    {
        var query = Native.Get<LineEdit>(panel, "query");
        var pick = Native.Get<OptionButton>(panel, "pick");
        var group = Native.Get<OptionButton>(panel, "groupPicker");
        var level = Native.Get<OptionButton>(panel, "levelPicker");
        var summary = Native.Get<Label>(panel, "summary");
        var results = Native.Get<VBoxContainer>(panel, "results");
        var tabs = Native.Get<IDictionary>(panel, "tabButtons");
        if (query == null || pick == null || group == null || level == null || summary == null || results == null || tabs == null) return false;
        var heading = panel.GetChildCount() > 0 ? panel.GetChild(0) as Control : null;
        var find = query.GetParent().GetChildren().OfType<Button>().FirstOrDefault();
        if (!NameAll(new Node?[] { heading, query, find, pick, group, level, summary, results.GetParent(), results },
            new[] { "item_search_heading", "item_search_query", "item_search_find", "item_search_name", "item_search_group", "item_search_level", "item_search_summary", "item_search_scroll", "item_search_results" }))
            return false;
        foreach (DictionaryEntry tab in tabs) ((Node)tab.Value!).Name = "item_search_tab_" + tab.Key;
        query.KeepEditingOnTextSubmit = true;
        NativeWindows.OnDescendantAdded(results, node => { if (node.GetParent() == results) NativeCommunication.DetachQueued(results); });
        return true;
    }

    // Merchant Search ----------------------------------------------------------------------------

    private static void PrepareMerchantSearch(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_merchant_search_controls")) return;
        var edit = Native.Get<LineEdit>(world, "MerchantSearch.searchEdit");
        var scope = Native.Get<OptionButton>(world, "MerchantSearch.searchScope");
        var status = Native.Get<Label>(world, "MerchantSearch.searchStatus");
        var pages = Native.Get<HBoxContainer>(world, "MerchantSearch.searchPages");
        var lines = Native.Get<IList>(world, "MerchantSearch.searchLines");
        var model = Native.Get<MerchantSearch>(world, "MerchantSearch.search");
        if (edit == null || scope == null || status == null || pages == null || lines == null || model == null) return;
        var body = window.Body;
        var top = edit.GetParent();
        var footer = status.GetParent();
        var header = top.GetParent().GetChild(top.GetIndex() + 1);
        var tip = body.GetChildren().OfType<Label>().FirstOrDefault();
        var topLabels = top.GetChildren().OfType<Label>().ToArray();
        var topButtons = top.GetChildren().OfType<Button>().Where(b => b != scope).ToArray();
        var headerLabels = header.GetChildren().OfType<Label>().ToArray();
        var byName = header.GetChildren().OfType<Button>().FirstOrDefault();
        var priceHeader = header.GetChildren().OfType<VBoxContainer>().FirstOrDefault();
        var sorts = priceHeader?.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Button>().ToArray();
        var footerButtons = footer.GetChildren().OfType<Button>().ToArray();
        if (topLabels.Length < 2 || topButtons.Length < 2 || headerLabels.Length < 3 || byName == null || priceHeader == null || sorts is not { Length: >= 2 } || footerButtons.Length < 3) return;
        var nodes = new Node?[]
        {
            tip, topLabels[0], edit, topButtons[0], topButtons[1], topLabels[1], scope, headerLabels[0], headerLabels[1], headerLabels[2],
            byName, priceHeader.GetChildren().OfType<Label>().FirstOrDefault(), sorts[0], sorts[1], status, footerButtons[0], pages, footerButtons[1], footerButtons[^1],
        };
        var names = new[]
        {
            "tip", "query_label", "query", "find", "all", "scope_label", "scope", "header_chat", "header_location", "header_history",
            "name_sort", "header_price", "low_sort", "high_sort", "status", "previous", "pages", "next", "refresh",
        };
        if (!NameAll(nodes, names.Select(n => "merchant_search_" + n).ToArray())) return;
        for (int index = 0; index < lines.Count; index++)
        {
            var line = lines[index]!;
            foreach (var part in new[] { "Root", "Whisper", "Move", "View", "Icon", "Name", "Price" })
                if (Native.Get<Node>(line, part) is { } node) node.Name = "merchant_search_" + (part == "Root" ? "row" : part.ToLowerInvariant()) + "_" + index;
        }
        edit.KeepEditingOnTextSubmit = true;
        NativeWindows.OnDescendantAdded(pages, node => { if (node.GetParent() == pages) NativeCommunication.DetachQueued(pages); });
        NativeWindows.Sync(window, () =>
        {
            var visible = model.Visible();
            for (int i = 0; i < lines.Count; i++)
            {
                string tooltip = i < visible.Count ? visible[i].ItemName + "\n" + visible[i].Seller : "";
                if (Native.Get<Control>(lines[i]!, "Name") is { } label) label.TooltipText = tooltip;
                if (Native.Get<Control>(lines[i]!, "Icon") is { } icon) icon.TooltipText = tooltip;
            }
            if (visible.Count == 0 && status.Text.Length == 0 && !Native.Get<bool>(world, "MerchantSearch.searchLoading")) status.Text = NoMatchingItems;
        });
        window.SetMeta("classic_merchant_search_controls", 1);
    }

    // Power-Up Store -----------------------------------------------------------------------------

    private static readonly (string Field, string Name)[] StoreControls =
    {
        ("ShoppingMall.CatalogView.categoryList", "category_list"), ("ShoppingMall.CatalogView.gridTitle", "grid_title"), ("ShoppingMall.CatalogView.cashValue", "cash"),
        ("ShoppingMall.CatalogView.searchInput", "query"), ("ShoppingMall.CatalogView.sortPick", "sort"), ("ShoppingMall.CatalogView.gridScroll", "grid_scroll"), ("ShoppingMall.CatalogView.grid", "grid"), ("ShoppingMall.CatalogView.gridEmpty", "grid_empty"),
        ("ShoppingMall.CartView.cartTitle", "cart_title"), ("ShoppingMall.CartView.clearCart", "clear_cart"), ("ShoppingMall.CartView.cartScroll", "cart_scroll"),
        ("ShoppingMall.CartView.cartLines", "cart_lines"), ("ShoppingMall.CartView.cartEmpty", "cart_empty"), ("ShoppingMall.CartView.giftButton", "gift"),
        ("ShoppingMall.CartView.giftBanner", "gift_banner"), ("ShoppingMall.CartView.giftName", "gift_name"), ("ShoppingMall.CartView.giftInfo", "gift_info"),
        ("ShoppingMall.CartView.totalRow", "total"), ("ShoppingMall.CartView.shortfall", "shortfall"), ("ShoppingMall.CartView.checkout", "checkout"), ("ShoppingMall.CartView.cartStatus", "cart_status"),
        ("ShoppingMall.overlay", "overlay"), ("ShoppingMall.DetailsView.detailsBox", "details"), ("ShoppingMall.DetailsView.detailsIcon", "details_icon"),
        ("ShoppingMall.DetailsView.detailsName", "details_name"), ("ShoppingMall.DetailsView.detailsCategory", "details_category"), ("ShoppingMall.DetailsView.detailsDescription", "details_description"),
        ("ShoppingMall.DetailsView.detailsPrice", "details_price"), ("ShoppingMall.DetailsView.detailsAdd", "details_add"), ("ShoppingMall.DetailsView.detailsBuy", "details_buy"), ("ShoppingMall.DetailsView.detailsStatus", "details_status"),
        ("PowerUpStoreGift.giftBox", "gift_box"), ("PowerUpStoreGift.GiftSearch", "gift_query"), ("PowerUpStoreGift.giftPickPane", "gift_pick"),
        ("PowerUpStoreGift.giftListTitle", "gift_list_title"), ("PowerUpStoreGift.giftList", "gift_list"), ("PowerUpStoreGift.giftCard", "gift_card"),
        ("PowerUpStoreGift.giftCardName", "recipient_name"), ("PowerUpStoreGift.giftCardInfo", "recipient_info"), ("PowerUpStoreGift.giftCardWarning", "recipient_warning"), ("PowerUpStoreGift.giftStatus", "gift_status"),
    };

    private static void PrepareStore(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_store_controls")) return;
        var controls = StoreControls.Select(c => Native.Get<Node>(world, c.Field)).ToArray();
        var columns = window.Body.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Control>().ToArray();
        var tools = Native.Get<Node>(world, "ShoppingMall.CatalogView.sortPick")?.GetParent() is BoxContainer row ? Reorientable(row) : null;
        if (columns is not { Length: 3 } || tools == null) return;
        if (!NameAll(controls.Append(window.Body).Append(columns[0]).Append(columns[2]).Append(tools).ToArray(),
            StoreControls.Select(c => c.Name).Append("body").Append("categories").Append("cart").Append("tools").Select(n => "pus_" + n).ToArray()))
            return;
        var categories = Native.Get<VBoxContainer>(world, "ShoppingMall.CatalogView.categoryList")!;
        var grid = Native.Get<GridContainer>(world, "ShoppingMall.CatalogView.grid")!;
        var cartLines = Native.Get<VBoxContainer>(world, "ShoppingMall.CartView.cartLines")!;
        var giftList = Native.Get<VBoxContainer>(world, "PowerUpStoreGift.giftList")!;
        NativeWindows.OnDescendantAdded(categories, node => DescribeCategory(world, categories, node));
        NativeWindows.OnDescendantAdded(grid, node => DescribeCard(world, grid, node));
        NativeWindows.OnDescendantAdded(cartLines, node => DescribeCartLine(world, cartLines, node));
        NativeWindows.OnDescendantAdded(giftList, node => DescribeContact(giftList, node));
        foreach (var node in categories.GetChildren()) DescribeCategory(world, categories, node);
        foreach (var node in grid.GetChildren()) DescribeCard(world, grid, node);
        foreach (var node in cartLines.GetChildren()) DescribeCartLine(world, cartLines, node);
        foreach (var node in giftList.GetChildren()) DescribeContact(giftList, node);
        bool compact = false;
        NativeWindows.Sync(window, () =>
        {
            compact = FitCompactStore(window, world, columns[0], columns[2], tools, compact);
            CentreTouchStore(window);
        });
        window.SetMeta("classic_store_world", world);
        window.SetMeta("classic_store_controls", 1);
    }

    /// <summary>
    /// The card buttons of the local Power Up Store art: Buy opens the card's native details (which hold Buy now), and
    /// Add to Basket is the details' native Add to cart.
    /// </summary>
    internal static bool StoreCardAction(HudWindow window, Control card, bool addToCart)
    {
        if (StoreWorld(window) is not { } world || CardEntry(world, card) is not { } entry || StorePending(window)) return false;
        Native.Call(world, "ShoppingMall.OpenDetails", entry);
        if (addToCart) Native.Call(world, "ShoppingMall.AddDetailsToCart");
        return true;
    }

    /// <summary>True while a native purchase waits for the server.</summary>
    public static bool StorePending(HudWindow window) => StoreWorld(window) is { } world && Convert.ToInt32(Native.Get<object>(world, "ShoppingMall.Model.Pending")) != 0;

    private static WorldRoot? StoreWorld(HudWindow window) => window.HasMeta("classic_store_world") ? window.GetMeta("classic_store_world").AsGodotObject() as WorldRoot : null;

    /// <summary>Replaces the store's search row with a box that can stack vertically on compact screens.</summary>
    private static BoxContainer Reorientable(BoxContainer row)
    {
        if (row is not HBoxContainer || row.GetParent() is not { } parent) return row;
        var box = new BoxContainer { SizeFlagsHorizontal = row.SizeFlagsHorizontal, SizeFlagsVertical = row.SizeFlagsVertical };
        box.AddThemeConstantOverride("separation", row.GetThemeConstant("separation"));
        parent.AddChild(box);
        parent.MoveChild(box, row.GetIndex());
        foreach (var child in row.GetChildren()) child.Reparent(box, false);
        parent.RemoveChild(row);
        row.QueueFree();
        return box;
    }

    private static void DescribeCategory(WorldRoot world, VBoxContainer list, Node node)
    {
        if (node is not Button button || button.GetParent() != list) return;
        var categories = Native.Get<IList>(world, "ShoppingMall.Model.Categories");
        var catalogue = Native.Get<IList>(world, "ShoppingMall.Model.Catalog");
        var search = Native.Get<LineEdit>(world, "ShoppingMall.CatalogView.searchInput");
        if (categories == null || catalogue == null || search == null) return;
        var ids = new List<int>();
        if (catalogue.Cast<PowerUpStoreEntry>().Any(e => e.Featured)) ids.Add(PowerUpStoreCatalog.FeaturedCategory);
        ids.AddRange(categories.Cast<ShoppingMallCategory>().Select(c => (int)c.Id));
        int index = button.GetIndex();
        if (index >= ids.Count) return;
        button.Name = "pus_category_" + ids[index];
        button.SetMeta("pus_category_selected", ids[index] == Native.Get<int>(world, "ShoppingMall.CatalogView.Category") && search.Text.Trim().Length == 0);
    }

    private static PowerUpStoreEntry? CardEntry(WorldRoot world, Control card)
    {
        if (Native.Get<IDictionary>(world, "ShoppingMall.CatalogView.cards") is not { } cards) return null;
        foreach (var view in cards.Values)
            if (Native.Get<Control>(view!, "Card") == card) return Native.Get<PowerUpStoreEntry>(view!, "Entry");
        return null;
    }

    private static void DescribeCard(WorldRoot world, GridContainer grid, Node node)
    {
        if (node is not PanelContainer card || card.GetParent() != grid || CardEntry(world, card) is not { } entry) return;
        card.Name = "pus_card_" + entry.Id;
        card.SetMeta("pus_card_id", entry.Id);
        card.SetMeta("pus_card_featured", entry.Featured);
        card.SetMeta("pus_card_active", false);
        var box = card.GetChildren().OfType<VBoxContainer>().FirstOrDefault();
        if (box?.GetChildren().OfType<PanelContainer>().FirstOrDefault() is { } icon) icon.Name = "pus_card_icon";
        if (box?.GetChildren().OfType<Label>().FirstOrDefault() is { } name) { name.Name = "pus_card_name"; name.TooltipText = entry.Name; }
        var overlay = card.GetChildren().OfType<Control>().LastOrDefault(c => c is not Container);
        var chips = overlay?.GetChildren().OfType<PanelContainer>().Where(c => c.TooltipText != entry.Name && c.TooltipText != "").ToArray() ?? Array.Empty<PanelContainer>();
        foreach (var chip in chips) chip.Name = chip.TooltipText switch { "In your cart" => "pus_cart_chip", "Time left at this price" => "pus_timer_chip", _ => "pus_featured_badge" };
        card.ThemeChanged += () => StyleCard(world, card);
    }

    /// <summary>Applies the Classic card material for the card's native state whenever the client re-applies its own.</summary>
    private static void StyleCard(WorldRoot world, PanelContainer card)
    {
        if (!card.HasMeta("pus_card_id") || !card.IsInsideTree()) return;
        int id = card.GetMeta("pus_card_id").AsInt32();
        int inCart = Native.Get<PowerUpStoreCart>(world, "ShoppingMall.Model.Cart")?.Lines.FirstOrDefault(l => l.Entry.Id == id)?.Count ?? 0;
        bool active = inCart > 0 || Native.Get<int>(world, "ShoppingMall.CatalogView.hoveredCard") == id && Convert.ToInt32(Native.Get<object>(world, "ShoppingMall.modal")) == 0;
        card.SetMeta("pus_card_active", active);
        string key = "pus_card_" + (card.GetMeta("pus_card_featured").AsBool() ? "featured_" : "") + (active ? "active" : "normal");
        if (!card.HasThemeStylebox(key)) return;
        var style = card.GetThemeStylebox(key);
        if (card.GetThemeStylebox("panel") != style) card.AddThemeStyleboxOverride("panel", style);
    }

    private static void DescribeCartLine(WorldRoot world, VBoxContainer lines, Node node)
    {
        if (node is not Control row || row.GetParent() != lines) return;
        var cart = Native.Get<PowerUpStoreCart>(world, "ShoppingMall.Model.Cart")?.Lines.ToArray();
        int index = row.GetIndex();
        if (cart != null && index < cart.Length) row.Name = "pus_cart_line_" + cart[index].Entry.Id;
    }

    private static void DescribeContact(VBoxContainer list, Node node)
    {
        if (node is not PanelContainer row || row.GetParent() != list) return;
        void Style(string key) { if (row.HasThemeStylebox(key)) row.AddThemeStyleboxOverride("panel", row.GetThemeStylebox(key)); }
        row.MouseEntered += () => Style("pus_contact_active");
        row.MouseExited += () => Style("pus_contact_normal");
    }

    /// <summary>Narrows the category and cart columns while the store is fitted to a compact screen.</summary>
    private static bool FitCompactStore(HudWindow window, WorldRoot world, Control categories, Control cart, BoxContainer tools, bool wasCompact)
    {
        if (!Native.Get<bool>(world, "ShoppingMall.shown") || !window.IsInsideTree()) return wasCompact;
        bool compact = PowerUpStoreLayout.WindowSize(window.GetViewportRect().Size, Platform.TouchUi).X < CompactStoreWidth;
        if (compact == wasCompact) return compact;
        int categoryWidth = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.PowerUpStoreCatalogView"), "CategoryWidth"), cartWidth = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.PowerUpStoreCartView"), "CartWidth");
        int margin = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.PowerUpStoreCartView"), "WellMargin");
        categories.CustomMinimumSize = new Vector2(compact ? CompactCategoryWidth : categoryWidth, 0);
        cart.CustomMinimumSize = new Vector2(compact ? CompactCartWidth : cartWidth, 0);
        foreach (var field in new[] { "ShoppingMall.CartView.cartEmpty", "ShoppingMall.CartView.shortfall", "ShoppingMall.CartView.cartStatus" })
            if (Native.Get<Control>(world, field) is { } label) label.CustomMinimumSize = new Vector2((compact ? CompactCartWidth : cartWidth) - 2 * margin, 0);
        // The Classic store moves the search row into its own header, where it always has room for one line.
        tools.Vertical = compact && !tools.HasMeta("pus_header_row");
        window.GetTree().CreateTimer(0).Timeout += () => RefitStore(window, world);
        return compact;
    }

    /// <summary>
    /// Refits on a later frame: the client's own deferred fit connects a one-shot centring to process_frame,
    /// and connecting it twice in one frame logs an error.
    /// </summary>
    private static void RefitStore(HudWindow window, WorldRoot world)
    {
        if (!GodotObject.IsInstanceValid(window) || !window.IsInsideTree()) return;
        var tree = window.GetTree();
        var centre = Callable.From((Action)Delegate.CreateDelegate(typeof(Action), Native.Get<object>(world, "ShoppingMall")!, "CentrePowerUpStore"));
        if (tree.IsConnected(SceneTree.SignalName.ProcessFrame, centre)) { tree.CreateTimer(0).Timeout += () => RefitStore(window, world); return; }
        Native.Call(world, "ShoppingMall.FitPowerUpStore");
    }

    /// <summary>On touch screens the full-screen store is centred without the pointer layout's screen margin.</summary>
    private static void CentreTouchStore(HudWindow window)
    {
        if (!Platform.TouchUi || !window.IsInsideTree()) return;
        var screen = window.GetViewportRect().Size;
        if (window.Position != WindowPlacement.Centre(screen, window.Size)) return;
        var centred = ((screen - window.Size) / 2).Max(Vector2.Zero);
        if (window.Position != centred) window.Position = centred;
    }

    // Akara's Altar ------------------------------------------------------------------------------

    private static readonly (string Field, string Name)[] AuctionFields =
    {
        ("SpecialAuctionWindow.auctionSelectedName", "selected_name"), ("SpecialAuctionWindow.auctionGold", "gold"), ("SpecialAuctionWindow.auctionChecks", "checks"),
        ("SpecialAuctionWindow.auctionTotal", "total"), ("SpecialAuctionWindow.auctionWords", "words"), ("SpecialAuctionWindow.auctionCurrent", "current"),
        ("SpecialAuctionWindow.auctionMinimum", "minimum"), ("SpecialAuctionWindow.auctionClock", "clock"), ("SpecialAuctionWindow.auctionStatus", "status"),
        ("SpecialAuctionWindow.AuctionMillions", "millions"), ("SpecialAuctionWindow.AuctionCheckInput", "check_input"), ("SpecialAuctionWindow.auctionSchedule", "schedule"),
        ("SpecialAuctionWindow.auctionBidList", "bid_list"), ("SpecialAuctionWindow.auctionWinList", "win_list"), ("SpecialAuctionWindow.auctionLog", "log"),
    };

    private static void PrepareAuction(HudWindow window, WorldRoot world)
    {
        if (window.HasMeta("classic_auction_controls")) return;
        var fields = AuctionFields.Select(f => Native.Get<Control>(world, f.Field)).ToArray();
        var tabs = Native.Get<IDictionary>(world, "SpecialAuctionWindow.auctionTabButtons");
        var pages = Native.Get<IDictionary>(world, "SpecialAuctionWindow.auctionPages");
        var slots = Native.Get<IList>(world, "SpecialAuctionWindow.auctionLotSlots");
        if (fields.Any(f => f == null) || tabs == null || pages == null || slots is not { Count: > 0 }) return;
        Control Field(string name) => fields[Array.FindIndex(AuctionFields, f => f.Name == name)]!;
        var placeBid = Native.TryGet<Button>(world, "SpecialAuctionWindow.auctionPlaceBid", out var place) ? place : Section(Field("total"))?.FindChildren("*", "Button", true, false).OfType<Button>().LastOrDefault();
        var refresh = Native.TryGet<Button>(world, "SpecialAuctionWindow.auctionRefresh", out var again) ? again : Field("clock").GetParent().GetChildren().OfType<Button>().FirstOrDefault();
        var tabButtons = new Dictionary<string, Button>();
        foreach (DictionaryEntry tab in tabs) tabButtons[tab.Key.ToString()!] = (Button)tab.Value!;
        var grid = ((Node)slots[0]!).GetParent()?.GetParent();
        var nodes = fields.Cast<Node?>().Concat(new Node?[]
        {
            placeBid, refresh, tabButtons.Values.First().GetParent(), grid,
            Section(Field("gold")), Section(Field("total")), Section(Field("current")),
        }).ToArray();
        var names = AuctionFields.Select(f => f.Name).Concat(new[] { "place_bid", "refresh", "tabs", "lots_grid", "balance", "bid", "status_section" });
        if (!NameAll(nodes, names.Select(n => "auction_" + n).ToArray())) return;
        foreach (var (key, button) in tabButtons) button.Name = "auction_tab_" + key;
        foreach (DictionaryEntry page in pages) { var node = (Node)page.Value!; node.Name = "auction_page_" + page.Key; node.GetParent().Name = "auction_pages"; }
        for (int i = 0; i < slots.Count; i++) ((Node)slots[i]!).Name = "auction_lot_" + i;
        if (Section(grid!) is { } lots) lots.Name = "auction_lots";
        if (Section(Field("selected_name")) is { } selection) selection.Name = "auction_selection";
        if (window.Body.GetChildren().OfType<Label>().LastOrDefault() is { } maintenance)
        {
            maintenance.Name = "auction_maintenance";
            maintenance.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        foreach (var button in tabButtons.Values)
        {
            button.SetMeta("auction_tab_selected", button.ButtonPressed);
            button.ThemeChanged += () => StyleAuctionTab(button);
        }
        var rowButtons = Native.TryGet<List<Button>>(world, "SpecialAuctionWindow.auctionRowButtons", out var tracked) ? tracked : null;
        foreach (var (list, won) in new[] { (Field("bid_list"), false), (Field("win_list"), true) })
            NativeWindows.OnDescendantAdded(list, node => DescribeAuctionRow(list, node, won, rowButtons));
        var log = Field("log");
        int noHistoryId = Native.Get<int>(Native.StaticOwner("LibreKO.Features.Windows.SpecialAuctionFeature"), "NoHistoryText");
        NativeWindows.OnDescendantAdded(log, node =>
        {
            if (node is Label line && line.GetParent() == log && line.Text != ItemData.Text(noHistoryId, "There is no transaction history"))
                line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        });
        NativeWindows.Sync(window, () =>
        {
            foreach (var button in tabButtons.Values) button.SetMeta("auction_tab_selected", button.ButtonPressed);
            CountBadges(window.Body);
        });
        window.SetMeta("classic_auction_controls", 1);
    }

    /// <summary>Stackable items show their count even for a single unit, as in the inventory.</summary>
    private static void CountBadges(Node root)
    {
        foreach (var slot in root.FindChildren("*", "PanelContainer", true, false).OfType<ItemSlotView>())
        {
            if (slot.Item.IsEmpty) continue;
            var def = ItemData.Get(slot.Item.ItemId);
            string badge = NativeUi.CountBadge(def, ItemData.ShownCount(def, slot.Item));
            if (slot.CountLabel is { } count && count.Text != badge) count.Text = badge;
        }
    }

    private static PanelContainer? Section(Node node)
    {
        for (var current = node.GetParent(); current != null; current = current.GetParent())
            if (current is PanelContainer section) return section;
        return null;
    }

    /// <summary>Re-applies the Classic tab materials after the client paints its own top-tab styles.</summary>
    private static void StyleAuctionTab(Button button)
    {
        if (_stylingTab || !button.HasThemeStylebox("auction_tab_selected") || !new[] { "normal", "hover", "pressed" }.Any(s => NativeTopTab(button.GetThemeStylebox(s)))) return;
        bool on = button.ButtonPressed;
        button.SetMeta("auction_tab_selected", on);
        _stylingTab = true;
        try
        {
            button.AddThemeStyleboxOverride("normal", button.GetThemeStylebox(on ? "auction_tab_selected" : "auction_tab_normal"));
            // The original hover art is the selected blue; an unselected tab keeps its own art while hovered.
            button.AddThemeStyleboxOverride("hover", button.GetThemeStylebox(on ? "auction_tab_selected" : "auction_tab_normal"));
            button.AddThemeStyleboxOverride("pressed", button.GetThemeStylebox("auction_tab_selected"));
        }
        finally { _stylingTab = false; }
    }

    private static bool _stylingTab;

    private static readonly StyleBoxFlat[] TopTabs = { UiTheme.TopTab(false), UiTheme.TopTab(true), UiTheme.TopTab(false, true), UiTheme.TopTab(true, true) };

    private static bool NativeTopTab(StyleBox style) =>
        style is StyleBoxFlat flat && TopTabs.Any(t => t.BgColor == flat.BgColor && t.BorderColor == flat.BorderColor && t.BorderWidthBottom == flat.BorderWidthBottom);

    private static void DescribeAuctionRow(Control list, Node node, bool won, List<Button>? rowButtons)
    {
        if (node is not PanelContainer row || row.GetParent() != list) return;
        foreach (var button in row.FindChildren("*", "Button", true, false).OfType<Button>())
            if (rowButtons == null || rowButtons.Contains(button)) button.SetMeta("auction_row_action", true);
        if (!won) return;
        var text = row.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<VBoxContainer>().FirstOrDefault();
        if (text?.GetChildren().OfType<Label>().FirstOrDefault() is not { } name) return;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = name.Text;
    }
}

/// <summary>
/// Paints the price chart over the Classic <c>chart_background</c> theme stylebox. The client draws its own
/// inset background inside <see cref="PriceChart"/>'s draw call; while the theme provides a Classic
/// background, the native drawing is hidden and repeated here on top of that background.
/// </summary>
public partial class NativeChartBackground : Control
{
    private const string ThemeKey = "chart_background";
    private static readonly string[] Members =
        { "days", "top", "bottom", "hoverDay", "hoverPart", "Plot", "Column", "ValueY", "PlotPadding", "BarWidth", "MarkerHalf", "MarkerHeight", "AxisWidth", "AxisFontSize", "DayFontSize", "BarFill", "BarHover", "BarEdge", "MaxColor", "MinColor", "Midline", "Baseline" };
    private readonly PriceChart _chart = null!;

    public NativeChartBackground() { }

    public NativeChartBackground(PriceChart chart)
    {
        _chart = chart;
        Name = "chart_background_painter";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public static bool Supported(PriceChart chart) =>
        Members.All(m => Native.Has(chart, m) || Native.HasMethod(chart, m));

    public override void _Process(double delta)
    {
        bool themed = _chart.HasThemeStylebox(ThemeKey);
        var tint = themed ? Colors.Transparent : Colors.White;
        if (_chart.SelfModulate != tint) _chart.SelfModulate = tint;
        Visible = themed;
        if (themed) QueueRedraw();
    }

    private T Value<T>(string member) => Native.Get<T>(_chart, member)!;
    private float Column(int index) => (float)Native.Call(_chart, "Column", index)!;
    private float ValueY(long value) => (float)Native.Call(_chart, "ValueY", value)!;

    public override void _Draw()
    {
        if (!_chart.HasThemeStylebox(ThemeKey)) return;
        DrawStyleBox(_chart.GetThemeStylebox(ThemeKey), new Rect2(Vector2.Zero, Size));
        var plot = Value<Rect2>("Plot");
        var font = GetThemeDefaultFont();
        var days = Value<IReadOnlyList<MarketPriceDay>>("days") ?? Array.Empty<MarketPriceDay>();
        long top = Value<long>("top"), bottom = Value<long>("bottom");
        int hoverDay = Value<int>("hoverDay"), hoverPart = Convert.ToInt32(Native.Get<object>(_chart, "hoverPart"));
        float padding = Value<float>("PlotPadding"), barWidth = Value<float>("BarWidth"), axisWidth = Value<float>("AxisWidth");
        int axisFont = Value<int>("AxisFontSize"), dayFont = Value<int>("DayFontSize");

        float mid = plot.Position.Y + plot.Size.Y / 2f;
        DrawLine(new Vector2(plot.Position.X, mid), new Vector2(plot.End.X, mid), Value<Color>("Midline"), 1f);
        DrawLine(new Vector2(plot.Position.X, plot.End.Y), new Vector2(plot.End.X, plot.End.Y), Value<Color>("Baseline"), 1f);
        DrawLine(new Vector2(plot.Position.X, plot.Position.Y), new Vector2(plot.Position.X, plot.End.Y), Value<Color>("Baseline"), 1f);

        if (_chart.HasData)
        {
            Axis(font, top, plot.Position.Y, axisWidth, axisFont);
            Axis(font, (top + bottom) / 2, mid, axisWidth, axisFont);
            Axis(font, bottom, plot.End.Y, axisWidth, axisFont);
        }

        for (int i = 0; i < MarketPrice.DaysShown; i++)
        {
            float x = Column(i);
            DrawString(font, new Vector2(x - 60f, Size.Y - padding - 4f), _chart.DayLabel(i), HorizontalAlignment.Center, 120f, dayFont, UiTheme.TextLo);
            if (!_chart.HasData || i >= days.Count) continue;
            var day = days[i];
            if (day.HasTrades)
            {
                float y = ValueY(day.Average);
                var bar = new Rect2(x - barWidth / 2f, y, barWidth, Math.Max(1f, plot.End.Y - y));
                DrawRect(bar, hoverDay == i && hoverPart == 1 ? Value<Color>("BarHover") : Value<Color>("BarFill"));
                DrawRect(bar, Value<Color>("BarEdge"), filled: false, width: 1f);
            }
            if (day.Max > 0) Marker(x, ValueY(day.Max), Value<Color>("MaxColor"), pointsDown: true, hoverDay == i && hoverPart == 2);
            if (day.Min > 0) Marker(x, ValueY(day.Min), Value<Color>("MinColor"), pointsDown: false, hoverDay == i && hoverPart == 3);
        }
    }

    private void Axis(Font font, long value, float y, float axisWidth, int size) =>
        DrawString(font, new Vector2(4f, y + size / 2f - 2f), MarketPrice.AxisLabel(value), HorizontalAlignment.Right, axisWidth - 10f, size, UiTheme.TextLo);

    private void Marker(float x, float y, Color color, bool pointsDown, bool hover)
    {
        float markerHalf = Value<float>("MarkerHalf"), markerHeight = Value<float>("MarkerHeight");
        float half = hover ? markerHalf + 2f : markerHalf;
        DrawLine(new Vector2(x - half - 3f, y), new Vector2(x + half + 3f, y), color, 2f);
        float tip = pointsDown ? y - 1f : y + 1f;
        float back = pointsDown ? y - markerHeight - 1f : y + markerHeight + 1f;
        DrawColoredPolygon(new[] { new Vector2(x, tip), new Vector2(x - half, back), new Vector2(x + half, back) }, color);
    }
}
