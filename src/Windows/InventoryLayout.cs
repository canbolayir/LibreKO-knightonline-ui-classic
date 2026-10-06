using Godot;
using LibreKO.Domain;
using KnightOnlineUiClassic.Layout;

namespace KnightOnlineUiClassic.Windows;

/// <summary>Classic inventory artwork with an editable Chaos-inspired left drawer.</summary>
internal static class InventoryLayout
{
    public const int Width=366, Height=578, DrawerWidth=170, DrawerGap=-4;
    public static int DrawerTop=>Plugin.Kit.Nation==1?40:39;
    public static int DrawerBottom=>Plugin.Kit.Nation==1?561:558;
    public const int CostumeArea=17, BagItemArea=19, BagContentArea=18;
    public static readonly (int Position,string Caption,int Column,int Row,int IconX,int IconY)[] CostumeSlots=
    {
        (InventoryConstants.CosPosTattoo,"Tattoo",0,0,25,202),
        (InventoryConstants.CosPosHelmet,"Helmet",1,0,83,202),
        (InventoryConstants.CosPosFairy,"Fairy",2,0,143,202),
        (InventoryConstants.CosPosGloveRight,"Pathos R",0,1,25,261),
        (InventoryConstants.CosPosPauldron,"Outfit",1,1,83,261),
        (InventoryConstants.CosPosGloveLeft,"Pathos L",2,1,143,261),
        (InventoryConstants.CosPosTalisman,"Talisman",0,2,231,194),
        (InventoryConstants.CosPosWing,"Wings",1,2,289,194),
        (InventoryConstants.CosPosEmblem,"Emblem",2,2,349,194),
    };

    public static LayoutNode Build(LayoutNode source)
    {
        var root=new LayoutNode { Type="base",Id="inventory_window",W=Width,H=Height };
        foreach(var child in source.Children)
        {
            if(child.IsImage && child.W==32 && child.H==31 && child.Y-source.Y==313) continue;
            if(child.IsImage && child.Id!="elmo_ecli666" || child.IsButton && child.Id=="btn_close")
                root.Children.Add(CharacterLayout.CopyImage(child,-source.X,-source.Y));
            else if(child.IsArea && child.AreaType is not (1 or 2))
                root.Children.Add(new LayoutNode { Type="area",Id=child.Id,AreaType=child.AreaType,
                    X=child.X-source.X,Y=child.Y-source.Y,W=child.W,H=child.H });
        }
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="equipment_backing",X=193,Y=46,W=167,H=267});
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="grid_backing",X=12,Y=346,W=345,H=199});
        foreach(var area in source.All().Where(n=>n.IsArea && n.AreaType==1 && int.Parse(n.Id)<14))
            Slot(root,area.Id,1,area.X-source.X,area.Y-source.Y);
        for(int i=0;i<28;i++) Slot(root,i.ToString(),2,16+i%7*49,349+i/7*49);
        var trash=source.All().First(n=>n.IsArea && n.AreaType==1 && n.Id=="14");
        Slot(root,"trash",0,trash.X-source.X,trash.Y-source.Y);
        var equipment=source.Children.First(n=>n.IsImage && n.W==362 && n.H==275);
        root.Children.Add(new LayoutNode {Type="image",Id="trash_icon",X=trash.X-source.X,Y=trash.Y-source.Y,W=45,H=45,
            Texture=equipment.Texture,SrcX=equipment.SrcX+trash.X-equipment.X,SrcY=equipment.SrcY+trash.Y-equipment.Y,SrcW=45,SrcH=45});
        var coin=Plugin.Kit.Layout("el_inventory_us").Find("btn_gold")!.Images.First();
        {
            var artwork=CharacterLayout.CopyImage(coin,-source.X,-source.Y);
            artwork.X=206;artwork.Y=316;artwork.W=22;artwork.H=25;
            artwork.Id="coin_icon";root.Children.Add(artwork);
        }
        Text(root,"text_weight","",14,319,181,20);
        Text(root,"text_gold","",231,319,119,20,right:true);
        var toggle=new LayoutNode {Type="inventory_corner_button",Id="costume_toggle",X=3,Y=DrawerTop,W=80,H=64,
            Tooltip="Open costume and magic bags"};
        root.Children.Add(toggle);
        return root;
    }

    public static LayoutNode Drawer()
    {
        var root=new LayoutNode {Type="base",Id="costume_drawer",W=DrawerWidth,H=DrawerBottom-DrawerTop};
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="drawer_backing",X=9,Y=6,W=156,H=root.H-11});
        root.Children.Add(new LayoutNode {Type="inventory_frame_component",Id="drawer_frame",X=3,W=166,H=root.H});
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="costume_grid_backing",X=12,Y=8,W=149,H=158});
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="bag_equipment_backing",X=12,Y=213,W=149,H=52});
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id="bag_grid_backing",X=12,Y=346-DrawerTop,W=149,H=199});
        foreach(var slot in CostumeSlots)
        {
            int x=16+slot.Column*49,y=11+slot.Row*53;
            Slot(root,slot.Position.ToString(),CostumeArea,x,y);
        }
        for(int bag=0;bag<InventoryConstants.BagSlotMax;bag++)
        {
            Slot(root,bag.ToString(),BagItemArea,16+bag*49,216);
            root.Children.Add(new LayoutNode {Type="classic_button",Id="bag_tab_"+bag,Text=(bag+1).ToString(),X=12+bag*49,Y=276,W=51,H=24});
        }
        for(int i=0;i<InventoryConstants.MagicBagMax;i++) Slot(root,i.ToString(),BagContentArea,16+i%3*49,349-DrawerTop+i/3*49);
        return root;
    }

    public static AtlasTexture EmptyIcon(int x,int y)=>new() {Atlas=Plugin.Kit.Texture("classic_costume_icons.png"),
        Region=new Rect2(x+4,y+4,35,35),FilterClip=true};

    public static AtlasTexture EquipmentIcon(int slot)
    {
        int[] xs={363,415,467,363,415,467,363,415,467,363,415,467,363,415};
        int[] ys={45,45,45,96,96,96,148,148,148,199,199,199,251,251};
        return new AtlasTexture {Atlas=Plugin.Kit.Texture("classic_equipment_icons.png"),
            Region=new Rect2(336+xs[slot]-349+4,237+ys[slot]-33+4,35,35),FilterClip=true};
    }

    private static void Slot(LayoutNode root,string id,int type,int x,int y)
    {
        root.Children.Add(new LayoutNode {Type="inventory_backing",Id=$"backing_{type}_{id}",X=x,Y=y,W=45,H=45});
        bool karus=Plugin.Kit.Nation==1;
        root.Children.Add(new LayoutNode {Type="image",Id=$"frame_{type}_{id}",X=x-4,Y=y-3,W=51,H=52,
            Texture=karus?"ui_ka_inven_us.png":"ui_el_inven_us.png",SrcX=karus?159:157,SrcY=karus?326:333,SrcW=51,SrcH=52});
        root.Children.Add(new LayoutNode {Type="area",Id=id,AreaType=type,X=x,Y=y,W=45,H=45});
    }

    private static void Text(LayoutNode root,string id,string text,int x,int y,int w,int h,int size=SkillLayout.BodyFontPoints,bool center=false,bool right=false)
        =>root.Children.Add(new LayoutNode {Type="string",Id=id,Text=text,X=x,Y=y,W=w,H=h,Font=SkillLayout.FontFamily,Size=size,Bold=true,
            Color=ClassicReportDesign.Value,Style=TextStyle.SingleLine|TextStyle.AlignVCenter|(center?TextStyle.AlignCenter:right?TextStyle.AlignRight:TextStyle.AlignLeft)});
}
