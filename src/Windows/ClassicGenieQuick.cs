using Godot;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Native;
using Spec = KnightOnlineUiClassic.Layout.ClassicOverlayLayout.GenieQuick;

namespace KnightOnlineUiClassic.Windows;

/// <summary>
/// The genie quick panel on the event board frame, with the original whisper window's minimise and close buttons on
/// its caption row. The remaining time (in minutes, on the gauge) sits beside the client's Start/Stop and settings
/// buttons, so the minimised panel still shows the time and whether the Genie runs. Closed, the panel stays hidden
/// until the Genie window opens or the Genie starts. The client keeps every control, refresh and its drag handle.
/// </summary>
public static class ClassicGenieQuick
{
    public const string Minimise = "genie_quick_minimise", Close = "genie_quick_close", TimeRow = "genie_quick_time_row";

    public static void Apply(PanelContainer panel)
    {
        if (NativeGenieQuick.Prepare(panel) is not { } parts) return;
        var column = parts.Column;
        column.AddThemeConstantOverride("separation", Spec.RowGap);

        // Caption row: title, then minimise and close.
        var head = parts.Head;
        head.AddThemeConstantOverride("separation", Spec.CaptionGap);
        head.CustomMinimumSize = new Vector2(head.CustomMinimumSize.X, Spec.CaptionHeight);
        ClassicEventPanel.Bold(parts.Title, ClassicDesign.FrameTitle, Spec.TitleFont);
        parts.Title.ClipText = true; parts.Title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var minimise = CaptionButton(Minimise);
        var close = CaptionButton(Close);
        ClassicChatControls.SkinButton(close, Plugin.Kit.Layout("co_whisper_open_us").Find("btn_close")!);
        close.TooltipText = "Close";
        head.AddChild(minimise);
        head.AddChild(close);

        // Time row: the gauge with the minutes, Start/Stop and the settings button.
        var time = new HBoxContainer { Name = TimeRow, MouseFilter = Control.MouseFilterEnum.Ignore };
        time.SetMeta(ClassicEventFamily.Skinned, true);
        time.AddThemeConstantOverride("separation", Spec.ControlGap);
        time.CustomMinimumSize = new Vector2(0, Spec.TimeHeight);
        column.AddChild(time);
        column.MoveChild(time, 1);
        parts.Gauge.Reparent(time, false);
        parts.Gauge.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parts.Gauge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        parts.Gauge.CustomMinimumSize = new Vector2(0, Spec.GaugeHeight);
        parts.Toggle.Reparent(time, false);
        parts.Settings.Reparent(time, false);
        foreach (var button in new[] { parts.Toggle, parts.Settings }) button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        parts.Toggle.CustomMinimumSize = Spec.Toggle; parts.Settings.CustomMinimumSize = Spec.Settings;
        // Since 3277df5 the last monster row has no action button (its check adds the selected monster); every row
        // keeps the button height so the rows stay evenly spaced.
        float rowHeight = parts.Rows.Select(r => r.GetCombinedMinimumSize().Y).DefaultIfEmpty(0).Max();
        foreach (var row in parts.Rows) row.CustomMinimumSize = new Vector2(row.CustomMinimumSize.X, rowHeight);
        ClassicEventPanel.Bold(parts.Points, ClassicEventPanel.Ink, Spec.TimeFont);
        parts.Points.AddThemeConstantOverride("outline_size", Spec.TimeOutline);
        parts.Points.AddThemeColorOverride("font_outline_color", Colors.Black);

        var state = new ClassicGenieQuickState(panel, parts, minimise) { Name = "classic_genie_quick" };
        panel.AddChild(state);
        minimise.Pressed += () => { NativeGenieQuick.Minimised = !NativeGenieQuick.Minimised; state.Sync(); };
        close.Pressed += () => { NativeGenieQuick.Closed = true; panel.Hide(); };
        panel.VisibilityChanged += () =>
        {
            if (panel.Visible && NativeGenieQuick.Closed) Callable.From(() => { if (GodotObject.IsInstanceValid(panel) && NativeGenieQuick.Closed) panel.Hide(); }).CallDeferred();
        };
        state.Sync();
        if (NativeGenieQuick.Closed) panel.Hide();
    }

    private static Button CaptionButton(string name)
    {
        var button = new Button { Name = name, CustomMinimumSize = Spec.Caption, FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.SetMeta(ClassicEventFamily.Skinned, true);
        return button;
    }
}

/// <summary>
/// Follows the quick panel's state every frame: the minutes on the gauge, the minimised rows and their button art,
/// and the reopening of a closed panel when the Genie window opens or the Genie starts.
/// </summary>
public partial class ClassicGenieQuickState : Node
{
    private readonly PanelContainer _panel;
    private readonly NativeGenieQuick.Parts _parts;
    private readonly Button _minimise;
    private bool? _minimised;
    private bool _windowShown, _running;

    public ClassicGenieQuickState() { _panel = null!; _minimise = null!; }

    public ClassicGenieQuickState(PanelContainer panel, NativeGenieQuick.Parts parts, Button minimise)
    {
        _panel = panel; _parts = parts; _minimise = minimise;
        var world = NativeGenieQuick.WorldOf(panel);
        _windowShown = NativeGenieQuick.WindowShown(world);
        _running = NativeGenieQuick.Running;
    }

    public override void _Process(double delta)
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel)) return;
        string time = NativeGenieQuick.TimeText(_parts.Points.Text);
        if (time != _parts.Points.Text) _parts.Points.Text = time;
        var world = NativeGenieQuick.WorldOf(_panel);
        bool shown = NativeGenieQuick.WindowShown(world), running = NativeGenieQuick.Running;
        if (NativeGenieQuick.Closed && ((shown && !_windowShown) || (running && !_running))) Reopen(world);
        _windowShown = shown; _running = running;
        Sync();
    }

    private void Reopen(LibreKO.Scene.WorldRoot? world)
    {
        NativeGenieQuick.Closed = false;
        // The client decides whether the panel shows (time left or running); without a world it is shown as it was.
        if (world == null || !Native.Native.TryCall(world, "GenieSub.RefreshSub", out _)) _panel.Show();
    }

    /// <summary>Applies the minimised state: the monster rows fold away and the panel fits its rows.</summary>
    public void Sync()
    {
        bool minimised = NativeGenieQuick.Minimised;
        if (_minimised == minimised) return;
        _minimised = minimised;
        foreach (var row in _parts.Rows) row.Visible = !minimised;
        ClassicChatControls.SkinButton(_minimise, minimised
            ? Plugin.Kit.Layout("co_whisper_close_us").Find("btn_open")!
            : Plugin.Kit.Layout("co_whisper_open_us").Find("btn_hide")!);
        _minimise.TooltipText = minimised ? "Restore" : "Minimise";
        Callable.From(() => { if (GodotObject.IsInstanceValid(_panel)) _panel.ResetSize(); }).CallDeferred();
    }
}
