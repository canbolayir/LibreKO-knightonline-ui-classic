using Godot;

namespace KnightOnlineUiClassic.Layout;

public partial class LogText : Control
{
    private readonly RichTextLabel _label;
    private readonly ScrollContainer _scroll;
    private readonly Queue<string> _lines = new();
    private VScrollBar? _rail;
    private bool _syncing;
    private bool _follow = true;
    private bool _contentPending;

    public LogText(int fontSize, Color color, Font font)
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(_scroll);
        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(content);
        content.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _label = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
        };
        foreach (var name in new[] { "normal_font", "bold_font", "italics_font", "bold_italics_font" })
            _label.AddThemeFontOverride(name, font);
        foreach (var name in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            _label.AddThemeFontSizeOverride(name, fontSize);
        _label.AddThemeColorOverride("default_color", color);
        _label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 1);
        _label.AddThemeConstantOverride("shadow_offset_x", 1);
        _label.AddThemeConstantOverride("shadow_offset_y", 1);
        if (Plugin.Kit.Nation == 1)
        {
            _label.AddThemeConstantOverride("outline_size", 2);
            _label.AddThemeConstantOverride("shadow_outline_size", 1);
        }
        _label.AddThemeConstantOverride("line_separation", 2);
        foreach (var state in new[] { "normal", "focus" })
            _label.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        content.AddChild(_label);
        var bar = _scroll.GetVScrollBar();
        bar.Changed += () =>
        {
            SyncRail();
            if (_follow && IsInsideTree()) Callable.From(FollowEnd).CallDeferred();
        };
        bar.ValueChanged += _ =>
        {
            if (!_syncing && !_contentPending) _follow = AtEnd();
            SyncRail();
        };
    }

    public event Action<string>? MetaClicked, MetaHoverStarted;
    public event Action? MetaHoverEnded;
    public void EnableLinks()
    {
        _label.MouseFilter=MouseFilterEnum.Pass;_label.MetaUnderlined=false;
        _label.MetaClicked+=v=>MetaClicked?.Invoke(v.AsString());
        _label.MetaHoverStarted+=v=>MetaHoverStarted?.Invoke(v.AsString());
        _label.MetaHoverEnded+=_=>MetaHoverEnded?.Invoke();
    }
    public void SetFontSize(int size)
    {
        foreach(var name in new[]{"normal_font_size","bold_font_size","italics_font_size","bold_italics_font_size"}) _label.AddThemeFontSizeOverride(name,size);
    }

    public void BindScrollBar(VScrollBar rail)
    {
        _rail = rail;
        rail.ValueChanged += value =>
        {
            if (_syncing) return;
            _scroll.GetVScrollBar().Value = value;
            _follow = AtEnd();
        };
        SyncRail();
    }

    public void Set(IEnumerable<string> lines)
    {
        _lines.Clear();
        foreach (var line in lines) _lines.Enqueue(line);
        _follow = true;
        Apply();
    }

    public void SetPreservingScroll(IEnumerable<string> lines)
    {
        if(!_contentPending) _follow=AtEnd();
        _lines.Clear();foreach(var line in lines) _lines.Enqueue(line);Apply();
    }

    public void Append(string bbcode)
    {
        if (!_contentPending) _follow = AtEnd();
        _lines.Enqueue(bbcode);
        Apply();
    }

    public void Scroll(float pixels)
    {
        _scroll.GetVScrollBar().Value -= pixels;
        _follow = AtEnd();
    }

    private bool AtEnd()
    {
        var bar = _scroll.GetVScrollBar();
        return bar.Value + bar.Page >= bar.MaxValue - 2;
    }

    private void Apply()
    {
        while (_lines.Count > 200) _lines.Dequeue();
        _contentPending = true;
        _label.Text = string.Join("\n", _lines);
        Callable.From(() => { FollowEnd(); _contentPending = false; }).CallDeferred();
    }

    private void FollowEnd()
    {
        if (_follow)
        {
            _syncing = true;
            var bar = _scroll.GetVScrollBar();
            bar.Value = Mathf.Max(0, bar.MaxValue - bar.Page);
            _syncing = false;
        }
        SyncRail();
    }

    private void SyncRail()
    {
        if (_rail == null || _syncing) return;
        _syncing = true;
        var bar = _scroll.GetVScrollBar();
        _rail.MinValue = bar.MinValue;
        _rail.MaxValue = bar.MaxValue;
        _rail.Page = bar.Page;
        _rail.Step = 1;
        _rail.Value = bar.Value;
        _syncing = false;
    }
}
