using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Composed subpages retain the native models and callbacks, without another window shell.</summary>
public partial class CharacterEmbeddedPage : Control
{
    public string WindowId { get; }
    public string ParentPage => WindowId is "character_clan_details" or "clanpoint"?"clan":WindowId=="quests"?"quest":"character";
    public readonly HudWindow NativeWindow;
    private readonly Control _body;
    private readonly Button? _close;
    private readonly LayoutView _layout;
    public ScrollContainer ContentScroll { get; }
    private readonly List<Control> _moved=new();
    private Control? _notice;
    private ScrollContainer? _clanList;
    private Control? _noticeEdit,_clanHint;
    private Label? _clanStatus;
    private Button? _noticeTab,_pointsTab,_unionTab;
    private Button? _leave,_ally;
    private Control? _questDetail,_questActions;
    public ClassicContentScrollRail? QuestScrollRail { get; private set; }
    public Action? Back;
    public CharacterEmbeddedPage(HudWindow window,Control body,Button? close)
    {
        WindowId=window.Id;NativeWindow=window;_body=body;_close=close;
        Size=new Vector2(360,550);ClipContents=true;MouseFilter=MouseFilterEnum.Ignore;
        string caption=WindowId switch { "titles"=>"Titles","presets"=>"Stat Preset","character_clan_details"=>"Clan Management","clanpoint"=>"Save Contribution",_=>"Quest Details" };
        _layout=new LayoutView(Plugin.Kit,CharacterEmbeddedLayout.Page(caption));AddChild(_layout);
        _layout.OnPressed("back",()=>Back?.Invoke());
        ContentScroll=new ScrollContainer { Position=CharacterEmbeddedLayout.Content.Position,Size=CharacterEmbeddedLayout.Content.Size,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Auto };
        AddChild(ContentScroll);
        CharacterDetailsSkin.StyleTree(body);
        if(WindowId=="titles") ComposeTitles();
        else if(WindowId=="character_clan_details") ComposeClan();
        else if(WindowId=="quests") ComposeQuest();
        else
        {
            Move(body,ContentScroll);
            CharacterDetailsSkin.ConstrainWidth(body);
            if(WindowId=="presets") ClassicDetailPanel.ComposePresets(body);
            if(WindowId=="presets")
            {
                ContentScroll.VerticalScrollMode=ScrollContainer.ScrollMode.Disabled;
                foreach(var canvas in body.GetChildren().OfType<Control>().Where(c=>c.Visible)) canvas.CustomMinimumSize=new Vector2(344,402);
            }
        }
        foreach(var control in _moved) CharacterDetailsSkin.ConstrainWidth(control);
        body.Visible=body.GetParent()==ContentScroll;
        CharacterDetailsSkin.StyleTree(ContentScroll);
        foreach(var tab in CharacterDetailsSkin.Tree(this).OfType<Button>().Where(b=>b.ToggleMode).ToArray()) tab.AddChild(new EmbeddedTabSelection(tab));
    }
    private void Move(Control control,Node parent)
    {
        control.Reparent(parent);_moved.Add(control);
        control.Visible=true;
    }
    private void Place(Control control,int x,int y,int width,int height)
    {
        Move(control,this);control.CustomMinimumSize=Vector2.Zero;
        control.Position=new Vector2(x,y);control.Size=new Vector2(width,height);
        if(control is Label label) { label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.ClipText=true; }
        control.SetMeta("embedded_rect",new Rect2(x,y,width,height));
    }
    private void Place(Control control,string slot)
    {
        var r=CharacterEmbeddedLayout.Slots[slot];Place(control,(int)r.Position.X,(int)r.Position.Y,(int)r.Size.X,(int)r.Size.Y);
    }
    private void ComposeTitles()
    {
        ContentScroll.Visible=false;
        var hint=_body.GetChildren().OfType<Label>().First();
        Place(hint,"title_hint");
        var caption=CharacterEmbeddedLayout.Slots["title_caption"];
        var label=new Label { Text="Title and earned bonuses",Position=caption.Position,Size=caption.Size };
        label.AddThemeFontOverride("font",Plugin.Kit.Bold);label.AddThemeFontSizeOverride("font_size",12);
        label.AddThemeColorOverride("font_color",ClassicReportDesign.Caption);AddChild(label);
        var scroll=_body.GetChildren().OfType<ScrollContainer>().First();
        Place(scroll,"title_list");scroll.HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled;
        var frame=CharacterEmbeddedLayout.Slots["title_frame"];
        AddChild(new ClassicReportSection { Position=frame.Position,Size=frame.Size,ShowBehindParent=true });
        scroll.GetChild<Control>(0).ChildEnteredTree+=row=>Callable.From(()=>StyleTitleRow(row)).CallDeferred();
        foreach(var row in scroll.GetChild(0).GetChildren()) StyleTitleRow(row);
    }
    private static void StyleTitleRow(Node row)
    {
        if(row is not PanelContainer panel || !GodotObject.IsInstanceValid(row) || row.HasMeta("classic_title_row")) return;
        row.SetMeta("classic_title_row",true);
        panel.CustomMinimumSize=new Vector2(1,52);
        foreach(var margin in CharacterDetailsSkin.Tree(panel).OfType<MarginContainer>())
            foreach(var side in new[]{"margin_left","margin_right"}) margin.AddThemeConstantOverride(side,4);
        panel.AddChild(new TitleRowRule(panel));
    }
    private void ComposeQuest()
    {
        var browser=_body.GetChildren().OfType<VBoxContainer>().Single();
        var detail=browser.GetChildren().OfType<HBoxContainer>().Last().GetChildren().OfType<PanelContainer>().Single();
        var content=detail.GetChild<VBoxContainer>(0);
        _questDetail=content.GetChildren().OfType<VBoxContainer>().Single();
        _questActions=_questDetail.GetChildren().OfType<HBoxContainer>().Last();
        Place(_questActions,8,502,344,22);
        foreach(var button in _questActions.GetChildren().OfType<Button>()) button.CustomMinimumSize=new Vector2(1,22);
        detail.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        foreach(var inset in detail.GetChildren().OfType<ClassicDetailInset>()) inset.Visible=false;
        AddChild(new ClassicReportSection { Position=new Vector2(8,116),Size=new Vector2(344,374),ShowBehindParent=true });
        ContentScroll.Position=new Vector2(14,122);ContentScroll.Size=new Vector2(312,362);
        ContentScroll.VerticalScrollMode=ScrollContainer.ScrollMode.ShowNever;
        Move(detail,ContentScroll);detail.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        CharacterDetailsSkin.ConstrainWidth(detail);
        ContentScroll.CustomMinimumSize=Vector2.Zero;
        QuestScrollRail=new ClassicContentScrollRail(ContentScroll) { Position=new Vector2(330,122),Size=new Vector2(18,362) };
        AddChild(QuestScrollRail);
    }
    private void ComposeClan()
    {
        ContentScroll.Visible=false;
        var card=CharacterDetailsSkin.Tree(_body).OfType<PanelContainer>().First();
        var labels=CharacterDetailsSkin.Tree(card).OfType<Label>().ToArray();
        for(int i=0;i<labels.Length;i++) Place(labels[i],12,116+i*22,336,22);
        var tabs=CharacterDetailsSkin.Tree(_body).OfType<HBoxContainer>().First(c=>c.GetChildren().OfType<Button>().Count(b=>b.ToggleMode)==3);
        var buttons=tabs.GetChildren().OfType<Button>().ToArray();
        _noticeTab=buttons[0];_pointsTab=buttons[1];_unionTab=buttons[2];_noticeTab.Text="Notice";
        for(int i=0;i<3;i++) { Place(buttons[i],8+i*116,210,112,22);buttons[i].CustomMinimumSize=new Vector2(0,22); }
        var mine=card.GetParent<Control>();
        var nativeRoot=mine.GetParent<Control>();
        var children=mine.GetChildren().OfType<Control>().ToArray();
        _notice=children.OfType<Label>().First(l=>l.Text!="Notice");
        var noticeRect=CharacterEmbeddedLayout.Slots["clan_notice"];
        var noticeScroll=new ScrollContainer { Position=noticeRect.Position,Size=noticeRect.Size,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };
        AddChild(noticeScroll);Move(_notice,noticeScroll);_notice.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _notice.SetMeta("notice_scroll",noticeScroll);
        var frame=CharacterEmbeddedLayout.Slots["clan_frame"];
        AddChild(new ClassicReportSection { Position=frame.Position,Size=frame.Size,ShowBehindParent=true });
        _noticeEdit=children.OfType<HBoxContainer>().First(c=>c.GetChildren().OfType<LineEdit>().Any());
        Place(_noticeEdit,"clan_editor");
        foreach(var child in _noticeEdit.GetChildren().OfType<Control>())
        {
            child.CustomMinimumSize=new Vector2(child.CustomMinimumSize.X,24);
            child.SizeFlagsVertical=SizeFlags.Fill;
        }
        _clanList=children.OfType<ScrollContainer>().First();Place(_clanList,"clan_list");
        _clanHint=children.OfType<Label>().Last();Place(_clanHint,"clan_hint");
        _clanStatus=nativeRoot.GetChildren().OfType<Label>().LastOrDefault();
        if(_clanStatus!=null) Place(_clanStatus,"clan_status");
        var actions=children.OfType<HBoxContainer>().Last();
        var actionButtons=actions.GetChildren().OfType<Button>().ToArray();
        _ally=actionButtons[2];_leave=actionButtons[3];
        for(int i=0;i<actionButtons.Length;i++)
        {
            if(i==0) Place(actionButtons[i],"clan_refresh");
            if(i==1) { actionButtons[i].Text="Save NP";Place(actionButtons[i],"clan_save"); }
            if(i==2) Place(actionButtons[i],"clan_ally");
            if(i==3) { actionButtons[i].Text="Leave";Place(actionButtons[i],"clan_leave"); }
            actionButtons[i].CustomMinimumSize=Vector2.Zero;
        }
        _noticeTab.Pressed+=()=>UpdateClanMode();_pointsTab.Pressed+=()=>UpdateClanMode();_unionTab.Pressed+=()=>UpdateClanMode();
        UpdateClanMode();
    }
    public void CloseNative() => _close?.EmitSignal(BaseButton.SignalName.Pressed);
    public override void _Ready() { _layout.ApplyDeclaredBounds(); }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()) return;
        if(_questActions!=null && _questDetail!=null) _questActions.Visible=_questDetail.Visible;
        if(WindowId=="character_clan_details") UpdateClanMode();
        if(_clanStatus!=null) _clanStatus.TooltipText=_clanStatus.Text;
        if(WindowId=="presets")
            foreach(var control in CharacterDetailsSkin.Tree(_body).OfType<Control>())
            {
                if(control is Button button && button.GetThemeStylebox("normal") is StyleBoxFlat flat)
                {
                    bool selected=flat.BorderWidthTop>=2;ClassicReportDesign.StyleButton(button,button.ToggleMode);
                    if(button.ToggleMode) button.SetPressedNoSignal(selected);
                }
                if(control.HasMeta("classic_fixed_rect"))
                {
                    var rect=control.GetMeta("classic_fixed_rect").AsRect2();control.CustomMinimumSize=Vector2.Zero;control.Position=rect.Position;control.Size=rect.Size;
                }
            }
        foreach(var control in _moved)
        {
            if(control.HasMeta("embedded_rect"))
            {
                var rect=control.GetMeta("embedded_rect").AsRect2();control.Position=rect.Position;
                control.CustomMinimumSize=Vector2.Zero;control.Size=rect.Size;
            }
        }
    }
    private void UpdateClanMode()
    {
        if(_notice==null || _clanList==null) return;
        bool notice=_noticeTab?.ButtonPressed==true;
        var clan=Plugin.Kit.Game.Windows.CharacterPanel?.Clan;
        if(_leave!=null) _leave.Text=clan?.IsChief==true?"Disband":"Leave";
        if(_ally!=null) _ally.Visible=_unionTab?.ButtonPressed==true && clan?.IsChief==true && clan?.Flag>=LibreKO.Network.ClanTypes.Promoted;
        if(_notice.GetMeta("notice_scroll").AsGodotObject() is Control scroll) scroll.Visible=notice;
        _clanList.Visible=!notice;
        if(_clanHint!=null && notice) _clanHint.Visible=false;
        if(_noticeEdit!=null) _noticeEdit.Visible=notice && Plugin.Kit.Game.Windows.CharacterPanel?.Clan.IsChief==true;
    }
}

public partial class EmbeddedTabSelection : Control
{
    private readonly Button _button;
    private bool _selected;
    public EmbeddedTabSelection(Button button) { _button=button;MouseFilter=MouseFilterEnum.Ignore; }
    public override void _Process(double delta)
    {
        bool selected=_button.ButtonPressed;
        if(Size!=_button.Size || selected!=_selected) { Size=_button.Size;_selected=selected;QueueRedraw(); }
    }
    public override void _Draw()
    {
        if(_selected) DrawLine(new Vector2(5,Size.Y-3),new Vector2(Size.X-5,Size.Y-3),new Color(ClassicDesign.Karus?"534038":"e7d3a0"),1);
    }
}

public partial class TitleRowRule : ClassicReportSurface
{
    private readonly Control _row;
    public TitleRowRule(Control row):base(true) { _row=row; }
    public override void _Process(double delta) { Position=new Vector2(0,_row.Size.Y-2);Size=new Vector2(_row.Size.X,2); }
}
