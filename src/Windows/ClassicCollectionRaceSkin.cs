using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
using Spec = KnightOnlineUiClassic.Layout.ClassicEventsLayout.CollectionRace;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The Collection Race tracker: event time and winners in a quest inset, then Hunt, Collect and Rewards as
/// the NPC quest cards over the client's own objective and reward rows. The frame grows with its content
/// up to a fixed height and scrolls beyond it, showing whole cards only.
/// </summary>
public partial class ClassicCollectionRacePanel : ClassicEventPanel
{
    private sealed class Section
    {
        public Control Block = null!, Rows = null!, Cards = null!;
        public Label Heading = null!, NativeHeading = null!;
        public Control[] Sources = Array.Empty<Control>();
        public bool Hunt, Grid;
    }

    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _stack;
    private readonly MarginContainer _lane;
    private readonly Control _winnersRow, _nativeWinnersRow, _complete, _nativeComplete;
    private readonly Label _completing;
    private readonly Section[] _sections;
    private float _top;

    public ClassicCollectionRacePanel(HudWindow window, Control body, WorldRoot world)
        : base(window, body, world, new Vector2(Spec.Width, Spec.MaxHeight))
    {
        _scroll = new ScrollContainer { Name = "cr_scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Content.AddChild(_scroll);
        _scroll.Position = Spec.Content.Position;
        _scroll.Size = new Vector2(Spec.Content.Size.X, 1);
        Scroll(_scroll);
        // The scroll bar gets its own lane so cards never run underneath it.
        _lane = new MarginContainer { Name = "cr_lane", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _scroll.AddChild(_lane);
        _stack = new VBoxContainer { Name = "cr_stack", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _stack.AddThemeConstantOverride("separation", ClassicEventsLayout.CardGap);
        _lane.AddChild(_stack);

        var info = new PanelContainer { Name = "cr_info", MouseFilter = MouseFilterEnum.Ignore };
        info.AddThemeStyleboxOverride("panel", ClassicNpcLayout.InsetBox(6));
        _stack.AddChild(info);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 0);
        info.AddChild(rows);
        Row(rows, "Event time", Find<Label>("cr_time"), Amount);
        _completing = Find<Label>("cr_completing");
        Row(rows, "Completing", _completing, Ink);
        _nativeWinnersRow = Find<Control>("cr_winners_row");
        _winnersRow = Row(rows, "Winners", Find<Label>("cr_winners"), Ink);

        _sections = new[]
        {
            Build("cr_hunt", true, false),
            Build("cr_collect", false, false),
            Build("cr_rewards", false, true),
        };

        _nativeComplete = Find<Control>("cr_complete");
        var title = Find<Label>("cr_complete_title");
        _complete = new MarginContainer { Name = "cr_complete_row" };
        _stack.AddChild(_complete);
        title.Reparent(_complete, false);
        Bold(title, Accepted);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(1, ClassicEventsLayout.Row);
        Refresh();
    }

    private Control Row(VBoxContainer rows, string caption, Label value, Color ink)
    {
        var row = new HBoxContainer { Name = "cr_row_" + caption.ToLowerInvariant().Replace(' ', '_'), CustomMinimumSize = new Vector2(1, ClassicEventsLayout.Row) };
        row.AddThemeConstantOverride("separation", 4);
        rows.AddChild(row);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(Spec.InfoCaption, 0), MouseFilter = MouseFilterEnum.Ignore };
        Bold(label, Caption);
        row.AddChild(label);
        value.Reparent(row, false);
        Bold(value, ink);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        value.ClipText = true;
        return row;
    }

    private Section Build(string box, bool hunt, bool grid)
    {
        var rows = Find<Control>(box);
        var block = Find<Control>(box + "_block");
        var section = new Section
        {
            Rows = rows, Block = new VBoxContainer { Name = box + "_section" }, Hunt = hunt, Grid = grid,
            NativeHeading = block.GetChildren().OfType<Label>().First(),
        };
        section.Block.AddThemeConstantOverride("separation", 4);
        _stack.AddChild(section.Block);
        section.Heading = new Label { Name = box + "_heading", CustomMinimumSize = new Vector2(1, 20), MouseFilter = MouseFilterEnum.Ignore };
        Bold(section.Heading, Caption);
        section.Block.AddChild(section.Heading);
        section.Cards = new VBoxContainer { Name = box + "_cards" };
        section.Cards.AddThemeConstantOverride("separation", ClassicEventsLayout.CardGap);
        section.Block.AddChild(section.Cards);
        return section;
    }

    protected override void Refresh()
    {
        _winnersRow.Visible = _nativeWinnersRow.Visible;
        ThemeNames.SetColor(_completing, HotNames.FontColor, _completing.Text == "0" ? Ink : Accepted);
        _complete.Visible = _nativeComplete.Visible;
        foreach (var section in _sections)
        {
            var block = section.Rows.GetParent()?.GetParent();
            section.Block.Visible = block is Control { Visible: true };
            section.Heading.Text = section.NativeHeading.Text;
            var sources = section.Rows.GetChildren().OfType<Control>().Where(c => !c.IsQueuedForDeletion()).ToArray();
            if (sources.SequenceEqual(section.Sources)) continue;
            section.Sources = sources;
            foreach (var old in section.Cards.GetChildren()) { section.Cards.RemoveChild(old); old.QueueFree(); }
            var cards = sources.Select(source => new ClassicNpcCard(source, section.Hunt, false) { CustomMinimumSize = new Vector2(1, ClassicEventsLayout.CardHeight) }).ToList();
            if (section.Grid) section.Cards.AddChild(new ClassicNpcCardGrid(cards) { Name = "cr_reward_grid" });
            else foreach (var card in cards) section.Cards.AddChild(card);
        }
        // The stack's own minimum excludes the end padding WholeItems adds.
        float content = _stack.GetMinimumSize().Y;
        float visible = Math.Min(content, Spec.MaxHeight - Spec.Top - Spec.Bottom);
        if (Math.Abs(_scroll.Size.Y - visible) > .5f) _scroll.Size = new Vector2(Spec.Content.Size.X, visible);
        ThemeNames.SetConstant(_lane, HotNames.MarginRight, content > visible + .5f ? 16 : 0);
        SetFullSize(new Vector2(Spec.Width, Mathf.Ceil(Spec.Top + visible + Spec.Bottom)));
        WholeItems(content, visible);
    }

    /// <summary>The info box, headings, cards (reward cards per grid row) and the banner, top to bottom.</summary>
    private IEnumerable<Control> Items()
    {
        foreach (var child in _stack.GetChildren().OfType<Control>().Where(c => c.Visible))
        {
            var section = _sections.FirstOrDefault(s => s.Block == child);
            if (section == null) { yield return child; continue; }
            yield return section.Heading;
            foreach (var card in section.Cards.GetChildren().OfType<Control>().Where(c => !c.IsQueuedForDeletion()))
                if (card is ClassicNpcCardGrid grid) foreach (var cell in grid.GetChildren().OfType<Control>()) yield return cell;
                else yield return card;
        }
    }

    /// <summary>
    /// A long race scrolls from item top to item top and hides an item cut by either edge; the stack is padded
    /// so the last items can scroll fully into view.
    /// </summary>
    private void WholeItems(float content, float page)
    {
        var items = Items().ToArray();
        float origin = _stack.GlobalPosition.Y;
        var tops = items.Select(i => i.GlobalPosition.Y - origin).ToArray();
        // Items are laid out a frame after a rebuild.
        if (items.Length == 0 || items.Any(i => i.Size.Y <= 0)) return;
        float end = content > page + .5f ? tops.Where(t => content - t <= page + .5f).DefaultIfEmpty(tops[^1]).Min() : 0;
        float minimum = content > page + .5f ? end + page : 0;
        if (Math.Abs(_stack.CustomMinimumSize.Y - minimum) > .5f) _stack.CustomMinimumSize = new Vector2(0, minimum);
        var bar = _scroll.GetVScrollBar();
        float value = (float)bar.Value;
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            float target = value >= _top ? tops.Where(t => t >= value - .5f).DefaultIfEmpty(end).Min() : tops.Where(t => t <= value + .5f).DefaultIfEmpty(0).Max();
            target = Math.Min(target, end);
            if (Math.Abs(value - target) > .01f) bar.Value = value = target;
            _top = target;
        }
        for (int i = 0; i < items.Length; i++)
        {
            bool whole = tops[i] >= value - .5f && tops[i] + items[i].Size.Y <= value + page + .5f;
            items[i].Modulate = whole ? Colors.White : Colors.Transparent;
        }
    }
}

/// <summary>Godot names used every frame in this file, created once (a literal creates a finalizable wrapper per call).</summary>
file static class HotNames
{
    internal static readonly StringName FontColor = "font_color";
    internal static readonly StringName MarginRight = "margin_right";
}
