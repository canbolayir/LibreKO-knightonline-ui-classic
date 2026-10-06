using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class CharacterWindow : Control
{
    private const string FrameLayout = "{nation}_various_frame_us";
    private const string PageLayout = "{nation}_page_state_us";

    private static readonly (string Button, int StatRow)[] StatButtons =
    {
        ("Btn_Strength", 0),
        ("Btn_Stamina", 1),
        ("Btn_Dexterity", 2),
        ("Btn_Intelligence", 3),
        ("Btn_MagicAttack", 4),
    };

    private readonly LayoutView _page;
    private readonly LayoutView _frame;
    private readonly PluginGame _game;
    private readonly WindowHost _host;

    public CharacterWindow(WindowHost host)
    {
        _host = host;
        var kit = Plugin.Kit;
        _game = kit.Game;
        var pageLayout = CharacterLayout.Page("character");
        var frameLayout = CharacterLayout.Frame();
        _frame = new LayoutView(kit, frameLayout);
        _page = new LayoutView(kit, pageLayout);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_frame);
        AddChild(_page);
        _page.Position = new Vector2(pageLayout.X - frameLayout.X, pageLayout.Y - frameLayout.Y);
        CustomMinimumSize = _frame.Size;
        Size = CustomMinimumSize;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Nearest;

        host.SetDragHandle(_frame.MakeDragHandle(new Rect2(44, 0, _frame.Size.X - 48, 38)));
        _frame.OnPressed("btn_close", host.Close);
        foreach (var (button, row) in StatButtons)
        {
            int which = row;
            Bind(_page, button, () => _game.Character.AllocateStat(which));
        }
        InitializePages();
        Refresh();
    }

    public override void _EnterTree()
    {
        CharacterPageRouter.Owner=this;
        Callable.From(CharacterPageRouter.ResumeVisible).CallDeferred();
        _host.Hidden+=ClearEmbedded;
        _game.Character.Changed += Refresh;
        _game.BecameAvailable += Refresh;
        _host.Shown += Refresh;
        Refresh();
    }

    public override void _Ready()
    {
        _frame.ApplyDeclaredBounds();
        foreach(var page in _pages.Values) page.ApplyDeclaredBounds();
    }

    public override void _ExitTree()
    {
        CharacterPageRouter.Release(this);
        _host.Hidden-=ClearEmbedded;
        if(CharacterPageRouter.Owner==this) CharacterPageRouter.Owner=null;
        _game.Character.Changed -= Refresh;
        _game.BecameAvailable -= Refresh;
        _host.Shown -= Refresh;
    }

    private void Refresh()
    {
        if (!_game.Available) return;
        var c = _game.Character;
        EnsurePanelStyles();
        var panel = _game.Windows.CharacterPanel;
        SetText(_page, "text_Id", c.Name);
        SetText(_page, "Text_Class", panel?.JobName ?? c.ClassName);
        SetText(_page, "Text_Level", panel?.LevelLabel ?? c.Level.ToString());
        SetText(_page, "Text_HP", $"{c.Hp} / {c.MaxHp}");
        SetText(_page, "Text_MP", $"{c.Mp} / {c.MaxMp}");
        SetText(_page, "Text_Weight", $"{c.Weight / 10f:0.0}/{c.MaxWeight / 10f:0.0}");
        SetText(_page, "Text_Race", panel?.RaceName ?? c.Race.ToString());
        SetText(_page, "Text_Nation", c.NationName);
        SetText(_page, "Text_Exp", $"{c.Exp:N0} / {c.MaxExp:N0}");
        if (Find(_page, "Text_Exp") is Label exp) exp.TooltipText = $"{c.Exp:N0} / {c.MaxExp:N0} ({c.ExpPercent:0.00}%)";
        SetText(_page, "Text_RealmPoint", c.Np.ToString("N0"));
        SetText(_page, "Text_AP", c.Ap.ToString());
        SetText(_page, "Text_GP", c.Ac.ToString());
        int[] stats = { c.Str, c.Sta, c.Dex, c.Intel, c.Mag };
        string[] fields = { "Text_Strength", "Text_Stamina", "Text_Dexterity", "Text_Intelligence", "Text_MagicAttack" };
        for (int row = 0; row < fields.Length; row++)
        {
            int bonus = panel?.StatBonus(row) ?? 0;
            var text = stats[row].ToString() + (bonus == 0 ? "" : $" ({bonus:+0;-0})");
            SetText(_page, fields[row], text);
            if (Find(_page, fields[row]) is Label label)
                label.TooltipText = $"Base: {stats[row]}\nBonus: {bonus:+0;-0;0}\nTotal: {stats[row] + bonus}";
        }
        SetText(_page, "Text_BonusPoint", c.Points.ToString());
        string[] resist = { "Text_RegistFire", "Text_RegistIce", "Text_RegistLightR", "Text_RegistMagic", "Text_RegistCurse", "Text_RegistPoison" };
        for (int i = 0; i < resist.Length; i++) SetText(_page, resist[i], c.Resist(i).ToString());
        foreach (var (button, row) in StatButtons)
            if (Find(_page, button) is BaseButton b) { b.Visible = c.Points > 0; b.Disabled = c.Points <= 0 || stats[row] >= 255; }
        UpdateHighlights(c.Class);
        int family = LibreKO.Domain.CharacterClassCatalog.Family(c.Class);
        int[] emphasized = family switch { 1 => new[] { 0, 1 }, 2 => new[] { 1, 2 }, 3 => new[] { 3, 4 }, 4 => new[] { 0, 3 }, _ => Array.Empty<int>() };
        for (int i = 0; i < 5; i++)
            if (Find(_page, "stat_caption_" + i) is Label caption)
                caption.AddThemeColorOverride("font_color", emphasized.Contains(i) ? ClassicReportDesign.Accent : ClassicReportDesign.Caption);
        if (_titleButton != null) { _titleButton.Text = (panel?.TitleName ?? "Title: none").Replace("Title: ", ""); _titleButton.TooltipText = panel?.TitleName ?? "Choose a title"; }
        if (IsVisibleInTree()) RefreshPages();
    }
}
