using Godot;
using LibreKO;
using LibreKO.Network;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureNpcServices(int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/upstream-4772e7a-audit");
        if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} directory)output=directory;
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck","build/client/source-content/content/characters.pck","build/client/source-content/content/armor.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size=new Vector2I(1280,800);
        AddChild(new ColorRect{Color=new Color("252822"),Size=new Vector2(1280,800),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo{Nation=nation,Class=nation==1?108:208,Race=nation==1?2:12,Name="Service Preview",Gear=new int[8],Inventory=new LibreKO.Domain.ItemSlot[LibreKO.Domain.InventoryConstants.InventoryTotal]});
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var screens=new List<object>();var checks=new List<string>();
        async Task Frames(){for(int i=0;i<12;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        foreach(var spec in new[]{("gender","BuildGenderChangeUiPreview","_genderShown",12),("nation","BuildNationTransferUiPreview","_transferShown",13),("search","BuildMerchantSearchUiPreview","_merchantSearchShown",14)})
        {
            var world=new World();
            var panel=(Control)typeof(World).GetMethod(spec.Item2,flags)!.Invoke(world,null)!;
            AddChild(panel);await Frames();panel.Position=((GetViewportRect().Size-panel.Size)/2).Round();await Frames();
            if(!GetViewportRect().Grow(.1f).Encloses(panel.GetGlobalRect()))throw new Exception(spec.Item1+" exceeds viewport");
            checks.Add(spec.Item1+" rendered native window fits viewport");
            string file=(nation==1?"karus":"human")+"-"+spec.Item1+".png";
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);screens.Add(new{state=spec.Item1,file});
            typeof(World).GetMethod("BuildEscapeStack",flags)!.Invoke(world,null);
            var entries=(List<(Func<bool>,Action)>)typeof(World).GetField("_escapeStack",flags)!.GetValue(world)!;
            var entry=entries[spec.Item4];
            if(!entry.Item1())throw new Exception(spec.Item1+" not registered as open in Escape stack");
            entry.Item2();
            if(entry.Item1()||panel.Visible)throw new Exception(spec.Item1+" Escape callback failed to close");
            checks.Add(spec.Item1+" existing Escape stack closes its native panel");
            panel.Free();world.Free();await Frames();
        }
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-services-verification.json",JsonSerializer.Serialize(new{clientHash=Hash(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/.godot/mono/temp/bin/ExportRelease/LibreKO.dll")),screens,checks},new JsonSerializerOptions{WriteIndented=true}));
        net.Free();GD.Print("NPC_SERVICES_AUDIT_OK "+checks.Count);
    }
}
