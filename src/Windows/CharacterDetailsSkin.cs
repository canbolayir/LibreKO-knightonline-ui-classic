using Godot;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Classic presentation using the original live control instances and callbacks.</summary>
public static class CharacterDetailsSkin
{
    public static readonly string[] WindowIds = { "presets", "titles", "quests", "character_clan_details", "clanpoint", "quest_target", "quest_receipt", "quest_available", "npc_dialog", "userinfo" };
    public static void Extend(Control body) => body.Ready += () => Callable.From(() => Apply(body)).CallDeferred();
    public static Control? Apply(Control body)
    {
        Control? top=body;
        while(top!=null && top is not LibreKO.HudWindow) top=top.GetParent() as Control;
        if(top is not LibreKO.HudWindow window || window.HasMeta("classic_detail_shell")) return null;
        var bar=window.Header?.GetChildren().OfType<HBoxContainer>().FirstOrDefault();
        if(bar==null) return null;
        window.SetMeta("classic_detail_shell",true);
        var close=bar.GetChildren().OfType<Button>().LastOrDefault(b=>b.TooltipText=="Close");
        foreach(var child in window.GetChildren().OfType<Control>()) child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        if(CharacterPageRouter.Supports(window.Id)) return CharacterPageRouter.Register(window,body,close);
        if(window.Id=="npc_dialog")
        {
            var dialogue=new ClassicNpcDialogue(window,body,bar,close);
            window.AddChild(dialogue);window.ResetSize();return dialogue;
        }
        var panel=new ClassicDetailPanel(window.Id,body,bar,close);window.AddChild(panel);window.ResetSize();return panel;
    }
    internal static IEnumerable<Node> Tree(Node node)
    {
        yield return node;
        foreach(var child in node.GetChildren()) foreach(var item in Tree(child)) yield return item;
    }
    internal static void ConstrainWidth(Control node)
    {
        foreach(var control in Tree(node).OfType<Control>())
        {
            if(control is Container or RichTextLabel) control.CustomMinimumSize=new Vector2(1,control.CustomMinimumSize.Y);
            if(control is Label label)
            {
                bool fixedColumn=label.GetParent() is HBoxContainer && (label.SizeFlagsHorizontal&Control.SizeFlags.Expand)==0;
                label.AutowrapMode=fixedColumn?TextServer.AutowrapMode.Off:TextServer.AutowrapMode.WordSmart;
                label.CustomMinimumSize=new Vector2(fixedColumn?Math.Min(160,label.CustomMinimumSize.X):1,label.CustomMinimumSize.Y);
                if(fixedColumn) label.ClipText=false;
            }
            if(control is Container && !control.HasMeta("classic_width_observer"))
            {
                control.SetMeta("classic_width_observer",true);
                control.ChildEnteredTree+=child=>Callable.From(()=> { if(child is Control added && GodotObject.IsInstanceValid(added)) ConstrainWidth(added); }).CallDeferred();
            }
        }
    }
    internal static void StyleTree(Node node)
    {
        if(!GodotObject.IsInstanceValid(node) || node.HasMeta("classic_detail_control")) return;
        node.SetMeta("classic_detail_control",true);
        bool npc=IsNpcContent(node);
        if(node is VBoxContainer vertical) vertical.AddThemeConstantOverride("separation",6);
        if(node is HBoxContainer horizontal)
        {
            horizontal.AddThemeConstantOverride("separation",4);
            if(horizontal.GetChildren().OfType<Label>().Count()>=2) horizontal.CustomMinimumSize=new Vector2(horizontal.CustomMinimumSize.X,22);
        }
        if(node is HSeparator separator)
        {
            separator.AddThemeStyleboxOverride("separator",new StyleBoxLine { Color=ClassicReportDesign.Caption,Thickness=1 });
        }
        if(node is GridContainer grid) { grid.AddThemeConstantOverride("h_separation",8);grid.AddThemeConstantOverride("v_separation",4); }
        if(node is PanelContainer section && !npc)
        {
            var old=section.GetThemeStylebox("panel") as StyleBoxFlat;
            if(old?.ContentMarginTop==5)
            {
                var plate=new StyleBoxFlat { BgColor=Colors.Transparent,BorderColor=new Color("00ff00") };
                if(old.BorderColor.A>.6f) plate.SetBorderWidthAll(1);
                plate.SetContentMarginAll(0);section.AddThemeStyleboxOverride("panel",plate);
            }
            else
            {
                var empty=new StyleBoxEmpty();empty.SetContentMarginAll(6);section.AddThemeStyleboxOverride("panel",empty);
                section.AddChild(new ClassicDetailInset(section));
            }
        }
        if(node is Button button && node is not CheckBox && !npc)
        {
            bool tab=button.ToggleMode && button.Text.Length>0;
            bool selected=button.GetThemeStylebox("normal") is StyleBoxFlat tabBox && tabBox.BorderWidthTop>=2;
            ClassicReportDesign.StyleButton(button,tab);
            if(tab && selected) { button.SetPressedNoSignal(true);button.AddThemeStyleboxOverride("normal",ClassicDesign.ButtonBox("pressed",true)); }
            float textWidth=Plugin.Kit.Bold.GetStringSize(button.Text,HorizontalAlignment.Left,-1,tab?13:12).X+12;
            button.CustomMinimumSize=new Vector2(Math.Max(button.CustomMinimumSize.X,Math.Min(180,textWidth)),tab?36:22);
            button.ClipText=true;button.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            button.AddThemeConstantOverride("icon_max_width",18);
            button.AddThemeColorOverride("icon_normal_color",ClassicReportDesign.Caption);
            button.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
        }
        if(node is CheckBox check)
        {
            check.AddThemeFontOverride("font",Plugin.Kit.Regular);check.AddThemeFontSizeOverride("font_size",13);
            foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color"}) check.AddThemeColorOverride(state,ClassicReportDesign.Value);
            check.CustomMinimumSize=new Vector2(0,22);
            var socket=Plugin.Kit.Layout("{nation}_chat_us").Find("btn_check_normal");
            if(socket!=null)
                foreach(var state in new[]{"unchecked","checked","radio_unchecked","radio_checked","unchecked_disabled","checked_disabled","radio_unchecked_disabled","radio_checked_disabled"})
                {
                    var art=socket.Images.FirstOrDefault(i=>i.Tag==(state.Contains("unchecked")?0:1));
                    if(art?.Texture is {} texture) check.AddThemeIconOverride(state,new AtlasTexture { Atlas=Plugin.Kit.Texture(texture),Region=new Rect2(art.SrcX,art.SrcY,art.SrcW,art.SrcH),FilterClip=true });
                }
            if(check.Text.Length>55)
            {
                string description=check.Text;check.TooltipText=description;
                check.Text=description.StartsWith("Automatic")?"Automatic accumulation":"Free accumulation";
                var hint=new Label { Text=description,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(1,0) };
                hint.AddThemeFontSizeOverride("font_size",13);hint.AddThemeColorOverride("font_color",ClassicReportDesign.Value);
                check.GetParent().AddChild(hint);check.GetParent().MoveChild(hint,check.GetIndex()+1);
            }
        }
        if(node is Label label && !(npc && label.HasMeta("quest_status")))
        {
            int oldSize=label.GetThemeFontSize("font_size");var ink=label.GetThemeColor("font_color");
            bool heading=oldSize>=15 || ink.R>ink.B*1.3f && ink.G>ink.B*1.1f;
            label.AddThemeFontOverride("font",heading?Plugin.Kit.Bold:Plugin.Kit.Regular);label.AddThemeFontSizeOverride("font_size",13);
            label.AddThemeColorOverride("font_color",heading?ClassicReportDesign.Caption:ClassicReportDesign.Value);
            if(ink.G>ink.R*1.2f || ink.R>ink.G*1.5f) label.AddThemeColorOverride("font_color",ink);
            label.AddThemeColorOverride("font_shadow_color",Colors.Black);label.AddThemeConstantOverride("shadow_offset_x",1);label.AddThemeConstantOverride("shadow_offset_y",1);
            label.VerticalAlignment=VerticalAlignment.Center;
            if(label.Text.Length>55 && label.AutowrapMode==TextServer.AutowrapMode.Off) label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            if(label.AutowrapMode!=TextServer.AutowrapMode.Off) label.CustomMinimumSize=new Vector2(1,label.CustomMinimumSize.Y);
        }
        if(node is LineEdit input)
        {
            input.AddThemeFontOverride("font",Plugin.Kit.Regular);input.AddThemeFontSizeOverride("font_size",13);input.AddThemeColorOverride("font_color",ClassicReportDesign.Value);
            var box=ClassicDesign.InputBox();box.SetContentMargin(Side.Top,1);box.SetContentMargin(Side.Bottom,1);
            input.AddThemeStyleboxOverride("normal",box);input.AddThemeStyleboxOverride("focus",box);input.CustomMinimumSize=new Vector2(input.CustomMinimumSize.X,22);
        }
        if(node is TextEdit edit)
        {
            edit.AddThemeFontOverride("font",Plugin.Kit.Regular);edit.AddThemeFontSizeOverride("font_size",13);edit.AddThemeColorOverride("font_color",ClassicReportDesign.Value);
            edit.AddThemeStyleboxOverride("normal",ClassicDesign.InputBox());edit.AddThemeStyleboxOverride("focus",ClassicDesign.InputBox());
        }
        if(node is RichTextLabel rich)
        {
            foreach(var face in new[]{"normal","bold","italics","bold_italics","mono"}) rich.AddThemeFontOverride(face+"_font",face.StartsWith("bold")?Plugin.Kit.Bold:Plugin.Kit.Regular);
            rich.AddThemeFontSizeOverride("normal_font_size",13);rich.AddThemeFontSizeOverride("bold_font_size",13);rich.AddThemeColorOverride("default_color",ClassicReportDesign.Value);
            rich.CustomMinimumSize=new Vector2(1,rich.CustomMinimumSize.Y);
        }
        if(node is ScrollContainer scroll)
        {
            var track=new StyleBoxFlat { BgColor=new Color("171715"),BorderColor=ClassicReportDesign.Caption };track.SetBorderWidthAll(1);track.SetContentMarginAll(0);
            var grab=ClassicDesign.ButtonBox("normal");grab.SetContentMarginAll(0);
            var bar=scroll.GetVScrollBar();bar.AddThemeStyleboxOverride("scroll",track);
            foreach(var state in new[]{"grabber","grabber_highlight","grabber_pressed"}) bar.AddThemeStyleboxOverride(state,grab);
            bar.CustomMinimumSize=new Vector2(12,0);
        }
        // The owning NPC surface must also style children inserted after the shell opens.
        if(npc) ClassicNpcQuestChrome.StyleControl(node);
        node.ChildEnteredTree+=child=>Callable.From(()=>StyleTree(child)).CallDeferred();
        foreach(var child in node.GetChildren().ToArray()) StyleTree(child);
    }
    private static bool IsNpcContent(Node node)
    {
        for(Node? parent=node;parent!=null;parent=parent.GetParent())
            if(parent.HasMeta("classic_npc_content")) return true;
        return false;
    }
    public static void StyleDialog(Window dialog)
    {
        if(!GodotObject.IsInstanceValid(dialog)) return;
        if(dialog is AcceptDialog && dialog.HasMeta("classic_confirmation_frame")) return;
        dialog.AddThemeStyleboxOverride("panel",ClassicDesign.SectionBox(8));dialog.AddThemeFontOverride("font",Plugin.Kit.Regular);
        dialog.AddThemeFontSizeOverride("font_size",13);dialog.AddThemeColorOverride("font_color",ClassicReportDesign.Value);
        if(dialog is AcceptDialog accept)
        {
            accept.AddThemeConstantOverride("buttons_min_width",80);
            accept.AddThemeConstantOverride("buttons_min_height",22);
            accept.AddThemeConstantOverride("buttons_separation",12);
            accept.DialogAutowrap=true;
            StyleTree(accept.GetLabel());StyleTree(accept.GetOkButton());
            if(accept is ConfirmationDialog confirmation) StyleTree(confirmation.GetCancelButton());
            if(!accept.HasMeta("classic_confirmation_frame"))
            {
                accept.SetMeta("classic_confirmation_frame",true);accept.Borderless=true;accept.Transparent=true;accept.TransparentBg=true;
                var empty=new StyleBoxEmpty();empty.SetContentMargin(Side.Top,88);empty.SetContentMargin(Side.Bottom,24);
                empty.SetContentMargin(Side.Left,12);empty.SetContentMargin(Side.Right,12);
                accept.AddThemeStyleboxOverride("panel",empty);accept.MinSize=new Vector2I(360,230);
                // Keep decorative controls outside AcceptDialog's direct child layout.
                var decoration=new Node();accept.AddChild(decoration);
                var frame=new ClassicConfirmationFrame(accept) { ZIndex=-1 };decoration.AddChild(frame);
            }
        }
        else foreach(var child in dialog.GetChildren()) StyleTree(child);
    }
}

/// <summary>Inset art follows the owning panel and contributes no content minimum.</summary>
public partial class ClassicDetailInset : ClassicReportSection
{
    private readonly Control _owner;
    public ClassicDetailInset(Control owner) { _owner=owner;ShowBehindParent=true; }
    public override void _Process(double delta) { Position=Vector2.Zero;Size=_owner.Size; }
}
