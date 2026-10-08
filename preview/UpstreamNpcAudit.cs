using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureUpstreamNpcAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/upstream-7f21442-audit/native");
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(1280,800);
        AddChild(new ColorRect{Color=new Color("252822"),Size=new Vector2(1280,800),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo{Nation=nation,Class=nation==1?108:208,Race=nation==1?2:12,Name="Service Preview",Gear=new int[8],Inventory=new LibreKO.Domain.ItemSlot[LibreKO.Domain.InventoryConstants.InventoryTotal]});
        net.Sheet.SeedWealth(1_180_000,0);ItemData.EnsureLoaded();
        var world=new World();DetailCall(world,"BuildInventoryPanel");
        var inventory=(Control)DetailField(world,"_invContent")!;inventory.Visible=false;world.AddChild(inventory);
        foreach(string method in new[]{"BuildKingWindows","BuildSiegeWindows","BuildSpecialAuctionPanel","BuildItemCombinePanel","BuildCombineRecipeBook","BuildMarketPricePanel","FortuneInit","DisguiseInit","BuildBifrostUi","BuildInZoneLeaveUi"})DetailCall(world,method);
        var windows=Descendants(world).OfType<HudWindow>().ToArray();
        foreach(var layer in world.GetChildren().OfType<CanvasLayer>().ToArray())layer.Reparent(this);
        var screenshots=new List<object>();var checks=new List<string>();
        async Task Frames(){for(int i=0;i<10;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        void Require(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        async Task Capture(string state,HudWindow? window=null)
        {
            await Frames();
            if(window!=null){window.Position=((GetViewportRect().Size-window.Size)/2).Round();await Frames();Require(GetViewportRect().Encloses(window.GetGlobalRect()),state+" fits viewport");}
            string file=(nation==1?"karus":"human")+"-"+state+".png";
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);screenshots.Add(new{file,id=state});
        }
        foreach(var window in windows)
        {
            foreach(var other in windows)other.Visible=false;
            window.Visible=true;await Frames();window.Position=((GetViewportRect().Size-window.Size)/2).Round();await Frames();
            if(!GetViewportRect().Encloses(window.GetGlobalRect()))throw new Exception(window.Id+" exceeds viewport: "+window.Size);
            checks.Add(window.Id+" native composition fits viewport");
            string file=(nation==1?"karus":"human")+"-"+window.Id+".png";GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);
            var bounds=Descendants(window).OfType<Control>().Where(c=>c.IsVisibleInTree()).Select(c=>new{name=c.Name.ToString(),type=c.GetType().Name,x=c.GlobalPosition.X,y=c.GlobalPosition.Y,w=c.Size.X,h=c.Size.Y}).ToArray();
            screenshots.Add(new{file,id=window.Id,bounds});
        }
        foreach(var window in windows)window.Visible=false;
        DetailCall(world,"OpenCombineRecipeBook");
        var book=(CombineBook)DetailField(world,"_combineBook")!;
        Require(book.Recipes.Length>0&&book.Categories.Length>0,"Combination book loads real generated content");
        var recipe=book.Recipes.First(r=>r.Listed&&ItemData.Get(r.Result)!=null);
        DetailCall(world,"SelectCombineCategory",recipe.Category);DetailCall(world,"SelectCombineRecipe",recipe.Id);
        var bookPanel=(HudWindow)DetailField(world,"_combineBookPanel")!;
        await Capture("combination-recipe-selected",bookPanel);bookPanel.Visible=false;
        DetailCall(world,"EnsureAuctionTable");DetailCall(world,"RenderAuctionSchedule");
        Require(((Array)DetailField(world,"_auctionTable")!).Length>0,"Akara schedule loads generated lots");
        var auctionPanel=(HudWindow)DetailField(world,"_specialAuctionPanel")!;
        typeof(World).GetField("_auctionToday",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,new AuctionToday(SpecialAuction.Bidding,1,3600,1,Array.Empty<AuctionOffer>()));
        var tab=Enum.Parse(typeof(World).GetNestedType("AuctionTab",BindingFlags.NonPublic)!,"Schedule");
        DetailCall(world,"ShowAuctionTab",tab);auctionPanel.Visible=true;
        await Capture("akara-schedule-populated",auctionPanel);auctionPanel.Visible=false;
        var forms=(DisguiseForm[])DetailField(world,"_disguiseTable")!;
        Require(forms.Length>0,"Transformation list loads generated forms");
        var groups=Disguise.Groups(Disguise.FormsFor(forms,forms.First(f=>Disguise.Allows(f.Access,0)).Item,0));
        Require(groups.Count>0,"Non-premium transformation fixture has selectable groups");
        typeof(World).GetField("_disguiseGroups",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,groups);
        DetailCall(world,"PickDisguiseGroup",0);
        var disguisePanel=(HudWindow)DetailField(world,"_disguisePanel")!;disguisePanel.Visible=true;
        await Capture("transformation-selected",disguisePanel);disguisePanel.Visible=false;
        var deck=(FortuneDeck)DetailField(world,"_fortuneDeck")!;
        Require(deck.Readings.Length>0&&deck.FramesOf(Fortune.SpinFrames).Length>0&&deck.FramesOf(Fortune.FinalFrames).Length>0,"Fortune deck includes readings and both animation sequences");
        DetailCall(world,"OpenFortune");var fortunePanel=(HudWindow)DetailField(world,"_fortunePanel")!;
        await Capture("fortune-card-fan",fortunePanel);
        DetailCall(world,"PickFortuneCard",0);await Capture("fortune-spin",fortunePanel);
        await ToSignal(GetTree().CreateTimer(2.4),SceneTreeTimer.SignalName.Timeout);
        Require(((Control)DetailField(world,"_fortuneName")!).Visible,"Fortune spin completes into the reading");
        await Capture("fortune-reading",fortunePanel);fortunePanel.Visible=false;
        var zone=typeof(World).GetField("_zone",BindingFlags.Instance|BindingFlags.NonPublic)!;
        void SetZone(int value)=>zone.SetValue(world,Convert.ChangeType(value,zone.FieldType));
        PluginHost.Ui.ReplaceDialogs(request=>new KnightOnlineUiClassic.Windows.MessageBox(request));
        SetZone(86);DetailCall(world,"RefreshInZoneLeaveUi");
        var banner=(Control)DetailField(world,"_inZoneLeaveBanner")!;await Frames();banner.Position=new Vector2(480,24);
        Require(banner.Visible,"UTC leave banner appears in the event zone");
        DetailCall(world,"OnInZoneLeavePressed");var notice=NativeEvents.LeavePrompt(world)!;notice.Reparent(this);
        Require(Descendants(notice).Any(n=>n.GetType().FullName=="KnightOnlineUiClassic.Windows.MessageBox"),"Event leave uses the Classic plugin dialog");
        DetailCall(world,"OnInZoneLeavePressed");Require(ReferenceEquals(notice,NativeEvents.LeavePrompt(world)),"Repeated leave clicks retain one confirmation");
        await Capture("utc-leave-confirmation");
        var dialog=Descendants(notice).OfType<Control>().First(n=>n.GetType().FullName=="KnightOnlineUiClassic.Windows.MessageBox");
        dialog._Input(new InputEventKey{Pressed=true,Keycode=Key.Escape});await Frames();
        Require(NativeEvents.LeavePrompt(world)==null&&banner.Visible,"Escape cancels leaving and retains the event banner");
        DetailCall(world,"OnInZoneLeavePressed");notice=NativeEvents.LeavePrompt(world)!;notice.Reparent(this);
        SetZone(21);DetailCall(world,"RefreshInZoneLeaveUi");await Frames();
        Require(NativeEvents.LeavePrompt(world)==null&&!banner.Visible,"Changing zone dismisses a stale leave confirmation");
        DetailCall(world,"OnInZoneLeavePressed");Require(NativeEvents.LeavePrompt(world)==null,"Leave confirmation cannot open outside an event zone");
        string[] required={"assets/ui/item_combine.json","assets/ui/special_auction.json","assets/skills/disguise.json","assets/ui/fortune/fortune.json","assets/ui/fortune/back.png","assets/ui/fortune/back_hover.png"};
        var missing=required.Where(p=>p.EndsWith(".png") ? !ResourceLoader.Exists("res://"+p) : !Godot.FileAccess.FileExists("res://"+p)).ToArray();
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{checks,screenshots,missingContent=missing,liveTransactions=false},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("UPSTREAM_NPC_AUDIT_OK "+checks.Count+"; missing content="+missing.Length);
        DetailCall(world,"DisguiseDispose");
        foreach(var layout in Descendants(this).OfType<HudLayout>().ToArray())
            foreach(string name in new[]{"_moveGrip","_corner"})
                if(typeof(HudLayout).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(layout) is Node overlay&&GodotObject.IsInstanceValid(overlay))overlay.Free();
        foreach(var layer in GetChildren().OfType<CanvasLayer>().ToArray())layer.Free();
        foreach(var field in typeof(World).GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
            if(field.GetValue(world) is Node orphan&&GodotObject.IsInstanceValid(orphan)&&orphan.GetParent()==null)orphan.Free();
        world.Free();net.Free();await Frames();
    }
}
