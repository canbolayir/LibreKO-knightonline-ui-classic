using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Shared geometry for auxiliary pages inside the existing Various window.</summary>
public static class CharacterEmbeddedLayout
{
    public static readonly Rect2 Content = new(8,116,344,410);
    public static readonly IReadOnlyDictionary<string,Rect2> Slots=new Dictionary<string,Rect2>
    {
        ["title_hint"]=new(12,118,336,54),["title_caption"]=new(12,176,262,20),
        ["title_list"]=new(12,204,336,306),["title_frame"]=new(8,198,344,316),
        ["clan_notice"]=new(12,246,336,194),["clan_frame"]=new(8,240,344,208),
        ["clan_editor"]=new(8,452,344,24),["clan_list"]=new(12,292,336,148),
        ["clan_hint"]=new(12,240,336,46),["clan_status"]=new(12,480,336,20),
        ["clan_refresh"]=new(8,504,62,20),["clan_save"]=new(74,504,70,20),
        ["clan_ally"]=new(148,504,120,20),["clan_leave"]=new(272,504,80,20)
    };
    public static LayoutNode Page(string caption)
    {
        var root=new LayoutNode { Type="base",Id="embedded_page",W=360,H=550 };
        root.Children.Add(new LayoutNode { Type="classic_button",Id="back",Text="Back",Font="Arial",Size=9,Bold=true,X=8,Y=88,W=62,H=20 });
        root.Children.Add(new LayoutNode { Type="string",Id="heading",Text=caption,Font="Arial",Size=10,Bold=true,Color=ClassicReportDesign.Caption,
            Style=TextStyle.SingleLine|TextStyle.AlignVCenter|TextStyle.AlignCenter,X=78,Y=88,W=274,H=20 });
        root.Children.Add(new LayoutNode { Type="classic_rule",Id="heading_rule",X=3,Y=112,W=355,H=2 });
        return root;
    }
}
