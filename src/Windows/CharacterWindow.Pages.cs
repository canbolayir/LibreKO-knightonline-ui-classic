using Godot;
using KnightOnlineUiClassic.Layout;
using LibreKO.Network;
using LibreKO.Plugins;

namespace KnightOnlineUiClassic.Windows;

public partial class CharacterWindow
{
    private readonly Dictionary<string, LayoutView> _pages = new();
    private readonly Dictionary<string, CharacterList> _lists = new();
    private readonly Dictionary<string, Label> _hints = new();
    private readonly List<(string Page, string Id, string Action)> _actions = new();
    private readonly Dictionary<string, Button> _extras = new();
    private readonly Dictionary<string, (BaseButton Previous, BaseButton Next)> _pagers = new();
    private string _activePage = "character";
    private bool _unionMode;
    private double _elapsed;
    private Button? _titleButton;
    private OptionButton? _questFilter, _questKind;
    private PopupMenu? _contextMenu;
    private readonly List<(string Action, string Selection)> _contextActions = new();
    private bool _dialogsStyled;
    private LineEdit? _friendName;
    private IGameCharacterPanel? Panel => _game.Windows.CharacterPanel;
    private CharacterEmbeddedPage? _embedded;
    private readonly Stack<CharacterEmbeddedPage> _embeddedTrail=new();
    public void ShowEmbedded(CharacterEmbeddedPage page)
    {
        if(_embedded==page) return;
        if(_embedded!=null && _embedded!=page) { _embedded.Visible=false;_embeddedTrail.Push(_embedded); }
        _embedded=page;
        _activePage=page.ParentPage;
        if(Panel?.SelectedPage!=_activePage) Panel?.SelectPage(_activePage);
        foreach(var view in _pages.Values) view.Visible=false;
        if(page.GetParent()!=this) page.Reparent(this);
        page.Position=Vector2.Zero;page.Visible=true;page.Back=()=>BackFromEmbedded();
        RefreshTabs();
        if(!IsVisibleInTree()) _game.Windows.Open("character_info");
    }
    public void DismissEmbedded(CharacterEmbeddedPage page)
    {
        if(_embedded==page) BackFromEmbedded(false);
    }
    private void BackFromEmbedded(bool closeNative=true)
    {
        if(_embedded==null) return;
        var previous=_embedded;_embedded=null;previous.Visible=false;
        if(closeNative) previous.CloseNative();
        while(_embeddedTrail.TryPop(out var parent))
        {
            if(!GodotObject.IsInstanceValid(parent) || !parent.NativeWindow.Visible) continue;
            _embedded=parent;parent.Visible=true;_activePage=parent.ParentPage;
            break;
        }
        RefreshPages();
    }
    private void ClearEmbedded()
    {
        while(_embedded!=null) BackFromEmbedded();
    }

    private static Control? Find(LayoutView view, string id) =>
        view.Where(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Control;

    private static void Bind(LayoutView view, string id, Action action)
    {
        if (Find(view, id) is BaseButton button) button.Pressed += action;
    }

    private static void SetText(LayoutView view, string id, string text)
    {
        if (Find(view, id) is not Label label) return;
        label.Text = text;
        label.TooltipText = text;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
    }

    private void InitializePages()
    {
        _pages["character"] = _page;
        StylePage(_frame); StylePage(_page);
        foreach (var key in new[] { "friends", "quest", "clan", "knights", "union_main", "union_sub" })
        {
            var root = CharacterLayout.Page(key);
            var view = new LayoutView(Plugin.Kit, root) { Visible = false };
            view.Position = new Vector2(root.X - _frame.Root.X, root.Y - _frame.Root.Y);
            AddChild(view); _pages[key] = view; StylePage(view);
            BuildList(key, view);
        }
        foreach (var (button, page) in new[] { ("btn_state", "character"), ("btn_quest", "quest"),
                     ("btn_clan", "clan"), ("btn_knights", "clan"), ("btn_friends", "friends") })
        {
            if (Find(_frame, button) is not BaseButton tab) continue;
            tab.ZIndex = 1;
            tab.ToggleMode = true;
            tab.Pressed += () => SwitchPage(page, true);
            tab.MouseEntered += RefreshTabs;
            tab.MouseExited += RefreshTabs;
        }
        BuildStateExtensions();
        BuildFriends();
        BuildClan("clan"); BuildClan("knights");
        BuildUnion("union_main"); BuildUnion("union_sub");
        BuildQuest();
        RefreshPages();
    }

    private static void StylePage(LayoutView view)
    {
        foreach (var (node, control) in view.Where(n => n.IsString))
            if (control is Label label)
            {
                ClassicReportDesign.StyleLabel(label, node);
                label.ClipText = true;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }
    }

    private void BuildStateExtensions()
    {
        _titleButton = (Button)Find(_page, "btn_title")!;
        Bind(_page, "btn_title", () => Panel?.Act("titles"));
        Bind(_page, "btn_presets", () => Panel?.Act("presets"));
    }

    private void BuildList(string key, LayoutView view)
    {
        var nodes = view.Root.All().Where(n => n.Type == "list").OrderBy(n => n.X).ToArray();
        if (nodes.Length == 0) return;
        float x = nodes.Min(n => n.X), y = nodes.Min(n => n.Y);
        float right = nodes.Max(n => n.X + n.W), bottom = nodes.Max(n => n.Y + n.H);
        foreach (var node in nodes) if (view.ControlOf(node) is { } c) c.Visible = false;
        int rows=(int)(bottom-y)/ClassicDesign.RowHeight;
        var list = new CharacterList(new Rect2(x - view.Root.X, y - view.Root.Y, right - x, bottom - y),
            nodes.Select(n => n.X - x).ToArray(), nodes.Select(n => (float)n.W).ToArray(), rows);
        view.AddChild(list); _lists[key] = list;
        list.SetEmptyText(key == "friends" ? "No friends yet.\nRegister a name or target." : key == "quest" ? "No quests in this view.\nTalk to NPCs to find quests." : key.StartsWith("union") ? "No confederacy members." : "No clan members.");
        list.SelectedRow += id => { if (key == "quest") Panel?.Act("quest_select", id); RefreshPages(); };
        list.ActivatedRow += id => Panel?.Act(key == "friends" ? "friend_private" : key == "quest" ? "quest_details" : key.StartsWith("union") ? "clan_management" : "clan_private", id);
        list.ContextRow += id => ShowContext(key, id);
    }

    private void BuildFriends()
    {
        var view = _pages["friends"];
        _friendName = (LineEdit)Find(view, "friend_name")!;
        _friendName.MaxLength = 20;
        _friendName.PlaceholderText = "Character name";
        _friendName.TooltipText = "Enter a name, or select a player and press Register";
        _friendName.TextSubmitted += _ => AddFriend();
        Bind(view, "btn_add", AddFriend);
        RegisterAction("friends", "btn_delete", "friend_remove");
        RegisterAction("friends", "btn_whisper", "friend_private");
        RegisterAction("friends", "btn_party", "friend_party");
        Bind(view, "btn_refresh", () => Panel?.Refresh("friends"));
        BindPager("friends", "btn_page_up", "btn_page_down");
        _hints["friends"] = (Label)Find(view, "page_hint")!;
    }

    private void AddFriend()
    {
        Panel?.Act("friend_add", value: _friendName?.Text ?? "");
        _friendName?.Clear(); _friendName?.ReleaseFocus();
    }

    private void BuildClan(string key)
    {
        var view = _pages[key];
        RegisterAction(key, "btn_clan_party", "clan_party");
        RegisterAction(key, "btn_clan_whisper", "clan_private");
        RegisterAction(key, "btn_clan_admit", "clan_invite");
        Bind(view, "btn_clan_refresh", () => Panel?.Refresh("clan"));
        BindPager(key, "btn_clan_down", "btn_clan_up");
        Bind(view, "btn_union", ShowUnion);
        _hints[key] = (Label)Find(view, "page_hint")!;
        Bind(view, "btn_management", () => Panel?.Act("clan_management"));
        Bind(view, "btn_contribution", () => Panel?.Act("clan_contribution"));
        Bind(view, "btn_leave", () => Panel?.Act("clan_leave"));
        _extras[key + ":points"] = (Button)Find(view, "btn_contribution")!;
        _extras[key + ":leave"] = (Button)Find(view, "btn_leave")!;
    }

    private void BuildUnion(string key)
    {
        var view = _pages[key];
        RegisterAction(key, "btn_knights_admit", "union_add");
        RegisterAction(key, "Btn_Remove", "union_remove");
        Bind(view, "btn_knights", () => { _unionMode = false; Panel?.Refresh("clan"); RefreshPages(); });
        Bind(view, "btn_refresh", () => Panel?.Refresh("union"));
        BindPager(key, "btn_pagedown", "btn_pageup");
        Bind(view, "Btn_knights_chat", () => Panel?.Act("clan_chat"));
        Bind(view, "btn_union_chat", () => Panel?.Act("union_chat"));
        _hints[key] = (Label)Find(view, "page_hint")!;
        Bind(view, "btn_management", () => Panel?.Act("clan_management"));
        Bind(view, "btn_leave", () => Panel?.Act("union_leave"));
        if (Find(view, "btn_leave") is Button leave) _extras[key + ":leave"] = leave;
    }

    private void ShowUnion()
    {
        _unionMode = true;
        Panel?.Refresh("union"); RefreshPages();
    }

    private void BuildQuest()
    {
        var view = _pages["quest"];
        BindPager("quest", "btn_page_down", "btn_page_up");
        _questFilter = Filter(view, "quest_filter", new[] { "Available", "In Progress", "Completed" });
        _questKind = Filter(view, "quest_kind", new[] { "All", "Story", "Hunt", "Delivery" });
        foreach (var (key, action) in new[] { ("details", "quest_details"), ("track", "quest_track"), ("abandon", "quest_abandon"), ("complete", "quest_complete") })
        {
            _extras["quest:" + key] = (Button)Find(view, "btn_" + key)!;
            Bind(view, "btn_" + key, () => ActSelected("quest", action));
        }
    }

    private OptionButton Filter(LayoutView view, string id, string[] labels)
    {
        var button = (OptionButton)Find(view, id)!;
        StylePopup(button.GetPopup());
        foreach (var label in labels) button.AddItem(label);
        button.ItemSelected += index => { Panel?.Act(id, value: index.ToString()); RefreshPages(); };
        return button;
    }

    private void RegisterAction(string key, string id, string action)
    {
        _actions.Add((key, id, action)); Bind(_pages[key], id, () => ActSelected(key, action));
    }
    private void ActSelected(string key, string action) => Panel?.Act(action, _lists[key].Selected);
    private void Page(string key, int delta) { _lists[key].ChangePage(delta); RefreshPages(); }

    private void BindPager(string key, string first, string second, bool vertical = false)
    {
        if (Find(_pages[key], first) is not BaseButton a || Find(_pages[key], second) is not BaseButton b) return;
        if ((vertical ? a.Position.Y > b.Position.Y : a.Position.X > b.Position.X)) (a, b) = (b, a);
        _pagers[key] = (a, b);
        a.Pressed += () => Page(key, -1); b.Pressed += () => Page(key, 1);
    }

    private void SwitchPage(string page, bool notify)
    {
        ClearEmbedded();
        _activePage = page; _unionMode = false;
        if (notify) Panel?.SelectPage(page);
        RefreshPages();
    }

    public override void _Process(double delta)
    {
        if (!_game.Available || !IsVisibleInTree()) return;
        _elapsed += delta;
        if (_elapsed < 0.25) return;
        _elapsed = 0; Refresh();
    }

    private void RefreshPages()
    {
        var panel = Panel;
        EnsurePanelStyles();
        if (panel != null && panel.SelectedPage != _activePage) { _activePage = panel.SelectedPage; _unionMode = false; }
        var clan = panel?.Clan ?? default;
        if(_embedded?.ParentPage=="clan" && !clan.InClan) { ClearEmbedded();return; }
        string clanKey = clan.Flag >= ClanTypes.Promoted ? "knights" : "clan";
        string key = _activePage == "clan" ? (_unionMode ? clan.IsChief && (clan.AllianceId == 0 || clan.AllianceId == clan.ClanId) ? "union_main" : "union_sub" : clanKey) : _activePage;
        foreach (var (name, view) in _pages) view.Visible = _embedded==null && name == key;
        RefreshTabs();
        if (panel == null || !_lists.TryGetValue(key, out var list)) return;
        string section = key is "clan" or "knights" ? "clan" : key.StartsWith("union") ? "union" : key == "quest" ? "quests" : key;
        list.SetRows(panel.Rows(section));
        var page = _pages[key];
        foreach (var id in new[] { "Text_clan_Page", "string_page", "text_page", "Text_Page" })
        {
            SetText(page, id, $"{list.Page + 1}/{list.Pages}");
            if (Find(page, id) is { } counter) counter.TooltipText = $"Page {list.Page + 1} of {list.Pages}";
        }
        RefreshPageData(key, section, panel, clan, list, page);
    }

    private void EnsurePanelStyles()
    {
        var panel = Panel;
        if (panel != null && !_dialogsStyled)
        {
            _dialogsStyled = true;
            foreach (var dialog in panel.Dialogs) CharacterDetailsSkin.StyleDialog(dialog);
        }
    }

    private void RefreshPageData(string key, string section, IGameCharacterPanel panel, MyClanInfo clan, CharacterList list, LayoutView page)
    {
        if (key == "friends")
        {
            SetText(page, "text_friend", $"Friend ({panel.Rows("friends").Count(r=>r.Online)})");
            if(Find(page,"text_friend") is { } heading) heading.TooltipText=$"{panel.Rows("friends").Count(r=>r.Online)} online / {list.Count} friends";
            var status = panel.Status("friends");
            _hints[key].Text = status; _hints[key].TooltipText = status;
        }
        if (section is "clan" or "union")
        {
            foreach (var id in new[] { "Text_clansName", "Text_KnightsName" }) SetText(page, id, clan.InClan ? clan.Name : "No clan");
            foreach (var id in new[] { "Text_clan_Duty", "Text_knights_Duty" }) SetText(page, id, clan.InClan ? ClanRanks.Name(clan.Fame) : "-");
            SetText(page, "Text_clan_MemberCount", $"{list.Count}/{clan.MaxMembers}");
            SetText(page, "Text_MemberCount", list.Count.ToString());
            string text = clan.InClan ? section == "union" ? panel.Status("union") : clan.Notice : "Visit an Inn Hostess to found a clan.";
            if (section == "clan" && panel.Status("clan").Length > 0) text = panel.Status("clan");
            _hints[key].Text = text;
            _hints[key].TooltipText = clan.InClan ? $"{ClanTypes.Standing(clan.Flag, clan.Grade)}\n{clan.Online} online / {panel.Rows("clan").Count} members\nContribution: {clan.PointFund:N0}\n{clan.Notice}\n{text}" : $"Create a clan at an Inn Hostess.\nRequired level: {ClanTypes.CreationLevel}\nRequired gold: {ClanTypes.CreationCoins:N0}";
            if (_extras.TryGetValue(key + ":points", out var points)) points.Disabled = !clan.InClan;
            if (_extras.TryGetValue(key + ":leave", out var leave)) { leave.Disabled = section == "union" ? !clan.IsChief || clan.AllianceId == 0 : !clan.InClan; if (section == "clan") leave.Text = clan.IsChief ? "Disband" : "Leave"; }
            UpdateGrade(page, clan);
        }
        foreach (var (actionPage, id, action) in _actions)
        {
            if (actionPage != key || Find(page, id) is not BaseButton button) continue;
            var selection = list.Selection;
            bool selected = selection.HasValue;
            bool other = selected && list.Selected != _game.Character.Name;
            bool can = action switch {
                "clan_invite" => clan.CanInvite,
                "clan_appoint" or "clan_remove" => clan.IsChief && other,
                "clan_private" => other,
                "clan_party" => other && selection?.Online == true,
                "union_add" => clan.IsChief && clan.Flag >= ClanTypes.Promoted && (clan.AllianceId == 0 || clan.AllianceId == clan.ClanId),
                "union_remove" => clan.IsChief && clan.AllianceId == clan.ClanId && selected && list.Selected != clan.ClanId.ToString(),
                "union_leave" => clan.IsChief && clan.AllianceId != 0,
                "friend_private" or "friend_party" => selected && selection?.Online == true,
                _ => selected };
            button.Disabled = !can;
        }
        UpdatePager(key, list);
        if (key == "quest")
        {
            _questFilter?.Select(panel.QuestFilter); _questKind?.Select(panel.QuestKind);
            var selection = list.Selection;
            _extras["quest:details"].Disabled = !selection.HasValue;
            _extras["quest:track"].Disabled = selection?.Trackable != true;
            _extras["quest:track"].Text = selection?.Tracked == true ? "Untrack" : "Track";
            _extras["quest:abandon"].Disabled = selection?.Abandonable != true;
            _extras["quest:complete"].Disabled = selection?.Claimable != true;
            _extras["quest:details"].TooltipText = "View objectives, rewards, quest targets and available actions";
            _extras["quest:complete"].TooltipText = "Talk to the quest NPC and choose any required reward in Details";
        }
    }

    private void RefreshTabs()
    {
        bool knights = (Panel?.Clan.Flag ?? 0) >= ClanTypes.Promoted;
        foreach (var (id, page) in new[] { ("btn_state", "character"), ("btn_quest", "quest"), ("btn_clan", "clan"), ("btn_knights", "clan"), ("btn_friends", "friends") })
        {
            if (Find(_frame, id) is not BaseButton button) continue;
            button.Visible = id == "btn_knights" ? knights : id != "btn_clan" || !knights;
            button.SetPressedNoSignal(_activePage == page);
            foreach (var label in button.GetChildren().OfType<Label>())
                label.AddThemeColorOverride("font_color", Plugin.Kit.Nation == 1 ? new Color("ece6d6") : new Color("f7de88"));
        }
    }

    private void UpdatePager(string key, CharacterList list)
    {
        if (!_pagers.TryGetValue(key, out var pair)) return;
        pair.Previous.Disabled = list.Page == 0;
        pair.Next.Disabled = list.Page >= list.Pages - 1;
    }

    private void UpdateHighlights(int code)
    {
        int family = LibreKO.Domain.CharacterClassCatalog.Family(code);
        var enabled = family switch { 1 => new[] { "img_str", "img_sta" }, 2 => new[] { "img_sta", "img_dex" }, 3 => new[] { "img_int", "img_map" }, 4 => new[] { "img_str", "img_int" }, _ => Array.Empty<string>() };
        foreach (var id in new[] { "img_str", "img_sta", "img_dex", "img_int", "img_map" })
        {
            bool active = enabled.Contains(id);
            if (Find(_page, id) is Label c)
            {
                c.Visible = active;
                foreach (var (node, control) in _page.Where(n => n.IsString && n.Id != id && n.Text.Trim() == c.Text.Trim()))
                    control.Visible = !active;
            }
        }
    }

    private static void UpdateGrade(LayoutView view, MyClanInfo clan)
    {
        SetText(view,"grade_number",clan.InClan ? clan.Grade.ToString() : "");
        foreach(var id in new[]{"grade_number","grade_caption"})
            if(Find(view,id) is { } caption) caption.Visible=clan.InClan;
        foreach (var (node, control) in view.Where(n => n.IsImage && n.Id.Contains("grade", StringComparison.OrdinalIgnoreCase)))
        {
            var digit = node.Id.LastOrDefault(char.IsDigit);
            if (digit != default) control.Visible = clan.InClan && digit - '0' == (clan.Grade is >= 1 and <= 5 ? clan.Grade - 1 : 4);
        }
    }

    private void ShowContext(string key, string id)
    {
        _contextMenu ??= new PopupMenu();
        StylePopup(_contextMenu);
        if (_contextMenu.GetParent() == null) { AddChild(_contextMenu); _contextMenu.IdPressed += item => { if (item >= 0 && item < _contextActions.Count) { var a = _contextActions[(int)item]; Panel?.Act(a.Action, a.Selection); } }; }
        _contextMenu.Clear(); _contextActions.Clear();
        void Add(string text, string action, bool enabled = true)
        {
            _contextMenu.AddItem(text, _contextActions.Count);
            _contextMenu.SetItemDisabled(_contextMenu.ItemCount - 1, !enabled);
            _contextActions.Add((action, id));
        }
        var row = _lists[key].Selection;
        if (key == "friends") { Add("Private", "friend_private", row?.Online == true); Add("Invite to party", "friend_party", row?.Online == true); Add("Delete", "friend_remove"); }
        else if (key == "quest") { Add("Details", "quest_details"); Add("Track / Untrack", "quest_track", row?.Trackable == true); Add("Abandon", "quest_abandon", row?.Abandonable == true); Add("Turn in", "quest_complete", row?.Claimable == true); }
        else if (key.StartsWith("union"))
        {
            Add("Management", "clan_management");
            var clan = Panel?.Clan ?? default;
            if (clan.IsChief && clan.AllianceId == clan.ClanId) Add("Expel from alliance", "union_remove", id != clan.ClanId.ToString());
        }
        else
        {
            Add("Private", "clan_private", id != _game.Character.Name); Add("Invite to party", "clan_party", id != _game.Character.Name && row?.Online == true); Add("User information", "clan_info");
            if (Panel?.Clan.IsChief == true && id != _game.Character.Name) { Add("Appoint vice-chief", "clan_appoint"); Add("Hand over leadership", "clan_handover"); Add("Expel from clan", "clan_remove"); }
        }
        _contextMenu.ResetSize(); _contextMenu.Position = (Vector2I)GetViewport().GetMousePosition(); _contextMenu.Popup();
    }

    private static void StylePopup(PopupMenu menu)
    {
        menu.AddThemeStyleboxOverride("panel", ClassicDesign.InputBox());
        menu.AddThemeFontOverride("font", Plugin.Kit.Regular); menu.AddThemeFontSizeOverride("font_size", ClassicDesign.FontSize);
        menu.AddThemeColorOverride("font_color", ClassicDesign.Text);
    }
}
