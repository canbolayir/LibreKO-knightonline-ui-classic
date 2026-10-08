using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class LogWindow : Control
{
    private static readonly Vector2 DefaultSize = new(420, 205);
    private static readonly Vector2 Minimum = new(290, 135);
    private readonly PluginGame _game = Plugin.Kit.Game;
    private readonly LogText _log;
    private readonly ClassicScrollRail _rail;
    private readonly Panel _background;
    private readonly Panel _title;
    private readonly Control _surface = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly ClassicChatTransparency _transparency;
    private HudLayout? _layout;

    public LogWindow()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Size = DefaultSize;
        CustomMinimumSize = Minimum;
        AddChild(_surface);
        _surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background = ClassicChatControls.Backdrop();
        _surface.AddChild(_background);
        _log = new LogText(13, Colors.White, Plugin.Kit.ChatFont);
        _surface.AddChild(_log);
        _rail = new ClassicScrollRail(_log);
        _surface.AddChild(_rail);
        _title = ClassicChatControls.Title("Info");
        _surface.AddChild(_title);
        _transparency = new ClassicChatTransparency(_surface, _title, "log.transparency_step");
        ClassicChatControls.SnapToPixels(this);
        Resized += Arrange;
        Arrange();
    }

    public override void _EnterTree()
    {
        _game.Log.LineAdded += OnLine;
        _game.BecameAvailable += Reload;
        Config.EffectsChanged += ApplyVisibility;
        _layout ??= NativeLayout.Attach(this, "classic_log", _title, DefaultPosition,
            resizable: true, defaultSize: DefaultSize, minimumSize: Minimum, resizeCorner: HudLayout.Corner.TopLeft,
            backgroundOpacityChanged: _ => _transparency.Cycle(),legacyResizeGrip:true, resizeSnapPeerId:"classic_chat");
        Reload();
        ApplyVisibility();
    }

    public override void _ExitTree()
    {
        _game.Log.LineAdded -= OnLine;
        _game.BecameAvailable -= Reload;
        Config.EffectsChanged -= ApplyVisibility;
    }

    private void ApplyVisibility() => Visible = Config.CombatLog;

    private void Arrange()
    {
        float footer = Mathf.Round(Size.Y) - 24;
        _background.Size = new Vector2(Size.X, footer);
        _title.Position = new Vector2(Size.X - 64, footer);
        _title.Size = new Vector2(64, 24);
        _log.Position = new Vector2(4, 4);
        _log.Size = new Vector2(Size.X - 28, footer - 8);
        _rail.Position = new Vector2(Size.X - 21, 4);
        _rail.Size = new Vector2(18, footer - 8);
    }

    private Vector2 DefaultPosition()
    {
        var room = GetViewport().GetVisibleRect().Size;
        return new Vector2(room.X - Size.X - 8, room.Y - Taskbar.BarHeight - Size.Y - 4);
    }

    private void Reload() => _log.Set(_game.Log.History.Select(Format));
    private void OnLine(GameLogLine line) => _log.Append(Format(line));
    private static string Format(GameLogLine line) =>
        $"[color=#{line.Color.ToHtml(false)}]{line.Text.Replace("[", "[lb]")}[/color]";
}
