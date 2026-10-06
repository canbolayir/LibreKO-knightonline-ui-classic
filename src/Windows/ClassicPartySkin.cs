using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Original party artwork with live LibreKO controls and callbacks.</summary>
public static class ClassicPartySkin
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static Control? Apply(Control body)
    {
        Node? owner = body;
        while (owner != null && owner is not HudWindow) owner = owner.GetParent();
        if (owner is not HudWindow window || window.HasMeta("classic_party")) return null;
        window.SetMeta("classic_party", true);
        var grip = window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close = grip.GetChildren().OfType<Button>().Single(b => b.TooltipText == "Close");
        foreach (var child in window.GetChildren().OfType<Control>()) child.Visible = false;
        window.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        window.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        Control panel = window.Id == "party" ? new ClassicPartyRoster(window, body, grip) : new ClassicPartyBoard(body, grip, close);
        window.AddChild(panel); window.ResetSize();
        return panel;
    }
    internal static Label Caption(Control parent, string text, Rect2 rect, Color? color = null, int size = 12)
    {
        var label = new Label { Text = text, Position = rect.Position, Size = rect.Size, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", Plugin.Kit.ChatStrong);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? new Color("ffff80"));
        parent.AddChild(label);
        Callable.From(() => label.Size = rect.Size).CallDeferred();
        return label;
    }
    internal static void Drag(Control parent, Control native, Rect2 rect)
    {
        var grip = new Control { Name = "party_drag", Position = rect.Position, Size = rect.Size, MouseDefaultCursorShape = Control.CursorShape.Move };
        parent.AddChild(grip);
        grip.GuiInput += ev => native.EmitSignal(Control.SignalName.GuiInput, ev);
    }
    internal static Button Place(Control parent, Button button, LayoutNode art, string? caption = null)
    {
        ClassicChatControls.SkinButton(button, art);
        button.Icon = null; button.CustomMinimumSize = Vector2.Zero;
        button.AddThemeFontOverride("font", Plugin.Kit.Bold); button.AddThemeFontSizeOverride("font_size", 10);
        var ink = Plugin.Kit.Nation == 1 ? Colors.Black : new Color("ffff80");
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) button.AddThemeColorOverride(state, ink);
        KarusButtonContrast(button);
        if (caption != null) button.Text = caption;
        ClassicVendorSkin.Move(button, parent, new Rect2(art.Position, art.SizeVec));
        button.SetMeta("party_expected_rect", new Rect2(art.Position, art.SizeVec));
        return button;
    }
    internal static void KarusButtonContrast(Button button)
    {
        if (Plugin.Kit.Nation != 1) return;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" })
            button.AddThemeColorOverride(state, new Color("17130f"));
        button.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        button.AddThemeConstantOverride("outline_size", 0);
        // Keep inactive captions readable; distinguish state with the original artwork's brightness.
        if (button.GetThemeStylebox("normal") is StyleBoxTexture normal)
        {
            var disabled = (StyleBoxTexture)normal.Duplicate();
            disabled.ModulateColor = new Color(.72f, .72f, .72f, 1);
            button.AddThemeStyleboxOverride("disabled", disabled);
        }
    }
}

public partial class ClassicPartyRoster : Control
{
    private readonly HudWindow _window;
    private readonly VBoxContainer _members;
    private readonly Control _actions = new();
    public ClassicPartyRoster(HudWindow window, Control body, Control grip)
    {
        Name = "classic_party_roster"; _window = window;
        MouseFilter = MouseFilterEnum.Ignore;
        _members = CharacterDetailsSkin.Tree(body).OfType<VBoxContainer>().Single(n => n.Name == "party_members");
        _members.Reparent(this); _members.Position = new Vector2(0, 25); _members.CustomMinimumSize = new Vector2(131, 0);
        _members.AddThemeConstantOverride("separation", 0);
        _members.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var heading = Plugin.Kit.Layout("co_party_us").Strings.First();
        ClassicPartySkin.Caption(this, "Party", new Rect2(heading.Position,heading.SizeVec),heading.Color,heading.Size);
        ClassicPartySkin.Drag(this, grip, new Rect2(1, 2, 123, 22));
        AddChild(_actions);
        var buttons = CharacterDetailsSkin.Tree(body).OfType<Button>().Where(b => b.Text is "Invite" or "Leave" or "Disband" or "Seek Party").ToArray();
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i]; ClassicChatControls.SkinButton(b, Plugin.Kit.Layout("{nation}_partyboard_us").Find("btn_party")!);
            b.AddThemeFontOverride("font", Plugin.Kit.Bold); b.AddThemeFontSizeOverride("font_size", 10); b.ClipText = true;
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) b.AddThemeColorOverride(state, Plugin.Kit.Nation == 1 ? Colors.Black : new Color("ffff80"));
            ClassicPartySkin.KarusButtonContrast(b);
            ClassicVendorSkin.Move(b, _actions, new Rect2(6 + (i % 2) * 60, (i / 2) * 21, 58, 18));
        }
        _members.ChildEnteredTree += _ => Callable.From(Refresh).CallDeferred();
        _members.ChildExitingTree += _ => Callable.From(Refresh).CallDeferred();
        Resized += () => Size = CustomMinimumSize;
    }
    public override void _Ready() => Refresh();
    private void Refresh()
    {
        if (!IsInsideTree()) return;
        foreach (var row in _members.GetChildren().OfType<Control>())
        {
            if (row.HasMeta("party_id"))
            {
                if (!row.HasMeta("classic_party_row"))
                {
                    row.SetMeta("classic_party_row", true);
                    foreach (var child in row.GetChildren().OfType<Control>()) child.Visible = false;
                    var art = new ClassicPartyMember(row, _members); row.AddChild(art);
                    row.CustomMinimumSize = new Vector2(131, 45);
                }
            }
            else if (row is Label hint)
            {
                hint.Text = "No party members"; hint.CustomMinimumSize = new Vector2(131, 24);
                hint.AddThemeFontOverride("font", Plugin.Kit.Bold); hint.AddThemeFontSizeOverride("font_size", 11);
            }
        }
        int height = _members.GetChildren().OfType<Control>().Count(c => c.HasMeta("party_id")) * 45;
        _actions.Position = new Vector2(0, 25 + Math.Max(24, height));
        _actions.GetChildren().OfType<Button>().Single(b=>b.Text=="Leave").Disabled = height == 0;
        CustomMinimumSize = new Vector2(131, _actions.Position.Y + 43); Size = CustomMinimumSize;
        _members.Size = new Vector2(131, Math.Max(24, height));
        _window.ResetSize();
    }
}

public partial class ClassicPartyMember : Control
{
    private readonly Control _owner;
    private readonly Node _list;
    private readonly LayoutNode _layout;
    private readonly string _status;
    private readonly float _hp, _mp;
    private int _phase = -1;
    private int _selection = -1;
    public ClassicPartyMember(Control owner, Node list)
    {
        _owner = owner; _list = list; _layout = Plugin.Kit.Layout("co_party_us");
        MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(131, 45); Size = CustomMinimumSize;
        _status = owner.GetMeta("party_status").AsString();
        _hp = Mathf.Clamp(owner.GetMeta("party_hp").AsSingle() / Math.Max(1, owner.GetMeta("party_max_hp").AsSingle()), 0, 1);
        _mp = Mathf.Clamp(owner.GetMeta("party_mp").AsSingle() / Math.Max(1, owner.GetMeta("party_max_mp").AsSingle()), 0, 1);
        var label = ClassicPartySkin.Caption(this, owner.GetMeta("party_name").AsString(), new Rect2(9, 20, 100, 15), new Color("8080ff"), 10);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        if (owner.GetMeta("party_leader").AsBool()) label.AddThemeColorOverride("font_color", new Color("ffff80"));
    }
    public override void _Process(double delta)
    {
        int phase = (int)(Time.GetTicksMsec() / 1000) % 2;
        if (phase != _phase) { _phase = phase; QueueRedraw(); }
        // Selection can change without rebuilding the server-owned member rows.
        int selected = _list.HasMeta("party_selected") ? _list.GetMeta("party_selected").AsInt32() : -1;
        if (selected != _selection) { _selection = selected; QueueRedraw(); }
    }
    private void Bar(string id, Rect2 rect, float value)
    {
        var source = _layout.Find(id)!;
        foreach (var art in source.Images.OrderBy(a => a.Tag))
        {
            var dest = rect; var src = new Rect2(art.SrcX, art.SrcY, art.SrcW, art.SrcH);
            if (art.Tag == 1) { dest.Size *= new Vector2(value, 1); src.Size *= new Vector2(value, 1); }
            if (dest.Size.X > 0) DrawTextureRectRegion(Plugin.Kit.Texture(art.Texture!), dest, src);
        }
    }
    public override void _Draw()
    {
        bool poison = _status.Split(", ").Any(s => s is "1" or "2" or "3");
        bool curse = _status.Split(", ").Contains("4");
        string hp = poison && (!curse || _phase == 0) ? "progress_hp_0_drop" : curse ? "progress_hp_0_slow" : "progress_hp_0";
        Bar(hp, new Rect2(8, 1, 108, 12), _hp);
        Bar("progress_mp_0_curse", new Rect2(8, 11, 108, 7), _mp);
        if (_list.HasMeta("party_selected") && _list.GetMeta("party_selected").AsInt32() == _owner.GetMeta("party_id").AsInt32())
            DrawRect(new Rect2(6, 0, 112, 38), new Color("00ff00"), false, 1);
    }
}

public partial class ClassicPartyBoard : Control
{
    private readonly LayoutNode _art;
    private readonly VBoxContainer _list;
    private readonly Label _details;
    private readonly Button _invite, _private;
    private readonly Button _register, _remove;
    private readonly Label _page;
    private readonly Control _recruit = new();
    private Control? _selected;
    public ClassicPartyBoard(Control body, Control grip, Button close)
    {
        Name = "classic_party_board"; CustomMinimumSize = new Vector2(320, 481); Size = CustomMinimumSize;
        _art = Plugin.Kit.Layout("{nation}_partyboard_us"); TextureFilter = TextureFilterEnum.Nearest;
        ClassicPartySkin.Drag(this, grip, new Rect2(8, 3, 284, 40));
        var title = _art.Strings.Last(n=>n.Text=="Party Window");
        ClassicPartySkin.Caption(this, "Party Window", new Rect2(title.Position,title.SizeVec),title.Color,title.Size).HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var item in new[] { ("ID",14,112), ("Level",131,61), ("Job",196,110) })
            ClassicPartySkin.Caption(this, item.Item1, new Rect2(item.Item2, 52, item.Item3, 22),title.Color,11).HorizontalAlignment = HorizontalAlignment.Center;
        var nodes = CharacterDetailsSkin.Tree(body).ToArray();
        Button Native(string text) => nodes.OfType<Button>().Single(b => b.Text == text);
        ClassicPartySkin.Place(this, close, _art.Find("btn_exit")!, "");
        ClassicPartySkin.Place(this, Native("Refresh"), _art.Find("btn_refresh")!);
        ClassicPartySkin.Place(this, Native("◄ Prev"), _art.Find("btn_page_up")!, "");
        ClassicPartySkin.Place(this, Native("Next ►"), _art.Find("btn_page_down")!, "");
        var register = Native("Look for a party"); _register = register;
        // Keep the server-driven text state on the native control; use the original short caption above it.
        register.Text = "Register"; register.ClipText = true;
        ClassicPartySkin.Place(this, register, _art.Find("btn_add")!);
        var remove = new Button { Text = "Delete", FocusMode = FocusModeEnum.None }; _remove = remove;
        AddChild(remove); ClassicPartySkin.Place(this, remove, _art.Find("btn_delete")!);
        remove.Pressed += () => { if (register.HasMeta("seeking") && register.GetMeta("seeking").AsBool()) register.EmitSignal(BaseButton.SignalName.Pressed); };
        register.TooltipText = "Register or remove your seek-party listing";
        _invite = new Button { Text = "Invite", Disabled = true, FocusMode = FocusModeEnum.None }; AddChild(_invite);
        _private = new Button { Text = "Private", Disabled = true, FocusMode = FocusModeEnum.None }; AddChild(_private);
        ClassicPartySkin.Place(this, _invite, _art.Find("btn_party")!); ClassicPartySkin.Place(this, _private, _art.Find("btn_whisper")!);
        _invite.Pressed += () => Forward("Invite"); _private.Pressed += () => Forward("Private");
        var page = nodes.OfType<Label>().Single(l => l.Text == "Page 1"); _page = page;
        ClassicVendorSkin.Font(page,10); page.HorizontalAlignment = HorizontalAlignment.Center;
        ClassicVendorSkin.Move(page, this, new Rect2(131,383, 62, 28));
        _details = ClassicPartySkin.Caption(this, "Select a player to invite or send a private message.", new Rect2(18, 283, 284, 84), new Color("ffff80"), 11);
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _details.VerticalAlignment = VerticalAlignment.Top;
        _list = nodes.OfType<VBoxContainer>().Single(n => n.Name == "seek_members");
        _list.Reparent(this); _list.Position = new Vector2(14,79); _list.CustomMinimumSize = new Vector2(292,0);
        _list.AddThemeConstantOverride("separation",0); _list.Size = new Vector2(292,180);
        _list.ChildEnteredTree += _ => Callable.From(Refresh).CallDeferred();
        _list.ChildExitingTree += _ => Callable.From(Refresh).CallDeferred();
        // Recruiting is a LibreKO extension, kept inside the unused lower table area.
        var opt = nodes.OfType<OptionButton>().Single(); var msg = nodes.OfType<LineEdit>().Single(); var post = Native("Post");
        _recruit.Position = new Vector2(18,283); _recruit.Size = new Vector2(284,84); _recruit.Visible = false; AddChild(_recruit);
        ClassicPartySkin.Caption(_recruit, "Recruit (party leader only)", new Rect2(0,0,284,18),new Color("ffff80"),11);
        ClassicPartySkin.Caption(_recruit, "Class", new Rect2(0,23,72,18),new Color("ffff80"),10);
        ClassicVendorSkin.Input(msg); ClassicVendorSkin.Font(opt,10); ClassicVendorSkin.Button(post, _art.Find("btn_party"));
        ClassicChatControls.SkinButton(opt, _art.Find("btn_party")!);
        ClassicPartySkin.KarusButtonContrast(opt); ClassicPartySkin.KarusButtonContrast(post);
        var inputStyle=ClassicDesign.InputBox(); inputStyle.SetContentMarginAll(1);
        msg.AddThemeStyleboxOverride("normal", inputStyle); msg.AddThemeStyleboxOverride("focus", inputStyle);
        ClassicVendorSkin.Move(opt,_recruit,new Rect2(76,23,134,18));
        ClassicVendorSkin.Move(msg,_recruit,new Rect2(0,50,210,22));
        ClassicVendorSkin.Move(post,_recruit,new Rect2(218,50,66,22));
        var recruitToggle=new Button { Text="Recruit",FocusMode=FocusModeEnum.None }; AddChild(recruitToggle);
        ClassicPartySkin.Place(this,recruitToggle,_art.Find("btn_party")!);
        recruitToggle.Position=new Vector2(238,387); recruitToggle.Size=new Vector2(68,18);
        recruitToggle.SetMeta("party_expected_rect",new Rect2(new Vector2(238,387),_art.Find("btn_party")!.SizeVec));
        recruitToggle.Pressed+=()=> { _recruit.Visible=!_recruit.Visible; _details.Visible=!_recruit.Visible; };
        Resized += () => Size = CustomMinimumSize;
    }
    public override void _Ready() => Refresh();
    public override void _Process(double delta)
    {
        bool seeking = _register.HasMeta("seeking") && _register.GetMeta("seeking").AsBool();
        _register.Text = "Register"; _register.Disabled = seeking; _remove.Disabled = !seeking;
        if (_page.HasMeta("page_caption")) _page.Text = _page.GetMeta("page_caption").AsString();
    }
    private void Forward(string text)
    {
        if (_selected == null || !GodotObject.IsInstanceValid(_selected) || !_selected.IsInsideTree()) return;
        CharacterDetailsSkin.Tree(_selected).OfType<Button>().Single(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
    }
    private void Refresh()
    {
        if (!IsInsideTree()) return;
        if (_selected != null && (!GodotObject.IsInstanceValid(_selected) || _selected.GetParent() != _list))
        { _selected = null; _invite.Disabled = _private.Disabled = true; _details.Text = "Select a player to invite or send a private message."; }
        if (_selected == null) _details.Text = _list.GetChildren().OfType<Control>().Any(c=>c.HasMeta("seek_name"))
            ? "Select a player to invite or send a private message." : "Nobody is seeking a party right now.";
        foreach (var row in _list.GetChildren().OfType<Control>())
        {
            if (!row.HasMeta("seek_name"))
            {
                if (row is Label empty) empty.Visible = false;
                continue;
            }
            if (row.HasMeta("classic_seek_row")) continue;
            row.SetMeta("classic_seek_row",true);
            foreach (var child in row.GetChildren().OfType<Control>()) child.Visible = false;
            if(row is PanelContainer panel) panel.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
            var content = new Control { CustomMinimumSize = new Vector2(292,18), MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(content); row.CustomMinimumSize = new Vector2(292,18);
            var name = ClassicPartySkin.Caption(content,row.GetMeta("seek_name").AsString(),new Rect2(0,0,115,18),Colors.White,10);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            ClassicPartySkin.Caption(content,row.GetMeta("seek_level").AsString(),new Rect2(118,0,60,18),Colors.White,10).HorizontalAlignment=HorizontalAlignment.Center;
            ClassicPartySkin.Caption(content,row.GetMeta("seek_class").AsString(),new Rect2(182,0,110,18),Colors.White,10);
            row.TooltipText=row.GetMeta("seek_name").AsString()+"\n"+row.GetMeta("seek_detail").AsString();
            row.GuiInput += ev => { if(ev is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left})
            { _selected=row; _invite.Disabled=_private.Disabled=false; _details.Text=row.TooltipText; QueueRedraw(); } };
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        var image=_art.Images.First();
        DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(Vector2.Zero,CustomMinimumSize),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));
        // The server supplies ten rows per page. Use the remainder for selected-player details.
        DrawRect(new Rect2(15,276,290,97),Colors.Black);
        DrawLine(new Vector2(15,276),new Vector2(305,276),new Color("8c7950"),1);
        if(_selected != null && GodotObject.IsInstanceValid(_selected) && _selected.GetParent()==_list)
            DrawRect(new Rect2(_list.Position+_selected.Position,new Vector2(292,18)),new Color("00ff00"),false,1);
    }
}

