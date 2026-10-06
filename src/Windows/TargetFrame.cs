using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class TargetFrame : Control
{
    private const string LayoutName = "co_targetbar_us";
    private const string HpBar = "pro_target";
    private const string NameText = "text_target";
    private const float TopMargin = 8f;

    private readonly LayoutView _view;
    private readonly PluginGame _game;

    public TargetFrame()
    {
        var kit = Plugin.Kit;
        _game = kit.Game;
        _view = new LayoutView(kit, kit.Layout(LayoutName));
        Size = _view.Size;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_view);
        _view.Hide("Progress_HP_slow", "Progress_HP_drop", "Progress_HP_lasting", "Button_Board");
        Visible = false;
    }

    public override void _EnterTree()
    {
        _game.Target.Changed += Refresh;
        _game.BecameAvailable += Refresh;
        _game.BecameUnavailable += Refresh;
        GetViewport().SizeChanged += Place;
        Place();
        Refresh();
    }

    public override void _ExitTree()
    {
        _game.Target.Changed -= Refresh;
        _game.BecameAvailable -= Refresh;
        _game.BecameUnavailable -= Refresh;
        GetViewport().SizeChanged -= Place;
    }

    private void Place()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        Position = new Vector2(Mathf.Round((width - Size.X) * 0.5f), TopMargin);
    }

    private void Refresh()
    {
        var t = _game.Target;
        Visible = _game.Available && t.Has;
        if (!Visible) return;
        _view.SetText(NameText, t.Level > 0 ? $"{t.Name}  Lv.{t.Level}" : t.Name);
        _view.SetProgress(HpBar, t.MaxHp > 0 ? t.Hp / (float)t.MaxHp : 1f);
    }
}
