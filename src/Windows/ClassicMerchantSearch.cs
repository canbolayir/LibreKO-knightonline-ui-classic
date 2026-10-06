using Godot;
using LibreKO;
namespace KnightOnlineUiClassic.Windows;

/// <summary>LibreKO item search inside the Classic quest-frame artwork family.</summary>
public partial class ClassicMerchantSearch : ClassicFrame
{
    public ClassicMerchantSearch(HudWindow window,Control body)
    {
        BackgroundColor=Colors.Black;BackgroundAlpha=1;Size=CustomMinimumSize=new Vector2(470,534);TextureFilter=TextureFilterEnum.Nearest;
        var grip=window.Header!.GetChildren().OfType<HBoxContainer>().First();
        var close=grip.GetChildren().OfType<Button>().Single(b=>b.TooltipText=="Close");
        foreach(var child in window.GetChildren().OfType<Control>())child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        ClassicPartySkin.Drag(this,grip,new Rect2(0,0,470,28));
        var heading=ClassicPartySkin.Caption(this,"Item Search",new Rect2(22,8,426,20),new Color("efd9b4"));heading.HorizontalAlignment=HorizontalAlignment.Center;ClassicMerchantSkin.Font(heading);
        ClassicVendorSkin.Move(body,this,new Rect2(20,40,430,444));body.Visible=true;
        var theme=new Theme {DefaultFont=Plugin.Kit.Bold,DefaultFontSize=12};body.Theme=theme;
        void Style(Node node){if(node.HasMeta("classic_search_control"))return;node.SetMeta("classic_search_control",true);if(node is Control control)ClassicMerchantSkin.Font(control);if(node is VScrollBar rail){
            var track=new StyleBoxFlat {BgColor=new Color("171715"),BorderColor=new Color("bfa777")};track.SetBorderWidthAll(1);track.SetContentMarginAll(0);rail.AddThemeStyleboxOverride("scroll",track);
            foreach(string state in new[]{"grabber","grabber_highlight","grabber_pressed"}){var grab=ClassicDesign.ButtonBox(state=="grabber_highlight"?"hover":state=="grabber_pressed"?"pressed":"normal");grab.SetContentMarginAll(0);rail.AddThemeStyleboxOverride(state,grab);}rail.CustomMinimumSize=new Vector2(12,0);rail.FocusMode=FocusModeEnum.None;
        }if(node is Button button){button.CustomMinimumSize=new Vector2(button is OptionButton?80:button.Text=="Registration"?90:71,25);ClassicVendorSkin.Button(button,Plugin.Kit.Layout("co_tradeprice_us").Find("btn_ok")!);foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color","font_disabled_color"})button.AddThemeColorOverride(state,new Color("efd9b4"));ClassicMerchantSkin.PaletteButton(button,true);}foreach(var child in node.GetChildren())Style(child);node.ChildEnteredTree+=Style;}
        Style(body);
        var cancel=new Button {Text="Cancel"};AddChild(cancel);cancel.Pressed+=()=>close.EmitSignal(BaseButton.SignalName.Pressed);ClassicMerchantSkin.Button(cancel,Plugin.Kit.Layout("co_tradeprice_us").Find("btn_cancel")!,this,new Rect2(200,500,71,25));
        Resized+=()=>Size=CustomMinimumSize;
    }
}
