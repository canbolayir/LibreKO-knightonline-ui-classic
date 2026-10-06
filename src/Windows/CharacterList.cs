using Godot;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

/// <summary>One shared row selection and pager for the original aligned list columns.</summary>
public partial class CharacterList : Control
{
    private readonly float[] _positions;
    private readonly float[] _widths;
    private readonly int _pageSize;
    private readonly Label _empty;
    private readonly List<Button> _buttons = new();
    private IReadOnlyList<GamePanelRow> _rows = Array.Empty<GamePanelRow>();
    private string _signature = "";
    public string Selected { get; private set; } = "";
    public int Page { get; private set; }
    public int Pages => Math.Max(1, (_rows.Count + _pageSize - 1) / _pageSize);
    public int Count => _rows.Count;
    public GamePanelRow? Selection => _rows.FirstOrDefault(r => r.Id == Selected) is var r && r.Id != null ? r : null;
    public event Action<string>? SelectedRow;
    public event Action<string>? ActivatedRow;
    public event Action<string>? ContextRow;

    public CharacterList(Rect2 rect, float[] positions, float[] widths, int pageSize)
    {
        Position = rect.Position.Round(); Size = rect.Size.Round(); ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        _positions = positions; _widths = widths; _pageSize = pageSize;
        _empty = new Label { Text = "No entries.", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        _empty.AddThemeFontOverride("font", Plugin.Kit.Regular); _empty.AddThemeFontSizeOverride("font_size", 12);
        _empty.AddThemeColorOverride("font_color", new Color("ece6d6"));
        _empty.AddThemeColorOverride("font_outline_color", Colors.Black); _empty.AddThemeConstantOverride("outline_size", 1);
        AddChild(_empty); _empty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } b && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { ChangePage(b.ButtonIndex == MouseButton.WheelUp ? -1 : 1); AcceptEvent(); }
        };
    }

    public void SetRows(IReadOnlyList<GamePanelRow> rows)
    {
        _empty.Visible = rows.Count == 0;
        string signature = string.Join("\u001e", rows.Select(r => r.Id + "\u001f" + string.Join("\u001f", r.Cells) + r.Hint + r.Color.ToHtml()
            + $"/{r.Online}/{r.Trackable}/{r.Abandonable}/{r.Claimable}/{r.Tracked}"));
        if (signature == _signature) return;
        _signature = signature; _rows = rows;
        if (!_rows.Any(r => r.Id == Selected)) Selected = "";
        Page = Math.Clamp(Page, 0, Pages - 1);
        BuildRows();
    }

    public void SetEmptyText(string text) => _empty.Text = text;

    public void ChangePage(int delta)
    {
        int next = Math.Clamp(Page + delta, 0, Pages - 1);
        if (next == Page) return;
        Page = next; Selected = ""; BuildRows();
    }

    private void BuildRows()
    {
        foreach (var button in _buttons) { RemoveChild(button); button.QueueFree(); }
        _buttons.Clear();
        float height = Mathf.Floor(Size.Y / _pageSize);
        foreach (var (row, i) in _rows.Skip(Page * _pageSize).Take(_pageSize).Select((r, i) => (r, i)))
        {
            var button = new Button { Position = new Vector2(4, i * height),
                Flat = false, ToggleMode = true, FocusMode = FocusModeEnum.None, TooltipText = row.Hint, MouseFilter = MouseFilterEnum.Stop };
            button.AddThemeFontOverride("font",Plugin.Kit.Regular);
            button.AddThemeFontSizeOverride("font_size",11);
            bool selected = row.Id == Selected;
            button.SetPressedNoSignal(selected);
            var normal = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color("00ff00") };
            if (selected) normal.SetBorderWidthAll(1);
            normal.SetContentMarginAll(0);
            var hover = selected ? normal : new StyleBoxFlat { BgColor = new Color(.16f, .18f, .19f, .45f) };
            hover.SetContentMarginAll(0);
            button.AddThemeStyleboxOverride("normal", normal); button.AddThemeStyleboxOverride("hover", hover);
            button.AddThemeStyleboxOverride("pressed", normal); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("hover_pressed", normal);
            AddChild(button); _buttons.Add(button);
            button.UpdateMinimumSize(); _=button.GetCombinedMinimumSize(); button.Size=new Vector2(Size.X-8,height);
            for (int col = 0; col < _positions.Length; col++)
            {
                string text = col < row.Cells.Length ? row.Cells[col] : "";
                if (_positions.Length == 4 && col == 0) text = text switch { "Vice-chief" => "Vice", "CommandCaptain" => "Cmd", _ => text };
                if (_positions.Length == 4 && col == 3 && _widths[col]<50) text=text switch { "Warrior"=>"War.","Rogue"=>"Rog.","Priest"=>"Pri.","Magician" or "Mage"=>"Mag.",_=>text };
                if (_positions.Length == 3 && col == 1 && _widths[col]<70) text=text switch { "In Progress"=>"Active","Completed"=>"Done",_=>text };
                var label = new Label { Text = text, Position = new Vector2(_positions[col] + 1, 0),
                    VerticalAlignment = VerticalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    MouseFilter = MouseFilterEnum.Ignore };
                ClassicDesign.StyleLabel(label);
                label.AddThemeFontSizeOverride("font_size", 13);
                label.AddThemeColorOverride("font_color", row.Color);
                button.AddChild(label);
                label.UpdateMinimumSize(); _=label.GetCombinedMinimumSize(); label.Size=new Vector2(_widths[col]-10,height);
            }
            button.Pressed += () => Choose(row.Id);
            button.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true } b) return;
                if (b.ButtonIndex == MouseButton.Right) { button.AcceptEvent(); Choose(row.Id); ContextRow?.Invoke(row.Id); }
                else if (b.ButtonIndex == MouseButton.Left && b.DoubleClick) ActivatedRow?.Invoke(row.Id);
            };
        }
    }

    private void Choose(string id)
    {
        Selected = id;
        var rows = _rows.Skip(Page * _pageSize).Take(_pageSize).ToArray();
        for (int i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            bool selected = rows[i].Id == id;
            button.SetPressedNoSignal(selected);
            var normal = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color("00ff00") };
            if (selected) normal.SetBorderWidthAll(1);
            normal.SetContentMarginAll(0);
            var hover = selected ? normal : new StyleBoxFlat { BgColor = new Color(.16f, .18f, .19f, .45f) };
            hover.SetContentMarginAll(0);
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("pressed", normal);
            button.AddThemeStyleboxOverride("hover", hover);
            button.AddThemeStyleboxOverride("hover_pressed", normal);
            foreach (var label in button.GetChildren().OfType<Label>())
                label.AddThemeColorOverride("font_color", rows[i].Color);
        }
        // Keep the clicked control alive so Godot can deliver a second click to it.
        SelectedRow?.Invoke(id);
    }
}
