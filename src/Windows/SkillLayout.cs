using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Editable skill geometry on the original nation-specific artwork.</summary>
internal static class SkillLayout
{
    internal const string FontFamily="Arial";
    internal const int BodyFontPoints=9;
    public static LayoutNode Build(LayoutNode source)
    {
        var root = new LayoutNode { Type="base", Id="skill_window", W=365, H=574 };
        foreach (var child in source.Children.Where(n=>n.IsImage || n.IsButton))
            root.Children.Add(CharacterLayout.CopyImage(child,0,0));
        int offset=Plugin.Kit.Nation==1 ? 2 : 0;
        Text(root,"text_Skill Point","Skill Point",14,78+offset,88,22,true);
        Text(root,"string_skillpoint","",106,78+offset,44,22,true,true);
        string[] national={"Leadership","Politics","Language","Siege Weapon"};
        for(int row=0;row<4;row++)
        {
            int x=row%2==0?4:184, y=103+offset+row/2*20;
            Text(root,"national_"+row,national[row],x,y,87,20);
            Text(root,"string_"+row,"0",row%2==0?93:271,y,59,20,true,true);
            Text(root,"tree_name_"+row,"",x,153+offset+row/2*20,87,20);
            Text(root,"string_"+(row+4),"",row%2==0?93:271,153+offset+row/2*20,59,20,true,true);
        }
        for(int i=0;i<6;i++)
        {
            int x=i%2==0?24:188, y=249+i/2*49;
            root.Children.Add(new LayoutNode { Type="area",Id=i.ToString(),AreaType=7,X=x,Y=y,W=32,H=32 });
            var name=Text(root,"string_list_"+i,"",x+42,y-1,110,34,true);
            name.Style=TextStyle.AlignLeft|TextStyle.AlignVCenter;
        }
        Text(root,"string_info","",25,396,312,42);
        string[] details={"string_skill_mp","string_skill_point","string_skill_item0","string_skill_item1","string_skill_item2"};
        for(int i=0;i<details.Length;i++) Text(root,details[i],"",25,440+i*15,312,15);
        Text(root,"string_page","",146,525,72,22,true,true);
        var title=source.Find("img_public")!;
        var fallback=Text(root,"class_fallback","",title.X,title.Y,title.W,title.H,false,true);
        fallback.Size=13;
        return root;
    }

    private static LayoutNode Text(LayoutNode root,string id,string text,int x,int y,int w,int h,bool bold=true,bool centered=false)
    {
        var node=new LayoutNode { Type="string",Id=id,Text=text,X=x,Y=y,W=w,H=h,Font=FontFamily,Size=BodyFontPoints,Bold=bold,
            Color=ClassicReportDesign.Value,Style=TextStyle.SingleLine|TextStyle.AlignVCenter|(centered?TextStyle.AlignCenter:TextStyle.AlignLeft) };
        root.Children.Add(node);
        return node;
    }
}
