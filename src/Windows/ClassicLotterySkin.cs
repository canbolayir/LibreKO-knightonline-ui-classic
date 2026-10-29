using NativeApi = KnightOnlineUiClassic.Native.Native;
using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventsLayout.Lottery;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The lottery: draw details and the player's tickets in quest insets, the ticket price as an inventory
/// cell and plaque above the client's Buy button, then the prize cells. After a draw the prize area lists
/// the winners the server announced.
/// </summary>
public partial class ClassicLotteryPanel : ClassicEventPanel
{
    private readonly Label _time, _status, _prizeCaption, _noPrizes;
    private readonly TextureRect _coin;
    private readonly ScrollContainer _prizes, _winners;
    private readonly HBoxContainer _rewards;
    private readonly VBoxContainer _winnerRows;
    private int _winnersRevision = -1;

    public ClassicLotteryPanel(HudWindow window, Control body, WorldRoot world) : base(window, body, world, Spec.Size)
    {
        AddClose(() => NativeApi.Call(World, "Lottery.Close"));
        Inset(Spec.Info);
        string[] captions = { "Time remaining", "Tickets sold", "Prize winners" };
        string[] values = { "lottery_time", "lottery_sold", "lottery_winners" };
        for (int row = 0; row < 3; row++)
        {
            var rect = Spec.InfoRow(row);
            Text(captions[row], new Rect2(rect.Position, new Vector2(150, rect.Size.Y)), Caption);
            Value(Find<Label>(values[row]), new Rect2(rect.Position.X + 150, rect.Position.Y, rect.Size.X - 150, rect.Size.Y), row == 0 ? Accepted : Ink);
        }
        _time = Find<Label>("lottery_time");
        Inset(Spec.Tickets);
        Text("Your tickets", new Rect2(Spec.TicketsRow.Position, new Vector2(150, Spec.TicketsRow.Size.Y)), Caption);
        Value(Find<Label>("lottery_tickets"), new Rect2(Spec.TicketsRow.Position.X + 150, Spec.TicketsRow.Position.Y, Spec.TicketsRow.Size.X - 150, Spec.TicketsRow.Size.Y), Accepted);
        Value(Find<Label>("lottery_player"), Spec.Player, ClassicReportDesign.Caption, HorizontalAlignment.Center);

        var slot = Find<PanelContainer>("lottery_req_slot");
        Place(slot, Spec.Requirement);
        Cell(slot);
        var box = Find<PanelContainer>("lottery_cost_box");
        Place(box, Spec.Cost);
        var plaque = ClassicNpcLayout.InsetBox();
        plaque.SetContentMargin(Side.Left, 8); plaque.SetContentMargin(Side.Right, 8);
        box.AddThemeStyleboxOverride("panel", plaque);
        var cost = Find<Label>("lottery_cost");
        Bold(cost, Amount);
        cost.HorizontalAlignment = HorizontalAlignment.Right;
        cost.ClipText = true; cost.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        cost.CustomMinimumSize = Vector2.Zero;
        var coinArt = Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
        _coin = new TextureRect { Name = "lottery_coin", Texture = ClassicChatControls.Atlas(coinArt), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        Content.AddChild(_coin);
        _coin.Position = Spec.CostCoin.Position; _coin.Size = Spec.CostCoin.Size;
        _coin.SetMeta(ExpectedRect, Spec.CostCoin);
        Action(Find<Button>("lottery_buy"), Spec.Buy);
        _status = Find<Label>("lottery_status");
        Value(_status, Spec.Status, Accepted, HorizontalAlignment.Center);

        _prizeCaption = Text("Prize", Spec.PrizeCaption, Caption);
        _prizeCaption.Name = "lottery_prize_caption";
        Rule(Spec.PrizeRule);
        _prizes = new ScrollContainer { Name = "lottery_prizes", VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Content.AddChild(_prizes);
        _prizes.Position = Spec.Prizes(0).Position; _prizes.Size = Spec.Prizes(Spec.PrizeCells).Size;
        Scroll(_prizes);
        var inset = new MarginContainer();
        inset.AddThemeConstantOverride("margin_left", 4); inset.AddThemeConstantOverride("margin_top", 3);
        _prizes.AddChild(inset);
        _rewards = Find<HBoxContainer>("lottery_rewards");
        _rewards.Reparent(inset, false);
        _rewards.Alignment = BoxContainer.AlignmentMode.Begin;
        _rewards.AddThemeConstantOverride("separation", ClassicEventsLayout.CellPitch - 45);
        _rewards.Visible = true;

        _noPrizes = Text("No prizes announced.", new Rect2(Spec.Prizes(0).Position, new Vector2(Spec.Prizes(0).Size.X, Spec.PrizeCells)), Muted, HorizontalAlignment.Center);
        _noPrizes.Name = "lottery_no_prizes";
        _winners = new ScrollContainer { Name = "lottery_winner_list", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        Content.AddChild(_winners);
        _winners.Position = Spec.Prizes(0).Position; _winners.Size = Spec.Prizes(Spec.Winners(1)).Size;
        Scroll(_winners);
        _winnerRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _winnerRows.AddThemeConstantOverride("separation", 2);
        _winners.AddChild(_winnerRows);
        _winners.GetVScrollBar().CustomStep = Spec.WinnerPitch;
        Refresh();
    }

    private LibreKO.Network.LotteryState? State => NativeApi.Get<LibreKO.Network.LotteryState>(World, "Lottery.State");

    protected override void Refresh()
    {
        var state = State;
        bool active = state is { Active: true };
        _coin.Visible = active && state!.ReqItemId is QuestData.CoinItemId or 0;
        // The client tints the timer only while it counts; derive the ink from the draw itself.
        _time.SelfModulate = Colors.White;
        ThemeNames.SetColor(_time, HotNames.FontColor, !active ? Muted : state!.RemainingSeconds <= 60 ? Refused : Accepted);
        if (_status.SelfModulate != Colors.White)
        {
            ThemeNames.SetColor(_status, HotNames.FontColor, Semantic(_status.SelfModulate, Ink));
            _status.SelfModulate = Colors.White;
        }
        foreach (var card in _rewards.GetChildren().OfType<PanelContainer>()) Cell(card);
        var view = NativeEventWindows.Lottery(World);
        bool results = !active && view.Winners.Count > 0;
        _prizes.Visible = !results;
        _noPrizes.Visible = !results && !_rewards.GetChildren().OfType<Control>().Any(c => !c.IsQueuedForDeletion());
        _winners.Visible = results;
        _prizeCaption.Text = results ? "Winners" : "Prize";
        // The frame follows its content: one row of prize cells or up to four whole winner rows.
        bool overflow = _rewards.GetCombinedMinimumSize().X + 4 > Spec.Prizes(0).Size.X;
        float area = results ? Spec.Winners(view.Winners.Count) : Spec.PrizeCells + (overflow ? Spec.PrizeBar : 0);
        var shown = results ? _winners : _prizes;
        if (Math.Abs(shown.Size.Y - area) > .5f) shown.Size = Spec.Prizes(area).Size;
        SetFullSize(new Vector2(Spec.Width, Spec.Prizes(area).End.Y + Spec.Bottom));
        if (_winnersRevision == view.WinnersRevision) return;
        _winnersRevision = view.WinnersRevision;
        foreach (var old in _winnerRows.GetChildren()) { _winnerRows.RemoveChild(old); old.QueueFree(); }
        for (int i = 0; i < view.Winners.Count; i++) _winnerRows.AddChild(WinnerRow(view.Winners[i]));
    }

    private Control WinnerRow(LibreKO.Network.LotteryWinner winner)
    {
        var row = new Control { Name = "lottery_winner_" + winner.Place, CustomMinimumSize = new Vector2(1, 24), MouseFilter = MouseFilterEnum.Ignore };
        Text($"{winner.Place}.", new Rect2(2, 0, 24, 24), Amount, HorizontalAlignment.Right, row);
        Text(winner.CharacterName, new Rect2(32, 0, 118, 24), Ink, HorizontalAlignment.Left, row);
        var icon = new TextureRect { Texture = ItemIcon(winner.ItemId), 
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(icon);
        icon.Position = new Vector2(154, 2); icon.Size = new Vector2(20, 20);
        string item = winner.ItemId == QuestData.CoinItemId ? "Gold" : ItemData.Get(winner.ItemId) != null ? ItemData.DisplayName(winner.ItemId)
            : winner.ItemName.Length > 0 ? winner.ItemName : $"Item {winner.ItemId}";
        if (winner.ItemCount > 1) item += $" x{winner.ItemCount:N0}";
        var prize = Text(item, new Rect2(178, 0, 110, 24), Muted, HorizontalAlignment.Left, row);
        prize.TooltipText = item;
        return row;
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName FontColor = "font_color";
}
