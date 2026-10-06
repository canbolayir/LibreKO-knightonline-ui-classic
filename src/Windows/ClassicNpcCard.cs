using Godot;
using KnightOnlineUiClassic.Layout;
using System.Text.RegularExpressions;
namespace KnightOnlineUiClassic.Windows;

/// <summary>A compact view of a native objective/reward row, including its original input callbacks.</summary>
public partial class ClassicNpcCard : Control
{
    public Control Source { get; }
    private readonly Label _name,_amount;
    private readonly Label? _sourceName,_sourceAmount;
    private readonly TextureRect _icon;
    private readonly bool _choice;
    private bool _selected;
    private string _lastName="",_lastAmount="";
    private Color _lastInk;
    public ClassicNpcCard(Control source,bool hunt,bool choice)
    {
        Source=source;_choice=choice;Name=hunt?"monster_card":"reward_card";
        MouseFilter=MouseFilterEnum.Stop;TooltipText=source.TooltipText;
        _sourceName=source.GetChildren().OfType<Label>().FirstOrDefault();_sourceAmount=source.GetChildren().OfType<Label>().LastOrDefault();
        var nativeIcon=source.GetChildren().OfType<TextureRect>().FirstOrDefault();
        _icon=new TextureRect { Texture=hunt?Plugin.Kit.Texture("npc_monster_hunt_32.png"):nativeIcon?.Texture,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter=TextureFilterEnum.Nearest,MouseFilter=MouseFilterEnum.Ignore };
        AddChild(_icon);ClassicNpcLayout.Place(_icon,new Rect2(6,5,32,32));
        _name=ClassicNpcLayout.Text("",11);_name.ClipText=true;_name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        _amount=ClassicNpcLayout.Text("",11,true,new Color("dfc184"));_amount.ClipText=true;
        AddChild(_name);AddChild(_amount);Resized+=Arrange;Arrange();
        GuiInput+=input=> { if(IsInstanceValid(Source)) Source.EmitSignal(Control.SignalName.GuiInput,input); };
        MouseEntered+=()=> { if(IsInstanceValid(Source)) Source.EmitSignal(Control.SignalName.MouseEntered); };
        MouseExited+=()=> { if(IsInstanceValid(Source)) Source.EmitSignal(Control.SignalName.MouseExited); };
        UpdateSource();
    }
    private void Arrange()
    {
        ClassicNpcLayout.Place(_name,new Rect2(43,5,Math.Max(1,Size.X-50),17));
        ClassicNpcLayout.Place(_amount,new Rect2(43,23,Math.Max(1,Size.X-(_choice?72:50)),16));QueueRedraw();
    }
    private void UpdateSource()
    {
        if(!IsInstanceValid(Source)) return;
        string name=_sourceName?.Text??"";
        string amount=(_sourceAmount?.Text??"").TrimEnd('○','●',' ');
        bool selected=_choice && Source.HasMeta("quest_reward_selected") && Source.GetMeta("quest_reward_selected").AsBool();
        var originalInk=_sourceAmount?.GetThemeColor("font_color")??new Color("dfc184");
        var ink=originalInk.G>originalInk.R*1.1f && originalInk.G>originalInk.B*1.1f?originalInk:new Color("dfc184");
        if(name==_lastName && amount==_lastAmount && ink==_lastInk && selected==_selected) return;
        _lastName=name;_lastAmount=amount;_lastInk=ink;_amount.AddThemeColorOverride("font_color",ink);
        var upgrade=Regex.Match(name,@"^(.*?)\s*\(\+(\d+)\)$");
        _name.Text=upgrade.Success?upgrade.Groups[1].Value.Trim():name;
        _amount.Text=upgrade.Success?$"+{upgrade.Groups[2].Value} · {amount}":amount;
        TooltipText=name+(Source.TooltipText.Length>0?"\n"+Source.TooltipText:"");
        if(selected!=_selected) { _selected=selected;QueueRedraw(); }
    }
    public override void _Process(double delta) => UpdateSource();
    public override void _Draw()
    {
        ClassicNpcLayout.InsetBox().Draw(GetCanvasItem(),new Rect2(Vector2.Zero,Size));
        if(!_choice) return;
        var mark=new Rect2(Size.X-20,23,15,15);
        if(_selected)
        {
            DrawTextureRectRegion(Plugin.Kit.Texture("ui_message_us.png")!,mark,new Rect2(39,409,25,25));
            DrawRect(new Rect2(Vector2.Zero,Size).Grow(-.5f),new Color("e4c174"),false,1);
        }
        else { DrawRect(new Rect2(mark.Position+Vector2.One,new Vector2(13,13)),new Color("171511"));DrawRect(new Rect2(mark.Position+Vector2.One,new Vector2(13,13)),new Color("8f7b56"),false,1); }
    }
}

/// <summary>Two fixed columns that stay inside the live lower-content scroll viewport.</summary>
public partial class ClassicNpcCardGrid : Control
{
    public ClassicNpcCardGrid(IEnumerable<ClassicNpcCard> cards)
    {
        MouseFilter=MouseFilterEnum.Ignore;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        foreach(var card in cards) AddChild(card);
        int rows=(GetChildCount()+1)/2;CustomMinimumSize=new Vector2(1,rows*48-6);
        Resized+=Arrange;
    }
    public override void _Ready() => Arrange();
    private void Arrange()
    {
        int column=(int)(Size.X-6)/2;
        for(int index=0;index<GetChildCount();index++)
        {
            var card=GetChild<ClassicNpcCard>(index);int width=index%2==0?column:(int)Size.X-column-6;
            ClassicNpcLayout.Place(card,new Rect2(index%2*(column+6),index/2*48,width,42));
        }
    }
}
