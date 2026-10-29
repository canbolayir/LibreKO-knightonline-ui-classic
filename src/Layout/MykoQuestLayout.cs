using Godot;
namespace KnightOnlineUiClassic.Layout;

/// <summary>
/// The MYKO reference client's quest NPC menu (<c>re_quest_npc_menu</c>) and quest viewer (<c>re_quest_viewer</c>),
/// read from the bundled 2.x art (<c>assets/myko</c>, <see cref="LocalAssets"/>). Without both layouts (or one of
/// their textures) the quest NPC dialogue keeps its Classic design. Every rectangle below comes from the layout
/// files, so the composition stays editable there.
/// </summary>
public sealed class MykoQuestLayout
{
    public const string MenuLayout = "re_quest_npc_menu", ViewerLayout = "re_quest_viewer";
    private static MykoQuestLayout? _cached;
    private static (LayoutNode?, LayoutNode?) _cachedFrom;
    private static readonly HashSet<string> _warned = new();

    public LayoutNode Menu { get; }
    public LayoutNode Viewer { get; }

    // Menu (re_quest_npc_menu): head art above the first option row, rows of a fixed pitch, bottom art under the last row.
    public LayoutNode[] MenuHead { get; }
    public LayoutNode MenuTitle, MenuTalk, MenuScroll, MenuClose, MenuBottom;
    public LayoutNode RowStrip, RowPlate, RowButton, RowCaption;
    public int MenuWidth { get; }
    public int FirstRow { get; }
    public int RowPitch { get; }
    public int MaxRows { get; }
    /// <summary>Offset of the first option row group; row parts are placed relative to it.</summary>
    private readonly int _rowGroupY;

    // Viewer (re_quest_viewer): head art and note box, section captions, two-cell rows, pager strip and buttons.
    public LayoutNode[] ViewerHead { get; }
    public LayoutNode ViewerTitle, ViewerNote, ViewerScroll, ViewerClose;
    public LayoutNode FirstCaption, Caption, CellRow;
    public LayoutNode LeftCell, RightCell, CellIcon, CellFirst, CellSecond, CellCount;
    public LayoutNode PagerStrip, Prev, Next, PageText, ViewerBottom;
    public LayoutNode[] ActionFrames { get; }
    public LayoutNode LeftAction, RightAction, CloseAction;
    public int ViewerWidth { get; }
    /// <summary>Top of the first section caption (bottom of the note box art).</summary>
    public int ContentTop { get; }
    /// <summary>The original room between the note box and the pager strip (three sections of three rows).</summary>
    public int MaxContent { get; }
    /// <summary>Height of the pager strip plus the bottom art with the two action buttons.</summary>
    public int ViewerFooter { get; }

    /// <summary>The local quest windows, or null when either layout (or a texture or node it needs) is missing.</summary>
    public static MykoQuestLayout? Current
    {
        get
        {
            var local = Plugin.Kit.Local;
            var from = (local.Layout(MenuLayout), local.Layout(ViewerLayout));
            if (from.Item1 == null || from.Item2 == null) return null;
            if (_cached != null && ReferenceEquals(_cachedFrom.Item1, from.Item1) && ReferenceEquals(_cachedFrom.Item2, from.Item2)) return _cached;
            _cachedFrom = from;
            try { _cached = new MykoQuestLayout(from.Item1, from.Item2); }
            catch (InvalidOperationException e)
            {
                _cached = null;
                if (_warned.Add(e.Message)) Plugin.Kit.Log.Warn($"local quest layouts ignored: {e.Message}");
            }
            return _cached;
        }
    }

    private MykoQuestLayout(LayoutNode menu, LayoutNode viewer)
    {
        Menu = menu; Viewer = viewer;
        LayoutNode Need(LayoutNode root, string id) => root.Find(id) ?? throw new InvalidOperationException($"{(root == menu ? MenuLayout : ViewerLayout)} has no {id}");
        LayoutNode Child(LayoutNode parent, Func<LayoutNode, bool> match, string what) =>
            parent.Children.FirstOrDefault(match) ?? throw new InvalidOperationException($"{parent.Id} has no {what}");

        var rows = menu.Children.Where(n => n.Id.StartsWith("group_menu_")).OrderBy(n => n.Y).ToArray();
        if (rows.Length < 2) throw new InvalidOperationException($"{MenuLayout} needs two option rows");
        _rowGroupY = rows[0].Y; FirstRow = rows[0].Y; RowPitch = rows[1].Y - rows[0].Y; MaxRows = rows.Length;
        RowStrip = Need(rows[0], "img_menu"); RowPlate = Need(rows[0], "img_button_menu");
        RowButton = Child(rows[0], n => n.IsButton, "option button"); RowCaption = Child(RowButton, n => n.IsString, "option caption");
        MenuBottom = Need(menu, "img_bottom");
        MenuTitle = Need(menu, "txt_title"); MenuTalk = Need(menu, "txt_talk"); MenuScroll = Need(menu, "scroll"); MenuClose = Need(menu, "btn_close");
        MenuHead = menu.Children.Where(n => n.IsImage && n.Texture != null && n != MenuBottom && n.Y + n.H <= FirstRow).ToArray();
        MenuWidth = MenuHead.Max(n => n.X + n.W);

        ViewerTitle = Need(viewer, "txt_title"); ViewerNote = Need(viewer, "txt_note"); ViewerScroll = Need(viewer, "scroll"); ViewerClose = Need(viewer, "btn_hide");
        FirstCaption = Need(viewer, "group_middle_0"); Caption = Need(viewer, "group_middle_1"); CellRow = Need(viewer, "group_0");
        LeftCell = Need(viewer, "btn_needs_0"); RightCell = Need(viewer, "btn_needs_1"); CellIcon = Need(viewer, "area_needs_0");
        CellFirst = Need(viewer, "txt_needs_first_0"); CellSecond = Need(viewer, "txt_needs_second_0"); CellCount = Need(viewer, "txt_needs_count_0");
        var bottom = Need(viewer, "group_bottom");
        Prev = Need(bottom, "btn_prev"); Next = Need(bottom, "btn_next"); PageText = Need(bottom, "text_page");
        LeftAction = Need(bottom, "btn_accept"); RightAction = Need(bottom, "btn_giveup"); CloseAction = Need(bottom, "btn_close");
        var art = bottom.Children.Where(n => n.IsImage && n.Texture != null).ToArray();
        PagerStrip = art.FirstOrDefault(n => n.Y == bottom.Y) ?? throw new InvalidOperationException("group_bottom has no pager strip");
        ViewerBottom = art.Where(n => n.Y > bottom.Y && n.W > 300).OrderBy(n => n.Y).FirstOrDefault() ?? throw new InvalidOperationException("group_bottom has no bottom art");
        ActionFrames = art.Where(n => n != PagerStrip && n != ViewerBottom).OrderBy(n => n.X).ToArray();
        ContentTop = FirstCaption.Y;
        ViewerHead = viewer.Children.Where(n => n.IsImage && n.Texture != null && n.Y < ContentTop).ToArray();
        ViewerWidth = viewer.W;
        MaxContent = bottom.Y - ContentTop;
        ViewerFooter = ViewerBottom.Y + ViewerBottom.H - bottom.Y;
    }

    // ---- Menu geometry -------------------------------------------------------------------------------------

    /// <summary>Window height for <paramref name="rows"/> option rows (the client cuts the window to its options).</summary>
    public int MenuHeight(int rows, bool pager) => FirstRow + rows * RowPitch + (pager ? PagerStrip.H : 0) + MenuBottom.H;
    public Rect2 RowRect(int index) => new(0, FirstRow + index * RowPitch, MenuWidth, RowPitch);
    /// <summary>A part of option row <paramref name="index"/> (strip, plate, button) at its place in the window.</summary>
    public Rect2 RowPart(LayoutNode part, int index) => new(part.X, part.Y - _rowGroupY + FirstRow + index * RowPitch, part.W, part.H);

    // ---- Viewer geometry -----------------------------------------------------------------------------------

    public int CaptionHeight(bool first) => first ? CellRow.Y - FirstCaption.Y : Caption.H;
    public int CellRowHeight => CellRow.H;
    public int ViewerHeight(int content) => ContentTop + content + ViewerFooter;
    /// <summary>A cell part (icon, texts) relative to its cell.</summary>
    public Rect2 InCell(LayoutNode part) => new(part.X - LeftCell.X, part.Y - LeftCell.Y, part.W, part.H);
    /// <summary>A footer part relative to the top of the pager strip.</summary>
    public Rect2 InFooter(LayoutNode part) => new(part.X, part.Y - PagerStrip.Y, part.W, part.H);

    // ---- Art -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Share of the art's own alpha that the dark translucent ground (window body, text boxes, cells) keeps. On the
    /// MYKO client the world shows through these windows more than the textures' alpha (187 body, 102 cells) gives:
    /// the owner's screenshots put the body at about 40 % see-through (alpha ~150) and the cells about a quarter darker than
    /// the body around them (alpha ~82). Opaque art and lighter edges keep their alpha.
    /// </summary>
    public const float GroundAlpha = .8f;
    /// <summary>Translucent pixels at most this bright (0..255 per channel) belong to the dark ground.</summary>
    private const int GroundLevel = 17;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, Texture2D> _ground = new();

    /// <summary>The image's texture with its dark translucent ground thinned to <see cref="GroundAlpha"/>.</summary>
    public static Texture2D? Texture(LayoutNode image) =>
        image.Texture == null || Plugin.Kit.Local.Image(image.Texture) is not { } source ? null : _ground.GetValue(source, Ground);

    internal static void Clear()
    {
        foreach (var (_, texture) in _ground) texture.Dispose();
        _ground.Clear();
    }

    private static Texture2D Ground(Image source)
    {
        using var image = (Image)source.Duplicate();
        if (image.IsCompressed()) image.Decompress();
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        byte[] data = image.GetData();
        for (int i = 0; i + 3 < data.Length; i += 4)
            if (data[i + 3] is > 0 and < 255 && data[i] <= GroundLevel && data[i + 1] <= GroundLevel && data[i + 2] <= GroundLevel)
                data[i + 3] = (byte)Math.Round(data[i + 3] * GroundAlpha);
        using var ground = Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data);
        return ImageTexture.CreateFromImage(ground);
    }

    public static void Draw(CanvasItem canvas, LayoutNode image, Rect2 rect, Color? modulate = null)
    {
        if (Texture(image) is { } texture)
            canvas.DrawTextureRectRegion(texture, rect, new Rect2(image.SrcX, image.SrcY, image.SrcW, image.SrcH), modulate ?? Colors.White);
    }

    public static void Draw(CanvasItem canvas, LayoutNode image, float offsetY = 0) => Draw(canvas, image, new Rect2(image.X, image.Y + offsetY, image.W, image.H));

    /// <summary>
    /// Caption ink of the viewer's action buttons (Accept, Reject, Confirm, Abandon, Close): the neutral ink of the
    /// menu's stone buttons. The layout's green and red action inks are not used.
    /// </summary>
    public Color ActionInk => new(RowCaption.Color, 1);

    /// <summary>The state image of an original button: 0 normal, 1 pressed, 2 hover, 3 disabled.</summary>
    public static LayoutNode? State(LayoutNode button, int state) =>
        button.Images.FirstOrDefault(n => n.Tag == state && n.Texture != null) ?? button.Images.FirstOrDefault(n => n.Tag == 0 && n.Texture != null);

    /// <summary>Applies an original string's ink: Arial (Bold when the string is bold) at the string's size and colour.</summary>
    public static void Ink(Control control, LayoutNode text, Color? color = null)
    {
        control.AddThemeFontOverride("font", text.Bold ? Plugin.Kit.Bold : Plugin.Kit.Regular);
        control.AddThemeFontSizeOverride("font_size", UiKit.FontSize(text));
        control.AddThemeColorOverride("font_color", color ?? new Color(text.Color, 1));
        control.AddThemeConstantOverride("outline_size", 0);
        control.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        control.AddThemeConstantOverride("shadow_offset_x", 1); control.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    public static Label Text(LayoutNode text, string value = "", Color? color = null)
    {
        var label = new Label { Text = value, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        label.HorizontalAlignment = (text.Style & TextStyle.AlignCenter) != 0 ? HorizontalAlignment.Center
            : (text.Style & TextStyle.AlignRight) != 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Ink(label, text, color);
        return label;
    }
}
