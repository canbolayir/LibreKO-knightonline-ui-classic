using Godot;
using LibreKO;
using LibreKO.Domain;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using NativeSlot=LibreKO.Domain.ItemSlot;

public partial class Preview
{
    private async Task CaptureMerchantPalette(int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/merchant-color-options");System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(420,168);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(420,168),MouseFilter=MouseFilterEnum.Ignore});
        ItemData.EnsureLoaded();
        var groups=(Dictionary<int,List<ItemData.SellEntry>>)typeof(ItemData).GetField("_sell",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var ids=groups.Values.SelectMany(g=>g).Select(e=>e.Id).Distinct().Where(id=>ItemData.Get(id)!=null && ItemData.IsSellable(id)).ToArray();
        int stack=ids.First(id=>ItemData.Get(id)!.Countable!=0);var singles=ids.Where(id=>ItemData.Get(id)!.Countable==0).Take(7).ToArray();
        var cellType=typeof(World).GetNestedType("MerchantCell",BindingFlags.NonPublic)!;
        var palettes=new[]{("01-crimson","01 · Crimson",new Color(.92f,.42f,.48f),"f0a1aa","9d6268"),
            ("02-burgundy","02 · Burgundy",new Color(.73f,.43f,.58f),"ddafcb","83647c"),
            ("03-copper","03 · Copper",new Color(1,.72f,.49f),"efcaa6","a88764"),
            ("04-blue","04 · Royal Blue",new Color(.52f,.71f,1),"a7c8f5","6387b6"),
            ("05-teal","05 · Teal",new Color(.45f,.91f,.82f),"96ded3","609b91"),
            ("06-silver","06 · Silver",new Color(.88f,.90f,.94f),"dce0e6","929aa7")};
        var styler=new ClassicMerchantSigns();AddChild(styler);
        var title=new Label {Position=new Vector2(20,8),Size=new Vector2(380,24)};title.AddThemeFontOverride("font",KnightOnlineUiClassic.Plugin.Kit.Bold);title.AddThemeFontSizeOverride("font_size",14);title.AddThemeColorOverride("font_color",new Color("e4d7bc"));AddChild(title);
        var checks=new List<string>();var records=new List<object>();
        void Require(bool condition,string description){if(!condition)throw new Exception("MERCHANT_PALETTE: "+description);checks.Add(description);}
        async Task Frames(){for(int i=0;i<6;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        foreach(var palette in palettes)
        {
            title.Text=palette.Item2;var signs=new List<PanelContainer>();
            foreach(int count in new[]{4,8})
            {
                var sign=new PanelContainer {Position=new Vector2(count==4?20:220,44)};sign.SetMeta("merchant_buying",true);sign.AddToGroup("merchant_signs");AddChild(sign);signs.Add(sign);
                var grid=new GridContainer {Columns=4};sign.AddChild(grid);
                for(int i=0;i<count;i++)
                {
                    var cell=(Control)Activator.CreateInstance(cellType,new object[]{i,32})!;grid.AddChild(cell);
                    cellType.GetMethod("Set",new[]{typeof(NativeSlot),typeof(string)})!.Invoke(cell,new object[]{new NativeSlot {ItemId=i==0?stack:singles[i-1],Count=(short)(i==0?25:1)},"For sale at this shop"});
                }
            }
            styler._Process(.3);await Frames();
            foreach(var sign in signs)
            {
                ((StyleBoxTexture)sign.GetThemeStylebox("panel")).ModulateColor=palette.Item3;
                var caption=Descendants(sign).OfType<Label>().Single(l=>l.Text=="BUYING");caption.Text="SELLING";caption.AddThemeColorOverride("font_color",new Color(palette.Item4));
                ((StyleBoxFlat)caption.GetThemeStylebox("normal")).BorderColor=new Color(palette.Item5);
            }
            await Frames();
            foreach(var sign in signs)
            {
                var caption=Descendants(sign).OfType<Label>().Single(l=>l.Text=="SELLING");var grid=Descendants(sign).OfType<GridContainer>().Single();
                Require(sign.Size==new Vector2(180,grid.GetChildCount()==4?71:107),palette.Item1+" / shared Buying-template dimensions");
                Require(Mathf.Abs(caption.GetGlobalRect().GetCenter().X-sign.GetGlobalRect().GetCenter().X)<.1f,palette.Item1+" / centered Selling heading");
                Require(caption.GetGlobalRect().End.Y+4==grid.GetGlobalRect().Position.Y,palette.Item1+" / heading separates from icons");
                Require(GetViewportRect().Encloses(sign.GetGlobalRect()),palette.Item1+" / sign contained");
                foreach(var cell in grid.GetChildren().OfType<Control>())Require(cell.Modulate==Colors.White,palette.Item1+" / native icon color retained");
            }
            string file=(nation==1?"karus":"human")+"-"+palette.Item1+".png";GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);records.Add(new {option=palette.Item1,file});
            foreach(var sign in signs)sign.QueueFree();await Frames();
        }
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new {records,checks,previewOnly=true,installedBuildUnchanged=true},new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("MERCHANT_PALETTE_OK "+checks.Count);
    }
}
