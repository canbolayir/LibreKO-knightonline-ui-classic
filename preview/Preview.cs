using Godot;
using System.Reflection;
using LibreKO;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;

public partial class Preview : Control
{
    private PreviewData? _windowData,_chatData;
    private PreviewData[] _proxies=Array.Empty<PreviewData>();
    /// <summary>True in the *-fallback-audit modes, which turn the bundled 2.x art off to check the Classic fallback.</summary>
    internal static bool ClassicFallback => OS.GetCmdlineUserArgs().Any(a => a.EndsWith("-fallback-audit"));
    /// <summary>Turns the bundled 2.x art off for the rest of the run (points the art root at a missing folder).</summary>
    internal static void UseClassicFallback() => UseArt(false);
    /// <summary>Turns the bundled 2.x art on (the plugin's own resolution) or off; windows built afterwards follow it.</summary>
    internal static void UseArt(bool on)
    {
        System.Environment.SetEnvironmentVariable(LocalAssets.RootVariable, on ? null : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "libreko-classic-no-2x-art"));
        if (Plugin.Active) typeof(UiKit).GetField("_local", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Plugin.Kit, null);
    }
    static object Create(Type type, params object?[] args) => Activator.CreateInstance(type, BindingFlags.Instance|BindingFlags.NonPublic, null, args, null)!;
    public override async void _Ready()
    {
        try
        {
            string auditPack=OS.GetEnvironment("LIBREKO_AUDIT_CLIENT_PACK");
            if(auditPack.Length>0 && !ProjectSettings.LoadResourcePack(auditPack,false))throw new Exception("Missing audit resource pack: "+auditPack);
            // The installed game art and item data (items.json with its sell lists) are mounted before anything reads them.
            string content=ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck");
            if(System.IO.File.Exists(content) && !ProjectSettings.LoadResourcePack(content,false))throw new Exception("Missing content pack: "+content);
            // The client's resource scripts (BakedTable for the NPC name, quest and text tables) are registered like the client project's own.
            typeof(Godot.Bridge.ScriptManagerBridge).GetMethod("LookupScriptsInAssembly",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,new object[]{typeof(WorldRoot).Assembly});
            // The client's GameContent autoload installs its data loaders (items, skills, quests, NPC names); without it every table loads empty.
            Native.Call(Native.StaticOwner("LibreKO.Content.ContentLoaders"), "Install");
            // The client boots Game.Session and Game.Requests when its network node enters the tree; the audits read both.
            if(Game.Session==null)AddChild(new LibreKO.Network.Net{Name="preview_net"});
            // Config.Load (run by the network node) scales the UI to 0.8 below 860 px of window height, as the client does on a small
            // screen; the previews measure and sample their screenshots at 1:1, so the scale is chosen at 1.
            typeof(Config).GetProperty(nameof(Config.UiScale))!.SetValue(null,1f);typeof(Config).GetProperty(nameof(Config.UiScaleChosen))!.SetValue(null,true);Config.ApplyUiScale();
            HudLayout.PersistLayouts=false;
            PluginSettings.Enabled=false;
            // The bundled 2.x art (assets/myko) is the default; the *-fallback-audit modes run with it turned off to check the Classic fallback.
            if(ClassicFallback)UseClassicFallback();
            string root=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../"));
            var manifest=PluginManifest.Parse(System.IO.File.ReadAllText(root+"/plugin.json"));
            var info=(PluginInfo)Create(typeof(PluginInfo),root,manifest,PluginState.Loaded,"");
            var game=new PluginGame();
            var context=(PluginContext)Create(typeof(PluginContext),info,new List<PluginInfo>{info},new PluginUi(),game);
            var boot=new Plugin();boot.Initialize(context);
            int nation=OS.GetCmdlineUserArgs().Contains("karus") ? 1 : 2;
            var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var proxies=attach.GetParameters().Select(p=>
            {
                var proxy=DispatchProxy.Create(p.ParameterType,typeof(PreviewData));
                ((PreviewData)proxy).Nation=nation;
                return proxy;
            }).ToArray();
            _windowData=(PreviewData)proxies[Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameWindows))];
            _proxies=proxies.Cast<PreviewData>().ToArray();
            _chatData=(PreviewData)proxies[Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameChat))];
            bool characterAudit = OS.GetCmdlineUserArgs().Contains("character-audit");
            if (characterAudit || OS.GetCmdlineUserArgs().Contains("details-audit") || OS.GetCmdlineUserArgs().Contains("hud-audit") || OS.GetCmdlineUserArgs().Contains("context-menu-audit"))
                foreach (var proxy in proxies) ((PreviewData)proxy).CharacterAudit = new CharacterAuditData(nation) { ClanFlag=OS.GetCmdlineUserArgs().Contains("details-audit")?LibreKO.Network.ClanTypes.Accredited5:LibreKO.Network.ClanTypes.Training };
            attach.Invoke(game,proxies);
            NativeGame.Source=PreviewNative.Create(attach.GetParameters().Zip(proxies,(p,x)=>(p.ParameterType,(PreviewData)x)).ToDictionary(e=>e.Item1,e=>e.Item2));
            if(OS.GetCmdlineUserArgs().Contains("plugin-toggle-audit")){await CapturePluginToggleAudit(nation, info, boot);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("manes-hud-audit")){await CaptureManesHudAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("new-windows-audit")){await CaptureNewWindowsAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("perf-classic-audit")){await CapturePerfClassicAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("hud-buttons-audit")){await CapturePluginToggleAudit(nation, info, boot, hudButtons: true);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("pet-world-reconnect-audit")){await CapturePetWorldReconnectAudit(nation, info);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("pet-keyboard-audit")){await CapturePetKeyboardAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("pet-connection-audit")){await CapturePetConnectionAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("pet-portrait-audit")){await CapturePetPortraitAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("pet-audit")){await CapturePetAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("cape-audit")){await CaptureCapeAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("rebirth-audit")){await CaptureRebirthAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("events-a-audit")){await CaptureEventsAAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("gender-audit")){await CaptureGenderAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("beauty-audit")){await CaptureBeautyAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("nationtransfer-audit")){await CaptureNationTransferAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("equipview-audit")){await CaptureEquipViewAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("auction-audit")){await CaptureAuctionAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("store-audit")||OS.GetCmdlineUserArgs().Contains("store-fallback-audit")){await CaptureStoreAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("market-price-audit")){await CaptureMarketPriceAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("merchant-search-audit")){await CaptureMerchantSearchAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("chat-colours-audit")){await CaptureChatColoursAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("communication-audit")){await CaptureCommunicationAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("social-audit")){await CaptureSocialAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("chat-room-audit")){await CaptureChatRoomAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("user-vote-audit")){await CaptureUserVoteAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("command-audit")){await CaptureCommandListAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("command-function-audit")){await CaptureCommandFunctionAudit(nation, info);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("genie-audit")){await CaptureGenieAudit(nation, info);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("repurchase-audit")){await CaptureRepurchaseAudit(nation, info);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("mail-classic-audit")){await CaptureMailClassicAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("progression-audit")){await CaptureProgressionAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("council-rate-audit")){await CaptureCouncilRateAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("siege-audit")){await CaptureSiegeAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("king-nation-audit")){await CaptureKingNationAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("map-fortune-audit")){await CaptureMapFortuneAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("events-b-audit")){await CaptureEventsBAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("event-windows-audit")){await CaptureEventWindowsAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("myko-hud-audit")||OS.GetCmdlineUserArgs().Contains("myko-hud-fallback-audit")){await CaptureMykoHudAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("event-overlays-audit")){await CaptureEventOverlaysAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("identity-audit")){await CaptureIdentityAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("classic-services-audit")){await CaptureClassicServices(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("repair-audit")){await CaptureRepairAudit(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("storage-audit")){await CaptureStorageAudit(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("item-windows-audit")){await CaptureItemWindowsAudit(game,nation,false);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("item-lists-audit")){await CaptureItemWindowsAudit(game,nation,true);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("anvil-audit")){await CaptureAnvilAudit(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("inn-audit")){await CaptureInnAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("npc-opening-audit")){await CaptureNpcOpeningAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("premium-quest-audit")){await CapturePremiumQuestAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("npc-services-audit")){await CaptureNpcServices(nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("upstream-integration-audit")){await CaptureUpstreamIntegration(game,nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("merchant-palette")){await CaptureMerchantPalette(nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("upstream-npc-audit")){await CaptureUpstreamNpcAudit(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("context-menu-audit")){await CaptureContextMenuAudit(game,nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("world-overlays-audit")){await CaptureWorldOverlaysAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("forgotten-temple-audit")){await CaptureForgottenTempleAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("merchant-audit")){await CaptureMerchantAudit(game,nation);GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().Contains("trade-audit")) { await CaptureExchangeAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("party-audit")) { await CapturePartyAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("inventory-audit")) { await CaptureInventoryAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("skill-audit")) { await CaptureSkillAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("skill-rules-audit")) { await CaptureSkillRulesAudit(game,nation);await FinishManagedAudit();return; }
            if(OS.GetCmdlineUserArgs().Contains("vendor-audit")) { await CaptureVendorAudit(game,nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-integration-audit")) { await CaptureNpcIntegration(nation);await FinishManagedAudit();return; }
            if(OS.GetCmdlineUserArgs().Contains("hunt-icon-options")) { await CaptureHuntIconOptions(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-design-audit")) { await CaptureNpcDesigns(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("npc-portrait-audit")) { await CaptureNpcPortraitAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("hud-qa-audit")){await CaptureHudQaAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("hud-audit")) { await CaptureHudAudit(nation);GetTree().Quit();return; }
            if(OS.GetCmdlineUserArgs().Contains("quest-detail-audit")){await CaptureQuestDetailAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("myko-npc-audit")){await CaptureMykoNpcAudit(nation);await FinishManagedAudit();return;}
            if(OS.GetCmdlineUserArgs().Contains("details-audit")) { await CaptureDetails(nation);GetTree().Quit();return; }
            if (characterAudit) { await CaptureCharacterAudit(game, nation); GetTree().Quit(); return; }
            bool detail=OS.GetCmdlineUserArgs().Contains("detail");
            if(!detail) AddChild(new StatusHud());
            var hotbar=new HotkeyBar();
            AddChild(hotbar);
            if(OS.GetCmdlineUserArgs().Contains("horizontal")) {
                typeof(HotkeyBar).GetField("_horizontal",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(hotbar,true);
                typeof(HotkeyBar).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(hotbar,null);
            }
            if(OS.GetCmdlineUserArgs().Contains("hotbar-check")) {
                int hotbarIndex=Array.FindIndex(attach.GetParameters(),p=>p.ParameterType==typeof(IGameHotbar));
                CheckHotbars(hotbar,game,(PreviewData)proxies[hotbarIndex]);
            }
            if(detail) {
                hotbar.Scale=new Vector2(2,2);hotbar.Position=new Vector2(60,50);
                if(OS.GetCmdlineUserArgs().Contains("native-reference")) {
                    var source=Plugin.Kit.Layout("{nation}_hotkey_us").Images.First();
                    AddChild(new TextureRect {
                        Position=new Vector2(1100,50),Size=new Vector2(source.W,source.H),Scale=new Vector2(2,2),
                        Texture=new AtlasTexture { Atlas=Plugin.Kit.Texture(source.Texture!),
                            Region=new Rect2(source.SrcX,source.SrcY,source.SrcW,source.SrcH),FilterClip=true },
                        ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale
                    });
                }
            } else {
            AddChild(new ChatWindow());
            AddChild(new Taskbar());
            AddWindow("inventory",new Vector2(260,130),h=>new InventoryWindow(h));
            AddWindow("skills",new Vector2(940,130),h=>new SkillWindow(h));
            AddWindow("character_info",new Vector2(1360,130),h=>new CharacterWindow(h));
            foreach(var window in GetChildren().OfType<InventoryWindow>())
                typeof(InventoryWindow).GetMethod("SetCospreOpen",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{true});
            }
            await ToSignal(GetTree().CreateTimer(3),SceneTreeTimer.SignalName.Timeout);
            if(detail) hotbar.Position=new Vector2(60,50);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var image=GetViewport().GetTexture().GetImage();
            image.SavePng(root+"/preview/classic-"+(nation==1 ? "karus" : "elmo")+(OS.GetCmdlineUserArgs().Contains("horizontal") ? "-horizontal" : "")+(OS.GetCmdlineUserArgs().Contains("hotbar-check") ? "-multi" : "")+(detail ? "-detail":"")+(OS.GetCmdlineUserArgs().Contains("native-reference") ? "-native":"")+".png");
            GD.Print("CLASSIC_PREVIEW_OK");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static void CheckHotbars(HotkeyBar hotbar,PluginGame game,PreviewData data)
    {
        void Require(bool valid,string message) { if(!valid) throw new Exception("HOTBAR_CHECK: "+message); }
        Control Canvas()=>hotbar.GetNode<Control>("canvas");
        Control Bar(string id)=>Canvas().GetNode<Control>(id);
        IEnumerable<Control> Bars()=>Canvas().GetChildren().OfType<Control>();
        Transform2D InGroup(Control control)=>hotbar.GetGlobalTransform().AffineInverse()*control.GetGlobalTransform();
        bool Near(Vector2 a,Vector2 b)=>(a-b).Length()<0.01f;
        bool SameTransform(Transform2D a,Transform2D b)=>Near(a.Origin,b.Origin) && Near(a.X,b.X) && Near(a.Y,b.Y);
        // A slot's upright content layer, including nested parts such as the ready-flash overlay.
        Control? ContentOf(Control control)
        {
            for(Node? node=control;node is Control part && node!=Canvas();node=node.GetParent())
                if(part.Name=="content") return part;
            return null;
        }
        void Press(string bar,string button) { Bar(bar).GetNode<BaseButton>(button).EmitSignal(BaseButton.SignalName.Pressed); }
        void Raise()=>typeof(PluginGame).GetMethod("RaiseHotbar",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(game,null);
        string Page(string id)=>Bar(id).GetNode<Label>("page_label").Text;
        // The client opens the bar on page 1 (F1); the Classic bar keeps the page the client chose.
        Require(Page("main_bar")=="1","Main bar must start on the client's page F1");
        var initialPage=Bar("main_bar").GetNode<Label>("page_label");
        Require(new Rect2(Vector2.Zero,Bar("main_bar").Size).Encloses(new Rect2(initialPage.Position,initialPage.Size)),
            "The initial single bar's page number must fit its footer");
        Press("main_bar","add_bar"); Press("main_bar","add_bar");
        Require(Page("extra_bar_0")=="2" && Page("extra_bar_1")=="3","New bars must select consecutive pages");
        Press("extra_bar_0","btn_down");
        Require(Page("extra_bar_0")=="3" && Page("main_bar")=="1","Extra page arrows must not change main page");
        game.Hotbar.SetPage(4);Raise();
        Require(Page("main_bar")=="5" && Page("extra_bar_0")=="3","F keys must leave extra pages pinned");
        var slot=Bar("extra_bar_0").GetNode<HotSlot>("slot_0");
        // Slots activate on a completed click (press + release) so a press can still start a drag.
        slot._GuiInput(new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.Left });
        Require(data.LastActivate==-1,"A slot must not activate on press alone");
        slot._GuiInput(new InputEventMouseButton { Pressed=false,ButtonIndex=MouseButton.Left });
        Require(data.LastActivate==20,"Extra F3 slot must activate absolute slot 20");
        slot._DropData(Vector2.Zero,new Godot.Collections.Dictionary { {"id",123},{"barAbs",15} });
        Require(data.LastDrop==(20,123,15),"Cross-bar drag must keep source and target absolute slots");
        Press("extra_bar_0","remove_bar");
        Require(Page("extra_bar_0")=="3" && !Canvas().HasNode("extra_bar_1"),"Removing a middle bar must preserve the next bar's page");
        Require(data.ClearCount==0,"Closing a bar must never clear stored skills");
        game.Hotbar.SetPage(1);Raise();
        for(int i=0;i<12;i++) {
            if(Bar("main_bar").HasNode("add_bar")) Press("main_bar","add_bar");
        }
        Require(Bars().Count()==8,"Maximum must be eight bars");
        Require(!Bar("main_bar").HasNode("add_bar"),"Add button must hide at the limit");
        bool horizontal=OS.GetCmdlineUserArgs().Contains("horizontal");
        data.WithCooldown=true;Raise();
        var beforeSize=hotbar.Size;
        var snapshot=Canvas().FindChildren("*","Control",true,false).Cast<Control>().ToDictionary(
            c=>Canvas().GetPathTo(c).ToString(),c=>(Size:c.Size,Transform:InGroup(c),Text:c is Label,
                Content:ContentOf(c) is Control content ? Canvas().GetPathTo(content).ToString():null));
        Press("main_bar","rotate");
        Require(Near(hotbar.Size,new Vector2(beforeSize.Y,beforeSize.X)),"Rotation must swap the group's width and height exactly");
        Vector2 Turn(Vector2 point)=>horizontal ? new Vector2(beforeSize.Y-point.Y,point.X) : new Vector2(point.Y,beforeSize.X-point.X);
        foreach(var (path,old) in snapshot) {
            var control=Canvas().GetNode<Control>(path);
            var transform=InGroup(control);
            Require(Near(control.Size,old.Size),$"Rotation must preserve the size of {path}");
            if(old.Content is string contentPath && contentPath!=path) {
                var oldLocal=snapshot[contentPath].Transform.AffineInverse()*old.Transform;
                var newLocal=InGroup(Canvas().GetNode<Control>(contentPath)).AffineInverse()*transform;
                Require(SameTransform(oldLocal,newLocal),$"Icons, counts and cooldown must keep their internal alignment: {path}");
            } else Require(Near(transform*(control.Size/2),Turn(old.Transform*(old.Size/2))),$"Rotation must preserve the rotated center of {path}");
            if(old.Text || old.Content!=null) Require(Mathf.Abs(control.GetGlobalTransform().Rotation)<0.001f,$"Text and slot content must remain upright: {path}");
            else foreach(var corner in new[]{Vector2.Zero,new Vector2(old.Size.X,0),old.Size,new Vector2(0,old.Size.Y)})
                Require(Near(transform*corner,Turn(old.Transform*corner)),$"The entire frame/control must rotate rigidly: {path}");
        }
        var delta=InGroup(Bar("main_bar").GetNode<HotSlot>("slot_1")).Origin-InGroup(Bar("main_bar").GetNode<HotSlot>("slot_0")).Origin;
        Require(horizontal ? Near(delta,new Vector2(0,36)) : Near(delta,new Vector2(36,0)),"Rotate must change every bar's slot direction");
        foreach(var bar in Bars())
            foreach(var cell in bar.GetChildren().OfType<HotSlot>())
            {
                Require(Mathf.Abs(cell.GetNode<TextureRect>("content/icon").GetGlobalTransform().Rotation)<0.001f,
                    "Skill pictures must remain upright in both bar orientations");
                var shade=cell.GetNode<ColorRect>("content/cooldown");
                Require(shade.Visible && Near(shade.Size,new Vector2(32,16)) && Near(shade.Position,new Vector2(0,16)),
                    "Half cooldown must shade the lower half of the upright icon");
                Require(cell.GetNode<Label>("content/count").Text=="42","Item counts must remain visible in the upright content layer");
            }
        Press("main_bar","rotate");
        foreach(var (path,old) in snapshot) {
            var control=Canvas().GetNode<Control>(path);
            var transform=InGroup(control);
            Require(Near(transform.Origin,old.Transform.Origin) && Near(transform.X,old.Transform.X) && Near(transform.Y,old.Transform.Y),$"Rotating back must restore the exact layout: {path}");
        }
        foreach(Control bar in Bars())
            foreach(BaseButton button in bar.GetChildren().OfType<BaseButton>())
                Require(new Rect2(Vector2.Zero,bar.Size).Encloses(new Rect2(button.Position,button.Size)),"Controls must stay inside the bar");
        foreach(var bar in Bars()) {
            var page=bar.GetNode<Label>("page_label");
            var action=bar.GetNode<BaseButton>(bar.Name=="main_bar" ? "rotate":"remove_bar");
            Require(Mathf.Abs(page.Position.Y+page.Size.Y/2-action.Position.Y-action.Size.Y/2)<2 && page.Position.X>=action.Position.X+action.Size.X,
                $"The page number must sit beside the bottom controls: {bar.Name}, page={page.GetRect()}, control={action.GetRect()}");
            Require(new Rect2(Vector2.Zero,bar.Size).Encloses(new Rect2(page.Position,page.Size)),
                $"Page numbers must fit inside the footer: {bar.Name}, page={page.GetRect()}, bar={bar.Size}");
            Require(!new Rect2(page.Position,page.Size).Intersects(bar.GetNode<Control>("page_drag").GetRect()),
                "The top drag ornament must not contain the page number");
        }
        var keys=hotbar.FindChildren("key_*","Label",true,false).Cast<Label>().ToArray();
        Require(keys.Length==10,"The group must show only one 1–0 number rail");
        Require(keys.Select(k=>k.GetThemeFont("font").GetInstanceId()).Distinct().Count()==1 &&
            keys.Select(k=>k.GetThemeFontSize("font_size")).Distinct().Count()==1 &&
            keys.Select(k=>k.GetThemeColor("font_color")).Distinct().Count()==1,"All ten numerals must use the same font, size and color");
        var bars=Bars().OrderBy(b=>b.Position.X).ToList();
        for(int i=1;i<bars.Count;i++) Require(bars[i-1].Position.X+bars[i-1].Size.X==bars[i].Position.X,"Adjacent bars must touch without a gap");
        Require(Bar("main_bar").GetNode<BaseButton>("rotate").Position==new Vector2(2,Bar("main_bar").Size.Y-19),"Rotate must stay at the same dock within the rotated group");
        data.WithCooldown=false;Raise();
        while(Canvas().HasNode("extra_bar_0")) Press("extra_bar_0","remove_bar");
        Press("main_bar","add_bar");Press("main_bar","add_bar");
        GD.Print("CLASSIC_HOTBAR_CHECK_OK: add/remove, 8-bar limit, pinned pages, activation, cross-bar drag, rigid frame quarter-turn, upright icons/text/counts, vertical cooldown fill, exact layout restoration, control bounds, footer page numbers, shared font/number rail, flush edges, unchanged local dock");
    }
    private void AddWindow(string id,Vector2 at,Func<WindowHost,Control> build)
    {
        var shell=new Control();
        AddChild(shell);
        var host=(WindowHost)Create(typeof(WindowHost),id,id,shell,(Action)(()=>{}));
        var window=build(host); window.Position=at; AddChild(window);
    }
}

public class PreviewData : DispatchProxy
{
    public int Nation=2;
    public CharacterAuditData? CharacterAudit;
    public IGameCharacterPanel? LiveCharacterPanel;
    private int _page;
    public int LastActivate=-1,ClearCount;
    public bool WithCooldown;
    public bool WithUnreadMail;
    public Func<int>? MailUnreadCount;
    public Func<string,object?>? Values;
    /// <summary>Answers by member name and arguments (e.g. IGameMap.NpcKindPresent(kind)); null falls through.</summary>
    public Func<string,object?[]?,object?>? Calls;
    public Func<GameNpcPortrait?>? NpcPortraitSource;
    public Func<int,bool,string,IReadOnlyList<string>>? ChatReader;
    public IReadOnlyList<LibreKO.Domain.NearbyRow> NearbyRows=Array.Empty<LibreKO.Domain.NearbyRow>();
    public string LastSent="",LastMenu="";
    public int LastLinkTip;
    public (int,int,int) LastDrop;
    public object? Answer(MethodInfo method, object?[]? args) => Invoke(method, args);
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var type=method!.ReturnType;
        string name=method.Name;
        if(Values?.Invoke(name) is {} value) return value;
        if(Calls?.Invoke(name,args) is {} called) return called;
        if(name=="ReadHistory") return ChatReader?.Invoke((int)args![0]!, (bool)args[1]!, (string)args[2]!) ?? Array.Empty<string>();
        if(name=="NearbyPlayers") return NearbyRows;
        if(name=="Send") {LastSent=(string)args![0]!;return null;}
        if(name=="ShowLinkTooltip") {LastLinkTip=(int)args![0]!;return null;}
        if(name=="PlayerMenu") {LastMenu=(string)args![0]!;return null;}
        if(name=="get_NpcPortrait") return NpcPortraitSource?.Invoke();
        if(name=="NotificationCount") return (string)args![0]! == "mail" ? MailUnreadCount?.Invoke() ?? (WithUnreadMail?3:0) : 0;
        if(name=="SetPage") { _page=(int)args![0]!; return null; }
        if(name=="ActivateAbs") { LastActivate=(int)args![0]!; return null; }
        if(name=="Drop") { LastDrop=((int)args![0]!, (int)args[1]!, (int)args[2]!); return null; }
        if(name=="Clear") { ClearCount++; return null; }
        if (CharacterAudit != null && method.DeclaringType == typeof(IGameCharacter))
            return CharacterAudit.CharacterValue(name, type, args);
        if (name == "get_CharacterPanel" && LiveCharacterPanel != null) return LiveCharacterPanel;
        if (name == "get_CharacterPanel" && CharacterAudit != null) return CharacterAudit;
        if(type==typeof(void)) return null;
        if(type==typeof(bool) && name=="get_MiniMapVisible") return true;
        if(type==typeof(string)) return name switch {
            "get_Name"=>"Classic Preview", "get_ClassName"=>"Warrior", "get_ZoneName"=>"Moradon",
            "get_NationName"=>Nation==1 ? "Karus" : "El Morad", _=>"" };
        if(type==typeof(int)) return name switch {
            "get_Page"=>_page, "get_Nation"=>Nation, "get_Class"=>Nation==1 ? 105 : 205, "get_Level"=>70,
            "get_Hp"=>2400,"get_MaxHp"=>3200,"get_Mp"=>800,"get_MaxMp"=>1200,
            "get_Pages"=>8,"get_SlotsPerPage"=>10,"get_GridStart"=>14,"get_GridCount"=>28,
            "get_Gold"=>1234567,"get_Weight"=>320,"get_MaxWeight"=>1800,"get_Race"=>Nation==1 ? 1:11,
            _=>0 };
        if(type==typeof(double)) return name=="get_ExpPercent" ? 42.5 : 0d;
        if(type==typeof(float)) return name switch {"get_X"=>764.8f,"get_Z"=>416.8f,_=>0f};
        if(type==typeof(GameItem)) return GameItem.Empty((int)args![0]!);
        if(type==typeof(HotSlotInfo)) {
            int abs=(int)args![0]!;
            return WithCooldown ? new HotSlotInfo(abs,1000+abs,false,null,0.5f,"Cooldown fixture","",42):HotSlotInfo.Empty(abs);
        }
        if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0],0);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
