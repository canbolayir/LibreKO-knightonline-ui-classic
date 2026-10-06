using Godot;

namespace KnightOnlineUiClassic.Layout;

public partial class LayoutView : Control
{
    private const int MinFittedFontSize = 7;
    private const string Ellipsis = "…";
    private const char FirstCjkCodePoint = '⺀';
    private const int FitInset = 4;

    public LayoutNode Root { get; }
    public Control? DragHandle { get; private set; }

    private readonly UiKit _kit;
    private readonly List<(LayoutNode Node, Control Control)> _built = new();
    private readonly Dictionary<LayoutNode, Control> _byNode = new();

    public LayoutView(UiKit kit, LayoutNode root)
    {
        _kit = kit;
        Root = root;
        Name = root.Id.Length > 0 ? root.Id : "layout";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = root.SizeVec;
        Size = root.SizeVec;

        if (root.HasDrag)
        {
            DragHandle = new Control
            {
                Name = "drag",
                Position = new Vector2(root.DragX - root.X, root.DragY - root.Y),
                Size = new Vector2(root.DragW, root.DragH),
                MouseFilter = MouseFilterEnum.Stop,
            };
            AddChild(DragHandle);
        }

        foreach (var child in root.Children)
            Build(child, this, root.X, root.Y);
    }

    public Control? Get(string id)
    {
        foreach (var (n, c) in _built)
            if (n.Id == id) return c;
        return null;
    }

    public T? Get<T>(string id) where T : Control => Get(id) as T;

    public Control? Get(LayoutNode scope, string id) => scope.Find(id) is { } n ? ControlOf(n) : null;

    public T? Get<T>(LayoutNode scope, string id) where T : Control => Get(scope, id) as T;

    public Control? ControlOf(LayoutNode node) => _byNode.TryGetValue(node, out var c) ? c : null;

    public IEnumerable<(LayoutNode Node, Control Control)> Where(Func<LayoutNode, bool> pick)
    {
        foreach (var pair in _built)
            if (pick(pair.Node)) yield return pair;
    }

    public void ApplyDeclaredBounds()
    {
        // Read final theme/font metrics before committing the native pixel rectangles.
        foreach(var (node,control) in _built)
        {
            control.UpdateMinimumSize();
            _ = control.GetCombinedMinimumSize();
            control.Size=node.SizeVec;
        }
    }

    public IEnumerable<(LayoutNode Node, Control Control)> Areas(int areaType) =>
        Where(n => n.IsArea && n.AreaType == areaType);

    public void Hide(params string[] ids)
    {
        foreach (var id in ids)
            foreach (var (n, c) in _built)
                if (n.Id == id) c.Visible = false;
    }

    public void Hide(LayoutNode scope, params string[] ids)
    {
        foreach (var id in ids)
            if (Get(scope, id) is { } c) c.Visible = false;
    }

    public void FadeOut(params string[] ids)
    {
        foreach (var id in ids)
            foreach (var (n, c) in _built)
                if (n.Id == id) c.SelfModulate = Colors.Transparent;
    }

    public void SetText(string id, string text)
    {
        if (Get(id) is Label l) l.Text = text;
    }

    public void SetClippedText(string id, string text)
    {
        foreach (var (n, c) in _built)
        {
            if (n.Id != id || c is not Label l) continue;
            l.AutowrapMode = TextServer.AutowrapMode.Off;
            l.ClipText = true;
            int size = UiKit.FontSize(n);
            var font = _kit.FontFor(n);
            string shown = text;
            while (shown.Length > 1 && font.GetStringSize(shown + Ellipsis, HorizontalAlignment.Left, -1, size).X > n.W)
                shown = shown[..^1];
            l.Text = shown.Length < text.Length ? shown.TrimEnd() + Ellipsis : text;
            l.Size = n.SizeVec;
        }
    }

    public void OnPressed(LayoutNode scope, string id, Action action)
    {
        if (Get(scope, id) is BaseButton b) b.Pressed += action;
    }

    public void SetTextAll(string id, string text)
    {
        foreach (var (n, c) in _built)
            if (n.Id == id && c is Label l) l.Text = text;
    }

    public void SetFittedText(string id, string text)
    {
        foreach (var (n, c) in _built)
            if (n.Id == id && c is Label l)
                Fit(l, n, text);
    }

    public void CentreOnRect(Label label, LayoutNode node)
    {
        float height = label.GetCombinedMinimumSize().Y;
        if (height <= node.H) return;
        label.Position = new Vector2(label.Position.X, label.Position.Y - (height - node.H) * 0.5f);
    }

    public void SetProgress(string id, float fraction)
    {
        if (Get(id) is TextureProgressBar p) p.Value = Mathf.Clamp(fraction, 0f, 1f);
    }

    public void OnPressed(string id, Action action)
    {
        if (Get(id) is BaseButton b) b.Pressed += action;
    }

    public Label? Caption(string buttonId, string text, int fontSize, Color color)
    {
        if (Get(buttonId) is not { } button) return null;
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeFontOverride("font", _kit.Regular);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        button.AddChild(label);
        return label;
    }

    public Control MakeDragHandle(Rect2 localRect)
    {
        DragHandle = new Control
        {
            Name = "drag",
            Position = localRect.Position,
            Size = localRect.Size,
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(DragHandle);
        MoveChild(DragHandle, 0);
        return DragHandle;
    }

    private void Build(LayoutNode node, Control parent, int ox, int oy)
    {
        Control c = node.Type switch
        {
            "classic_surface" => new ClassicReportSurface(),
            "classic_header" => new ClassicReportHeader(),
            "classic_detail_header" => new ClassicReportHeader(false),
            "quest_frame_component" => new ClassicFrame { BackgroundColor=Colors.Black,BackgroundAlpha=1 },
            "npc_quest_frame_component" => new ClassicNpcQuestChrome(),
            "classic_section" => new ClassicReportSection(),
            "classic_rule" => new ClassicReportSurface(true),
            "inventory_backing" => new ColorRect {Color=Colors.Black,MouseFilter=MouseFilterEnum.Ignore},
            "inventory_frame_component" => new Windows.InventoryDrawerFrame(),
            "inventory_corner_button" => new Windows.InventoryCornerButton(),
            "classic_button" or "classic_tab" => DesignedButton(node),
            "classic_select" => DesignedSelect(node),
            "classic_input" => DesignedInput(),
            "image" => Image(node),
            "string" => Text(node),
            "button" => Button(node),
            "progress" or "gradualprogress" => Progress(node),
            "area" => Area(node),
            "edit" => Edit(node),
            "flash" => new Control { MouseFilter = MouseFilterEnum.Ignore },
            _ => Group(node),
        };
        c.Name = node.Id.Length > 0 ? node.Id : node.Type;
        c.Position = new Vector2(node.X - ox, node.Y - oy);
        c.Size = node.SizeVec;
        if (node.Tooltip.Length > 0) c.TooltipText = node.Tooltip;
        parent.AddChild(c);
        _built.Add((node, c));
        _byNode[node] = c;

        if (node.Type is "base" or "static" or "list" or "scrollbar" or "trackbar" or "tree" or "node" or "image")
            foreach (var child in node.Children)
                Build(child, c, node.X, node.Y);
    }

    private Control Group(LayoutNode node) => new() { MouseFilter = MouseFilterEnum.Ignore };

    private static Control DesignedButton(LayoutNode node)
    {
        var button = new ClassicReportButton { Text = node.Text, FocusMode = FocusModeEnum.None,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        ClassicReportDesign.StyleButton(button, node.Type == "classic_tab", node);
        return button;
    }

    private static Control DesignedSelect(LayoutNode node)
    {
        var button = new OptionButton { FocusMode = FocusModeEnum.None, ClipText=true,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis };
        ClassicReportDesign.StyleButton(button, false);
        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeColorOverride("font_color",new Color(ClassicDesign.Karus?"201c18":"f7de88"));
        var arrow=Plugin.Kit.Layout("{nation}_page_friends_us").All().First(n=>n.Id=="btn_page_down").Images.First(n=>n.Tag==0);
        button.AddThemeIconOverride("arrow",new AtlasTexture { Atlas=Plugin.Kit.Texture(arrow.Texture!),
            Region=new Rect2(arrow.SrcX+5,arrow.SrcY+3,arrow.SrcW-10,arrow.SrcH-6),FilterClip=true });
        button.AddThemeConstantOverride("modulate_arrow",0);
        button.AddThemeConstantOverride("arrow_margin",2);
        return button;
    }

    private static Control DesignedInput()
    {
        var input = new LineEdit();
        input.AddThemeFontOverride("font", Plugin.Kit.Regular);
        input.AddThemeFontSizeOverride("font_size", 12);
        input.AddThemeColorOverride("font_color", ClassicDesign.Text);
        var box = ClassicDesign.InputBox();
        box.SetContentMargin(Side.Top, 1); box.SetContentMargin(Side.Bottom, 1);
        input.AddThemeStyleboxOverride("normal", box);
        input.AddThemeStyleboxOverride("focus", box);
        return input;
    }

    private Control Area(LayoutNode node) => new() { MouseFilter = MouseFilterEnum.Ignore };

    public AtlasTexture? Atlas(LayoutNode image)
    {
        if (image.Texture == null || image.SrcW <= 0 || image.SrcH <= 0) return null;
        var tex = _kit.Texture(image.Texture);
        if (tex == null) return null;
        return new AtlasTexture
        {
            Atlas = tex,
            Region = new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH),
            FilterClip = true,
        };
    }

    private Control Image(LayoutNode node)
    {
        var atlas = Atlas(node);
        if (atlas == null) return new Control { MouseFilter = MouseFilterEnum.Ignore };
        var rect = new TextureRect
        {
            Texture = atlas,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            FlipH = node.FlipH,
            FlipV = node.FlipV,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if ((node.Style & ImageStyle.Animated) != 0 && node.Images.Any())
            AnimatedImage.Attach(rect, node.Images.OrderBy(i => i.Tag).Select(Atlas).ToList(), node.Fps);
        return rect;
    }

    private Control Text(LayoutNode node)
    {
        var label = new Label
        {
            Text = node.Text,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = (node.Style & TextStyle.AlignCenter) != 0 ? HorizontalAlignment.Center
                : (node.Style & TextStyle.AlignRight) != 0 ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            VerticalAlignment = (node.Style & TextStyle.AlignVCenter) != 0 ? VerticalAlignment.Center
                : (node.Style & TextStyle.AlignBottom) != 0 ? VerticalAlignment.Bottom
                : VerticalAlignment.Top,
            AutowrapMode = (node.Style & TextStyle.SingleLine) != 0 ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart,
            ClipText = false,
        };
        Style(label, node);
        return label;
    }

    public void Style(Label label, LayoutNode node)
    {
        label.AddThemeFontSizeOverride("font_size", UiKit.FontSize(node));
        label.AddThemeFontOverride("font", _kit.FontFor(node));
        label.AddThemeColorOverride("font_color", node.Color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        if (node.LineSpacing != 0) label.AddThemeConstantOverride("line_spacing", node.LineSpacing);
    }

    private Control Button(LayoutNode node)
    {
        var button = new TextureButton
        {
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.None,
        };
        foreach (var img in node.Images)
        {
            var atlas = Atlas(img);
            if (atlas == null) continue;
            switch (img.Tag)
            {
                case ButtonState.Normal: button.TextureNormal = atlas; break;
                case ButtonState.Pressed: button.TexturePressed = atlas; break;
                case ButtonState.Hover: button.TextureHover = atlas; break;
                case ButtonState.Disabled: button.TextureDisabled = atlas; break;
            }
        }
        var stateCaptions = new Dictionary<int, LayoutNode>();
        foreach (var img in node.Images)
            if (!stateCaptions.ContainsKey(img.Tag) && img.Strings.FirstOrDefault(s => IsLatin(s.Text)) is { } s)
                stateCaptions[img.Tag] = s;
        var captions = node.Strings.ToList();
        bool fromStates = captions.Count == 0 && stateCaptions.Count > 0;
        if (fromStates)
            captions.Add(stateCaptions.TryGetValue(ButtonState.Normal, out var normal) ? normal : stateCaptions.Values.First());
        foreach (var text in captions)
        {
            var caption = (Label)Text(text);
            caption.Name = text.Id.Length > 0 ? text.Id : "caption";
            if (fromStates)
            {
                caption.Position = new Vector2(0, text.Y - node.Y);
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                Fit(caption, text, text.Text, node.W);
                ColourByState(button, caption, stateCaptions);
            }
            else
            {
                caption.Position = new Vector2(text.X - node.X, text.Y - node.Y);
                caption.Size = text.SizeVec;
            }
            button.AddChild(caption);
            _built.Add((text, caption));
            _byNode[text] = caption;
        }
        return button;
    }

    private static void ColourByState(BaseButton button, Label caption, Dictionary<int, LayoutNode> states)
    {
        var normal = states.TryGetValue(ButtonState.Normal, out var n) ? n.Color : caption.GetThemeColor("font_color");
        var pressed = states.TryGetValue(ButtonState.Pressed, out var p) ? p.Color : normal;
        var hover = states.TryGetValue(ButtonState.Hover, out var h) ? h.Color : normal;
        bool hovering = false;
        void Apply() => caption.AddThemeColorOverride("font_color", button.ButtonPressed ? pressed : hovering ? hover : normal);
        button.Toggled += _ => Apply();
        button.MouseEntered += () => { hovering = true; Apply(); };
        button.MouseExited += () => { hovering = false; Apply(); };
        Apply();
    }

    private void Fit(Label label, LayoutNode node, string text, int? width = null)
    {
        int w = width ?? node.W;
        label.Text = text;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ClipText = true;
        int size = UiKit.FontSize(node);
        var font = _kit.FontFor(node);
        while (size > MinFittedFontSize && font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > w - FitInset)
            size--;
        label.AddThemeFontSizeOverride("font_size", size);
        label.Size = new Vector2(w, node.H);
    }

    private static bool IsLatin(string text) => text.Length > 0 && text.All(ch => ch < FirstCjkCodePoint);

    private Control Progress(LayoutNode node)
    {
        var bar = new TextureProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0,
            Value = 1,
            NinePatchStretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
            FillMode = (node.Style & ProgressStyle.RightToLeft) != 0 ? (int)TextureProgressBar.FillModeEnum.RightToLeft
                : (node.Style & ProgressStyle.TopToBottom) != 0 ? (int)TextureProgressBar.FillModeEnum.TopToBottom
                : (node.Style & ProgressStyle.BottomToTop) != 0 ? (int)TextureProgressBar.FillModeEnum.BottomToTop
                : (int)TextureProgressBar.FillModeEnum.LeftToRight,
        };
        foreach (var img in node.Images)
        {
            var atlas = Atlas(img);
            if (atlas == null) continue;
            if (img.Tag == ProgressPart.Background) bar.TextureUnder = atlas;
            else if (img.Tag == ProgressPart.Foreground) bar.TextureProgress = atlas;
        }
        return bar;
    }

    private Control Edit(LayoutNode node)
    {
        var edit = new LineEdit { MouseFilter = MouseFilterEnum.Stop, FocusMode = FocusModeEnum.Click };
        foreach (var state in new[] { "normal", "focus", "read_only" })
            edit.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        var text = node.Strings.FirstOrDefault();
        if (text != null)
        {
            edit.AddThemeFontSizeOverride("font_size", UiKit.FontSize(text));
            edit.AddThemeColorOverride("font_color", text.Color);
        }
        foreach (var img in node.Images)
        {
            if (Image(img) is not TextureRect back) continue;
            back.Position = new Vector2(img.X - node.X, img.Y - node.Y);
            back.Size = img.SizeVec;
            back.ShowBehindParent = true;
            edit.AddChild(back);
        }
        return edit;
    }
}

public partial class AnimatedImage : Node
{
    private readonly TextureRect _target;
    private readonly List<AtlasTexture?> _frames;
    private readonly double _frameTime;
    private double _clock;
    private int _frame;

    private AnimatedImage(TextureRect target, List<AtlasTexture?> frames, float fps)
    {
        _target = target;
        _frames = frames;
        _frameTime = fps > 0 ? 1.0 / fps : 1.0 / 30.0;
    }

    public static void Attach(TextureRect target, List<AtlasTexture?> frames, float fps)
    {
        if (frames.Count == 0) return;
        target.AddChild(new AnimatedImage(target, frames, fps));
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < _frameTime) return;
        _clock -= _frameTime;
        _frame = (_frame + 1) % _frames.Count;
        if (_frames[_frame] is { } tex) _target.Texture = tex;
    }
}
