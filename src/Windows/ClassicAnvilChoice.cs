using Godot;
using LibreKO;
namespace KnightOnlineUiClassic.Windows;

// Live choice callbacks use the original crest and button artwork.
public partial class ClassicAnvilChoice : Control
{
    public static void Extend(Control body) => body.Ready += () => Callable.From(()=>Apply(body)).CallDeferred();
    public static ClassicAnvilChoice? Apply(Control body)
    {
        Node? owner=body;
        while(owner!=null && owner is not HudWindow)owner=owner.GetParent();
        if(owner is not HudWindow window || window.HasMeta("classic_anvil_choice"))return null;
        window.SetMeta("classic_anvil_choice",true);
        var panel=new ClassicAnvilChoice(window,body);window.AddChild(panel);window.ResetSize();return panel;
    }
    public ClassicAnvilChoice(HudWindow window,Control body)
    {
        Name="classic_anvil_choice";Size=CustomMinimumSize=new Vector2(279,236);TextureFilter=TextureFilterEnum.Nearest;
        var nodes=CharacterDetailsSkin.Tree(body).ToArray();
        foreach(var child in window.GetChildren().OfType<Control>())child.Visible=false;
        window.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        var art=Plugin.Kit.Layout("co_upgradeselect_us");
        var label=nodes.OfType<Label>().First(l=>l.Text.Contains("anvil",StringComparison.OrdinalIgnoreCase));
        ClassicVendorSkin.Move(label,this,new Rect2(26,43,231,49));ClassicMerchantSkin.Font(label);
        label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var buttons=nodes.OfType<Button>().Where(b=>b.Text is "Upgrade Item" or "Compound Accessory" or "Walk away").ToArray();
        for(int i=0;i<buttons.Length;i++)
        {
            var source=art.Find(i==0?"upgrade_1":i==1?"upgrade_2":"btn_close")!;
            ClassicMerchantSkin.Button(buttons[i],source,this,new Rect2(37,i==0?110:i==1?147:180,207,23),buttons[i].Text);
            buttons[i].FocusMode=FocusModeEnum.None;
        }
    }
    public override void _Draw()
    {
        foreach(var image in Plugin.Kit.Layout("co_upgradeselect_us").Images)
            DrawTextureRectRegion(Plugin.Kit.Texture(image.Texture!),new Rect2(image.Position,image.SizeVec),new Rect2(image.SrcX,image.SrcY,image.SrcW,image.SrcH));
    }
}
