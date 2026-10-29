using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventsLayout.Roulette;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The event roulette: a reel of five inventory cells that turns while the spin request is answered and
/// stops on the prize the server granted, the event coin count, the client's Spin button and the recent
/// prize history the server keeps for the character.
/// </summary>
public partial class ClassicRoulettePanel : ClassicEventPanel
{
    private static readonly int[] Slowdown = { 90, 120, 160, 210, 270, 340 };
    private const int SpinStepMsec = 70;

    private readonly Panel[] _reel = new Panel[Spec.ReelCells];
    private readonly Texture2D?[] _shown = new Texture2D?[Spec.ReelCells];
    private readonly Label _result, _nativeCoins, _coins;
    private readonly VBoxContainer _logRows;
    private readonly Label _logEmpty;
    private readonly ClassicReelMark _mark;
    private int _historyRevision = -1, _step, _slowStep;
    private ulong _nextStep;
    private bool _spinning;

    public ClassicRoulettePanel(HudWindow window, Control body, WorldRoot world) : base(window, body, world, Spec.Size)
    {
        Text("Prize", Spec.ReelCaption, Caption).Name = "roulette_reel_caption";
        for (int i = 0; i < _reel.Length; i++)
        {
            _reel[i] = IconCell(Spec.ReelCell(i), Content);
            _reel[i].Name = "roulette_reel_" + i;
            _reel[i].SetMeta(ExpectedRect, Spec.ReelCell(i));
        }
        _mark = new ClassicReelMark { Name = "roulette_reel_mark" };
        Content.AddChild(_mark);
        _mark.Position = Spec.ReelCell(Spec.ReelCells / 2).Position; _mark.Size = ClassicEventsLayout.Cell;
        _result = Find<Label>("roulette_result");
        Value(_result, Spec.Result, Ink, HorizontalAlignment.Center);
        Inset(Spec.Coins);
        Text("Event coins", Spec.CoinsCaption, Caption);
        _nativeCoins = Find<Label>("roulette_coins");
        _nativeCoins.Visible = false;
        _coins = Text("", Spec.CoinsValue, Amount, HorizontalAlignment.Right);
        _coins.Name = "roulette_coin_value";
        Action(Find<Button>("roulette_spin"), Spec.Spin);
        Text("Recent prizes", Spec.LogCaption, Caption);
        Rule(Spec.LogRule);
        var log = new ScrollContainer { Name = "roulette_log", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Content.AddChild(log);
        log.Position = Spec.Log.Position; log.Size = Spec.Log.Size;
        log.SetMeta(ExpectedRect, Spec.Log);
        Scroll(log);
        log.GetVScrollBar().CustomStep = 26;
        _logRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _logRows.AddThemeConstantOverride("separation", 2);
        log.AddChild(_logRows);
        _logEmpty = Text("No prizes yet.", new Rect2(Spec.Log.Position, new Vector2(Spec.Log.Size.X, ClassicEventsLayout.Row)), Muted);
        _logEmpty.Name = "roulette_log_empty";
        Refresh();
    }

    public bool Spinning => _spinning;

    /// <summary>A spin lands (and tells the client it finished) even when the window was closed meanwhile.</summary>
    protected override bool TicksWhileHidden => true;

    private List<Texture2D> Faces()
    {
        var faces = new List<Texture2D>();
        if (GoldIcon is { } gold) faces.Add(gold);
        foreach (var entry in NativeEventWindows.Roulette(World).History)
            if (ItemIcon(entry.ItemId) is { } icon && !faces.Contains(icon)) faces.Add(icon);
        return faces;
    }

    protected override void Refresh()
    {
        _coins.Text = After(_nativeCoins.Text);
        var view = NativeEventWindows.Roulette(World);
        ulong now = Time.GetTicksMsec();
        if (view.Spinning && !_spinning) { _spinning = true; _step = 0; _slowStep = 0; _nextStep = now; }
        if (_spinning && !view.Spinning && view.ResultOk != true) { _spinning = false; ShowIdle(); }
        if (_spinning && now >= _nextStep)
        {
            if (view.CanLand && _slowStep >= Slowdown.Length) Land(view);
            else
            {
                Turn();
                _nextStep = now + (ulong)(view.CanLand ? Slowdown[_slowStep++] : SpinStepMsec);
            }
        }
        _result.Modulate = _spinning ? Colors.Transparent : Colors.White;
        _mark.Lit = _spinning;
        if (_result.SelfModulate != Colors.White) _result.SelfModulate = Colors.White;
        if (_historyRevision != view.HistoryRevision) { _historyRevision = view.HistoryRevision; History(view.History); }
    }

    private void Turn()
    {
        var faces = Faces();
        _step++;
        for (int i = 0; i < _reel.Length; i++)
        {
            var face = faces.Count == 0 ? null : faces[(_step + i) % faces.Count];
            _shown[i] = face; ShowIcon(_reel[i], face);
        }
    }

    private void Land(NativeEventWindows.RouletteView view)
    {
        _spinning = false;
        var prize = view.PrizeGold > 0 ? GoldIcon : ItemIcon(view.PrizeItem);
        for (int i = 0; i < _reel.Length; i++) ShowIcon(_reel[i], i == Spec.ReelCells / 2 ? prize : null);
        _reel[Spec.ReelCells / 2].TooltipText = _result.Text;
        NativeEventWindows.FinishSpin(World);
    }

    private void ShowIdle()
    {
        foreach (var cell in _reel) ShowIcon(cell, null);
    }

    private void History(LibreKO.Network.RoulettePrize[] entries)
    {
        foreach (var old in _logRows.GetChildren()) { _logRows.RemoveChild(old); old.QueueFree(); }
        _logEmpty.Visible = entries.Length == 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var row = new Control { Name = "roulette_log_" + i, CustomMinimumSize = new Vector2(1, 24), MouseFilter = MouseFilterEnum.Ignore };
            var icon = new TextureRect { Texture = entry.ItemId == 0 ? GoldIcon : ItemIcon(entry.ItemId), 
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(icon);
            icon.Position = new Vector2(4, 2); icon.Size = new Vector2(20, 20);
            string name = entry.ItemId == 0 ? $"{entry.Quantity:N0} gold"
                : (ItemData.Get(entry.ItemId) != null ? ItemData.DisplayName(entry.ItemId) : $"Item {entry.ItemId}") + (entry.Quantity > 1 ? $" x{entry.Quantity:N0}" : "");
            Text(name, new Rect2(30, 0, 196, 24), entry.ItemId == 0 ? Amount : Ink, HorizontalAlignment.Left, row).TooltipText = name;
            string time = entry.UnixTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(entry.UnixTime).ToLocalTime().ToString("HH:mm") : "";
            Text(time, new Rect2(228, 0, 54, 24), Muted, HorizontalAlignment.Right, row);
            _logRows.AddChild(row);
        }
    }
}

/// <summary>The gold selection frame around the reel's centre cell, brighter while it turns.</summary>
public partial class ClassicReelMark : Control
{
    private bool _lit;
    public ClassicReelMark() => MouseFilter = MouseFilterEnum.Ignore;
    public bool Lit { get => _lit; set { if (_lit == value) return; _lit = value; QueueRedraw(); } }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), new Color(_lit ? "fff0b8" : "ffe094"), false, 1);
        if (_lit) DrawRect(new Rect2(new Vector2(-2, -2), Size + Vector2.One * 4), new Color(1, .88f, .58f, .55f), false, 1);
    }
}
