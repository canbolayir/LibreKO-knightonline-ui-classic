using Godot;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Declarative pages sharing one integer grid, independent of live data and actions.</summary>
public static class CharacterLayout
{
    public const int Width = 360, Height = 550, Left = 8, Inner = Width - Left * 2;
    public const int ListTop = 166, ListRows = 11;
    public const int ListHeight = ListRows * ClassicDesign.RowHeight;
    public const int PagerY = 426, ActionY = 454, FooterY = 482;

    private static LayoutNode Root(string id) => new() { Type = "base", Id = id, W = Width, H = Height };
    private static LayoutNode Node(string type, string id, int x, int y, int w, int h) =>
        new() { Type = type, Id = id, X = x, Y = y, W = w, H = h };
    private static void Text(LayoutNode root, string id, string text, int x, int y, int w, int h = 22, bool heading = false, bool center = false)
    {
        var n = Node("string", id, x, y, w, h);
        n.Text = text; n.Font = "Arial"; n.Size = id.Equals("text_Id",StringComparison.OrdinalIgnoreCase)?11:10;
        n.Bold = heading || id.StartsWith("Text_",StringComparison.OrdinalIgnoreCase);
        n.Color = heading ? ClassicReportDesign.Caption : ClassicReportDesign.Value;
        if(text is "Hit Point" or "Mana Point" or "Experience Points" or "National Point") n.Color=ClassicDesign.Karus?Colors.White:new Color("ffff80");
        if(text is "Stat Point" or "Resistance" || id=="Text_BonusPoint") n.Color=ClassicReportDesign.Accent;
        if(id=="legend_online") n.Color=new Color("8ff099");
        if(id=="legend_party") n.Color=new Color("ff9292");
        if(id=="legend_offline") n.Color=new Color("bbbbbb");
        n.Style = TextStyle.SingleLine | TextStyle.AlignVCenter | (center ? TextStyle.AlignCenter : TextStyle.AlignLeft);
        root.Children.Add(n);
    }
    private static void Rule(LayoutNode root, int y) => root.Children.Add(Node("classic_rule", "", 3, y, Width - 5, 2));
    private static void Divider(LayoutNode root, int x, int y, int height) => root.Children.Add(Node("classic_rule", "", x, y, 2, height));
    private static void Button(LayoutNode root, string id, string text, int x, int y, int w, int h = 18, bool tab = false)
    {
        var n = Node(tab ? "classic_tab" : "classic_button", id, x, y, w, h);
        n.Text=text; n.Size=tab?10:9; n.Bold=true; n.Font="Arial";
        LayoutNode? source=null;
        if(tab) source=Plugin.Kit.Layout("{nation}_various_frame_us").All().First(b=>b.IsButton && b.Id==id);
        else if(id.StartsWith("Btn_",StringComparison.OrdinalIgnoreCase) && root.Id=="page_character")
            source=Plugin.Kit.Layout("{nation}_page_state_us").All().FirstOrDefault(b=>b.IsButton && b.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
        if(text is "<" or ">") source=Plugin.Kit.Layout("{nation}_page_quest_us").All().First(b=>b.IsButton && b.Id==(text=="<"?"btn_page_down":"btn_page_up"));
        if(source!=null)
        {
            foreach(var image in source.Images) n.Children.Add(CopyImage(image,0,0));
            if(!tab) { n.Text=""; n.H=source.Images.First().SrcH; }
        }
        root.Children.Add(n);
    }
    private static void Pair(LayoutNode root, string caption, string id, int x, int y, int w = 168, int h = 20)
    {
        Text(root, "", caption, x + 6, y, 66, h, heading: true);
        Text(root, id, "", x + 76, y, w - 82, h);
    }
    private static void Full(LayoutNode root, string caption, string id, int y, int h = 20)
    {
        Text(root, "", caption, Left + 4, y, 124, h, heading: true);
        Text(root, id, "", Left + 130, y, Inner - 136, h);
    }
    private static void ActionRow(LayoutNode root, int y, params (string Id, string Text)[] actions)
    {
        const int gap = 4;
        int width = (Inner - gap * (actions.Length - 1)) / actions.Length;
        for (int i = 0; i < actions.Length; i++)
            Button(root, actions[i].Id, actions[i].Text, Left + i * (width + gap), y,
                i == actions.Length - 1 ? Width - Left - (Left + i * (width + gap)) : width);
    }
    private static void Pager(LayoutNode root, string previous, string next, string count)
    {
        Button(root, previous, "<", 122, PagerY, 32);
        Text(root, count, "1", 156, PagerY, 48, 18, center: true);
        Button(root, next, ">", 206, PagerY, 32);
    }
    private static void Table(LayoutNode root, int top, int rows, params (string Name, int Width)[] columns)
    {
        int x = Left;
        // The inset owns its perimeter. Separators end inside it and cannot protrude.
        root.Children.Add(Node("classic_section","table_frame",Left,top-28,Inner,rows*ClassicDesign.RowHeight+31));
        for (int i = 0; i < columns.Length; i++)
        {
            Text(root, "", columns[i].Name, x + 5, top - 24, columns[i].Width - 10, 22, heading: true);
            root.Children.Add(Node("list", "column_" + i, x, top, columns[i].Width, rows * ClassicDesign.RowHeight));
            if (i > 0) Divider(root, x, top - 24, rows * ClassicDesign.RowHeight + 24);
            x += columns[i].Width;
        }
        root.Children.Add(Node("classic_rule", "table_header_rule", Left + 4, top - 2, Inner - 8, 2));
    }

    public static LayoutNode Frame()
    {
        var root = Root("character_frame");
        int tabY = ClassicDesign.Karus ? 44 : 48;
        root.Children.Add(Node("classic_surface", "surface", 0, tabY, Width, Height - tabY));
        root.Children.Add(Node("classic_header", "header_rails", 0, tabY - 4, Width, 42));
        var source = Plugin.Kit.Layout("{nation}_various_frame_us");
        var close = source.All().First(n => n.IsButton && n.Id == "btn_close");
        var emblem = source.Children.First(n => n.IsImage && n.Y < 20);
        // The ornament is outside the rectangular panel. Keep its source/display transform intact.
        root.Children.Add(new LayoutNode { Type = "image", X = emblem.X, Y = emblem.Y,
            W = ClassicDesign.Karus ? 53 : 44, H = ClassicDesign.Karus ? 39 : 42,
            Texture = emblem.Texture, SrcX = emblem.SrcX, SrcY = emblem.SrcY,
            SrcW = ClassicDesign.Karus ? 53 : 44, SrcH = ClassicDesign.Karus ? 39 : 42 });
        root.Children.Add(CopyImage(close, 0, 0));
        foreach (var (id, caption, i) in new[] { ("btn_state", "Character\nReport", 0), ("btn_quest", "Quest", 1),
                     ("btn_clan", "Clan", 2), ("btn_knights", "Knights", 2), ("btn_friends", "Friend", 3) })
            Button(root, id, caption, 6 + i * 87, tabY, i == 3 ? 89 : 85, 36, true);
        return root;
    }

    public static LayoutNode Page(string key)
    {
        var root = Root("page_" + key);
        if (key == "character") { State(root); return root; }
        if (key == "friends")
        {
            Text(root, "text_friend", "Friend", 12, 88, 336, 20, heading: true, center: true);
            Table(root, 136, 12, ("Character", Inner));
            Pager(root, "btn_page_up", "btn_page_down", "Text_Page");
            Text(root, "legend_online", "Online", 12, 404, 108, 18);
            Text(root, "legend_party", "In party", 124, 404, 108, 18, center: true);
            Text(root, "legend_offline", "Offline", 236, 404, 112, 18, center: true);
            ActionRow(root, ActionY, ("btn_refresh", "Refresh"), ("btn_delete", "Delete"), ("btn_whisper", "Private"), ("btn_party", "Party"));
            Button(root, "btn_add", "Register", 270, FooterY, 82, 22);
            root.Children.Add(Node("classic_input", "friend_name", Left, FooterY, 256, 22));
            Text(root, "page_hint", "", 14, 508, 332, 18);
        }
        else if (key == "quest")
        {
            Text(root, "", "Quest", 12, 88, 336, heading: true, center: true);
            Table(root, ListTop, ListRows, ("Quest", 192), ("Status", 94), ("Time", 58));
            Pager(root, "btn_page_down", "btn_page_up", "text_page");
            root.Children.Add(Node("classic_select", "quest_filter", 8, 116, 168, 20));
            root.Children.Add(Node("classic_select", "quest_kind", 184, 116, 168, 20));
            ActionRow(root, ActionY, ("btn_details", "Details"), ("btn_track", "Track"), ("btn_abandon", "Abandon"), ("btn_complete", "Turn in"));
            Text(root, "page_hint", "Double-click to view objectives and rewards.", 12, FooterY, 336, 18);
        }
        else
        {
            bool union = key.StartsWith("union");
            Text(root, "Text_clansName", "", 84, 92, 260, heading: true);
            Text(root, "Text_clan_Duty", "", 84, 118, 160);
            if (!union) Text(root, "Text_clan_MemberCount", "", 252, 118, 92);
            Text(root, "page_hint", "", 84, 144, 260, 18);
            root.Children.Add(Node("classic_section","grade_frame",12,92,64,64));
            Text(root,"grade_number","",14,99,60,33,heading:true,center:true);
            Text(root,"grade_caption","Grade",14,135,60,18,center:true);
            foreach (var grade in Plugin.Kit.Layout("{nation}_page_clan_us").Children.Where(n => n.IsImage && n.Id.Contains("grade")))
                root.Children.Add(CopyImage(grade, 12 - grade.X, 92 - grade.Y));
            Table(root, 198, 10, union ? new[] { ("Clan", 246), ("Role", 98) } : new[] { ("Duty", 58), ("Character", 158), ("Level", 44), ("Class", 84) });
            if (union)
            {
                Pager(root, "btn_pagedown", "btn_pageup", "string_page");
                ActionRow(root, ActionY, ("btn_knights", "Knights"), ("btn_refresh", "Refresh"), ("btn_knights_admit", "Invite"), ("Btn_Remove", "Remove"));
                ActionRow(root, FooterY, ("Btn_knights_chat", "Clan chat"), ("btn_union_chat", "Union chat"), ("btn_management", "Manage"), ("btn_leave", "Leave"));
            }
            else
            {
                Pager(root, "btn_clan_down", "btn_clan_up", "Text_clan_Page");
                ActionRow(root, ActionY, ("btn_clan_refresh", "Refresh"), ("btn_clan_admit", "Invite"), ("btn_clan_party", "Party"), ("btn_clan_whisper", "Private"));
                ActionRow(root, FooterY, ("btn_management", "Manage"), ("btn_contribution", "Contribution"), ("btn_leave", "Leave"), ("btn_union", "Confederacy"));
            }
        }
        return root;
    }

    internal static LayoutNode CopyImage(LayoutNode source, int dx, int dy)
    {
        var copy = new LayoutNode { Type = source.Type, Id = source.Id, X = source.X + dx, Y = source.Y + dy,
            W = source.W, H = source.H, Texture = source.Texture, SrcX = source.SrcX, SrcY = source.SrcY,
            SrcW = source.SrcW, SrcH = source.SrcH, Tag = source.Tag, Style = source.Style };
        foreach (var child in source.Children) copy.Children.Add(CopyImage(child, dx, dy));
        return copy;
    }

    private static void State(LayoutNode root)
    {
        Text(root, "text_Id", "", 8, 84, 344, 30, true, true); Rule(root, 114);
        Pair(root, "Level", "Text_Level", Left, 116); Pair(root, "Job", "Text_Class", 184, 116);
        Pair(root, "Nation", "Text_Nation", Left, 140); Pair(root, "Race", "Text_Race", 184, 140);
        Divider(root, 180, 114, 46); Rule(root, 138); Rule(root, 160);
        Full(root, "Hit Point", "Text_HP", 162); Rule(root, 184);
        Full(root, "Mana Point", "Text_MP", 186); Rule(root, 206);
        Full(root, "Experience Points", "Text_Exp", 208); Rule(root, 228);
        Full(root, "National Point", "Text_RealmPoint", 230); Rule(root, 250);
        Pair(root, "Attack", "Text_AP", Left, 252); Pair(root, "Defense", "Text_GP", 184, 252);
        Divider(root, 180, 250, 24); Rule(root, 274);
        Full(root, "Weight", "Text_Weight", 276); Rule(root, 296);
        Full(root, "Stat Point", "Text_BonusPoint", 298, 22); Rule(root, 320);
        var stats = new[] { ("STR", "Text_Strength", "Btn_Strength", 8, 322, 0),
            ("HP", "Text_Stamina", "Btn_Stamina", 184, 322, 1),
            ("DEX", "Text_Dexterity", "Btn_Dexterity", 8, 344, 2),
            ("MP", "Text_MagicAttack", "Btn_MagicAttack", 184, 344, 4),
            ("INT", "Text_Intelligence", "Btn_Intelligence", 8, 366, 3) };
        foreach (var (caption, id, action, x, y, row) in stats)
        {
            Text(root, "stat_caption_" + row, caption, x + 4, y, 38, 20, true);
            Text(root, id, "", x + 46, y, 90, 20);
            Button(root, action, "+", x + 140, y + 1, 26, 18);
        }
        Divider(root, 180, 320, 68);
        foreach (var y in new[] { 342, 364, 388 }) Rule(root, y);
        Button(root, "btn_presets", "Stat Preset", 204, 367, 128, 18);
        Text(root, "resistance_heading", "Resistance", 12, 390, 336, 20, true); Rule(root, 412);
        var resist = new[] { ("Fire", "Text_RegistFire"), ("Magic", "Text_RegistMagic"), ("Ice", "Text_RegistIce"),
            ("Curse", "Text_RegistCurse"), ("Lightning", "Text_RegistLightR"), ("Poison", "Text_RegistPoison") };
        for (int i = 0; i < resist.Length; i++) Pair(root, resist[i].Item1, resist[i].Item2, i % 2 == 0 ? Left : 184, 414 + (i / 2) * 22, h: i >= 4 ? 22 : 20);
        Divider(root, 180, 412, 68);
        foreach (var y in new[] { 434, 456, 480 }) Rule(root, y);
        Text(root, "", "Title", 12, 488, 64, 20, true);
        Button(root, "btn_title", "None", 82, 489, 266, 18);
    }
}
