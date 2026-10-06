using Godot;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Outside ornament, native frame rails, and bounded live detail content.</summary>
public partial class ClassicDetailPanel : Control
{
    private readonly Control _body;
    private readonly LayoutView _frame;
    private double _elapsed;
    private bool _centerOnOpen=true;
    private int _openingFrames,_stableOpeningFrames;
    private (Vector2 Frame,Vector2 Body,Vector2 Minimum) _openingSnapshot;
    private ulong _openingStarted;
    private HBoxContainer? _portraitRow;
    private Control? _portraitIntroduction;
    private NpcPortraitCircle? _portrait;
    private int _portraitIntroductionIndex;
    private readonly Button? _npcOverflowClose;
    private Button? _npcMenuCloseSource;
    private const int MaximumNpcMenuRows=10;
    private (ulong Menu,ulong Quest) _npcPage;
    private bool QuestShell => WindowId is "npc_dialog" or "quest_available" or "quest_receipt";
    public string WindowId { get; }
    public ScrollContainer ContentScroll { get; }
    public ClassicDetailPanel(string id,Control body,HBoxContainer nativeBar,Button? nativeClose)
    {
        WindowId=id;_body=body;
        bool questShell=id is "npc_dialog" or "quest_available" or "quest_receipt";
        var size=id switch { "userinfo"=>new Vector2(360,440),"quest_target"=>new Vector2(360,500),
            "quest_receipt"=>new Vector2(363,330),"quest_available" or "npc_dialog"=>new Vector2(363,470),_=>new Vector2(360,550) };
        CustomMinimumSize=size;Size=size;
        if(questShell) Modulate=Colors.Transparent;
        var root=CharacterLayout.Frame();root.W=(int)size.X;root.H=(int)size.Y;
        root.Children.RemoveAll(n=>n.Type=="classic_tab");
        int top=ClassicDesign.Karus?40:44;
        var surface=root.Find("surface")!;surface.W=root.W;surface.Y=top;surface.H=root.H-top;
        var rails=root.Find("header_rails")!;rails.Type="classic_detail_header";rails.Y=top;rails.W=root.W;
        if(questShell)
        {
            root.Children.Clear();
            root.Children.Add(new LayoutNode { Type="npc_quest_frame_component",W=root.W,H=root.H });
            var closeSource=Plugin.Kit.Layout("co_questmenu_us").Find("btn_close")!;
            root.Children.Add(CharacterLayout.CopyImage(closeSource,0,32-closeSource.Y));
        }
        _frame=new LayoutView(Plugin.Kit,root);AddChild(_frame);
        if(_frame.Where(n=>n.Id=="btn_close").First().Control is BaseButton close)
            close.Pressed+=()=>nativeClose?.EmitSignal(BaseButton.SignalName.Pressed);
        nativeBar.Reparent(this);nativeBar.Position=questShell?new Vector2(36,28):new Vector2(48,top+4);
        nativeBar.Size=new Vector2(size.X-(questShell?82:60),questShell?32:36);
        nativeBar.AddThemeConstantOverride("separation",0);
        foreach(var control in nativeBar.GetChildren().OfType<Control>())
        {
            control.Visible=control is Label label && label.Text.Length>1 || control is Button && control!=nativeClose;
            if(control is Button action) CharacterDetailsSkin.StyleTree(action);
            if(control is Label title && control.Visible)
            {
                title.AddThemeFontOverride("font",Plugin.Kit.Bold);title.AddThemeFontSizeOverride("font_size",13);
                title.AddThemeColorOverride("font_color",ClassicReportDesign.Caption);
                title.VerticalAlignment=VerticalAlignment.Center;title.ClipText=true;
                if(id=="npc_dialog") title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            }
        }
        int contentY=questShell?80:top+48;
        int inset=questShell?18:8;
        ContentScroll=new ScrollContainer { Position=new Vector2(inset,contentY),Size=new Vector2(size.X-inset*2,size.Y-contentY-24),
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Auto,ClipContents=true };
        AddChild(ContentScroll);
        body.Reparent(ContentScroll);body.Visible=true;body.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        body.CustomMinimumSize=new Vector2(1,0);
        if(id=="presets") ComposePresets(body);
        if(id=="titles")
        {
            var list=body.GetChildren().OfType<ScrollContainer>().FirstOrDefault();
            if(list!=null)
            {
                int index=list.GetIndex();var section=new PanelContainer();body.AddChild(section);body.MoveChild(section,index);
                list.Reparent(section);list.CustomMinimumSize=new Vector2(1,296);
            }
        }
        if(questShell) body.SetMeta("classic_npc_content",true);
        if(id=="npc_dialog")
        {
            _npcOverflowClose=new Button { Name="npc_menu_close",Text="Close",Visible=false,FocusMode=FocusModeEnum.None };
            body.AddChild(_npcOverflowClose);
            _npcOverflowClose.Pressed+=()=>
            {
                var source=GodotObject.IsInstanceValid(_npcMenuCloseSource)?_npcMenuCloseSource:nativeClose;
                source?.EmitSignal(BaseButton.SignalName.Pressed);
            };
        }
        CharacterDetailsSkin.StyleTree(body);CharacterDetailsSkin.StyleTree(ContentScroll);
        CharacterDetailsSkin.ConstrainWidth(body);
        if(questShell)
        {
            StyleQuestContent(body);
            body.ChildEnteredTree+=child=> { if(child is Control control) StyleQuestContent(control); };
        }
        if(id=="userinfo")
        {
            foreach(var row in CharacterDetailsSkin.Tree(body).OfType<HBoxContainer>()) StyleInformationRow(row);
            foreach(var rows in body.GetChildren().OfType<Container>()) rows.ChildEnteredTree+=child=>Callable.From(()=> { if(child is HBoxContainer row && GodotObject.IsInstanceValid(row)) StyleInformationRow(row); }).CallDeferred();
        }
    }
    private static void StyleQuestContent(Control content)
    {
        CharacterDetailsSkin.StyleTree(content);
        CharacterDetailsSkin.ConstrainWidth(content);
        ClassicNpcQuestChrome.StyleContent(content);
        foreach(var title in CharacterDetailsSkin.Tree(content).OfType<Label>().Where(l=>l.GetParent() is HBoxContainer row && row.GetChildren().OfType<Button>().Any(b=>b.TooltipText=="Previous quest")))
        {
            title.AutowrapMode=TextServer.AutowrapMode.Off;title.ClipText=true;
            title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        }
    }
    private static void StyleInformationRow(HBoxContainer row)
    {
        var labels=row.GetChildren().OfType<Label>().ToArray();if(labels.Length!=2) return;
        row.CustomMinimumSize=new Vector2(1,28);labels[0].CustomMinimumSize=new Vector2(124,28);
        labels[0].SizeFlagsHorizontal=SizeFlags.Fill;labels[1].SizeFlagsHorizontal=SizeFlags.ExpandFill;
        labels[1].HorizontalAlignment=HorizontalAlignment.Right;
        row.AddChild(new TitleRowRule(row));
    }
    public override void _Ready()
    {
        if(QuestShell && GetParent() is LibreKO.HudWindow window)
        {
            window.SetMeta("content_open_anchor",true);
            _openingStarted=Time.GetTicksMsec();
            if(WindowId=="npc_dialog") window.Resized+=()=>Callable.From(()=>
            {
                if(GodotObject.IsInstanceValid(window) && window.Visible)
                    window.Position=((GetViewportRect().Size-window.Size)/2).Floor();
            }).CallDeferred();
            window.VisibilityChanged+=()=>
            {
                if(!window.Visible) return;
                _centerOnOpen=true;_openingFrames=_stableOpeningFrames=0;_openingSnapshot=default;
                _openingStarted=Time.GetTicksMsec();Modulate=Colors.Transparent;
                if(WindowId=="npc_dialog") UpdateNpcPortrait();
            };
        }
        _frame.ApplyDeclaredBounds();
        Callable.From(()=>
        {
            foreach(var control in CharacterDetailsSkin.Tree(_body).OfType<Control>().Where(c=>c.HasMeta("classic_fixed_rect")))
            {
                var rect=control.GetMeta("classic_fixed_rect").AsRect2();control.CustomMinimumSize=Vector2.Zero;
                control.UpdateMinimumSize();_=control.GetCombinedMinimumSize();control.Position=rect.Position;control.Size=rect.Size;
            }
        }).CallDeferred();
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()) return;
        _elapsed+=delta;if(!QuestShell && _elapsed<.15) return;_elapsed=0;
        if(QuestShell)
        {
            if(WindowId=="npc_dialog")
            {
                UpdateNpcPortrait();
            }
            if(WindowId=="npc_dialog") FitNpcContent();else FitQuestContent();
            if(_centerOnOpen && GetParent() is LibreKO.HudWindow window)
            {
                _openingFrames++;
                window.ResetSize();
                var snapshot=(Size,_body.Size,_body.GetCombinedMinimumSize());
                _stableOpeningFrames=snapshot==_openingSnapshot?_stableOpeningFrames+1:0;
                _openingSnapshot=snapshot;
                if(_stableOpeningFrames>=1)
                {
                    window.Position=((GetViewportRect().Size-window.Size)/2).Floor();
                    _centerOnOpen=false;Modulate=Colors.White;
                    SetMeta("classic_open_frames",_openingFrames);
                    SetMeta("classic_open_ms",Time.GetTicksMsec()-_openingStarted);
                }
            }
        }
        // Native refreshes can replace tab style boxes after assigning their live values.
        foreach(var button in CharacterDetailsSkin.Tree(_body).OfType<Button>())
            if(button is not CheckBox && button.GetThemeStylebox("normal") is StyleBoxFlat flat)
            {
                bool selected=flat.BorderWidthTop>=2;
                ClassicReportDesign.StyleButton(button,button.ToggleMode);
                if(button.ToggleMode) button.SetPressedNoSignal(selected);
                if(button.ToggleMode && selected) button.AddThemeStyleboxOverride("normal",ClassicDesign.ButtonBox("pressed",true));
            }
    }
    private void UpdateNpcPortrait()
    {
        float width=BoundNpcContentWidth();
        var npc=Plugin.Kit.Game.Windows.NpcPortrait;
        if(npc==null)
        {
            if(_portraitRow==null || _portraitIntroduction==null) return;
            _portraitIntroduction.Reparent(_body);_body.MoveChild(_portraitIntroduction,_portraitIntroductionIndex);
            _portraitIntroduction.CustomMaximumSize=new Vector2(-1,-1);
            _portraitRow.QueueFree();_portraitRow=null;_portrait=null;_portraitIntroduction=null;
            return;
        }
        if(_portraitRow==null)
        {
            _portraitIntroduction=_body.GetChildren().OfType<PanelContainer>().FirstOrDefault();
            if(_portraitIntroduction==null) return;
            _portraitIntroductionIndex=_portraitIntroduction.GetIndex();
            _portraitRow=new HBoxContainer { Name="npc_portrait_introduction",SizeFlagsHorizontal=SizeFlags.ExpandFill,Alignment=BoxContainer.AlignmentMode.Center };
            _body.AddChild(_portraitRow);_body.MoveChild(_portraitRow,_portraitIntroductionIndex);
            _portrait=new NpcPortraitCircle();_portraitRow.AddChild(_portrait);
            _portraitIntroduction.Reparent(_portraitRow);_portraitIntroduction.SizeFlagsHorizontal=SizeFlags.ExpandFill;
            _portraitRow.AddThemeConstantOverride("separation",8);_portraitRow.SetMeta("classic_detail_control",true);
        }
        _portraitRow.Visible=true;
        _portraitRow.CustomMaximumSize=new Vector2(width,-1);
        bool introduction=_portraitIntroduction!.Visible;
        _portrait!.FitCaptionWidth(introduction?NpcPortraitCircle.ColumnWidth:width);
        _portraitIntroduction.CustomMaximumSize=new Vector2(introduction?Math.Max(1,width-NpcPortraitCircle.ColumnWidth-8):width,-1);
        _portrait!.Show(npc);
    }
    private float BoundNpcContentWidth()
    {
        // Child minimum sizes must never feed back into the declared frame's width.
        float width=_frame.Root.W-36;
        ContentScroll.CustomMaximumSize=new Vector2(width,-1);
        var rail=ContentScroll.GetVScrollBar();
        float bodyWidth=width-(rail.Visible?Math.Max(rail.Size.X,rail.GetCombinedMinimumSize().X):0);
        _body.CustomMaximumSize=new Vector2(bodyWidth,-1);
        return bodyWidth;
    }
    private void FitNpcContent()
    {
        float width=BoundNpcContentWidth();
        ClassicNpcQuestChrome.StyleContent(_body);
        var scrolls=_body.GetChildren().OfType<ScrollContainer>().ToArray();
        if(scrolls.Length!=2) return;
        var quest=scrolls[0];var menu=scrolls[1];
        ulong FirstContent(ScrollContainer scroll) => scroll.Visible && scroll.GetChild<Control>(0).GetChildren().OfType<Control>().FirstOrDefault() is {} first?first.GetInstanceId():0;
        var page=(FirstContent(menu),FirstContent(quest));
        if(page!=_npcPage)
        {
            _npcPage=page;_centerOnOpen=true;_openingFrames=_stableOpeningFrames=0;_openingSnapshot=default;
            _openingStarted=Time.GetTicksMsec();Modulate=Colors.Transparent;
        }
        // Only the content that exceeds the screen gets a scrollbar; the shell follows its contents.
        float available=AvailableBodyHeight();
        var menuContent=menu.GetChild<Control>(0);
        var closeSource=menuContent.GetChildren().OfType<Button>().LastOrDefault(b=>b.Text=="Close" || b.Text.EndsWith(".   Close",StringComparison.Ordinal));
        float menuHeight=menu.Visible?menuContent.GetCombinedMinimumSize().Y:0;
        if(closeSource is { Visible:false }) menuHeight+=32+(menuContent is BoxContainer box?box.GetThemeConstant("separation"):0);
        float headingHeight=_body.GetChildren().OfType<Control>().Where(c=>c.Visible && c is not ScrollContainer && c!=_npcOverflowClose)
            .Sum(c=>c.GetCombinedMinimumSize().Y);
        int visibleParts=_body.GetChildren().OfType<Control>().Count(c=>c.Visible && c!=_npcOverflowClose);
        float gaps=Math.Max(0,visibleParts-1)*_body.GetThemeConstant("separation");
        float questHeight=quest.Visible?quest.GetChild<Control>(0).GetCombinedMinimumSize().Y:0;
        float rowGap=menuContent is BoxContainer menuBox?menuBox.GetThemeConstant("separation"):0;
        float menuLimit=Math.Min(MaximumNpcMenuRows*32+(MaximumNpcMenuRows-1)*rowGap,
            Math.Max(32,available-headingHeight-gaps-(quest.Visible?80:0)));
        bool overflow=menu.Visible && !quest.Visible && menuHeight>menuLimit;
        if(_npcOverflowClose!=null) _npcOverflowClose.Visible=overflow;
        _npcMenuCloseSource=overflow?closeSource:null;
        if(closeSource!=null && closeSource.Visible==overflow) closeSource.Visible=!overflow;
        if(overflow)
        {
            float footer=32+_body.GetThemeConstant("separation");
            gaps+=footer;
            menuLimit=Math.Max(32,Math.Min(menuLimit,available-headingHeight-gaps));
            if(closeSource!=null) menuHeight-=32+rowGap;
        }
        menuHeight=Math.Min(menuHeight,menuLimit);
        questHeight=Math.Min(questHeight,Math.Max(80,available-headingHeight-gaps-menuHeight));
        menu.CustomMinimumSize=new Vector2(1,menuHeight);
        quest.CustomMinimumSize=new Vector2(1,questHeight);
        menu.VerticalScrollMode=quest.VerticalScrollMode=ScrollContainer.ScrollMode.Auto;
        menu.CustomMaximumSize=new Vector2(width,menuHeight);
        quest.CustomMaximumSize=new Vector2(width,questHeight);
        menu.SizeFlagsVertical=quest.SizeFlagsVertical=SizeFlags.Fill;
        FitNpcScrollWidth(menu,width);FitNpcScrollWidth(quest,width);
        float bodyHeight=headingHeight+gaps+menuHeight+questHeight;
        ResizeQuestShell(bodyHeight);
    }
    private static void FitNpcScrollWidth(ScrollContainer scroll,float maximumWidth)
    {
        if(scroll.GetChildCount()==0) return;
        var content=scroll.GetChild<Control>(0);
        var bar=scroll.GetVScrollBar();
        // Keep native content out of the rail's actual rendered bounds, including after reflow.
        float railWidth=Math.Max(bar.Size.X,bar.GetCombinedMinimumSize().X);
        float width=bar.Visible?Math.Max(1,Math.Min(bar.Position.X,maximumWidth-railWidth)):maximumWidth;
        var maximum=new Vector2(width,-1);
        if(content.CustomMaximumSize!=maximum) content.CustomMaximumSize=maximum;
    }
    private float AvailableBodyHeight()
    {
        // New NPC pages get the whole screen budget rather than a previous short page's centred position.
        float top=WindowId!="npc_dialog" && !_centerOnOpen && GetParent() is Control window?window.Position.Y:12;
        return Math.Max(80,GetViewportRect().Size.Y-top-126);
    }
    private void FitQuestContent()
    {
        ClassicNpcQuestChrome.StyleContent(_body);
        foreach(var title in CharacterDetailsSkin.Tree(_body).OfType<Label>().Where(l=>l.GetParent() is HBoxContainer row && row.GetChildren().OfType<Button>().Any(b=>b.TooltipText=="Previous quest")))
        {
            title.AutowrapMode=TextServer.AutowrapMode.Off;title.ClipText=true;
            title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        }
        ResizeQuestShell(_body.GetCombinedMinimumSize().Y);
    }
    private void ResizeQuestShell(float bodyHeight)
    {
        bool scroll=bodyHeight>AvailableBodyHeight();
        float height=Mathf.Ceil(Math.Max(174,80+Math.Min(bodyHeight,AvailableBodyHeight())+34));
        ContentScroll.VerticalScrollMode=scroll?ScrollContainer.ScrollMode.Auto:ScrollContainer.ScrollMode.Disabled;
        ContentScroll.Size=new Vector2(327,height-114);
        if(CustomMinimumSize.Y!=height)
        {
            CustomMinimumSize=new Vector2(363,height);Size=CustomMinimumSize;
            _frame.Root.H=(int)height;
            _frame.CustomMinimumSize=Size;_frame.Size=Size;
            foreach(var item in _frame.Where(n=>n.Type=="npc_quest_frame_component")) { item.Node.H=(int)height;item.Control.Size=Size; }
            if(GetParent() is LibreKO.HudWindow window)
            {
                window.ResetSize();
                var screen=GetViewportRect().Size;
                window.Position=new Vector2(Math.Clamp(window.Position.X,0,Math.Max(0,screen.X-window.Size.X)),
                    Math.Clamp(window.Position.Y,0,Math.Max(0,screen.Y-window.Size.Y)));
            }
        }
    }
    private static void Place(Control control,Control parent,int x,int y,int width,int height)
    {
        control.Reparent(parent);control.CustomMinimumSize=Vector2.Zero;
        control.Position=new Vector2(x,y);control.Size=new Vector2(width,height);
        control.SetMeta("classic_fixed_rect",new Rect2(x,y,width,height));
        if(control is Label label) { label.ClipText=true;label.VerticalAlignment=VerticalAlignment.Center; }
    }
    internal static void ComposePresets(Control body)
    {
        var controls=body.GetChildren().OfType<Control>().ToArray();
        if(controls.Length!=10 || controls[3] is not GridContainer || controls[8] is not GridContainer) return;
        var canvas=new Control { CustomMinimumSize=new Vector2(344,422) };body.AddChild(canvas);
        var plans=controls[0].GetChildren().OfType<Button>().ToArray();
        for(int i=0;i<plans.Length;i++) Place(plans[i],canvas,i*87,0,83,36);
        Place(controls[1],canvas,6,44,332,22);Place(controls[2],canvas,6,66,332,22);
        Allocation(controls[3],canvas,90,5);
        Place(controls[4],canvas,6,204,332,22);
        Place(controls[6],canvas,6,238,332,22);Place(controls[7],canvas,6,260,332,22);
        Allocation(controls[8],canvas,284,4);
        Place(controls[9],canvas,6,376,332,22);
        foreach(var control in controls.Where(c=>c.GetParent()==body)) control.Visible=false;
        canvas.AddChild(new ClassicReportSurface(true) { Position=new Vector2(0,232),Size=new Vector2(344,2) });
    }
    private static void Allocation(Control grid,Control canvas,int top,int rows)
    {
        var cells=grid.GetChildren().OfType<Control>().ToArray();
        canvas.AddChild(new ClassicReportSection { Position=new Vector2(0,top-2),Size=new Vector2(344,rows*22+4) });
        for(int row=0;row<rows;row++)
        {
            for(int column=0;column<4;column++)
            {
                int[] x={6,148,272,307},w={138,112,26,26};
                var control=cells[row*4+column];Place(control,canvas,x[column],top+row*22,w[column],column<2?22:18);
            }
        }
    }
}
