using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureUpstreamIntegration(PluginGame game, int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/upstream-20261006-audit");
        if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} directory)output=directory;
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;
        GetWindow().Size=new Vector2I(1280,800);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(1280,800),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        Game.Session.Self.Sheet.SeedWealth(1_234_567,50_000);
        Native.Set(Game.Session.Self, "LastEnter",new MyInfo {Nation=nation,Class=nation==1?105:205,Name="Classic Preview",Gear=new int[8]}); Game.Session.Self.Inventory.Reset(new LibreKO.Domain.ItemSlot[LibreKO.Domain.InventoryConstants.InventoryTotal]);
        var screens=new List<object>();var checks=new List<string>();
        void Require(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        async Task Frames(){for(int i=0;i<8;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        foreach(string state in new[]{"pus-empty","pus-category","pus-details","pus-gift-search","pus-gift-card","pus-gift-set","pus-short","mail-store","mail-attachments","pus-compact"})
        {
            GetWindow().Size=state=="pus-compact"?new Vector2I(800,600):new Vector2I(1280,800);
            var world=PreviewFixtures.Prepared(new WorldRoot());
            var bridge=Native.Call(world, "CreatePluginAdapters")!;var gameWindows=Native.Get<IGameWindows>(bridge, "Windows")!;
            var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Native.Call(bridge, "AttachTo", game);
            Node surface=state.StartsWith("pus-")
                ? (Node)Native.Call(world, "Preview.BuildPowerUpStore", state)!
                : (Node)Native.Call(world, state=="mail-store"?"BuildMailStoreUiPreview":"BuildMailReadManyUiPreview")!;
            var windows=Descendants(surface).OfType<HudWindow>().ToArray();
            if(surface is HudWindow own)windows=windows.Append(own).ToArray();
            AddChild(surface);
            // Apply the skins the plugin registers for these ids in the game (Plugin.cs), not the generic frame.
            foreach(var window in windows)
            {
                if(ClassicMailSkin.WindowIds.Contains(window.Id)){NativeCommunication.Prepare(window,world);ClassicMailSkin.Apply(window.Body);}
                else if(window.Id=="shoppingmall"){NativeMarket.Prepare(window,world);ClassicStoreSkin.Apply(window.Body);}
                else{NativeMarket.Prepare(window,world);ClassicSkin.Apply(window);}
            }
            await Frames();
            foreach(var window in windows.Where(w=>w.Visible))window.Position=((GetViewportRect().Size-window.Size)/2).Round();
            await Frames();
            foreach(var window in windows.Where(w=>w.Visible))Require(GetViewportRect().Grow(.1f).Encloses(window.GetGlobalRect()),state+" window fits viewport: "+window.GetGlobalRect());
            if(state.StartsWith("pus-"))Require(gameWindows.IsOpen("shoppingmall"),"PUS bridge retains shoppingmall identifier: "+state);
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
