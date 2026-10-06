using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic;

public static class ClassicSkin
{
    public static StyleBoxTexture PanelStyle()
    {
        var kit = Plugin.Kit;
        // A single original inventory cell: preserve its gold border when resized.
        bool karus=kit.Nation==1;
        var tex = kit.Texture(karus ? "ui_ka_inven_us.png" : "ui_el_inven_us.png");
        var style = new StyleBoxTexture { Texture = tex, RegionRect = karus ? new Rect2(159, 326, 51, 52) : new Rect2(157, 333, 51, 52) };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        {
            style.SetTextureMargin(side, 3);
            style.SetContentMargin(side, 7);
        }
        return style;
    }

    public static StyleBoxTexture ButtonStyle(string state)
    {
        int tag=state=="pressed" ? 1 : state=="hover" ? 2 : 0;
        var button=Plugin.Kit.Layout("{nation}_skilltree_us").Find("btn_public")!;
        var part=button.Images.FirstOrDefault(i=>i.Tag==tag) ?? button.Images.First();
        var style=new StyleBoxTexture { Texture=Plugin.Kit.Texture(part.Texture!),RegionRect=new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH) };
        foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom})
        {
            style.SetTextureMargin(side,4);
            style.SetContentMargin(side,side is Side.Left or Side.Right ? 7:3);
        }
        if(state=="disabled") style.ModulateColor=new Color(0.55f,0.55f,0.55f);
        return style;
    }

    public static void Apply(Control root)
    {
        var theme = new Theme();
        theme.SetStylebox("panel", "PanelContainer", PanelStyle());
        theme.SetStylebox("panel", "Panel", PanelStyle());
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var style = ButtonStyle(state);
            theme.SetStylebox(state, "Button", style);
        }
        var ink=Plugin.Kit.Nation==1 ? new Color(0.10f,0.09f,0.08f) : new Color(0.95f,0.88f,0.65f);
        theme.SetColor("font_color", "Button",ink);
        theme.SetColor("font_hover_color", "Button",ink);
        theme.SetColor("font_pressed_color", "Button",ink);
        theme.SetColor("font_shadow_color","Button",Plugin.Kit.Nation==1 ? new Color(0.85f,0.85f,0.85f,0.5f) : Colors.Black);
        theme.SetFont("font","Button",Plugin.Kit.Regular);
        theme.SetFontSize("font_size","Button",12);
        theme.SetConstant("shadow_offset_x","Button",1);
        theme.SetConstant("shadow_offset_y","Button",1);
        root.Theme = theme;
    }

    public static void ExtendWindow(Control body)
    {
        // Extension runs before the window finishes adding its controls.
        body.Ready += () => Callable.From(() =>
        {
            Control top=body;
            while (top.GetParent() is Control parent && top is not LibreKO.HudWindow) top=parent;
            Apply(top);
            if (top is PanelContainer panel)
            {
                panel.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
                var frame=new ClassicFrame();
                panel.AddChild(frame); panel.MoveChild(frame,0);
            }
            if (top is LibreKO.HudWindow window)
                window.SetHeaderAccent(new Color(0.12f,0.11f,0.09f),new Color(0.58f,0.51f,0.34f),new Color(0.94f,0.87f,0.66f));
            if(top is LibreKO.HudWindow native && native.Id is "shoppingmall" or "mail" or "mailread")
                top.AddChild(new ClassicNativeButtonSkin());
            void Restyle(Node n)
            {
                if (n is Button button)
                    foreach(var state in new[]{"normal","hover","pressed","disabled"})
                        button.AddThemeStyleboxOverride(state,ButtonStyle(state));
                if (n is Label label)
                {
                    label.AddThemeFontOverride("font",Plugin.Kit.Regular);
                    label.AddThemeFontSizeOverride("font_size",Math.Max(12,(int)Plugin.Kit.Metric("modernFontSize",13)));
                    label.AddThemeColorOverride("font_shadow_color",Colors.Black);
                    label.AddThemeConstantOverride("shadow_offset_x",1); label.AddThemeConstantOverride("shadow_offset_y",1);
                }
                foreach(Node child in n.GetChildren()) Restyle(child);
            }
            Restyle(body);
        }).CallDeferred();
    }

    public static Button Button(string text, Vector2 position, Vector2 size, Action pressed)
    {
        var button = new Button { Text=text, Position=position, Size=size, FocusMode=Control.FocusModeEnum.None };
        Apply(button);
        button.Pressed += pressed;
        return button;
    }
}

public partial class ClassicNativeButtonSkin : Node
{
    private double _elapsed;
    public override void _Ready()=>Apply();
    public override void _Process(double delta)
    {
        if(GetParent() is Control control && !control.IsVisibleInTree())return;
        _elapsed+=delta;if(_elapsed<.08)return;_elapsed=0;Apply();
    }
    private void Apply()
    {
        bool karus=Plugin.Kit.Nation==1;
        var ink=karus?new Color("191713"):new Color("efd9b4");
        var scope=GetParent().GetParent() is CanvasLayer layer?layer:GetParent();
        foreach(var button in KnightOnlineUiClassic.Windows.CharacterDetailsSkin.Tree(scope).OfType<Button>())
        {
            if(button.HasMeta("classic_native_button"))continue;
            button.SetMeta("classic_native_button",true);
            bool selected=button.GetThemeStylebox("normal") is StyleBoxFlat normal && normal.BgColor==LibreKO.UiTheme.ListRow(true).BgColor;
            foreach(var state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})
                button.AddThemeStyleboxOverride(state,ClassicSkin.ButtonStyle(state=="hover_pressed" || state=="normal" && selected?"pressed":state));
            foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color","font_disabled_color"})
                button.AddThemeColorOverride(state,state=="font_disabled_color"?(karus?new Color("4a4640"):new Color("998972")):ink);
            foreach(var state in new[]{"icon_normal_color","icon_hover_color","icon_pressed_color","icon_hover_pressed_color"})
                button.AddThemeColorOverride(state,ink);
            button.AddThemeFontOverride("font",Plugin.Kit.Bold);
        }
    }
}

// HUD factories run before the game bridge attaches. Rebuild once the real nation is known.
public partial class NationHud : Control
{
    private readonly Func<Control> _factory;
    private int _nation = -1;
    public NationHud(Func<Control> factory) { _factory=factory; MouseFilter=MouseFilterEnum.Ignore; }
    public override void _Ready()
    {
        Plugin.Kit.Game.BecameAvailable += Refresh;
        Plugin.Kit.Game.Character.Changed += Refresh;
        Refresh();
    }
    private void Refresh()
    {
        if (!Plugin.Kit.Game.Available) return;
        int nation = Plugin.Kit.Game.Character.Nation;
        if (nation == _nation) return;
        _nation = nation;
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        AddChild(_factory());
    }
    public override void _ExitTree()
    {
        Plugin.Kit.Game.BecameAvailable -= Refresh;
        Plugin.Kit.Game.Character.Changed -= Refresh;
    }
}
