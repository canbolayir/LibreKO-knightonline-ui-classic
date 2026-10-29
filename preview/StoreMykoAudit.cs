using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;

public partial class Preview
{
    /// <summary>Actual rendered bounds of the store on the original Power Up Store art against <see cref="ClassicStoreMykoLayout"/>.</summary>
    private static void MykoStoreGeometry(string state, HudWindow window, ClassicStoreMykoPanel panel, Action<bool, string> require)
    {
        var art = ClassicStoreMykoLayout.Art!;
        T Part<T>(string name) where T : Control => Descendants(window).OfType<T>().Single(c => c.Name == "pus_" + name);
        var stage = panel.GetChildren().OfType<Control>().Single(c => c.Name == "pus_stage");
        float scale = stage.Scale.X;
        require(panel.GetGlobalRect() == window.GetGlobalRect(), state + " original art covers the whole window " + panel.GetGlobalRect());
        require(scale == ClassicStoreMykoLayout.Scale(panel.GetViewportRect().Size) && stage.Scale.Y == scale && panel.Size == (ClassicStoreMykoLayout.Stage * scale).Ceil(),
            state + " stage keeps the original 1024x765 proportions at scale " + scale + " " + panel.Size);
        Rect2 Global(Rect2 rect) => new(stage.GlobalPosition + rect.Position * scale, rect.Size * scale);
        bool Near(Rect2 a, Rect2 b) => (a.Position - b.Position).Length() < .6f && (a.Size - b.Size).Length() < .6f;
        void Same(Control control, Rect2 rect, string what) => require(Near(control.GetGlobalRect(), Global(rect)), state + " " + what + " at its UIF rectangle " + control.GetGlobalRect() + " vs " + Global(rect));
        Rect2 Of(string id) => ClassicStoreMykoLayout.Plate(art.Find(id)!);
        Same(Descendants(panel).OfType<Button>().Single(b => b.TooltipText == "Close"), Of(ClassicStoreMykoLayout.Close), "CLOSE plate");
        Same((Control)Part<LineEdit>("query").GetParent().GetParent(), ClassicStoreMykoLayout.SearchField, "search field");
        Same(Part<OptionButton>("sort"), ClassicStoreMykoLayout.Sort, "sort list");
        Same(Part<Label>("grid_title"), ClassicStoreMykoLayout.GridTitle, "catalogue title");
        Same(Part<Label>("cash"), ClassicStoreMykoLayout.Cash, "Knight Cash balance");
        var recharge = Descendants(stage).OfType<Button>().Single(b => b.Text == ClassicStoreMykoLayout.RechargeCaption);
        Same(recharge, Of(ClassicStoreMykoLayout.Recharge), "native recharge");
        Same(Part<ScrollContainer>("grid_scroll"), ClassicStoreMykoLayout.Grid, "item well");
        Same(Part<VBoxContainer>("basket"), ClassicStoreMykoLayout.Basket, "basket lines");
        Same(Part<Label>("cart_title"), ClassicStoreMykoLayout.BasketTitle, "basket title");
        Same(Part<Button>("clear_cart"), Of(ClassicStoreMykoLayout.ClearCart), "Clear all plate");
        Same(Part<Button>("checkout"), Of(ClassicStoreMykoLayout.Checkout), "checkout plate");
        if (Part<Button>("gift").Visible) Same(Part<Button>("gift"), Of(ClassicStoreMykoLayout.Gift), "gift plate");
        Same(Part<HBoxContainer>("total"), ClassicStoreMykoLayout.Total, "basket total");
        Same(Part<Button>("page_previous"), Of(ClassicStoreMykoLayout.Previous), "previous page");
        Same(Part<Button>("page_next"), Of(ClassicStoreMykoLayout.Next), "next page");
        Same(Part<Label>("page"), ClassicStoreMykoLayout.PageText, "page number");
        if (Platform.TouchUi)
            foreach (var plate in new[] { Part<Button>("checkout"), Part<Button>("clear_cart"), Part<Button>("gift"), Part<Button>("page_previous"), Part<Button>("page_next"), recharge }.Where(b => b.IsVisibleInTree()))
                require(plate.GetGlobalRect().Size.Y >= 36 * scale - .5f, state + " touch target on the original plate " + plate.Name + " " + plate.GetGlobalRect());
        require(!Part<BoxContainer>("tools").IsVisibleInTree() && !Part<Control>("categories").IsVisibleInTree() && !Part<Control>("cart").IsVisibleInTree(), state + " native columns are fully taken over");
        foreach (var label in new[] { Part<Label>("cart_empty"), Part<Label>("shortfall"), Part<Label>("cart_status") }.Where(l => l.IsVisibleInTree()))
            require(Global(ClassicStoreMykoLayout.Basket).Grow(.5f).Encloses(label.GetGlobalRect()), state + " basket message stays inside the basket " + label.Name + " " + label.GetGlobalRect());
        foreach (var row in Part<VBoxContainer>("cart_lines").GetChildren().OfType<Control>().Where(r => r.IsVisibleInTree()))
        {
            require(row.Size.Y >= ClassicStoreMykoLayout.BasketRowHeight && row.GetThemeStylebox("panel") is StyleBoxTexture, state + " basket line on the original row art " + row.GetRect());
            require(Global(ClassicStoreMykoLayout.Basket).Grow(.5f).Encloses(row.GetGlobalRect()), state + " basket line fits the original basket width " + row.GetGlobalRect());
            foreach (var b in Descendants(row).OfType<Button>().Where(b => b.IsVisibleInTree()))
            {
                float side = b.TooltipText == "Remove from the cart" ? ClassicStoreMykoLayout.RemoveSide : ClassicStoreMykoLayout.StepperSide;
                require(b.GetThemeStylebox("normal") is StyleBoxTexture && b.Text == "" && Math.Abs(b.Size.X - side * scale) < .6f && Math.Abs(b.Size.Y - side * scale) < .6f && row.GetGlobalRect().Encloses(b.GetGlobalRect()),
                    state + " basket " + b.TooltipText + " on its original button art " + b.GetGlobalRect());
            }
            require(row.GetChildren().OfType<HBoxContainer>().Single().GetChildren().OfType<TextureRect>().Single().IsVisibleInTree() == ClassicStoreMykoLayout.BasketIcons, state + " basket icon shown on pointer rows only");
        }
        // Tabs: one original plate per native category, in one row, mirroring the native selection.
        var plates = Part<Control>("tabs").GetChildren().OfType<Button>().ToArray();
        var native = Part<VBoxContainer>("category_list").GetChildren().OfType<Button>().ToArray();
        require(plates.Length == native.Length, state + " one tab per native category " + plates.Length);
        for (int i = 0; i < plates.Length; i++)
        {
            var rect = ClassicStoreMykoLayout.Tab(i, plates.Length);
            Same(plates[i], rect, "tab " + plates[i].Text);
            require(plates[i].Text == native[i].Text && plates[i].GetMeta("pus_tab_selected").AsBool() == native[i].GetMeta("pus_category_selected", false).AsBool(), state + " tab mirrors native category state " + plates[i].Text);
            require(plates[i].GetThemeColor("font_color") == (plates[i].GetMeta("pus_tab_selected").AsBool() ? ClassicStoreMykoLayout.TabSelectedInk : ClassicStoreMykoLayout.TabInk), state + " tab ink marks the selection " + plates[i].Text);
        }
        require(plates.Count(p => p.GetMeta("pus_tab_selected").AsBool()) <= 1, state + " at most one selected tab");
        // Cards: original 196x153 cards, three per row, whole pages inside the well.
        var grid = Part<GridContainer>("grid"); var well = Global(ClassicStoreMykoLayout.Grid);
        var cards = grid.GetChildren().OfType<PanelContainer>().Where(c => c.IsVisibleInTree()).ToArray();
        require(cards.Length == 0 || grid.Columns == ClassicStoreMykoLayout.Columns, state + " three cards per row " + grid.Columns);
        foreach (var card in cards)
        {
            require(card.Size == ClassicStoreMykoLayout.Card, state + " original card size " + card.Size);
            var cardRect = card.GetGlobalRect(); if (!well.Grow(.5f).Intersects(cardRect)) continue;
            require(well.Grow(.5f).Encloses(cardRect), state + " visible card lies whole inside the well " + cardRect);
            Rect2 Local(Rect2 r) => new(cardRect.Position + r.Position * scale, r.Size * scale);
            var face = card.GetChildren().OfType<Control>().Single(c => c.Name == "pus_card_face");
            var buy = face.GetChildren().OfType<Button>().Single(b => b.Name == "pus_card_buy"); var add = face.GetChildren().OfType<Button>().Single(b => b.Name == "pus_card_add");
            require(Near(buy.GetGlobalRect(), Local(ClassicStoreMykoLayout.CardBuy)) && Near(add.GetGlobalRect(), Local(ClassicStoreMykoLayout.CardAdd)) && buy.Text == "Buy" && add.Text == "Add to Basket", state + " card Buy and Add to Basket at their UIF rectangles " + buy.GetGlobalRect());
            require(buy.Disabled == NativeMarket.StorePending(window) && add.Disabled == buy.Disabled, state + " card buttons follow the native pending state");
            var name = face.GetChildren().OfType<Label>().Single(l => l.Name == "pus_card_name");
            require(Near(name.GetGlobalRect(), Local(ClassicStoreMykoLayout.CardName)) && name.ClipText && name.TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis, state + " card name on one clipped line " + name.Text);
            var icon = face.GetChildren().OfType<PanelContainer>().Single(c => c.Name == "pus_card_icon");
            require(Near(icon.GetGlobalRect(), Local(ClassicStoreMykoLayout.CardIcon)), state + " item icon centred in the original icon cell " + icon.GetGlobalRect());
            var price = face.GetChildren().OfType<HBoxContainer>().Single(c => c.Name == "pus_card_price");
            require(cardRect.Encloses(price.GetGlobalRect()) && !price.GetGlobalRect().Intersects(buy.GetGlobalRect()) && price.GetChildren().OfType<Label>().Last().Text.EndsWith(" KC"), state + " card price inside the card " + price.GetGlobalRect());
            var caption = face.GetChildren().OfType<Label>().Single(l => l.Name == "pus_card_price_caption");
            require(price.GetCombinedMinimumSize().X <= price.Size.X + .5f && (!caption.Visible || price.GetGlobalRect().Position.X >= caption.GetGlobalRect().Position.X + caption.GetMinimumSize().X * scale),
                state + " card price fits after its caption " + caption.Visible + " " + price.GetGlobalRect());
            var chips = card.GetChildren().OfType<Control>().Where(c => c is not Container && c != face).SelectMany(o => o.GetChildren().OfType<Control>()).Where(c => c.IsVisibleInTree()).ToArray();
            foreach (var chip in chips)
                require(cardRect.Grow(-3 * scale).Encloses(chip.GetGlobalRect()) && !chip.GetGlobalRect().Intersects(buy.GetGlobalRect()) && !chip.GetGlobalRect().Intersects(price.GetGlobalRect())
                    && chips.All(o => o == chip || !o.GetGlobalRect().Intersects(chip.GetGlobalRect())), state + " card chip inside the card border, clear of price, buttons and other chips " + chip.Name + " " + chip.GetGlobalRect());
        }
        var pageText = Part<Label>("page").Text.Split(" / ");
        int pages = Math.Max(1, (int)Math.Ceiling(Math.Ceiling(cards.Length / 3.0) / 3.0));
        require(pageText.Length == 2 && int.Parse(pageText[1]) == pages && Part<Button>("page_previous").Disabled == (pageText[0] == "1") && Part<Button>("page_next").Disabled == (pageText[0] == pageText[1]), state + " pager shows whole pages " + Part<Label>("page").Text);
        foreach (var modal in Descendants(window).OfType<PanelContainer>().Where(m => m.Name.ToString() is "pus_details" or "pus_gift_box" && m.IsVisibleInTree()))
        {
            var frame = modal.GetChildren().OfType<Control>().Single(c => c.Name == "pus_modal_frame");
            require(frame.GetGlobalRect() == modal.GetGlobalRect() && frame is ClassicStoreFrame, state + " modal uses the plain store frame " + modal.GetGlobalRect());
        }
    }

    /// <summary>The controls the original art adds: card Buy / Add to Basket (native details and cart) and the pager.</summary>
    private async Task MykoActions(WorldRoot world, HudWindow window, PowerUpStoreCart cart, GridContainer grid, Func<Control, Task> click, Func<int, Task> frames, Func<string, Task> capture, Action<bool, string> require)
    {
        T Part<T>(string name) where T : Control => Descendants(window).OfType<T>().Single(c => c.Name == "pus_" + name);
        Button CardButton(PanelContainer card, string name) => Descendants(card).OfType<Button>().Single(b => b.Name == name);
        var card = grid.GetChildren().OfType<PanelContainer>().First(); int units = cart.Units;
        await click(CardButton(card, "pus_card_add"));
        require(cart.Units == units + 1 && !Part<Control>("overlay").Visible, "Card Add to Basket adds one unit through the native details action");
        await capture("pus-myko-added");
        card = grid.GetChildren().OfType<PanelContainer>().First();
        await click(CardButton(card, "pus_card_buy"));
        require(Part<PanelContainer>("details").IsVisibleInTree() && Part<Label>("details_name").Text == Descendants(card).OfType<Label>().Single(l => l.Name == "pus_card_name").Text, "Card Buy opens the native details with Buy now");
        await capture("pus-myko-buy-details");
        await click(Descendants(Part<PanelContainer>("details")).OfType<Button>().Single(b => b.TooltipText == "Close"));
        require(!Part<Control>("overlay").Visible && cart.Units == units + 1, "Closing the details leaves the basket unchanged");
        // Pager: a long category turns by whole pages (buttons and wheel).
        var original = ((List<PowerUpStoreEntry>)DetailField(world, "ShoppingMall.Model.Catalog")!).ToList();
        var longer = original.Concat(Enumerable.Range(0, 18).Select(i => original[i % original.Count] with { Id = 200 + i, Category = 1, Featured = false })).ToList();
        DetailCall(world, "ShoppingMall.OnCatalog", longer); await frames(8);
        var page = Part<Label>("page"); var scroll = Part<ScrollContainer>("grid_scroll");
        require(page.Text == "1 / 3" && Part<Button>("page_previous").Disabled && !Part<Button>("page_next").Disabled, "Long category shows page 1 of 3 " + page.Text);
        await capture("pus-myko-page-1");
        await click(Part<Button>("page_next")); await frames(4);
        require(page.Text == "2 / 3" && scroll.ScrollVertical == (int)ClassicStoreMykoLayout.PageHeight, "Next turns one whole page " + scroll.ScrollVertical);
        await capture("pus-myko-page-2");
        var at = scroll.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.WheelDown, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.WheelDown, Pressed = false }, true); await frames(4);
        require(page.Text == "3 / 3" && Part<Button>("page_next").Disabled, "Wheel turns to the last page " + page.Text);
        await capture("pus-myko-page-3");
        await click(Part<Button>("page_previous")); await frames(4);
        require(page.Text == "2 / 3", "Previous turns back one page");
        DetailCall(world, "ShoppingMall.OnCatalog", original); await frames(8);
        require(page.Text == "1 / 1" && scroll.ScrollVertical == 0, "Native catalogue refresh returns to the first page");
    }
}
