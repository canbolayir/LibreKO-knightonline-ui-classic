using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
using System.Text.RegularExpressions;
namespace KnightOnlineUiClassic.Windows;

/// <summary>The selected composition bound to native NPC controls, data, and callbacks.</summary>
public partial class ClassicNpcDialogue : Control
{
    private readonly HudWindow _window;
    private readonly Control _nativeBody;
    private readonly RichTextLabel _nativeSpeech;
    private readonly ScrollContainer _nativeQuest,_nativeMenu;
    private readonly VBoxContainer _questSource,_menuSource;
    private readonly Button? _nativeClose;
    private readonly ClassicNpcQuestChrome _chrome=new();
    private readonly NpcPortraitCircle _portrait=new(true);
    private readonly ClassicNpcSpeech _speech=new();
    private readonly Control _menu=new(),_footer=new();
    private readonly ScrollContainer _quest=new() { HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.ShowNever,ClipContents=true };
    private readonly VBoxContainer _content=new() { SizeFlagsHorizontal=SizeFlags.ExpandFill };
    private readonly ClassicContentScrollRail _rail;
    private readonly HBoxContainer _header;
    private readonly Label _title;
    private readonly List<Button> _topics=new(),_actions=new();
    private Button? _menuClose;
    private object? _signature;
    private Vector2 _viewport;
    private int _page,_capacity=8,_settling;
    private bool _offer;
    private bool _centerPending=true;
    public ClassicNpcSpeech Speech => _speech;
    public NpcPortraitCircle Portrait => _portrait;
    public ScrollContainer QuestScroll => _quest;
    public Label? Status { get; private set; }
    public int Page => _page;
    public int PageCount => Math.Max(1,(_topics.Count+_capacity-1)/_capacity);
    public ClassicNpcDialogue(HudWindow window,Control body,HBoxContainer header,Button? close)
    {
        Name="classic_npc_dialogue";_window=window;_nativeBody=body;_header=header;_nativeClose=close;
        _title=header.GetChildren().OfType<Label>().First(l=>l.Text.Length>1);
        _nativeSpeech=CharacterDetailsSkin.Tree(body).OfType<RichTextLabel>().First();
        var scrolls=body.GetChildren().OfType<ScrollContainer>().ToArray();
        _nativeQuest=scrolls[0];_nativeMenu=scrolls[1];
        _questSource=_nativeQuest.GetChild<VBoxContainer>(0);_menuSource=_nativeMenu.GetChild<VBoxContainer>(0);
        body.Reparent(this);body.Visible=false;
        AddChild(_chrome);AddChild(_portrait);AddChild(_speech);AddChild(_menu);AddChild(_quest);AddChild(_footer);
        _quest.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());_quest.AddChild(_content);_content.AddThemeConstantOverride("separation",9);
        _rail=new ClassicContentScrollRail(_quest);AddChild(_rail);
        ClassicNpcLayout.Place(_portrait,ClassicNpcLayout.Portrait);ClassicNpcLayout.Place(_speech,ClassicNpcLayout.Speech);
        header.Reparent(this);ClassicNpcLayout.Place(header,ClassicNpcLayout.TitleText);
        header.CustomMinimumSize=Vector2.Zero;header.CustomMaximumSize=ClassicNpcLayout.TitleText.Size;header.AddThemeConstantOverride("separation",0);
        foreach(var node in header.GetChildren().OfType<Control>())
        {
            node.Visible=node is Label label && label.Text.Length>1;
            if(node is Label title && node.Visible)
            {
                title.CustomMinimumSize=new Vector2(1,0);title.ClipText=true;title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
                title.AddThemeFontOverride("font",ClassicNpcLayout.HeadingFont);title.AddThemeFontSizeOverride("font_size",13);
                title.AddThemeColorOverride("font_color",new Color("dfc184"));title.VerticalAlignment=VerticalAlignment.Center;
            }
        }
        var closeView=new Button { Name="npc_close",TooltipText="Close",FocusMode=FocusModeEnum.None,TextureFilter=TextureFilterEnum.Nearest,
            CustomMinimumSize=ClassicNpcLayout.Close.Size,CustomMaximumSize=ClassicNpcLayout.Close.Size };
        ClassicReportDesign.StyleButton(closeView,false,Plugin.Kit.Layout("co_questmenu_us").Find("btn_close")!);
        closeView.Pressed+=()=> { if(IsInstanceValid(_nativeClose)) _nativeClose!.EmitSignal(BaseButton.SignalName.Pressed); };
        AddChild(closeView);ClassicNpcLayout.Place(closeView,ClassicNpcLayout.Close);
        CustomMinimumSize=new Vector2(363,300);Size=CustomMinimumSize;
        window.SetMeta("content_open_anchor",true);
        window.VisibilityChanged+=()=> { if(window.Visible) { _signature=null;_settling=2;Modulate=Colors.Transparent; } };
    }
    private static ulong FirstId(Node parent) => parent.GetChildCount()==0?0:parent.GetChild(0).GetInstanceId();
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()) return;
        var npc=Plugin.Kit.Game.Windows.NpcPortrait;
        object signature=(_title.Text,_nativeSpeech.Text,_nativeQuest.Visible,FirstId(_questSource),FirstId(_menuSource),_menuSource.GetChildCount(),
            npc?.AppearanceKey,npc?.Name,npc?.Level);
        if(!signature.Equals(_signature))
        {
            _signature=signature;_page=0;_offer=_nativeQuest.Visible;Rebuild();
            string identity=npc?.Name.Trim()??(_offer?"Quest details":_title.Text);
            if(identity.StartsWith('[') && identity.EndsWith(']')) identity=identity[1..^1];
            var paragraph=_offer?_questSource.GetChildren().OfType<RichTextLabel>().FirstOrDefault():_nativeSpeech;
            _speech.ShowContent(identity,paragraph?.Text??"");
            _portrait.Visible=npc!=null;if(npc!=null) _portrait.Show(npc);
            _centerPending=true;_settling=2;Modulate=Colors.Transparent;
        }
        var viewport=GetViewportRect().Size;
        if(viewport!=_viewport) { _viewport=viewport;_centerPending=true;ArrangeMenu(); }
        Fit();
        if(_settling>0 && --_settling==0) { Modulate=Colors.White;_centerPending=false; }
    }
    private static void Clear(Control owner)
    {
        foreach(var child in owner.GetChildren()) { owner.RemoveChild(child);child.QueueFree(); }
    }
    private void Rebuild()
    {
        _topics.Clear();_actions.Clear();_menuClose=null;Status=null;Clear(_content);
        foreach(var source in _menuSource.GetChildren().OfType<Control>())
        {
            if(source is Button button)
            {
                if(Regex.IsMatch(button.Text,@"^(?:\d+\.\s*)?Close$",RegexOptions.IgnoreCase)) _menuClose=button;
                else _topics.Add(button);
            }
            else if(source is HBoxContainer actions) _actions.AddRange(actions.GetChildren().OfType<Button>());
        }
        if(_offer) BuildQuestContent();
        _quest.Visible=_offer;_menu.Visible=!_offer;
        _quest.ScrollVertical=0;ArrangeMenu();
    }
    private void BuildQuestContent()
    {
        var nativeStatus=_questSource.GetChildren().OfType<Label>().FirstOrDefault(l=>l.HasMeta("quest_status"));
        string heading="";bool firstParagraph=true;
        foreach(var source in _questSource.GetChildren().OfType<Control>())
        {
            if(source is Label label)
            {
                if(label.HasMeta("quest_status")) continue;
                heading=label.Text;continue;
            }
            if(source is RichTextLabel paragraph)
            {
                if(firstParagraph) { firstParagraph=false;continue; }
                _content.AddChild(Paragraph(paragraph.Text));continue;
            }
            if(source is not PanelContainer panel) continue;
            var section=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };
            section.AddThemeConstantOverride("separation",2);_content.AddChild(section);
            var caption=ClassicNpcLayout.Text(heading=="Choose one"?"Choose one reward":heading,12,true,ClassicReportDesign.Caption);
            var sectionHeader=new HBoxContainer { CustomMinimumSize=new Vector2(1,20),SizeFlagsHorizontal=SizeFlags.ExpandFill };
            sectionHeader.AddThemeConstantOverride("separation",12);section.AddChild(sectionHeader);
            caption.CustomMinimumSize=new Vector2(1,20);caption.SizeFlagsHorizontal=SizeFlags.ExpandFill;sectionHeader.AddChild(caption);
            if(Status==null && nativeStatus!=null)
            {
                Status=ClassicNpcLayout.Text(nativeStatus.Text,12,true,ClassicQuestStatus.Ink((LibreKO.Network.QuestViewState)nativeStatus.GetMeta("quest_status").AsInt32()));
                Status.Name="npc_quest_status";Status.HorizontalAlignment=HorizontalAlignment.Right;Status.TooltipText=nativeStatus.Text;
                sectionHeader.AddChild(Status);
            }
            var rows=panel.GetChild<Control>(0).GetChildren().OfType<Control>().ToArray();
            bool hunt=heading=="Hunt";
            var cards=new List<ClassicNpcCard>();
            foreach(var row in rows)
            {
                if(row is HBoxContainer) cards.Add(new ClassicNpcCard(row,hunt,row.HasMeta("quest_reward_selected")));
                else if(row is Label hint) section.AddChild(Paragraph(hint.Text));
                else if(row is RichTextLabel text) section.AddChild(Paragraph(text.Text));
            }
            if(cards.Count>0) section.AddChild(new ClassicNpcCardGrid(cards));
        }
    }
    private static RichTextLabel Paragraph(string text)
    {
        var paragraph=new RichTextLabel { Text=text,BbcodeEnabled=true,FitContent=true,ScrollActive=false,
            CustomMinimumSize=new Vector2(1,0),SizeFlagsHorizontal=SizeFlags.ExpandFill,MouseFilter=MouseFilterEnum.Ignore };
        foreach(string face in new[]{"normal","bold","italics","bold_italics","mono"})
            paragraph.AddThemeFontOverride(face+"_font",face.StartsWith("bold")?ClassicNpcLayout.HeadingFont:ClassicNpcLayout.BodyFont);
        paragraph.AddThemeFontSizeOverride("normal_font_size",12);paragraph.AddThemeFontSizeOverride("bold_font_size",12);
        paragraph.AddThemeColorOverride("default_color",ClassicReportDesign.Value);return paragraph;
    }
    private void ArrangeMenu()
    {
        Clear(_menu);Clear(_footer);
        int room=(int)GetViewportRect().Size.Y-24-ClassicNpcLayout.BodyY-56;
        _capacity=Math.Clamp((room+5)/37,1,8);_page=Math.Clamp(_page,0,PageCount-1);
        if(_offer)
        {
            int count=_actions.Count;
            for(int index=0;index<count;index++)
            {
                int width=(ClassicNpcLayout.InnerWidth-(count-1)*6)/Math.Max(1,count);
                var view=new ClassicNpcAction(_actions[index]);_footer.AddChild(view);
                ClassicNpcLayout.Place(view,new Rect2(index*(width+6),0,index==count-1?327-index*(width+6):width,32));
            }
            return;
        }
        bool tagged=_topics.Any(b=>Regex.IsMatch(b.Text,@"\[(In progress|Ready|Completed|Available)\]",RegexOptions.IgnoreCase));
        int first=_page*_capacity;
        for(int index=first;index<_topics.Count && index<first+_capacity;index++)
        {
            var view=new ClassicNpcAction(_topics[index],true,tagged);_menu.AddChild(view);
            ClassicNpcLayout.Place(view,new Rect2(0,(index-first)*37,327,32));
        }
        void Arrow(bool next,int x)
        {
            var view=new Button { Name=next?"npc_next_page":"npc_previous_page",Disabled=next?_page>=PageCount-1:_page==0 };
            ClassicReportDesign.StyleButton(view,false,Plugin.Kit.Layout("{nation}_page_quest_us").Find(next?"btn_page_up":"btn_page_down")!);
            view.Pressed+=()=> { _page+=next?1:-1;ArrangeMenu();Fit(); };
            _footer.AddChild(view);ClassicNpcLayout.Place(view,new Rect2(x,7,32,18));
        }
        Arrow(false,6);Arrow(true,107);
        var page=ClassicNpcLayout.Text($"{_page+1} / {PageCount}",12);page.HorizontalAlignment=HorizontalAlignment.Center;
        _footer.AddChild(page);ClassicNpcLayout.Place(page,new Rect2(43,4,60,24));
        var close=new ClassicNpcAction(_menuClose??_nativeClose,"Close") { Name="npc_footer_close" };
        _footer.AddChild(close);ClassicNpcLayout.Place(close,new Rect2(197,0,130,32));
    }
    private void Fit()
    {
        float maximum=GetViewportRect().Size.Y-24;
        float contentHeight;
        if(_offer)
        {
            float available=Math.Max(48,maximum-ClassicNpcLayout.BodyY-68);
            float minimum=_content.GetCombinedMinimumSize().Y;
            contentHeight=Math.Min(minimum,available);
            bool overflow=minimum>available+.5;
            int width=327-(overflow?22:0);
            _content.CustomMaximumSize=new Vector2(width,-1);
            ClassicNpcLayout.Place(_quest,new Rect2(18,208,width,contentHeight));
            ClassicNpcLayout.Place(_rail,new Rect2(327,208,18,contentHeight));
        }
        else contentHeight=Math.Max(32,Math.Min(_capacity,_topics.Count)*37-5);
        ClassicNpcLayout.Place(_menu,new Rect2(18,208,327,contentHeight));
        float footerY=208+contentHeight+12;
        ClassicNpcLayout.Place(_footer,new Rect2(18,footerY,327,32));
        var size=new Vector2(363,footerY+56);
        if(size!=Size) { CustomMinimumSize=size;Size=size;_window.ResetSize(); }
        _chrome.Size=size;
        if(_centerPending) _window.Position=((GetViewportRect().Size-_window.Size)/2).Floor();
    }
}

/// <summary>A native action binding with the selected ornamental plate and readable quest states.</summary>
public partial class ClassicNpcAction : Button
{
    public Button? Source { get; }
    private readonly bool _option,_states;
    private readonly string? _override;
    private readonly Label? _topic,_state;
    private string? _lastText,_lastTooltip;
    private bool? _lastDisabled;
    public ClassicNpcAction(Button? source,string? textOverride=null) : this(source,false,false,textOverride) { }
    public ClassicNpcAction(Button? source,bool option,bool states=false,string? textOverride=null)
    {
        Source=source;_option=option;_states=states;_override=textOverride;ClipText=true;
        TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;Alignment=option?HorizontalAlignment.Left:HorizontalAlignment.Center;
        FocusMode=FocusModeEnum.None;
        foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})
            AddThemeStyleboxOverride(state,new StyleBoxEmpty { ContentMarginLeft=18,ContentMarginRight=18 });
        AddChild(new ClassicNpcOptionPlate(this));
        AddThemeFontOverride("font",ClassicNpcLayout.HeadingFont);AddThemeFontSizeOverride("font_size",13);
        foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color"}) AddThemeColorOverride(state,new Color("dfc184"));
        AddThemeColorOverride("font_disabled_color",new Color("77746e"));
        if(option && states)
        {
            _topic=ClassicNpcLayout.Text("",12,true,new Color("dfc184"));_topic.ClipText=true;AddChild(_topic);
            _state=ClassicNpcLayout.Text("",11,true);_state.HorizontalAlignment=HorizontalAlignment.Right;AddChild(_state);
            Resized+=()=> { ClassicNpcLayout.Place(_topic,new Rect2(18,4,Math.Max(1,Size.X-120),24));ClassicNpcLayout.Place(_state,new Rect2(Size.X-100,4,82,24)); };
        }
        Pressed+=()=> { if(IsInstanceValid(Source) && !Source!.Disabled) Source.EmitSignal(BaseButton.SignalName.Pressed); };
        UpdateSource();
    }
    private void UpdateSource()
    {
        if(!IsInstanceValid(Source)) { Disabled=true;return; }
        string text=_override??Source!.Text;
        if(text==_lastText && Source!.TooltipText==_lastTooltip && Source.Disabled==_lastDisabled) return;
        _lastText=text;_lastTooltip=Source!.TooltipText;_lastDisabled=Source.Disabled;
        Disabled=Source.Disabled;TooltipText=Source.TooltipText;
        if(_topic==null || _state==null) { Text=text;return; }
        var tag=Regex.Match(text,@"\[(In progress|Ready|Completed|Available)\]\s*",RegexOptions.IgnoreCase);
        bool navigation=Regex.IsMatch(text,@"^(?:\d+\.\s*)?(Next page|Previous page)\b",RegexOptions.IgnoreCase);
        _topic.Text=tag.Success?text.Remove(tag.Index,tag.Length):text;
        _state.Visible=!navigation;
        if(navigation) { _topic.Size=new Vector2(Math.Max(1,Size.X-36),24);_state.Text="";Text="";return; }
        _state.Text=tag.Success?tag.Groups[1].Value:"Available";
        var state=_state.Text.ToLowerInvariant() switch { "in progress"=>LibreKO.Network.QuestViewState.InProgress,"ready"=>LibreKO.Network.QuestViewState.Claimable,"completed"=>LibreKO.Network.QuestViewState.Completed,_=>LibreKO.Network.QuestViewState.Available };
        _state.AddThemeColorOverride("font_color",ClassicQuestStatus.Ink(state));Text="";
    }
    public override void _Process(double delta) => UpdateSource();
}
