using Godot;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
namespace KnightOnlineUiClassic;

/// <summary>Original quest-menu plates and matching upper and lower filigree.</summary>
public partial class ClassicNpcQuestChrome : ClassicFrame
{
    public ClassicNpcQuestChrome() { BackgroundColor=Colors.Black;BackgroundAlpha=1;TextureFilter=TextureFilterEnum.Nearest; }
    public override void _Draw()
    {
        base._Draw();
        var quest=Plugin.Kit.Texture("ui_quest_us.png");
        if(quest==null) return;
        // A complete mirrored band keeps the corners and central filigree on the same seam.
        DrawRect(new Rect2(0,0,Size.X,42),Colors.Black);
        DrawSetTransform(new Vector2(0,42),0,new Vector2(1,-1));
        DrawOrnamentBand(quest,0);
        DrawSetTransform(Vector2.Zero);
        DrawOrnamentBand(quest,Size.Y-42);
        var title=Plate("normal");title.Draw(GetCanvasItem(),ClassicNpcLayout.TitlePlate);
        DrawLine(new Vector2(22,67),new Vector2(341,67),new Color("746143"));
    }
    private void DrawOrnamentBand(Texture2D texture,float y)
    {
        DrawTextureRectRegion(texture,new Rect2(0,y,42,42),new Rect2(0,269,42,42));
        DrawTextureRectRegion(texture,new Rect2(42,y,139,42),new Rect2(42,269,139,42));
        float extra=Size.X-362;
        if(extra>0) DrawTextureRectRegion(texture,new Rect2(181,y,extra,42),new Rect2(180,269,1,42));
        DrawTextureRectRegion(texture,new Rect2(181+extra,y,139,42),new Rect2(181,269,139,42));
        DrawTextureRectRegion(texture,new Rect2(Size.X-42,y,42,42),new Rect2(320,269,42,42));
    }
    internal static StyleBoxTexture Plate(string state)
    {
        var box=new StyleBoxTexture { Texture=Plugin.Kit.Texture("ui_quest_us.png"),RegionRect=new Rect2(0,317,354,32),
            ModulateColor=state=="disabled"?new Color(.55f,.55f,.55f):state is "pressed" or "hover_pressed"?new Color(.85f,.9f,1.1f):state=="hover"?new Color(1.2f,1.2f,1.2f):Colors.White };
        box.SetTextureMargin(Side.Left,14);box.SetTextureMargin(Side.Right,14);
        box.SetTextureMargin(Side.Top,4);box.SetTextureMargin(Side.Bottom,4);
        box.SetContentMargin(Side.Left,18);box.SetContentMargin(Side.Right,18);
        box.SetContentMargin(Side.Top,2);box.SetContentMargin(Side.Bottom,2);
        return box;
    }
    internal static void StyleContent(Control body)
    {
        foreach(var node in CharacterDetailsSkin.Tree(body).ToArray()) StyleControl(node);
    }
    internal static void StyleControl(Node node)
    {
        if(node is Label label && label.HasMeta("quest_status"))
            ClassicQuestStatus.StyleCaption(label);
        if(node is Button button)
        {
            if(button.HasMeta("classic_npc_plate")) { ClassicQuestStatus.StyleOption(button);return; }
            button.SetMeta("classic_npc_plate",true);
            bool pager=button.TooltipText is "Previous quest" or "Next quest";
            foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})
                button.AddThemeStyleboxOverride(state,new StyleBoxEmpty { ContentMarginLeft=pager?0:18,ContentMarginRight=pager?0:18,ContentMarginTop=2,ContentMarginBottom=2 });
            button.AddChild(new ClassicNpcOptionPlate(button));
            button.AddThemeFontOverride("font",Plugin.Kit.Bold);button.AddThemeFontSizeOverride("font_size",13);
            foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})
                button.AddThemeColorOverride(state,new Color("ead5ac"));
            button.AddThemeColorOverride("font_disabled_color",new Color("908878"));
            button.AddThemeConstantOverride("outline_size",1);button.AddThemeColorOverride("font_outline_color",Colors.Black);
            button.CustomMinimumSize=new Vector2(pager?32:1,32);button.SizeFlagsHorizontal=pager?SizeFlags.Fill:SizeFlags.ExpandFill;
            button.SizeFlagsVertical=SizeFlags.Fill;
            button.ClipText=true;button.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            if(pager) button.Text="";
            ClassicQuestStatus.StyleOption(button);
        }
        if(node is Control row && row.HasMeta("quest_reward_selected"))
            if(!row.HasMeta("classic_reward_selection"))
            {
                row.SetMeta("classic_reward_selection",true);
                var mark=new ClassicRewardSelection(row);row.AddChild(mark);
                if(row is BoxContainer) row.MoveChild(mark,0);
            }
        if(node is PanelContainer panel)
        {
            if(panel.HasMeta("classic_npc_inset")) return;
            panel.SetMeta("classic_npc_inset",true);
            foreach(var inset in panel.GetChildren().OfType<ClassicDetailInset>()) inset.Visible=false;
            var box=new StyleBoxTexture { Texture=Plugin.Kit.Texture("ui_quest_us.png"),RegionRect=new Rect2(18,47,328,186),DrawCenter=false };
            foreach(var side in new[]{Side.Left,Side.Top,Side.Right,Side.Bottom}) { box.SetTextureMargin(side,3);box.SetContentMargin(side,7); }
            panel.AddThemeStyleboxOverride("panel",box);
        }
    }
}

/// <summary>Native menu hover and pressed artwork inside unscaled ornamental end caps.</summary>
public partial class ClassicNpcOptionPlate : Control
{
    private readonly Button _button;
    private bool _hover;
    private int _state=-1;
    public ClassicNpcOptionPlate(Button button)
    {
        _button=button;MouseFilter=MouseFilterEnum.Ignore;ShowBehindParent=true;
        Size=button.Size;
        button.Resized+=()=> { Size=button.Size;QueueRedraw(); };
        button.MouseEntered+=()=> { _hover=true;QueueRedraw(); };
        button.MouseExited+=()=> { _hover=false;QueueRedraw(); };
    }
    public override void _Process(double delta)
    {
        if(Size!=_button.Size) { Size=_button.Size;QueueRedraw(); }
        int state=_button.Disabled?3:_button.IsPressed()?1:_hover?2:0;
        if(state!=_state) { _state=state;QueueRedraw(); }
    }
    public override void _Draw()
    {
        _state=_button.Disabled?3:_button.IsPressed()?1:_hover?2:0;
        if(_button.TooltipText is "Previous quest" or "Next quest")
        {
            var source=Plugin.Kit.Layout("{nation}_page_quest_us").Find(_button.TooltipText=="Previous quest"?"btn_page_down":"btn_page_up")!;
            var part=source.Images.FirstOrDefault(n=>n.Tag==_state)??source.Images.First();
            var arrow=Plugin.Kit.Texture(part.Texture!);if(arrow==null) return;
            DrawTextureRectRegion(arrow,new Rect2((Size.X-part.W)/2,(Size.Y-part.H)/2,part.W,part.H),new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH));
            return;
        }
        var texture=Plugin.Kit.Texture("ui_quest_us.png");if(texture==null) return;
        var ink=_button.Disabled?new Color(.55f,.55f,.55f):Colors.White;
        DrawTextureRectRegion(texture,new Rect2(0,0,14,32),new Rect2(0,317,14,32),ink);
        DrawTextureRectRegion(texture,new Rect2(Size.X-14,0,14,32),new Rect2(340,317,14,32),ink);
        int y=_state==1?378:_state==2?403:353;
        DrawTextureRectRegion(texture,new Rect2(14,4,Size.X-28,24),new Rect2(0,y,326,25),ink);
        DrawTextureRectRegion(texture,new Rect2(14,0,Size.X-28,4),new Rect2(14,317,326,4),ink);
        DrawTextureRectRegion(texture,new Rect2(14,28,Size.X-28,4),new Rect2(14,345,326,4),ink);
    }
}

/// <summary>Original nation checkbox and an explicit selected reward frame.</summary>
public partial class ClassicRewardSelection : Control
{
    private readonly Control _row;
    private bool? _selected;
    public ClassicRewardSelection(Control row)
    {
        _row=row;MouseFilter=MouseFilterEnum.Ignore;
        if(row is BoxContainer box)
        {
            CustomMinimumSize=new Vector2(18,32);SizeFlagsVertical=SizeFlags.Fill;
        }
    }
    public override void _Process(double delta)
    {
        bool selected=_row.GetMeta("quest_reward_selected").AsBool();
        if(_row.GetChildren().OfType<Label>().LastOrDefault() is {} count && (count.Text.EndsWith("○") || count.Text.EndsWith("●")))
            count.Text=count.Text[..^1].TrimEnd();
        if(_selected!=selected) { _selected=selected;QueueRedraw(); }
    }
    public override void _Draw()
    {
        var source=Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal")!;
        var part=source.Images.First(n=>n.Tag==(_selected==true?1:0));
        var texture=Plugin.Kit.Texture(part.Texture!);if(texture==null) return;
        DrawTextureRectRegion(texture,new Rect2(2,Mathf.Floor((Size.Y-15)/2),14,15),new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH));
        if(_selected==true)
            DrawRect(new Rect2(-Position,_row.Size).Grow(-.5f),new Color("e4c174"),false,1);
    }
}
