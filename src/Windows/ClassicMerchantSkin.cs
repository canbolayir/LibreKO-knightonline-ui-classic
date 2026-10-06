using Godot;
using LibreKO;
using KnightOnlineUiClassic.Layout;
namespace KnightOnlineUiClassic.Windows;

public static class ClassicMerchantSkin
{
    public static readonly string[] WindowIds={"merchantmenu","sellstall","shop","wishlist","wishfind","wantedstall"};
    public static void Extend(Control body)=>body.Ready+=()=>Callable.From(()=>Apply(body)).CallDeferred();
    public static Control? Apply(Control body)
    {
        Node? root=body;while(root!=null && root is not HudWindow)root=root.GetParent();
        if(root is not HudWindow window || window.HasMeta("classic_merchant"))return null;
        window.SetMeta("classic_merchant",true);
        if(window.Id=="wishfind") {var search=new ClassicMerchantSearch(window,body);window.AddChild(search);window.ResetSize();return search;}
        var panel=new ClassicMerchantPanel(window,body);window.AddChild(panel);window.ResetSize();
        Node parent=window;while(parent.GetParent()!=null && parent is not World)parent=parent.GetParent();
        foreach(var layer in CharacterDetailsSkin.Tree(parent).OfType<CanvasLayer>().Where(l=>l.HasMeta("merchant_amount")))ClassicMerchantAmount.Apply(layer);
        foreach(var layer in CharacterDetailsSkin.Tree(parent).OfType<CanvasLayer>().Where(l=>l.HasMeta("merchant_advert")))ClassicMerchantAdvert.Apply(layer);
        return panel;
    }
    internal static Color MerchantAccent(bool buying)=>buying?new Color(1,.72f,.49f):new Color(.88f,.90f,.94f);
    internal static Color MerchantHeading(bool buying)=>new Color(buying?"efcaa6":"dce0e6");
    internal static Color MerchantDivider(bool buying)=>new Color(buying?"a88764":"929aa7");
    internal static void Font(Control control){control.AddThemeFontOverride("font",Plugin.Kit.Bold);control.AddThemeFontSizeOverride("font_size",12);}
    internal static void Button(Button button,LayoutNode source,Control parent,Rect2 rect,string? text=null,bool? buying=null){ClassicVendorSkin.Move(button,parent,rect);ClassicVendorSkin.Button(button,source);Font(button);if(text!=null)button.Text=text;button.FocusMode=Control.FocusModeEnum.None;button.Shortcut=null;foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color","font_disabled_color"})button.AddThemeColorOverride(state,new Color("efd9b4"));
        buying ??= parent is ClassicMerchantPanel panel && panel.Window.Id!="merchantmenu"?panel.Window.Id is "wishlist" or "wantedstall":parent is ClassicMerchantSearch?true:null;
        if(buying==true)PaletteButton(button,true);
    }
    internal static void PaletteButton(Button button,bool buying){
        var normal=(StyleBoxTexture)button.GetThemeStylebox("normal");var accent=MerchantAccent(buying);
        foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled"}){
            var style=(StyleBoxTexture)normal.Duplicate();style.ModulateColor=state=="hover"?accent.Lightened(.18f):state is "pressed" or "hover_pressed"?accent.Darkened(.18f):state=="disabled"?accent.Darkened(.5f):accent;button.AddThemeStyleboxOverride(state,style);
        }
        var ink=MerchantHeading(buying);foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})button.AddThemeColorOverride(state,state=="font_hover_color"?ink.Lightened(.15f):ink);button.AddThemeColorOverride("font_disabled_color",ink.Darkened(.4f));
    }
}

public partial class ClassicMerchantPanel : Control
{
    private readonly LayoutNode _art;
    public readonly HudWindow Window;
    public ClassicMerchantPanel(HudWindow window,Control body)
    {
        Window=window;Name="classic_merchant";TextureFilter=TextureFilterEnum.Nearest;
        var controls=CharacterDetailsSkin.Tree(body).ToArray();
        bool menu=window.Id=="merchantmenu",sell=window.Id=="sellstall",wish=window.Id=="wishlist";
        _art=Plugin.Kit.Layout(menu?"co_saleboardselection_us":sell?"co_tradeinventory_us":"co_tradebuyinventory_us");
        Size=CustomMinimumSize=menu?new Vector2(327,240):sell?new Vector2(364,472):new Vector2(365,436);
        var grip=window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close=grip.GetChildren().OfType<Button>().Single(b=>b.TooltipText=="Close");
        foreach(var child in window.GetChildren().OfType<Control>())child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        ClassicPartySkin.Drag(this,grip,new Rect2(0,0,Size.X,22));close.Visible=false;
        Label Text(string text,Rect2 rect){var label=ClassicPartySkin.Caption(this,text,rect,new Color("efd9b4"),12);ClassicMerchantSkin.Font(label);return label;}
        if(menu) {
            Text("Merchant",new Rect2(24,16,279,20)).HorizontalAlignment=HorizontalAlignment.Center;
            foreach(var button in controls.OfType<Button>()) {
                var rect=new Rect2(91,button.Text=="Selling Merchant"?57:button.Text=="Buying Merchant"?93:129,144,27);
                if(button.Text=="Market Price"){rect.Position=new Vector2(91,165);button.Disabled=true;button.TooltipText="Market prices are not available on this server yet.";}
                ClassicMerchantSkin.Button(button,Plugin.Kit.Layout("co_tradeprice_us").Find("btn_ok")!,this,rect);
            }
            var cancel=new Button {Text="Cancel"};AddChild(cancel);cancel.Pressed+=()=>close.EmitSignal(BaseButton.SignalName.Pressed);
            ClassicMerchantSkin.Button(cancel,Plugin.Kit.Layout("co_tradeprice_us").Find("btn_cancel")!,this,new Rect2(91,129,144,27));return;
        }
        var heading=grip.GetChildren().OfType<Label>().First(l=>l.SizeFlagsHorizontal.HasFlag(SizeFlags.ExpandFill));
        ClassicVendorSkin.Move(heading,this,new Rect2(42,2,280,18));ClassicMerchantSkin.Font(heading);heading.HorizontalAlignment=HorizontalAlignment.Center;heading.ClipText=true;heading.AddThemeColorOverride("font_color",ClassicMerchantSkin.MerchantHeading(wish || window.Id=="wantedstall"));
        var grids=controls.OfType<GridContainer>().ToArray();
        for(int g=0;g<grids.Length;g++) {
            var cells=grids[g].GetChildren().OfType<Control>().ToArray();
            for(int i=0;i<cells.Length;i++) {
                var area=_art.Find(cells.Length==12?"at"+i:"a"+i)!;
                // Normalize one imported column's single-pixel drift to its row pitch.
                var position=area.Position;if(cells.Length==28)position.X=(sell?15:16)+i%7*48;
                ClassicVendorSkin.Move(cells[i],this,new Rect2(position-Vector2.One*2,new Vector2(48,48)));
                cells[i].Name="merchant_"+(cells.Length==12?"listing_":"bag_")+i;
                cells[i].AddThemeStyleboxOverride("panel",new StyleBoxEmpty {ContentMarginLeft=2,ContentMarginTop=2,ContentMarginRight=2,ContentMarginBottom=2});
            }
        }
        var money=controls.OfType<Label>().Where(l=>l.GetParent() is HBoxContainer && l.HorizontalAlignment==HorizontalAlignment.Right).ToArray();
        for(int i=0;i<money.Length;i++){var rect=sell?new Rect2(130,350+i*30,204,22):new Rect2(193,157,153,21);ClassicVendorSkin.Move(money[i],this,rect);ClassicMerchantSkin.Font(money[i]);money[i].VerticalAlignment=VerticalAlignment.Center;money[i].SetMeta("merchant_balance_rect",true);}
        if(sell){foreach(var entry in new[]{("Total",350),("Coins",380)}){var label=Text(entry.Item1,new Rect2(24,entry.Item2,90,22));label.Name="money_caption_"+entry.Item1.ToLowerInvariant();label.AddThemeColorOverride("font_color",new Color("c8b68e"));}}
        var status=controls.OfType<Label>().Last();ClassicVendorSkin.Move(status,this,new Rect2(15,sell?406:135,334,18));ClassicMerchantSkin.Font(status);status.AutowrapMode=TextServer.AutowrapMode.Off;status.ClipText=true;status.SetMeta("merchant_status_rect",true);status.HorizontalAlignment=HorizontalAlignment.Center;
        foreach(var button in controls.OfType<Button>()) {
            var source=_art.Find(button.Text is "Cancel"?"btn_cancel":"btn_ok") ?? Plugin.Kit.Layout("co_tradeinventory_us").Find(button.Text is "Cancel"?"btn_cancel":"btn_ok")!;
            var rect=new Rect2(source.Position,source.SizeVec);
            if(!sell && button.Text=="Cancel")rect=new Rect2(220,397,71,25);
            if(wish && button.Text!="Cancel")rect=new Rect2(74,397,71,25);
            ClassicMerchantSkin.Button(button,source,this,rect,button.Text=="Confirm"?"OK":null);
        }
        if(!sell && !wish){var ok=new Button {Text="OK"};AddChild(ok);ok.Pressed+=()=>close.EmitSignal(BaseButton.SignalName.Pressed);ClassicMerchantSkin.Button(ok,_art.Find("btn_ok")!,this,new Rect2(146,397,71,25));}
        foreach(var edit in controls.OfType<LineEdit>()){ClassicVendorSkin.Move(edit,this,new Rect2(24,388,314,18));ClassicMerchantSkin.Font(edit);edit.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());edit.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());}
        if(wish) {
            var summary=controls.OfType<VBoxContainer>().First(v=>v.CustomMinimumSize.Y==76);
            ClassicVendorSkin.Move(summary,this,new Rect2(16,193,332,180));summary.AddThemeConstantOverride("separation",0);
            foreach(var child in summary.GetChildren().OfType<Control>()){ClassicMerchantSkin.Font(child);if(child is Label label){label.ClipText=true;label.TooltipText=label.Text;}child.AddThemeColorOverride("font_color",new Color("c0c0c0"));}
            summary.ChildEnteredTree+=node=>{if(node is Control c){ClassicMerchantSkin.Font(c);if(c is Label label){label.ClipText=true;label.TooltipText=label.Text;}c.AddThemeColorOverride("font_color",new Color("c0c0c0"));}};
        }
        Resized+=()=>Size=CustomMinimumSize;
    }
    public override void _Draw(){DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);foreach(var part in _art.Images)DrawTextureRectRegion(Plugin.Kit.Texture(part.Texture!),new Rect2(part.Position,part.SizeVec),new Rect2(part.SrcX,part.SrcY,part.SrcW,part.SrcH),Window.Id=="merchantmenu"?Colors.White:ClassicMerchantSkin.MerchantAccent(Window.Id is "wishlist" or "wantedstall"));if(Window.Id=="sellstall")DrawLine(new Vector2(24,376),new Vector2(334,376),new Color("929aa7",0.3f));if(Window.Id=="wishlist")DrawRect(new Rect2(12,190,339,197),Colors.Black);}
}
