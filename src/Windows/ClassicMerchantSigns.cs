using Godot;
namespace KnightOnlineUiClassic.Windows;

/// <summary>Original four-icon sign, extended by one row for premium stalls.</summary>
public partial class ClassicMerchantSigns : Control
{
    private double _elapsed;
    public ClassicMerchantSigns(){MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Ready()=>GetTree().NodeAdded+=NodeAdded;
    public override void _ExitTree()=>GetTree().NodeAdded-=NodeAdded;
    private void NodeAdded(Node node){if(node is PanelContainer sign && sign.IsInGroup("merchant_signs"))Callable.From(()=>Style(sign)).CallDeferred();}
    public override void _Process(double delta)
    {
        _elapsed+=delta;if(_elapsed<0.25)return;_elapsed=0;
        foreach(var node in GetTree().GetNodesInGroup("merchant_signs"))if(node is PanelContainer sign)Style(sign);
    }
    private static void Style(PanelContainer sign)
    {
        if(!IsInstanceValid(sign) || sign.HasMeta("classic_sign"))return;
        var grid=sign.GetChildren().OfType<GridContainer>().FirstOrDefault();if(grid==null)return;
        sign.SetMeta("classic_sign",true);
        bool buying=sign.HasMeta("merchant_buying") && sign.GetMeta("merchant_buying").AsBool();
        var image=Plugin.Kit.Layout("co_tradeitemdisplay_us").Images.First();
        // The imported 220-pixel region includes 40 pixels beyond the four-slot border.
        var style=new StyleBoxTexture {Texture=Plugin.Kit.Texture(image.Texture!),RegionRect=new Rect2(image.SrcX,image.SrcY,180,image.SrcH)};
        foreach(var side in new[]{Side.Left,Side.Right,Side.Top,Side.Bottom}){style.SetTextureMargin(side,8);style.SetContentMargin(side,side is Side.Left or Side.Right?14:side==Side.Top?8:9);}
        style.ModulateColor=ClassicMerchantSkin.MerchantAccent(buying);
        sign.AddThemeStyleboxOverride("panel",style);
        grid.AddThemeConstantOverride("h_separation",8);grid.AddThemeConstantOverride("v_separation",4);
        int height=grid.GetChildCount()>4?85:49;
        {
            var body=new VBoxContainer {Name="merchant_sign_body"};body.AddThemeConstantOverride("separation",4);sign.AddChild(body);grid.Reparent(body,false);
            var caption=new Label {Name="merchant_sign_caption",Text=buying?"BUYING":"SELLING",HorizontalAlignment=HorizontalAlignment.Center,CustomMinimumSize=new Vector2(152,18),VerticalAlignment=VerticalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            ClassicMerchantSkin.Font(caption);caption.AddThemeColorOverride("font_color",ClassicMerchantSkin.MerchantHeading(buying));var header=new StyleBoxFlat {BgColor=Colors.Black,BorderColor=ClassicMerchantSkin.MerchantDivider(buying),BorderWidthBottom=1};header.SetContentMarginAll(0);header.ExpandMarginLeft=header.ExpandMarginRight=5;header.ExpandMarginTop=header.ExpandMarginBottom=4;caption.AddThemeStyleboxOverride("normal",header);body.AddChild(caption);body.MoveChild(caption,0);height+=22;
        }
        sign.CustomMinimumSize=new Vector2(180,height);
        foreach(var cell in grid.GetChildren().OfType<Control>()){cell.CustomMinimumSize=new Vector2(32,32);cell.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());}
    }
}

