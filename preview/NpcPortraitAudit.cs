using Godot;
using LibreKO;
using LibreKO.Plugins;
using LibreKO.Network;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Collections;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureNpcPortraitAudit(int nation)
    {
        bool widthAudit=OS.GetCmdlineUserArgs().Contains("npc-portrait-width-audit");
        bool negative=OS.GetCmdlineUserArgs().Contains("npc-width-negative-check");
        bool fitAudit=OS.GetCmdlineUserArgs().Contains("npc-portrait-fit-audit") || widthAudit;
        var output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/"+(widthAudit?"npc-portrait-width-audit":fitAudit?"npc-portrait-fit-audit":"npc-portraits-all-audit")+(negative?"/negative":"")));
        System.IO.Directory.CreateDirectory(output);
        foreach(string relative in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck","build/client/source-content/content/npcs.pck","build/client/source-content/content/weapons.pck"})
            if(!ProjectSettings.LoadResourcePack(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../"+relative)),false)) throw new Exception("Portrait resource pack could not be mounted: "+relative);
        var window=GetWindow();window.ContentScaleMode=Window.ContentScaleModeEnum.Disabled;window.Size=new Vector2I(1000,700);
        AddChild(new ColorRect { Color=new Color("252822"),Size=new Vector2(1000,700),MouseFilter=MouseFilterEnum.Ignore });
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        var model=new World();
        DetailCall(model,"BuildNpcDialog");DetailCall(model,"BuildInteractionDialogs");
        var layer=(CanvasLayer)DetailField(model,"_npcLayer")!;layer.Reparent(this);
        var panel=(HudWindow)DetailField(model,"_npcPanel")!;
        var actorType=typeof(World).GetNestedType("Ent",BindingFlags.NonPublic)!;
        var actor=Activator.CreateInstance(actorType,nonPublic:true)!;
        void Field(string name,object value) => actorType.GetField(name,BindingFlags.Instance|BindingFlags.Public)!.SetValue(actor,value);
        void Target(string name,int value) => typeof(World).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(model,value);
        ((IDictionary)DetailField(model,"_ents")!).Add(701,actor);
        var native=new NpcPortraitProbe(model);
        var seedPath=ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/Npcs.json");
        using var seed=JsonDocument.Parse(System.IO.File.ReadAllText(seedPath));
        var prototypes=seed.RootElement.EnumerateArray().GroupBy(p=>p.GetProperty("Id").GetInt32()).ToDictionary(g=>g.Key,g=>g.Last());
        void SetNpc(int id)
        {
            var data=prototypes[id];
            Field("NpcId",id);Field("ModelId",data.GetProperty("ModelId").GetInt32());Field("Level",data.GetProperty("Level").GetInt32());
            Field("NpcType",data.GetProperty("NpcType").GetInt32());Field("IsNpc",true);Field("Name",data.GetProperty("Name").GetString()!);
            Field("Gear",new int[8]);
            Target("_vendorNpcId",701);Target("_npcTalkId",-1);
        }
        const string royalStory="How are you? I am the Enchanter dispatched to Moradon Castle. I offer buffs to help you on your journey. There are three types of buffs that you can receive: attack power, defensive power and enhancement of physical strength, and they can be stacked! Possible payment means are Noah, and, for those people between level 36 and 60, can pay with the [sign of victory] too.";
        void Open(string? story=null)
        {
            DetailCall(model,"CloseNpcDialog");
            if(widthAudit) panel.Body.Size=new Vector2(420,100);
            string name=(string)actorType.GetField("Name")!.GetValue(actor)!;
            bool royal=name.Contains("Royal National Enchanter");
            DetailCall(model,"BeginNpcDialog",name,story??(royal?royalStory:"How can I help you? Select a service below."));
            var options=royal?new[]{"1.   [Buff]Noah/Symbol of Victory","2.   Spirit Aisles' of Mounting","3.   Settlement support free of charge buff"}:new[]{"1.   Tell me more...","2.   Close"};
            foreach(string option in options) DetailCall(model,"AddNpcMenuButton",option,(Action)(()=>{}));
            DetailCall(model,"EndNpcDialog",options.Length);
        }
        async Task WaitFrame() => await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        async Task Settle()
        {
            // Layout needs actual frames even when a busy GPU makes a short timer expire in one frame.
            for(int i=0;i<8;i++) await WaitFrame();
        }
        string prefix=nation==1?"karus":"human";
        async Task Shot(string suffix)
        {
            await WaitFrame();using var image=GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(output,prefix+"-portrait-"+suffix+".png"));
        }
        var widthChecks=new List<object>();
        string[] CheckWidth(ClassicDetailPanel frame,string stage)
        {
            float left=frame.GlobalPosition.X+18,right=left+327;
            var violations=new List<string>();
            if(frame.Size.X!=363 || panel.Size.X!=363 || frame.ContentScroll.Size.X>327.5f) violations.Add("frame/content width");
            foreach(var control in Descendants(panel.Body).OfType<Control>().Where(c=>c.IsVisibleInTree()))
            {
                var rect=control.GetGlobalRect();
                if(rect.Position.X<left-.5f || rect.End.X>right+.5f) violations.Add(control.GetType().Name+" "+control.Name+" "+rect);
            }
            widthChecks.Add(new {stage,frame=frame.GetGlobalRect().ToString(),content=frame.ContentScroll.GetGlobalRect().ToString(),
                body=panel.Body.GetGlobalRect().ToString(),minimum=panel.Body.GetCombinedMinimumSize().ToString(),violations});
            return violations.ToArray();
        }
        ClassicDetailPanel shell;
        int widthScreens=0;
        if(widthAudit)
        {
            // Live extenders attach while hidden; the portrait is available on the very first show.
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            _windowData!.NpcPortraitSource=()=>native.NpcPortrait;
            shell=(ClassicDetailPanel)CharacterDetailsSkin.Apply(panel.Body)!;
            SetNpc(18004);panel.Body.Size=new Vector2(420,100);
            DetailCall(model,"BeginNpcDialog","[Grand Merchant] Kaishan","");
            foreach(string option in new[]{"1.   Kaishan's trust","2.   1st job change","3.   Redistributions"}) DetailCall(model,"AddNpcMenuButton",option,(Action)(()=>{}));
            DetailCall(model,"EndNpcDialog",3);await Settle();
            var violations=CheckWidth(shell,"cold Kaishan: native 420px body, portrait available before first show");
            if(negative)
            {
                if(violations.Length==0) throw new Exception("The old build did not reproduce the live width regression");
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-negative-verification.json"),JsonSerializer.Serialize(widthChecks,new JsonSerializerOptions {WriteIndented=true}));
                GD.Print("NPC_WIDTH_NEGATIVE_OK: "+string.Join("; ",violations));
                _windowData.NpcPortraitSource=null;NpcPortraitCache.Clear();layer.Free();model.Free();net.Free();return;
            }
            if(violations.Length>0) throw new Exception("Cold native opening escaped the frame: "+string.Join("; ",violations));
            for(int i=0;i<100 && !Descendants(shell).OfType<NpcPortraitCircle>().Single().IsReady;i++) await WaitFrame();
            await Shot("kaishan-cold");widthScreens++;
            DetailCall(model,"CloseNpcDialog");_windowData.NpcPortraitSource=null;
        }
        else shell=null!;
        SetNpc(31508);Target("_npcTalkId",701);
        DetailCall(model,"NpcTick",.01);
        if((int)DetailField(model,"_npcTalkId")! !=-1) throw new Exception("Delayed NPC reply regression fixture did not clear the transient target");
        Open();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(!widthAudit) shell=(ClassicDetailPanel)CharacterDetailsSkin.Apply(panel.Body)!;
        await Settle();await Shot("baseline");
        NpcPortraitCache.Clear();_windowData!.NpcPortraitSource=()=>native.NpcPortrait;
        var fixtures=new (string Key,int Id)[] {
            ("royal-60",31508),("royal-50",31507),("royal-80",9010),("guard",13013),
            ("hostess",12000),("neria",16096),("sundries",12120),("blacksmith",14301),
            ("jeweller",31402),("dragon",31005),("karus-guard",21010),("karus-manager",18031),("vendor",16085) };
        if(fitAudit) fixtures=[..fixtures,("magpie",31506)];
        if(widthAudit) fixtures=[..fixtures,("kaishan",18004)];
        var results=new List<object>();
        var textures=new Dictionary<string,Texture2D>();
        foreach(var fixture in fixtures)
        {
            SetNpc(fixture.Id);
            // A visible world NPC has its resource loaded before the player opens dialogue.
            var scene=(PackedScene?)typeof(World).GetMethod("ResolveMobScene",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(model,new object[]{prototypes[fixture.Id].GetProperty("ModelId").GetInt32()});
            if(scene==null) throw new Exception("Missing fixture NPC appearance: "+fixture.Key);
            scene.Instantiate<Node3D>().Free();
            int before=NpcPortraitCache.RenderRequests;
            Open();await Settle();
            var descriptor=native.NpcPortrait??throw new Exception("Delayed-reply NPC descriptor missing: "+fixture.Key);
            var texture=await NpcPortraitCache.Get(this,descriptor);
            for(int i=0;i<100 && !Descendants(shell).OfType<NpcPortraitCircle>().Single().IsReady;i++) await WaitFrame();
            var portrait=Descendants(shell).OfType<NpcPortraitCircle>().Single();
            if(texture==null || !portrait.IsReady || portrait.PortraitTexture!=texture) throw new Exception("NPC portrait is missing: "+fixture.Key);
            if(textures.TryGetValue(descriptor.AppearanceKey,out var shared) && shared!=texture) throw new Exception("Equal appearances did not share a cached texture");
            textures[descriptor.AppearanceKey]=texture;
            if(NpcPortraitCache.ActiveRenderers!=0 || NpcPortraitCache.SceneNodeCount!=0 || NpcPortraitCache.RetainedViewportCount!=0) throw new Exception("Portrait retained live 3D resources");
            await Settle();
            if(widthAudit && CheckWidth(shell,"fixture "+fixture.Key).Length>0) throw new Exception("NPC fixture exceeds its actual frame: "+fixture.Key);
            string expected=descriptor.Name.StartsWith('[') && descriptor.Name.EndsWith(']')?descriptor.Name[1..^1]:descriptor.Name;
            if(portrait.DisplayedName!=expected || portrait.DisplayedLevel!="Lv. "+descriptor.Level) throw new Exception("NPC name or level is stale");
            var intro=Descendants(shell).OfType<HBoxContainer>().Single(c=>c.Name=="npc_portrait_introduction");
            var description=intro.GetChildren().OfType<PanelContainer>().Single();
            if(portrait.Size.X!=NpcPortraitCircle.ColumnWidth || description.Visible && description.GetRect().End.X>intro.Size.X+.5f) throw new Exception("Portrait/paragraph bounds overflow");
            foreach(var label in Descendants(portrait).OfType<Label>())
                if(!new Rect2(portrait.GlobalPosition,portrait.Size).Encloses(new Rect2(label.GlobalPosition,label.Size))) throw new Exception("NPC caption exceeds its portrait column");
            using(var image=texture.GetImage()) if(image.GetUsedRect().Size.X<25 || image.GetUsedRect().Size.Y<25) throw new Exception("Captured NPC portrait is blank");
            await Shot(fixture.Key);
            results.Add(new { fixture.Key,fixture.Id,descriptor.Name,descriptor.Level,descriptor.AppearanceKey,portraitSize=portrait.Size.ToString(),
                firstCaptureMs=NpcPortraitCache.RenderRequests>before?NpcPortraitCache.LastCaptureMilliseconds:0,renderRequests=NpcPortraitCache.RenderRequests,
                framing=NpcPortraitCache.LastFraming,target=NpcPortraitCache.LastTarget.ToString() });
        }
        if(textures.Count<=8 || NpcPortraitCache.RenderRequests!=textures.Count) throw new Exception("NPC coverage is still limited to eight appearances");
        SetNpc(31508);Open();await Settle();
        var royalTexture=Descendants(shell).OfType<NpcPortraitCircle>().Single().PortraitTexture;
        using var original=royalTexture!.GetImage();byte[] pixels=original.GetData();
        int initialRenders=NpcPortraitCache.RenderRequests;
        for(int i=0;i<12;i++)
        {
            SetNpc(i%2==0?31507:31508);Open();await WaitFrame();
            var caption=Descendants(shell).OfType<NpcPortraitCircle>().Single();
            if(caption.DisplayedLevel!="Lv. "+(i%2==0?50:60) || caption.PortraitTexture!=royalTexture) throw new Exception($"First-frame NPC caption or cached face is stale: iteration={i}, level={caption.DisplayedLevel}, sameTexture={caption.PortraitTexture==royalTexture}, ready={caption.IsReady}");
            if(await NpcPortraitCache.Get(this,native.NpcPortrait!)!=royalTexture) throw new Exception("Reopening replaced the cached portrait");
        }
        if(NpcPortraitCache.RenderRequests!=initialRenders) throw new Exception("Reopening restarted the portrait renderer");
        using(var after=royalTexture.GetImage()) if(!pixels.SequenceEqual(after.GetData())) throw new Exception("Cached NPC texture was redrawn");
        await Shot("reopened");
        Open("");await Settle();
        if(!Descendants(shell).OfType<NpcPortraitCircle>().Single().IsVisibleInTree()) throw new Exception("Empty NPC introduction hid its portrait");
        if(widthAudit && CheckWidth(shell,"reopened Royal with empty introduction").Length>0) throw new Exception("Empty introduction reused an expanded body width");
        await Shot("empty-introduction");
        int fitScreens=0;
        if(fitAudit)
        {
            SetNpc(31506);
            DetailCall(model,"ShowQuestView",new LibreKO.Network.QuestView(570,1,21,true,true,false,false,LibreKO.Network.QuestViewState.Available,0,
                "Hit and Miss Festival Begins","Please kill 10 of each monsters [Paramun, Doom Solider, Troll Berserker, Giant Golem]","",
                new LibreKO.Network.QuestObjectives(570,false,[new(10,[1],"Paramun"),new(10,[2],"Doom Soldier"),new(10,[3],"Troll Berserker"),new(10,[4],"Giant Golem")]),[0,0,0,0],[new(false,2,0,20000000,0)],[]));
            await Settle();
            var wide=Descendants(shell).OfType<NpcPortraitCircle>().Single();
            var name=Descendants(wide).OfType<Label>().Single(l=>l.Text==wide.DisplayedName);
            if(wide.CaptionWidth<300 || name.GetLineCount()!=1 || wide.DisplayedName!="[Lunar Lady] Magpie") throw new Exception("Available caption width still wraps Magpie's name");
            var magpieScroll=(ScrollContainer)DetailField(model,"_npcQuestScroll")!;
            var reward=((VBoxContainer)DetailField(model,"_npcQuestContent")!).GetChildren().OfType<PanelContainer>().Last();
            var questSize=((Control)DetailField(model,"_npcQuestContent")!).GetCombinedMinimumSize();
            if(shell.Modulate.A<1) throw new Exception("Magpie opening is not visible: frames="+typeof(ClassicDetailPanel).GetField("_openingFrames",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shell)+", snapshot="+typeof(ClassicDetailPanel).GetField("_openingSnapshot",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shell)+", body="+panel.Body.Size+", minimum="+panel.Body.GetCombinedMinimumSize());
            if(magpieScroll.GetVScrollBar().Visible || !magpieScroll.GetGlobalRect().Encloses(reward.GetGlobalRect()) || panel.Position!=((GetViewportRect().Size-panel.Size)/2).Floor()) throw new Exception($"Magpie page fit: frame={panel.GetGlobalRect()}, centre={((GetViewportRect().Size-panel.Size)/2).Floor()}, scroll={magpieScroll.GetGlobalRect()}, min={magpieScroll.CustomMinimumSize}, max={magpieScroll.CustomMaximumSize}, rail={magpieScroll.GetVScrollBar().Visible}, reward={reward.GetGlobalRect()}, content={questSize}");
            await Shot("magpie-quest");fitScreens++;
            if(widthAudit && CheckWidth(shell,"Magpie quest").Length>0) throw new Exception("Magpie quest exceeds its frame");
            int closeCalls=0,selected=-1;
            void GuardMenu()
            {
                SetNpc(13013);DetailCall(model,"CloseNpcDialog");
                if(widthAudit) panel.Body.Size=new Vector2(420,100);
                DetailCall(model,"BeginNpcDialog","[Guard] Patrick","What mission are you going to undertake?");
                string[] topics={"Orc Watcher hunting","Patrick's trust","[In progress] Bandicoot hunt","[In progress] Kecoon hunting","Bulcan hunt","Wild bulcan hunting","Kekoon warrior hunt","Subdual of Gavolt","Kekoon Captain hunt","Subdual of Vulture","Giant bulcan hunting","Werewolf elimination","Subdual of Silan","Giant Gavolt hunting","Werewolf skin","Glyptodont hunt","Gloomwing hunt"};
                for(int i=0;i<topics.Length;i++) { int index=i;DetailCall(model,"AddNpcMenuButton",$"{i+1}.   {topics[i]}",(Action)(()=>selected=index)); }
                DetailCall(model,"AddNpcMenuButton","18.   Close",(Action)(()=> { closeCalls++;DetailCall(model,"CloseNpcDialog"); }));
                DetailCall(model,"EndNpcDialog",18);
                ((ScrollContainer)DetailField(model,"_npcMenuScroll")!).ScrollVertical=0;
            }
            foreach(int height in new[]{700,540,900})
            {
                window.Size=new Vector2I(1000,height);await Settle();GuardMenu();await Settle();
                var menu=(ScrollContainer)DetailField(model,"_npcMenuScroll")!;
                var list=(VBoxContainer)DetailField(model,"_npcMenuBox")!;
                var close=Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_menu_close");
                var bounds=new Rect2(Vector2.Zero,GetViewportRect().Size);
                if(widthAudit && CheckWidth(shell,"Patrick "+height).Length>0) throw new Exception("Patrick exceeds his frame");
                if(!bounds.Encloses(panel.GetGlobalRect()) || !close.IsVisibleInTree() || !panel.GetGlobalRect().Encloses(close.GetGlobalRect())) throw new Exception("NPC frame or fixed Close escaped the viewport: "+height+" / "+panel.GetGlobalRect()+" / "+bounds);
                if(!menu.GetVScrollBar().Visible || menu.Size.Y>374.5f) throw new Exception("Portrait and long NPC menu were not budgeted together: "+height);
                if(list.GetChildren().OfType<Button>().Last().Visible) throw new Exception("Close appears both in the scrollable list and in its fixed footer");
                await Shot("guard-menu-"+height+"-top");fitScreens++;
                if(height!=900)
                {
                    menu.ScrollVertical=(int)list.Size.Y;await Settle();
                    var last=list.GetChildren().OfType<Button>().ElementAt(16);
                    if(!menu.GetGlobalRect().Encloses(last.GetGlobalRect()) || !close.IsVisibleInTree()) throw new Exception("Last Patrick mission or fixed Close is inaccessible");
                    if(widthAudit && CheckWidth(shell,"Patrick "+height+" bottom").Length>0) throw new Exception("Scrolled Patrick menu exceeds its frame");
                    last.EmitSignal(BaseButton.SignalName.Pressed);if(selected!=16) throw new Exception("Scrolled mission callback changed");
                    await Shot("guard-menu-"+height+"-bottom");fitScreens++;
                }
                if(height==700)
                {
                    close.EmitSignal(Control.SignalName.MouseEntered);await Shot("guard-menu-close-hover");fitScreens++;
                    close.EmitSignal(Control.SignalName.MouseExited);
                }
                int before=closeCalls;close.EmitSignal(BaseButton.SignalName.Pressed);
                if(panel.Visible || closeCalls!=before+1) throw new Exception("Fixed Close lost the original native callback");
            }
            GuardMenu();
            var fallbackList=(VBoxContainer)DetailField(model,"_npcMenuBox")!;
            var removedClose=fallbackList.GetChildren().OfType<Button>().Last();fallbackList.RemoveChild(removedClose);removedClose.QueueFree();
            DetailCall(model,"EndNpcDialog",17);await Settle();
            Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_menu_close").EmitSignal(BaseButton.SignalName.Pressed);
            if(panel.Visible || (bool)DetailField(model,"_npcDialogShown")!) throw new Exception("A server menu without Close lost its native window-close fallback");
            window.Size=new Vector2I(1000,700);SetNpc(31508);Open();await Settle();
            if(Descendants(shell).OfType<Button>().Single(b=>b.Name=="npc_menu_close").Visible) throw new Exception("A short NPC menu retained its overflow footer");
            if(widthAudit && CheckWidth(shell,"short Royal after tall Patrick").Length>0) throw new Exception("A short page retained expanded content");
            GD.Print("NPC_PORTRAIT_FIT_OK: 540/700/900px viewports, 17 missions, fixed native Close, bottom option callbacks, full-width Magpie name and short-menu restoration");
        }
        Open();await Settle();
        int eligible=0;
        foreach(var data in prototypes.Values.Where(p=>!p.GetProperty("IsMonster").GetBoolean()))
        {
            SetNpc(data.GetProperty("Id").GetInt32());
            var descriptor=native.NpcPortrait;
            if(descriptor==null || descriptor.Name!=data.GetProperty("Name").GetString() || descriptor.Level!=data.GetProperty("Level").GetInt32()) throw new Exception("An NPC prototype was excluded from portrait eligibility");
            eligible++;
        }
        SetNpc(31508);await Settle();
        Field("IsNpc",false);if(native.NpcPortrait!=null) throw new Exception("Player was treated as an NPC");Field("IsNpc",true);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(),true);
        var activePortrait=Descendants(shell).OfType<NpcPortraitCircle>().Single();
        async Task<object> Sample(bool visible)
        {
            activePortrait.Visible=visible;await Settle();
            var cpu=new List<double>();var gpu=new List<double>();var intervals=new List<double>();
            ulong previous=Time.GetTicksUsec();
            for(int i=0;i<120;i++)
            {
                await WaitFrame();ulong now=Time.GetTicksUsec();intervals.Add((now-previous)/1000.0);previous=now;
                cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()));gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
                if(NpcPortraitCache.RetainedViewportCount!=0 || NpcPortraitCache.RenderRequests!=initialRenders) throw new Exception("Cached portrait started 3D work");
            }
            double Median(List<double> values) => values.Order().ElementAt(values.Count/2);
            return new { visible,frames=120,medianRenderCpuMs=Median(cpu),medianRenderGpuMs=Median(gpu),medianFrameMs=Median(intervals) };
        }
        var withPortrait=await Sample(true);var withoutPortrait=await Sample(false);activePortrait.Visible=true;
        int actualRenders=NpcPortraitCache.RenderRequests,hits=NpcPortraitCache.CacheHits,entries=NpcPortraitCache.EntryCount;
        long bytes=NpcPortraitCache.CachedTextureBytes;
        NpcPortraitCache.Clear();
        // Exercise the memory bound without exporting a catalogue of NPC photographs.
        for(int i=0;i<NpcPortraitCache.Capacity+6;i++)
        {
            var fixture=new GameNpcPortrait("cache-pressure-"+i,"Cache pressure fixture",1,()=> {
                var root=new Node3D();root.AddChild(new MeshInstance3D { Mesh=new SphereMesh() });return root;
            });
            if(await NpcPortraitCache.Get(this,fixture)==null) throw new Exception("Cache capacity silently disabled further NPC portraits");
        }
        await WaitFrame();
        if(NpcPortraitCache.EntryCount!=NpcPortraitCache.Capacity || NpcPortraitCache.Evictions!=6 || NpcPortraitCache.RetainedViewportCount!=0) throw new Exception("Portrait cache memory bound failed");
        var cachePressure=new { captures=NpcPortraitCache.RenderRequests,entries=NpcPortraitCache.EntryCount,evictions=NpcPortraitCache.Evictions,textureBytes=NpcPortraitCache.CachedTextureBytes };
        var binary=ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll");
        if(widthAudit) GD.Print($"NPC_WIDTH_BOUNDS_OK: {widthChecks.Count} declared-frame checks, cold portrait source, forced 420px native body, empty/populated introductions, three heights and scroll endpoints");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-portrait-verification.json"),JsonSerializer.Serialize(new {
            eligibleNpcPrototypes=eligible,distinctAppearances=entries,renderRequests=actualRenders,cacheHits=hits,activeRenderers=0,sceneNodes=0,retainedViewports=0,
            textureBytes=bytes,resolution=NpcPortraitCache.Resolution,reopenings=12,delayedReplyTargetVerified=true,nameAboveLevelVerified=true,emptyIntroductionVerified=true,
            fixtures=results,cachePressure,withPortrait,withoutPortrait,fitScreens,portraitMenuFitVerified=fitAudit,widthScreens,widthChecks,coldOpeningWidthVerified=widthAudit,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(binary))),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(binary)!,"LibreKO.dll"))))
        },new JsonSerializerOptions { WriteIndented=true }));
        _windowData.NpcPortraitSource=null;NpcPortraitCache.Clear();layer.Free();model.Free();net.Free();
        GD.Print($"NPC_PORTRAITS_ALL_OK: {eligible} NPC prototypes, {entries} real appearances, delayed reply, names above levels, 12 first-frame reopenings, zero retained viewports, bounded texture cache and 240 performance frames");
    }
}
