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
    /// <summary>NPC portraits belong to the Classic NPC dialogue, the fallback of the bundled 2.x quest windows: the art is turned off.</summary>
    private async Task CaptureNpcPortraitAudit(int nation)
    {
        UseClassicFallback();
        var output=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../research/npc-portraits-all-audit"));
        if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} configured) output=configured;
        System.IO.Directory.CreateDirectory(output);
        foreach(string relative in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck","build/client/source-content/content/npcs.pck","build/client/source-content/content/weapons.pck"})
            if(!ProjectSettings.LoadResourcePack(System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../"+relative)),false)) throw new Exception("Portrait resource pack could not be mounted: "+relative);
        var window=GetWindow();window.ContentScaleMode=Window.ContentScaleModeEnum.Disabled;window.Size=new Vector2I(1000,700);
        AddChild(new ColorRect { Color=new Color("252822"),Size=new Vector2(1000,700),MouseFilter=MouseFilterEnum.Ignore });
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        var model=PreviewFixtures.Prepared(new WorldRoot());
        DetailCall(model,"NpcDialogWindow.Build");
        var layer=(CanvasLayer)DetailField(model,"NpcDialogWindow.layer")!;layer.Reparent(this);
        var panel=(HudWindow)DetailField(model,"NpcDialogWindow.Panel")!;
        var actorType=Native.ClientType("LibreKO.Presentation.EntityView")!;
        var actor=Activator.CreateInstance(actorType,nonPublic:true)!;
        void Field(string name,object value) => actorType.GetField(name,BindingFlags.Instance|BindingFlags.Public)!.SetValue(actor,value);
        void Target(string name,int value) => Native.Set(model, name, value);
        ((EntityRegistry)DetailField(model, "EntitySystem.Ents")!)[701] = (EntityView)actor;
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
            Target("Session.NpcQuest.LastNpcId",701);Target("Session.NpcQuest.TalkingNpcId",-1);
        }
        const string royalStory="How are you? I am the Enchanter dispatched to Moradon Castle. I offer buffs to help you on your journey. There are three types of buffs that you can receive: attack power, defensive power and enhancement of physical strength, and they can be stacked! Possible payment means are Noah, and, for those people between level 36 and 60, can pay with the [sign of victory] too.";
        void Open(string? story=null)
        {
            DetailCall(model,"NpcDialogWindow.Close");
            string name=(string)actorType.GetField("Name")!.GetValue(actor)!;
            bool royal=name.Contains("Royal National Enchanter");
            DetailCall(model,"NpcDialogWindow.Begin",name,story??(royal?royalStory:"How can I help you? Select a service below."));
            var options=royal?new[]{"1.   [Buff]Noah/Symbol of Victory","2.   Spirit Aisles' of Mounting","3.   Settlement support free of charge buff"}:new[]{"1.   Tell me more...","2.   Close"};
            foreach(string option in options) DetailCall(model,"AddNpcMenuButton",option,(Action)(()=>{}));
            DetailCall(model,"NpcDialogWindow.End",options.Length);
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
        ClassicNpcDialogue shell=null!;
        string[] CheckDialogue(string stage)
        {
            var violations=new List<string>();
            var frame=shell.GetGlobalRect();
            if(frame.Size.X!=363 || panel.Size.X!=363) violations.Add("frame width "+frame);
            if(!new Rect2(Vector2.Zero,GetViewportRect().Size).Encloses(panel.GetGlobalRect())) violations.Add("viewport "+panel.GetGlobalRect());
            var portrait=shell.Portrait;
            var slot=new Rect2(frame.Position+KnightOnlineUiClassic.Layout.ClassicNpcLayout.Portrait.Position,KnightOnlineUiClassic.Layout.ClassicNpcLayout.Portrait.Size);
            if(portrait.IsVisibleInTree() && !slot.Grow(.5f).Encloses(portrait.GetGlobalRect())) violations.Add("portrait "+portrait.GetGlobalRect()+" outside "+slot);
            foreach(var label in Descendants(portrait).OfType<Label>().Where(l=>l.IsVisibleInTree()))
                if(!slot.Grow(.5f).Encloses(label.GetGlobalRect())) violations.Add("portrait caption "+label.Text+" "+label.GetGlobalRect());
            var speech=shell.Speech.GetGlobalRect();
            if(shell.Speech.Size!=KnightOnlineUiClassic.Layout.ClassicNpcLayout.Speech.Size) violations.Add("speech grew "+shell.Speech.Size);
            if(!speech.Grow(.5f).Encloses(shell.Speech.Identity.GetGlobalRect())) violations.Add("identity "+shell.Speech.Identity.GetGlobalRect());
            if(portrait.GetGlobalRect().Intersects(speech)) violations.Add("portrait overlaps speech");
            return violations.ToArray();
        }
        SetNpc(31508);Target("Session.NpcQuest.TalkingNpcId",701);
        DetailCall(model,"NpcInteraction.RangeTick",.01);
        if((int)DetailField(model,"Session.NpcQuest.TalkingNpcId")! !=-1) throw new Exception("Delayed NPC reply regression fixture did not clear the transient target");
        Open();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        shell=(ClassicNpcDialogue)CharacterDetailsSkin.Apply(panel.Body)!;
        await Settle();await Shot("baseline");
        NpcPortraitCache.Clear();_windowData!.NpcPortraitSource=()=>native.NpcPortrait;
        var fixtures=new (string Key,int Id)[] {
            ("royal-60",31508),("royal-50",31507),("royal-80",9010),("guard",13013),
            ("hostess",12000),("neria",16096),("sundries",12120),("blacksmith",14301),
            ("jeweller",31402),("dragon",31005),("karus-guard",21010),("karus-manager",18031),("vendor",16085) };
        fixtures=[..fixtures,("magpie",31506),("kaishan",18004)];
        var results=new List<object>();
        var textures=new Dictionary<string,Texture2D>();
        foreach(var fixture in fixtures)
        {
            SetNpc(fixture.Id);
            // A visible world NPC has its resource loaded before the player opens dialogue.
            var scene=(PackedScene?)Native.Call(model, "EntityLooksSystem.ResolveMobScene", prototypes[fixture.Id].GetProperty("ModelId").GetInt32());
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
            if(CheckDialogue(fixture.Key) is {Length:>0} violations) throw new Exception("NPC fixture exceeds its frame: "+fixture.Key+": "+string.Join("; ",violations));
            string expected=descriptor.Name.StartsWith('[') && descriptor.Name.EndsWith(']')?descriptor.Name[1..^1]:descriptor.Name;
            if(portrait.DisplayedName!=expected || portrait.DisplayedLevel!="Lv. "+descriptor.Level || shell.Speech.Identity.Text!=expected) throw new Exception("NPC name or level is stale");
            if(shell.Speech.Identity.GetLineCount()!=1) throw new Exception("NPC name wraps inside the speech header: "+expected);
            using(var image=texture.GetImage()) if(image.GetUsedRect().Size.X<25 || image.GetUsedRect().Size.Y<25) throw new Exception("Captured NPC portrait is blank");
            await Shot(fixture.Key);
            results.Add(new { fixture.Key,fixture.Id,descriptor.Name,descriptor.Level,descriptor.AppearanceKey,portraitSize=portrait.Size.ToString(),
                firstCaptureMs=NpcPortraitCache.RenderRequests>before?NpcPortraitCache.LastCaptureMilliseconds:0,renderRequests=NpcPortraitCache.RenderRequests,
                framing=NpcPortraitCache.LastFraming,target=NpcPortraitCache.LastTarget.ToString(),portraitRect=portrait.GetGlobalRect().ToString(),
                identity=shell.Speech.Identity.Text,identityRect=shell.Speech.Identity.GetGlobalRect().ToString(),frame=panel.GetGlobalRect().ToString() });
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
        if(CheckDialogue("empty introduction") is {Length:>0} emptyViolations) throw new Exception("Empty introduction escaped its frame: "+string.Join("; ",emptyViolations));
        await Shot("empty-introduction");
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
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,prefix+"-portrait-verification.json"),JsonSerializer.Serialize(new {
            eligibleNpcPrototypes=eligible,distinctAppearances=entries,renderRequests=actualRenders,cacheHits=hits,activeRenderers=0,sceneNodes=0,retainedViewports=0,
            textureBytes=bytes,resolution=NpcPortraitCache.Resolution,reopenings=12,delayedReplyTargetVerified=true,nameAboveLevelVerified=true,emptyIntroductionVerified=true,
            fixtures=results,cachePressure,withPortrait,withoutPortrait,
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(binary))),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(binary)!,"LibreKO.dll"))))
        },new JsonSerializerOptions { WriteIndented=true }));
        _windowData.NpcPortraitSource=null;NpcPortraitCache.Clear();layer.Free();model.Free();net.Free();
        GD.Print($"NPC_PORTRAITS_ALL_OK: {eligible} NPC prototypes, {entries} real appearances, delayed reply, names above levels, 12 first-frame reopenings, zero retained viewports, bounded texture cache and 240 performance frames");
    }
}
