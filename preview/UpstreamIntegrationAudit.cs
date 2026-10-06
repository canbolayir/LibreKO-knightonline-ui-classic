using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureUpstreamIntegration(PluginGame game, int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/upstream-20261006-audit");
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size=new Vector2I(1280,800);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(1280,800),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        net.Sheet.SeedWealth(1_234_567,50_000);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo {Nation=nation,Class=nation==1?105:205,Name="Classic Preview",Gear=new int[8],Inventory=new LibreKO.Domain.ItemSlot[LibreKO.Domain.InventoryConstants.InventoryTotal]});
        var screens=new List<object>();var checks=new List<string>();
        void Require(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        async Task Frames(){for(int i=0;i<8;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        foreach(string state in new[]{"pus-empty","pus-category","pus-details","pus-gift-search","pus-gift-card","pus-gift-set","pus-short","mail-store","mail-attachments","pus-compact"})
        {
            GetWindow().Size=state=="pus-compact"?new Vector2I(800,600):new Vector2I(1280,800);
            var world=new World();
            var bridge=(IGameWindows)Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
            var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
            attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());
            Node surface=state.StartsWith("pus-")
                ? (Node)typeof(World).GetMethod("BuildPowerUpStoreUiPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(world,new object[]{state})!
                : (Node)typeof(World).GetMethod(state=="mail-store"?"BuildMailStoreUiPreview":"BuildMailReadManyUiPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(world,null)!;
            var windows=Descendants(surface).OfType<HudWindow>().ToArray();
            if(surface is HudWindow own)windows=windows.Append(own).ToArray();
            foreach(var window in windows)ClassicSkin.ExtendWindow(window.Body);
            AddChild(surface);await Frames();
            foreach(var window in windows.Where(w=>w.Visible))window.Position=((GetViewportRect().Size-window.Size)/2).Round();
            await Frames();
            foreach(var window in windows.Where(w=>w.Visible))Require(GetViewportRect().Grow(.1f).Encloses(window.GetGlobalRect()),state+" window fits viewport: "+window.GetGlobalRect());
            if(state.StartsWith("pus-"))Require(bridge.IsOpen("shoppingmall"),"PUS bridge retains shoppingmall identifier: "+state);
            string file=(nation==1?"karus":"human")+"-"+state+".png";
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);screens.Add(new{state,file});
            surface.Free();world.Free();await Frames();
        }
        var assembly=ProjectSettings.GlobalizePath("res://../bin/KnightOnlineUiClassic.dll");
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{pluginHash=Hash(assembly),clientHash=Hash(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/.godot/mono/temp/bin/ExportRelease/LibreKO.dll")),screens,checks},new JsonSerializerOptions{WriteIndented=true}));
        net.Free();GD.Print("UPSTREAM_INTEGRATION_AUDIT_OK "+checks.Count);
    }
}
