using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Native confirmation behavior with the same outside ornament and Classic rails.</summary>
public partial class ClassicConfirmationFrame : Control
{
    private readonly AcceptDialog _dialog;
    private readonly LayoutView _frame;
    private readonly Label _title;
    private readonly int _top=ClassicDesign.Karus?40:44;
    public ClassicConfirmationFrame(AcceptDialog dialog)
    {
        _dialog=dialog;MouseFilter=MouseFilterEnum.Ignore;
        var root=CharacterLayout.Frame();root.Children.RemoveAll(n=>n.Type=="classic_tab");
        root.Find("header_rails")!.Type="classic_detail_header";root.Find("header_rails")!.Y=_top;
        root.Find("surface")!.Y=_top;
        _frame=new LayoutView(Plugin.Kit,root);AddChild(_frame);
        var close=(BaseButton)_frame.Where(n=>n.Id=="btn_close").First().Control!;
        close.Pressed+=()=>dialog.EmitSignal(Window.SignalName.CloseRequested);
        _title=new Label { Position=new Vector2(48,_top+4),Size=new Vector2(300,36),VerticalAlignment=VerticalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore };
        _title.AddThemeFontOverride("font",Plugin.Kit.Bold);_title.AddThemeFontSizeOverride("font_size",13);
        _title.AddThemeColorOverride("font_color",ClassicReportDesign.Caption);AddChild(_title);
    }
    public override void _Ready() { _frame.ApplyDeclaredBounds(); }
    public override void _Process(double delta)
    {
        Position=Vector2.Zero;Size=(Vector2)_dialog.Size;_title.Text=_dialog.Title;_title.Size=new Vector2(Size.X-60,36);
        var surface=_frame.Where(n=>n.Id=="surface").First().Control!;surface.Size=new Vector2(Size.X,Math.Max(1,Size.Y-_top));
        var rails=_frame.Where(n=>n.Id=="header_rails").First().Control!;rails.Size=new Vector2(Size.X,42);
    }
}
