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
    private const float CompactStoreWidth = 960;
    private const float CompactCategoryWidth = 180;
    private const float CompactCartWidth = 264;

    public static void Prepare(HudWindow window, World world)
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

    private static void PrepareMarketPrice(HudWindow window, World world)
    {
        if (window.HasMeta("classic_market_price_controls")) return;
        var slot = Native.Get<ItemSlotView>(world, "_marketPriceSlot");
        var name = Native.Get<Label>(world, "_marketPriceName");
        var trades = Native.Get<Label>(world, "_marketPriceTrades");
        var chart = Native.Get<PriceChart>(world, "_marketPriceChart");
        var updated = Native.Get<Label>(world, "_marketPriceUpdated");
        var status = Native.Get<Label>(world, "_marketPriceStatus");
        var search = Native.Get<ItemSearchPanel>(world, "_marketPriceSearch");
        if (slot == null || name == null || trades == null || chart == null || updated == null || status == null || search == null) return;
        var find = name.GetParent().GetChildren().OfType<Button>().FirstOrDefault();
        var caption = trades.GetParent().GetChildren().OfType<Label>().FirstOrDefault(l => l != trades);
        var latest = updated.GetParent().GetChildren().OfType<Label>().LastOrDefault(l => l != updated);
        if (!PrepareItemSearch(search)) return;
        if (!NameAll(new Node?[] { slot, name, find, caption, trades, chart, updated, latest, status },
            new[] { "market_price_item", "market_price_name", "market_price_search", "market_price_trades_caption", "market_price_trades", "market_price_chart", "market_price_updated", "market_price_latest", "market_price_status" }))
            return;
        if (NativeChartBackground.Supported(chart)) chart.AddChild(new NativeChartBackground(chart));
        int noHistoryId = Native.Get<int>(typeof(World), "MarketPriceNoHistoryText");
        NativeWindows.Sync(window, () =>
        {
            name.TooltipText = name.Text;
            // A cached "no history" reply is drawn as an empty five-day history; show it as no history instead.
            int item = Native.Get<int>(world, "_marketPriceItem");
            if (item == 0 || updated.Text.Length == 0 || Native.Get<IDictionary>(world, "_marketPriceCache") is not { } cache) return;
            if (cache[item] is not MarketPriceReply { Result: MarketPrice.NoHistory }) return;
            Native.Call(world, "ClearMarketPriceChart");
            Native.Call(world, "SetMarketPriceStatus", ItemData.Text(noHistoryId, "There is no trade history for this item"));
        });
        window.SetMeta("classic_market_price_controls", 1);
    }

    private static bool PrepareItemSearch(ItemSearchPanel panel)
    {
        var query = Native.Get<LineEdit>(panel, "_query");
        var pick = Native.Get<OptionButton>(panel, "_pick");
        var group = Native.Get<OptionButton>(panel, "_group");
        var level = Native.Get<OptionButton>(panel, "_level");
        var summary = Native.Get<Label>(panel, "_summary");
        var results = Native.Get<VBoxContainer>(panel, "_results");
        var tabs = Native.Get<IDictionary>(panel, "_tabButtons");
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

    private static void PrepareMerchantSearch(HudWindow window, World world)
    {
        if (window.HasMeta("classic_merchant_search_controls")) return;
        var edit = Native.Get<LineEdit>(world, "_merchantSearchEdit");
        var scope = Native.Get<OptionButton>(world, "_merchantSearchScope");
        var status = Native.Get<Label>(world, "_merchantSearchStatus");
        var pages = Native.Get<HBoxContainer>(world, "_merchantSearchPages");
        var lines = Native.Get<IList>(world, "_merchantSearchLines");
        var model = Native.Get<MerchantSearch>(world, "_merchantSearch");
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
            if (visible.Count == 0 && status.Text.Length == 0 && !Native.Get<bool>(world, "_merchantSearchLoading")) status.Text = NoMatchingItems;
        });
        window.SetMeta("classic_merchant_search_controls", 1);
    }

    // Power-Up Store -----------------------------------------------------------------------------

    private static readonly (string Field, string Name)[] StoreControls =
    {
        ("_pusCategoryList", "category_list"), ("_pusGridTitle", "grid_title"), ("_pusCashValue", "cash"),
        ("_pusSearch", "query"), ("_pusSortPick", "sort"), ("_pusGridScroll", "grid_scroll"), ("_pusGrid", "grid"), ("_pusGridEmpty", "grid_empty"),
        ("_pusCartTitle", "cart_title"), ("_pusClearCart", "clear_cart"), ("_pusCartScroll", "cart_scroll"),
        ("_pusCartLines", "cart_lines"), ("_pusCartEmpty", "cart_empty"), ("_pusGiftButton", "gift"),
        ("_pusGiftBanner", "gift_banner"), ("_pusGiftName", "gift_name"), ("_pusGiftInfo", "gift_info"),
        ("_pusTotalRow", "total"), ("_pusShortfall", "shortfall"), ("_pusCheckout", "checkout"), ("_pusCartStatus", "cart_status"),
        ("_pusOverlay", "overlay"), ("_pusDetailsBox", "details"), ("_pusDetailsIcon", "details_icon"),
        ("_pusDetailsName", "details_name"), ("_pusDetailsCategory", "details_category"), ("_pusDetailsDescription", "details_description"),
        ("_pusDetailsPrice", "details_price"), ("_pusDetailsAdd", "details_add"), ("_pusDetailsBuy", "details_buy"), ("_pusDetailsStatus", "details_status"),
        ("_pusGiftBox", "gift_box"), ("_pusGiftSearch", "gift_query"), ("_pusGiftPickPane", "gift_pick"),
        ("_pusGiftListTitle", "gift_list_title"), ("_pusGiftList", "gift_list"), ("_pusGiftCard", "gift_card"),
        ("_pusGiftCardName", "recipient_name"), ("_pusGiftCardInfo", "recipient_info"), ("_pusGiftCardWarning", "recipient_warning"), ("_pusGiftStatus", "gift_status"),
    };

    private static void PrepareStore(HudWindow window, World world)
    {
        if (window.HasMeta("classic_store_controls")) return;
        var controls = StoreControls.Select(c => Native.Get<Node>(world, c.Field)).ToArray();
        var columns = window.Body.GetChildren().OfType<HBoxContainer>().FirstOrDefault()?.GetChildren().OfType<Control>().ToArray();
        var tools = Native.Get<Node>(world, "_pusSortPick")?.GetParent() is BoxContainer row ? Reorientable(row) : null;
        if (columns is not { Length: 3 } || tools == null) return;
        if (!NameAll(controls.Append(window.Body).Append(columns[0]).Append(columns[2]).Append(tools).ToArray(),
            StoreControls.Select(c => c.Name).Append("body").Append("categories").Append("cart").Append("tools").Select(n => "pus_" + n).ToArray()))
            return;
        var categories = Native.Get<VBoxContainer>(world, "_pusCategoryList")!;
        var grid = Native.Get<GridContainer>(world, "_pusGrid")!;
        var cartLines = Native.Get<VBoxContainer>(world, "_pusCartLines")!;
        var giftList = Native.Get<VBoxContainer>(world, "_pusGiftList")!;
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
        window.SetMeta("classic_store_controls", 1);
    }

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

    private static void DescribeCategory(World world, VBoxContainer list, Node node)
    {
        if (node is not Button button || button.GetParent() != list) return;
        var categories = Native.Get<IList>(world, "_pusCategories");
        var catalogue = Native.Get<IList>(world, "_pusCatalog");
        var search = Native.Get<LineEdit>(world, "_pusSearch");
        if (categories == null || catalogue == null || search == null) return;
        var ids = new List<int>();
        if (catalogue.Cast<PowerUpStoreEntry>().Any(e => e.Featured)) ids.Add(PowerUpStoreCatalog.FeaturedCategory);
        ids.AddRange(categories.Cast<ShoppingMallCategory>().Select(c => (int)c.Id));
        int index = button.GetIndex();
        if (index >= ids.Count) return;
        button.Name = "pus_category_" + ids[index];
        button.SetMeta("pus_category_selected", ids[index] == Native.Get<int>(world, "_pusCategory") && search.Text.Trim().Length == 0);
    }

    private static PowerUpStoreEntry? CardEntry(World world, Control card)
    {
        if (Native.Get<IDictionary>(world, "_pusCards") is not { } cards) return null;
        foreach (var view in cards.Values)
            if (Native.Get<Control>(view!, "Card") == card) return Native.Get<PowerUpStoreEntry>(view!, "Entry");
        return null;
    }

    private static void DescribeCard(World world, GridContainer grid, Node node)
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
        foreach (var chip in chips) chip.Name = chip.TooltipText == "In your cart" ? "pus_cart_chip" : "pus_timer_chip";
        card.ThemeChanged += () => StyleCard(world, card);
    }

    /// <summary>Applies the Classic card material for the card's native state whenever the client re-applies its own.</summary>
    private static void StyleCard(World world, PanelContainer card)
    {
        if (!card.HasMeta("pus_card_id") || !card.IsInsideTree()) return;
        int id = card.GetMeta("pus_card_id").AsInt32();
        int inCart = Native.Get<PowerUpStoreCart>(world, "_pusCart")?.Lines.FirstOrDefault(l => l.Entry.Id == id)?.Count ?? 0;
        bool active = inCart > 0 || Native.Get<int>(world, "_pusHoveredCard") == id && Convert.ToInt32(Native.Get<object>(world, "_pusModal")) == 0;
        card.SetMeta("pus_card_active", active);
        string key = "pus_card_" + (card.GetMeta("pus_card_featured").AsBool() ? "featured_" : "") + (active ? "active" : "normal");
        if (!card.HasThemeStylebox(key)) return;
        var style = card.GetThemeStylebox(key);
        if (card.GetThemeStylebox("panel") != style) card.AddThemeStyleboxOverride("panel", style);
    }

    private static void DescribeCartLine(World world, VBoxContainer lines, Node node)
    {
        if (node is not Control row || row.GetParent() != lines) return;
        var cart = Native.Get<PowerUpStoreCart>(world, "_pusCart")?.Lines.ToArray();
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
    private static bool FitCompactStore(HudWindow window, World world, Control categories, Control cart, BoxContainer tools, bool wasCompact)
    {
        if (!Native.Get<bool>(world, "_pusShown") || !window.IsInsideTree()) return wasCompact;
        bool compact = PowerUpStoreLayout.WindowSize(window.GetViewportRect().Size, Platform.TouchUi).X < CompactStoreWidth;
        if (compact == wasCompact) return compact;
        int categoryWidth = Native.Get<int>(typeof(World), "PusCategoryWidth"), cartWidth = Native.Get<int>(typeof(World), "PusCartWidth");
        int margin = Native.Get<int>(typeof(World), "PusWellMargin");
        categories.CustomMinimumSize = new Vector2(compact ? CompactCategoryWidth : categoryWidth, 0);
        cart.CustomMinimumSize = new Vector2(compact ? CompactCartWidth : cartWidth, 0);
        foreach (var field in new[] { "_pusCartEmpty", "_pusShortfall", "_pusCartStatus" })
            if (Native.Get<Control>(world, field) is { } label) label.CustomMinimumSize = new Vector2((compact ? CompactCartWidth : cartWidth) - 2 * margin, 0);
        tools.Vertical = compact;
        Native.Call(world, "FitPowerUpStore");
        return compact;
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
        ("_auctionSelectedName", "selected_name"), ("_auctionGold", "gold"), ("_auctionChecks", "checks"),
        ("_auctionTotal", "total"), ("_auctionWords", "words"), ("_auctionCurrent", "current"),
        ("_auctionMinimum", "minimum"), ("_auctionClock", "clock"), ("_auctionStatus", "status"),
        ("_auctionMillions", "millions"), ("_auctionCheckInput", "check_input"), ("_auctionSchedule", "schedule"),
        ("_auctionBidList", "bid_list"), ("_auctionWinList", "win_list"), ("_auctionLog", "log"),
    };

    private static void PrepareAuction(HudWindow window, World world)
    {
        if (window.HasMeta("classic_auction_controls")) return;
        var fields = AuctionFields.Select(f => Native.Get<Control>(world, f.Field)).ToArray();
        var tabs = Native.Get<IDictionary>(world, "_auctionTabButtons");
        var pages = Native.Get<IDictionary>(world, "_auctionPages");
        var slots = Native.Get<IList>(world, "_auctionLotSlots");
        if (fields.Any(f => f == null) || tabs == null || pages == null || slots is not { Count: > 0 }) return;
        Control Field(string name) => fields[Array.FindIndex(AuctionFields, f => f.Name == name)]!;
        var placeBid = Native.TryGet<Button>(world, "_auctionPlaceBid", out var place) ? place : Section(Field("total"))?.FindChildren("*", "Button", true, false).OfType<Button>().LastOrDefault();
        var refresh = Native.TryGet<Button>(world, "_auctionRefresh", out var again) ? again : Field("clock").GetParent().GetChildren().OfType<Button>().FirstOrDefault();
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
        var rowButtons = Native.TryGet<List<Button>>(world, "_auctionRowButtons", out var tracked) ? tracked : null;
        foreach (var (list, won) in new[] { (Field("bid_list"), false), (Field("win_list"), true) })
            NativeWindows.OnDescendantAdded(list, node => DescribeAuctionRow(list, node, won, rowButtons));
        var log = Field("log");
        int noHistoryId = Native.Get<int>(typeof(World), "SpecialAuctionNoHistoryText");
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
            button.AddThemeStyleboxOverride("hover", button.GetThemeStylebox("auction_tab_hover"));
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
        { "_days", "_top", "_bottom", "_hoverDay", "_hoverPart", "Plot", "Column", "ValueY", "PlotPadding", "BarWidth", "MarkerHalf", "MarkerHeight", "AxisWidth", "AxisFontSize", "DayFontSize", "BarFill", "BarHover", "BarEdge", "MaxColor", "MinColor", "Midline", "Baseline" };
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
        var days = Value<IReadOnlyList<MarketPriceDay>>("_days") ?? Array.Empty<MarketPriceDay>();
        long top = Value<long>("_top"), bottom = Value<long>("_bottom");
        int hoverDay = Value<int>("_hoverDay"), hoverPart = Convert.ToInt32(Native.Get<object>(_chart, "_hoverPart"));
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
