using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Domain;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class SkillWindow : Control
{
    private const string LayoutName = "{nation}_skilltree_us";
    private const int SlotAreaType = 7;
    private const int SlotsPerPage = 6;
    private const int TreeRows = 4;
    private const int TreeRowBase = 4;
    private const int NationalRows = 4;
    private const float TitleBarHeight = 30f;
    private const float ListNameWidth = 100f;
    private readonly string[] TabFamilies = Plugin.Kit.Nation==1
        ? new[] { "berserker", "hunter", "sorcerer", "shaman" }
        : new[] { "blade", "ranger", "mage", "cleric" };

    private readonly LayoutView _view;
    private readonly LayoutNode _layout;
    private readonly PluginGame _game;
    private readonly WindowHost _host;
    private readonly List<SkillCell> _cells = new();
    private readonly Dictionary<int,TextureButton> _tabButtons = new();
    private readonly Dictionary<TextureButton,int> _buttonCategories = new();
    private readonly List<GameSkill> _pageSkills = new();
    private int _family;
    private int _tab=SkillPage.Basic;
    private readonly ScrollContainer _descriptionScroll;
    private int _page;
    private int _selected = -1;
    private int _hovered = -1;
    private Rect2 _listRect;

    public SkillWindow(WindowHost host)
    {
        _host = host;
        var kit = Plugin.Kit;
        _game = kit.Game;
        _layout = SkillLayout.Build(kit.Layout(LayoutName));
        _view = new LayoutView(kit, _layout);
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = _layout.SizeVec;
        Size = _layout.SizeVec;
        AddChild(_view);
        host.SetDragHandle(_view.MakeDragHandle(new Rect2(0, 0, _layout.W, TitleBarHeight)));
        _view.OnPressed("btn_close", host.Close);
        _view.OnPressed("btn_left", () => TurnPage(-1));
        _view.OnPressed("btn_right", () => TurnPage(1));
        foreach(var (node,control) in _view.Where(n=>n.IsString))
            if(control is Label label)
            {
                label.ClipText=true;
                label.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
                label.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
                if(node.Id.StartsWith("string_list_")) label.MaxLinesVisible=2;
            }
        var description=_view.Get<Label>("string_info")!;
        _descriptionScroll=new ScrollContainer { Name="skill_description_scroll",Position=description.Position,Size=description.Size,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Auto };
        _view.AddChild(_descriptionScroll);
        var rail=_descriptionScroll.GetVScrollBar();
        var track=new StyleBoxFlat {BgColor=new Color("171715"),BorderColor=ClassicReportDesign.Caption};
        track.SetBorderWidthAll(1);track.SetContentMarginAll(0);
        rail.AddThemeStyleboxOverride("scroll",track);
        foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"})
        {
            var grab=ClassicDesign.ButtonBox(state=="grabber_highlight"?"hover":state=="grabber_pressed"?"pressed":"normal");
            grab.SetContentMarginAll(0);rail.AddThemeStyleboxOverride(state,grab);
        }
        rail.CustomMinimumSize=new Vector2(10,0);rail.Step=14;rail.FocusMode=FocusModeEnum.None;
        description.Reparent(_descriptionScroll,false);
        description.Position=Vector2.Zero;
        description.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        description.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        description.TextOverrunBehavior=TextServer.OverrunBehavior.NoTrimming;
        description.ClipText=false;
        description.VerticalAlignment=VerticalAlignment.Top;
        description.AddThemeConstantOverride("line_spacing",0);
        // Match the title sprite's alpha over the transparent cut-out in the frame atlas.
        var titleBackground=new StyleBoxFlat {BgColor=new Color(0,0,0,187f/255f)};
        titleBackground.SetContentMarginAll(0);
        _view.Get<Label>("class_fallback")!.AddThemeStyleboxOverride("normal",titleBackground);
        for (int row = 0; row < NationalRows; row++)
        {
            if(_view.Get<BaseButton>($"btn_{row}") is { } inactive) inactive.Disabled=true;
            foreach(string id in new[] {"national_"+row,"string_"+row})
                _view.Get(id)!.Modulate=new Color(.6f,.6f,.6f);
        }
        for (int row = 0; row < TreeRows; row++)
        {
            int type = row;
            _view.OnPressed($"btn_{TreeRowBase + row}", () => SpendOnRow(type));
        }

        foreach (var (node, control) in _view.Areas(SlotAreaType))
        {
            if (!int.TryParse(node.Id, out int index) || index >= SlotsPerPage) continue;
            var cell = new SkillCell(index)
            {
                Position = control.Position,
                Size = control.Size,
                OnSelect = Select,
                OnAdd = id => _game.Skills.AddToHotbar(id),
                OnHover = (id, over) => { if(over) _hovered=id;else if(_hovered==id) _hovered=-1;RefreshInfo(); },
            };
            control.GetParent().AddChild(cell);
            _cells.Add(cell);
            var rect = new Rect2(control.Position, control.Size + new Vector2(ListNameWidth, 0));
            _listRect = _listRect.Size == Vector2.Zero ? rect : _listRect.Merge(rect);
        }
        _cells.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach(var cell in _cells)
        {
            var name=_view.Get<Label>("string_list_"+cell.Index)!;
            name.MouseFilter=MouseFilterEnum.Pass;
            name.MouseEntered+=()=> { if(cell.SkillId>=0) { _hovered=cell.SkillId;RefreshInfo(); } };
            name.MouseExited+=()=> { if(_hovered==cell.SkillId) { _hovered=-1;RefreshInfo(); } };
        }
        Rebuild();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true } wheel) return;
        if (wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown) || !_listRect.HasPoint(wheel.Position)) return;
        TurnPage(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
        AcceptEvent();
    }

    public override void _EnterTree()
    {
        _game.Skills.Changed += Rebuild;
        _game.BecameAvailable += Rebuild;
        _host.Shown += Rebuild;
    }

    public override void _ExitTree()
    {
        _game.Skills.Changed -= Rebuild;
        _game.BecameAvailable -= Rebuild;
        _host.Shown -= Rebuild;
    }

    private void Rebuild()
    {
        int family=CharacterClassCatalog.Family(_game.Character.Class);
        int tier=CharacterClassCatalog.Tier(_game.Character.Class);
        _family=Math.Clamp(family-1,0,TabFamilies.Length-1);
        foreach(var (node,control) in _view.Where(n=>n.IsImage && !n.Id.StartsWith("img_skill"))) control.Visible=false;
        string[] masterTitles=Plugin.Kit.Nation==1
            ?new[] {"img_Berserker Hero","img_Shadow Bane","img_Elemental Lord","img_Shadow Knight"}
            :new[] {"img_Blade Master","img_kasar hood","img_Arc Mage","img_Paladin"};
        string title=tier==CharacterClassCatalog.TierMaster?masterTitles[_family]
            :tier==CharacterClassCatalog.TierBeginner?"img_public":"img_"+TabFamilies[_family];
        if(family<=4 && _view.Get(title) is { } heading) heading.Visible=true;
        var fallback=_view.Get<Label>("class_fallback")!;
        fallback.Visible=family>4;
        fallback.Text=CharacterClassCatalog.SpecializationName(_game.Character.Class);
        foreach(var (_,control) in _view.Where(n=>n.IsButton && (n.Id=="btn_public" || n.Id=="btn_master" || TabFamilies.Any(f=>n.Id.StartsWith("btn_"+f)))))
            control.Visible=false;
        _tabButtons.Clear();
        var group=new ButtonGroup();
        var tabs=_game.Skills.Tabs;
        foreach(var tab in tabs)
        {
            int category=tab.Category;
            string id=category==SkillPage.Basic?"btn_public":category==MasteryPoints.MasterTree?"btn_master"
                :$"btn_{TabFamilies[_family]}{category-MasteryPoints.FirstTree}";
            if(_view.Get<TextureButton>(id) is not { } button) continue;
            button.Visible=true;button.ToggleMode=true;button.ButtonGroup=group;
            if(!_buttonCategories.ContainsKey(button)) button.Pressed+=()=>SelectTab(_buttonCategories[button]);
            _buttonCategories[button]=category;
            var caption=button.GetChildren().OfType<Label>().FirstOrDefault() ?? _view.Caption(id,"",11,ClassicReportDesign.Value)!;
            caption.Text=category==SkillPage.Basic?"Basic Skill":tab.Label=="Assassinate"?"Assassin":tab.Label;
            caption.TooltipText=tab.Label;
            caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            caption.HorizontalAlignment=HorizontalAlignment.Center;caption.VerticalAlignment=VerticalAlignment.Center;
            caption.AddThemeFontOverride("font",Plugin.Kit.Bold);
            caption.AddThemeFontSizeOverride("font_size",11);
            caption.AddThemeColorOverride("font_color",new Color(Plugin.Kit.Nation==1?"201c18":"efd9b4"));
            caption.AddThemeColorOverride("font_shadow_color",Colors.Transparent);
            _tabButtons[category]=button;
        }
        if(!tabs.Any(t=>t.Category==_tab)) { _tab=tabs.Count>0?tabs[0].Category:SkillPage.Basic;_page=0; }
        RefreshTrees();
        RefreshPage();
        ApplyTextBounds();
    }

    public override void _Ready() => ApplyTextBounds();

    private void ApplyTextBounds()
    {
        // Theme metrics are resolved before restoring the declared pixel rectangles.
        foreach(var (node,control) in _view.Where(n=>n.IsString && n.Id!="string_info"))
        {
            control.UpdateMinimumSize();
            _=control.GetCombinedMinimumSize();
            control.Size=node.SizeVec;
        }
    }

    private void RefreshTrees()
    {
        _view.SetText("string_skillpoint",_game.Skills.MasteryPool.ToString());
        var trees=_game.Skills.ClassicTrees(_game.Character.Class);
        for(int row=0;row<4;row++)
        {
            var tree=row<trees.Count?trees[row]:default;
            _view.SetText("tree_name_"+row,tree.Shown?tree.Name:"");
            _view.SetText("string_"+(row+4),tree.Shown?tree.Points.ToString():"");
            if(_view.Get<TextureButton>("btn_"+(row+4)) is { } plus)
            { plus.Visible=tree.Shown;plus.Disabled=!tree.CanSpend;plus.TooltipText=tree.Hint; }
        }
    }

    private void SelectTab(int tab)
    {
        _tab = tab;
        _page = 0;
        _selected = -1;
        _hovered = -1;
        RefreshPage();
    }

    private void TurnPage(int delta)
    {
        int pages = Math.Max(1, (CurrentSkills().Count + SlotsPerPage - 1) / SlotsPerPage);
        int page = Math.Clamp(_page + delta, 0, pages - 1);
        if (page == _page) return;
        _page = page;
        _selected = _hovered = -1;
        RefreshPage();
    }

    private IReadOnlyList<GameSkill> CurrentSkills()
    {
        return _game.Skills.Skills(_tab);
    }

    private void RefreshPage()
    {
        foreach(var (category,button) in _tabButtons) button.SetPressedNoSignal(category==_tab);
        var skills = CurrentSkills();
        int pageCount=Math.Max(1,(skills.Count+SlotsPerPage-1)/SlotsPerPage);
        _page=Math.Clamp(_page,0,pageCount-1);
        int first = _page * SlotsPerPage;
        _pageSkills.Clear();
        for (int i = 0; i < _cells.Count; i++)
        {
            var skill = first + i < skills.Count ? skills[first + i] : (GameSkill?)null;
            _cells[i].Bind(skill);
            _view.SetText($"string_list_{i}", skill?.Name ?? "");
            _view.Get<Label>($"string_list_{i}")!.Modulate=skill is {Available:false}?new Color(.65f,.65f,.65f):Colors.White;
            _view.Get<Label>($"string_list_{i}")!.TooltipText=skill?.Tooltip??"";
            if (skill != null) _pageSkills.Add(skill.Value);
        }
        if(!_pageSkills.Any(s=>s.Id==_selected)) _selected=_pageSkills.Count>0?_pageSkills[0].Id:-1;
        if(!_pageSkills.Any(s=>s.Id==_hovered)) _hovered=-1;
        foreach (var cell in _cells) cell.SetSelected(cell.SkillId == _selected);
        _view.SetText("string_page", $"{_page+1} / {pageCount}");
        _view.Get<BaseButton>("btn_left")!.Disabled=_page==0;
        _view.Get<BaseButton>("btn_right")!.Disabled=_page>=pageCount-1;
        RefreshInfo();
    }

    private void Select(int skillId)
    {
        _selected = skillId;
        foreach (var cell in _cells) cell.SetSelected(cell.SkillId == _selected);
        RefreshInfo();
    }

    private void RefreshInfo()
    {
        int shown = _hovered >= 0 ? _hovered : _selected;
        var info = shown >= 0 ? _game.Skills.ClassicInfo(shown) : default;
        bool has = shown >= 0 && info.Description != null;
        string description=has?info.Description??"":"";
        if(_view.Get<Label>("string_info")!.Text!=description) _descriptionScroll.ScrollVertical=0;
        _view.SetText("string_info",description);
        _view.SetText("string_skill_mp",!has?"":info.Mp==0?"No MP consumed":$"MP consumed : {info.Mp}");
        _view.SetText("string_skill_point",!has?"":info.UsesPoints?$"Required Skill Point : {info.RequiredPoints}":$"Required Level : {info.RequiredLevel}");
        _view.SetText("string_skill_item0",!has?"":info.BasicItem.Length>0?$"Required weapon : {info.BasicItem}":"No weapon required");
        _view.SetText("string_skill_item1", !has ? "" : info.RequiredItem.Length > 0 ? $"Required item : {info.RequiredItem}" : "No required item");
        _view.SetText("string_skill_item2", !has ? "" : info.ConsumedItem.Length > 0 ? $"Item consumed : {info.ConsumedItem}" : "No item consumed");
        foreach(var (_,control) in _view.Where(n=>n.Id.StartsWith("string_skill_")))
            if(control is Label label) { label.TooltipText=label.Text;label.MouseFilter=MouseFilterEnum.Pass; }
    }

    private void SpendOnRow(int row)
    {
        var trees = _game.Skills.Trees;
        if (row < trees.Count) _game.Skills.SpendMastery(trees[row].Type);
    }
}

public partial class SkillCell : Control
{
    private const string DragKeyItem = "id";
    private static readonly Color UnavailableTint = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color SelectedFrame = new(0.95f, 0.80f, 0.35f, 1f);
    private const float FrameWidth = 2f;

    public int Index { get; }
    public int SkillId { get; private set; } = -1;
    public Action<int>? OnSelect { get; set; }
    public Action<int>? OnAdd { get; set; }
    public Action<int, bool>? OnHover { get; set; }

    private readonly TextureRect _icon;
    private GameSkill? _skill;
    private bool _selected;
    private Vector2 _grabPoint;

    public SkillCell(int index)
    {
        Index = index;
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { if (_skill != null) OnHover?.Invoke(_skill.Value.Id, true); };
        MouseExited += () => { if (_skill != null) OnHover?.Invoke(_skill.Value.Id, false); };
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _icon.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_icon);
    }

    public void Bind(GameSkill? skill)
    {
        _skill = skill;
        SkillId = skill?.Id ?? -1;
        Visible = skill != null;
        _icon.Texture = skill?.Icon;
        _icon.Modulate = skill is { Available: false } ? UnavailableTint : Colors.White;
        TooltipText = skill?.Tooltip ?? "";
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected) DrawRect(new Rect2(Vector2.Zero, Size), SelectedFrame, false, FrameWidth);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (_skill == null || ev is not InputEventMouseButton { Pressed: true } mb) return;
        if (mb.ButtonIndex == MouseButton.Left) {
            _grabPoint=IconDragPreview.GrabPoint(this,_icon,mb.Position);
            OnSelect?.Invoke(_skill.Value.Id); AcceptEvent();
        }
        else if (mb.ButtonIndex == MouseButton.Right && _skill.Value.Available) { OnAdd?.Invoke(_skill.Value.Id); AcceptEvent(); }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_skill is not { Available: true } skill) return default;
        LibreKO.DragLayer.Show(this,IconDragPreview.Create(_icon,_grabPoint));
        return new Godot.Collections.Dictionary { { DragKeyItem, skill.Id } };
    }
}
