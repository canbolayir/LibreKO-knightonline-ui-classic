using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Layout;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using ClassicSlot=KnightOnlineUiClassic.Layout.ItemSlot;
using NativeSlot=LibreKO.Domain.ItemSlot;

public partial class Preview
{
    private async Task CaptureInventoryAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/inventory-window-audit");
        System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck",
            "build/client/source-content/content/characters.pck","build/client/source-content/content/armor.pck",
            "build/client/source-content/content/weapons.pck","build/client/source-content/content/npcs.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false)) throw new Exception("Missing inventory audit pack: "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(900,680);
        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(900,680),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        net.Sheet.SeedWealth(1_786_597_823,50_000);net.Sheet.SetMaxWeight(17_100);net.Sheet.SeedProgress(83,0,100);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo {Nation=nation,Class=nation*100+6,Race=nation==1?1:11,
            Name="Inventory Preview",Gear=new int[8],Inventory=new NativeSlot[InventoryConstants.InventoryTotal]});
        ItemData.EnsureLoaded();
        var world=new World();
        typeof(World).GetField("_selfClass",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,nation*100+6);
        typeof(World).GetField("_selfRace",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,nation==1?1:11);
        var inv=(Inventory)typeof(World).GetProperty("Inv",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;
        inv.EnsureLength(InventoryConstants.InventoryTotal);
        DetailCall(world,"BuildInventoryPanel");
        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;
        attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());
        NativeGame.Source=new ClientGame();
        ((CanvasLayer)DetailField(world,"_itemTipLayer")!).Reparent(this);
        var shell=new Control {Position=new Vector2(300,42)};AddChild(shell);
        bool closed=false;
        var host=(WindowHost)Create(typeof(WindowHost),"inventory","Inventory",shell,(Action)(()=>closed=true));
        var actual=new InventoryWindow(host);shell.AddChild(actual);
        var view=(LayoutView)typeof(InventoryWindow).GetField("_view",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var drawer=(LayoutView)typeof(InventoryWindow).GetField("_extras",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var all=(List<ClassicSlot>)typeof(InventoryWindow).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var bags=(List<ClassicSlot>)typeof(InventoryWindow).GetField("_bagSlots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        var checks=new List<string>();var captures=new List<object>();string prefix=nation==1?"karus":"human";
        void Require(bool valid,string message) {if(!valid) throw new Exception("INVENTORY_AUDIT: "+message);checks.Add(message);}
        Require(UiIcons.Get("system/move")!=null,"Updated client resource pack contains the upstream chat move icon");
        var moveType=typeof(Net).GetNestedType("PendingItemMove",BindingFlags.NonPublic)!;
        var pendingMove=typeof(Net).GetField("_pendingItemMove",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var moveHandler=typeof(Net).GetMethod("HandleItemMove",BindingFlags.Instance|BindingFlags.NonPublic)!;
        object PendingMove(byte direction,byte source,byte destination)=>Activator.CreateInstance(moveType,
            new object[]{direction,source,destination,0}.Take(moveType.GetConstructors().Max(c=>c.GetParameters().Length)).ToArray())!;
        var snapshot=net.LastEnter;
        snapshot.Inventory[InventoryConstants.InventoryStart]=new NativeSlot {ItemId=700011001,Count=1};
        pendingMove.SetValue(net,PendingMove(ItemMove.InventoryToBagSlot,0,2));
        var accepted=new Packet(GameOpcodes.GS_ITEM_MOVE);accepted.WriteByte(1);accepted.WriteByte(1);moveHandler.Invoke(net,new object[]{accepted});
        inv.Reset(net.LastEnter.Inventory);
        Require(inv[InventoryConstants.BagSlotFor(2)].ItemId==700011001 && inv[InventoryConstants.InventoryStart].IsEmpty,
            "Network acknowledgement preserves equipped bags when a new world reloads the cached inventory");
        bool refused=false;net.ItemMoveResultEvent+=ok=>refused=!ok;
        pendingMove.SetValue(net,PendingMove(ItemMove.BagSlotToInventory,2,0));
        var rejected=new Packet(GameOpcodes.GS_ITEM_MOVE);rejected.WriteByte(1);rejected.WriteByte(0);moveHandler.Invoke(net,new object[]{rejected});
        Require(refused && pendingMove.GetValue(net)==null && net.LastEnter.Inventory[InventoryConstants.BagSlotFor(2)].ItemId==700011001,
            "Refused moves release the pending acknowledgement and preserve the zone snapshot");
        Array.Clear(net.LastEnter.Inventory);inv.Reset(net.LastEnter.Inventory);
        void Invoke(string method,params object[] args)=>typeof(InventoryWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(actual,args);
        void Refresh()=>typeof(PluginGame).GetMethod("RaiseInventory",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
        int ItemFor(int type)=>ItemData.All().Where(i=>(i.Slot==type || type is 101 or 102 && i.Slot==100 || type==112 && i.Slot==127)
            && i.Icon>0 && ResourceLoader.Exists($"res://assets/items/icons/{i.Icon}.png")).OrderBy(i=>i.Id).FirstOrDefault()?.Id ?? -1;
        NativeSlot Item(int id,int count=1)=>new() {ItemId=id,Count=(short)count,Durability=ItemData.MaxDurabilityOf(id)};
        async Task Frames(int count=8) {for(int i=0;i<count;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        async Task Capture(string state,Vector2? pointer=null)
        {
            var mouse=pointer??Vector2.Zero;
            Input.ParseInputEvent(new InputEventMouseMotion {Position=mouse,GlobalPosition=mouse});
            await Frames();
            var bounds=new List<object>();
            foreach(var layout in new[]{view,drawer})
            {
                if(!layout.Visible) continue;
                foreach(var pair in layout.Where(_=>true).Where(p=>p.Control.IsVisibleInTree()))
                {
                    Require(pair.Control.Size==pair.Node.SizeVec,state+": exact bounds "+pair.Node.Id);
                    Require(new Rect2(Vector2.Zero,layout.Size).Encloses(new Rect2(pair.Control.Position,pair.Control.Size)),state+": contained "+pair.Node.Id);
                    if(pair.Control is Label label && label.Text.Length>0)
                    {
                        Require(label.GetThemeFont("font")==Plugin.Kit.Bold,state+": Character Report typography "+pair.Node.Id);
                        Require(label.GetLineCount()<= (pair.Node.Id=="bag_empty_hint"?2:1),state+": caption fits "+pair.Node.Id);
                    }
                    bounds.Add(new {panel=layout.Name.ToString(),id=pair.Node.Id,x=pair.Control.Position.X,y=pair.Control.Position.Y,
                        width=pair.Control.Size.X,height=pair.Control.Size.Y,declaredX=pair.Node.X,declaredY=pair.Node.Y,declaredWidth=pair.Node.W,declaredHeight=pair.Node.H});
                }
            }
            foreach(var c in all.Concat(bags).Where(c=>c.IsVisibleInTree()))
            {
                Require(c.Size==new Vector2(45,45),state+": inventory-sized item cell "+c.Slot);
                var icon=(TextureRect)typeof(ClassicSlot).GetField("_icon",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(c)!;
                Require(icon.GetGlobalRect()==c.GetGlobalRect().Grow(-2),state+": identical two-pixel item inset "+c.Slot);
                var count=(Label)typeof(ClassicSlot).GetField("_count",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(c)!;
                Require(count.GetGlobalRect().End==c.GetGlobalRect().End-new Vector2(2,2),state+": count shares inventory pixel inset "+c.Slot);
            }
            Require(all.Count==54 && bags.Count==12,state+": 14 equipment, 28 inventory, 9 costume, 3 bags, 12 selected bag cells");
            Require(all.Concat(bags).Select(c=>c.Slot).Distinct().Count()==66,state+": unique slot bindings");
            var rect=new Rect2I((int)shell.Position.X,(int)shell.Position.Y,(int)actual.Size.X,(int)actual.Size.Y);
            using var image=GetViewport().GetTexture().GetImage();using var crop=image.GetRegion(rect);crop.SavePng(output+"/"+prefix+"-"+state+".png");
            System.IO.File.WriteAllText(output+"/"+prefix+"-"+state+"-bounds.json",JsonSerializer.Serialize(bounds,new JsonSerializerOptions {WriteIndented=true}));
            captures.Add(new {state,file=prefix+"-"+state+".png",width=rect.Size.X,height=rect.Size.Y,selectedBag=bags[0].Slot,slotMapping=all.Concat(bags).Select(c=>c.Slot)});
        }
        var corner=view.Get<InventoryCornerButton>("costume_toggle")!;
        var skillType=typeof(InventoryWindow).Assembly.GetType("KnightOnlineUiClassic.Windows.SkillLayout")!;
        var skillLayout=(LayoutNode)skillType.GetMethod("Build")!.Invoke(null,new object[]{Plugin.Kit.Layout("{nation}_skilltree_us")})!;
        var skillLabel=skillLayout.Find("text_Skill Point")!;
        int drawerTop=nation==1?40:39,drawerBottom=nation==1?561:558;
        foreach(string id in new[]{"text_weight","text_gold"})
        {
            var label=view.Get<Label>(id)!;
            Require(label.GetThemeFont("font")==Plugin.Kit.FontFor(skillLabel) && label.GetThemeFontSize("font_size")==UiKit.FontSize(skillLabel),
                "Inventory "+id+" shares the exact skill font and size without shrinking");
        }
        var gold=view.Get<Label>("text_gold")!;var coin=view.Get("coin_icon")!;
        Require(coin.Size==new Vector2(22,25) && coin.Position==new Vector2(206,316),
            "Both nations use the identical native gold artwork at the fixed original position and dimensions");
        foreach(var backing in view.Where(n=>n.Id.StartsWith("backing_")).Concat(drawer.Where(n=>n.Id.StartsWith("backing_"))))
            Require(backing.Control is ColorRect {Color:var color} && color==Colors.Black,"Every slot backing uses identical opaque black: "+backing.Node.Id);
        Require(!corner.Expanded && corner.ArrowDirection==-1,"Closed corner arrow points left to open the drawer");
        Require(corner.Position==new Vector2(3,drawerTop) && corner.Size==new Vector2(80,64),"Fold button sits in the native portrait radius");
        Require(corner._HasPoint(new Vector2(18,14)) && !corner._HasPoint(new Vector2(55,50)),"Only the textured radius is clickable; the portrait remains interactive");
        await Capture("closed");
        var initial=view.GlobalPosition;
        var cornerPoint=corner.GlobalPosition+new Vector2(18,14);
        await Capture("closed-corner-hover",cornerPoint);
        Require(corner.IsHovered(),"The radius receives hover above the portrait control");
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=cornerPoint,GlobalPosition=cornerPoint});await Frames(2);
        Require(corner.IsPressed() && !drawer.Visible,"Corner press waits for release before opening");
        await Capture("corner-pressed",cornerPoint);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=cornerPoint,GlobalPosition=cornerPoint});await Frames();
        Require(corner.Expanded && corner.ArrowDirection==1 && drawer.Visible,"Clicking the radius opens the drawer and reverses the arrow to close");
        await Capture("open-corner-hover",cornerPoint);
        Require(view.GlobalPosition==initial,"Opening the Chaos-style left drawer keeps the main inventory fixed");
        Require(drawer.GlobalPosition.X<view.GlobalPosition.X && actual.Size==new Vector2(532,578),"Drawer is joined on the left with the original main inventory height");
        var outer=drawer.Root.Find("drawer_frame")!;
        Require(view.Position.X+3==outer.X+outer.W,"Joined panels meet at the painted inventory border");
        Require(drawer.Position.Y+outer.H==drawerBottom,"Drawer bottom matches the nation's painted inventory border");
        Require(drawer.Root.Find("frame_18_0")!.X-outer.X==9 && outer.X+outer.W-(drawer.Root.Find("frame_18_2")!.X+51)==8,
            "Drawer side gutters match the native inventory: 9 px left and 8 px right");
        Require(drawer.Position.Y+drawer.Root.Find("frame_18_9")!.Y+52==view.Root.Find("frame_2_21")!.Y+52,
            "Drawer and inventory share the same last row and bottom gutter");
        Require(bags[0].GlobalPosition.Y==all.First(c=>c.Slot==InventoryConstants.InventoryStart).GlobalPosition.Y,"Bag contents align with the main inventory rows");
        Require(corner.GetParent().GetChildren().OfType<InventoryPortrait>().All(p=>p.GetIndex()<corner.GetIndex()),"Corner hit testing is above the portrait's rectangular viewport");
        Require(!drawer.Where(n=>n.IsString).Any(),"Drawer has no added text or headings");
        var frame=view.Root.Find("frame_2_0")!;var bagFrame=drawer.Root.Find("frame_18_0")!;
        Require(frame.Texture==bagFrame.Texture && frame.SrcX==bagFrame.SrcX && frame.SrcY==bagFrame.SrcY
            && frame.SizeVec==bagFrame.SizeVec && frame.SizeVec==new Vector2(51,52),"Bag and inventory share the exact unscaled cell artwork");
        using(var image=GetViewport().GetTexture().GetImage())
        {
            var a=view.GlobalPosition+frame.Position;var b=drawer.GlobalPosition+bagFrame.Position;
            for(int y=0;y<52;y++) for(int x=0;x<51;x++)
                if(x<4 || x>=49 || y<3 || y>=48)
                    Require(image.GetPixel((int)a.X+x,(int)a.Y+y)==image.GetPixel((int)b.X+x,(int)b.Y+y),$"Pixel-identical bag/inventory border {x},{y}");
            for(int y=drawerBottom-5;y<drawerBottom;y++) for(int x=0;x<16;x++)
                Require(image.GetPixel((int)view.GlobalPosition.X+100+x,(int)view.GlobalPosition.Y+y)
                    ==image.GetPixel((int)drawer.GlobalPosition.X+outer.X+32+x,(int)view.GlobalPosition.Y+y),
                    $"Pixel-identical native bottom rail {x},{y}");
        }
        foreach(var c in all.Where(c=>c.Slot<14)) Require(c.EmptyIcon is AtlasTexture {Atlas:var atlas} && atlas==Plugin.Kit.Texture("classic_equipment_icons.png"),"Equipment uses actual Chaos empty-slot artwork "+c.Slot);
        foreach(var c in all.Where(c=>InventoryConstants.IsCospreSlot(c.Slot))) Require(c.EmptyIcon!=null && c.EmptyHint.Length>0,"Costume slot has original artwork and a hint "+c.Slot);
        Require(bags.All(c=>!c.InputEnabled),"Missing bags disable all content cells");
        Require(Enumerable.Range(0,3).All(i=>drawer.Get<BaseButton>("bag_tab_"+i)!.Disabled),"All three absent bag selectors are disabled");
        await Capture("empty-costume");
        int stack=ItemData.All().Where(i=>i.Countable>0 && i.Slot<100 && ItemData.Icon(i.Id)!=null).OrderByDescending(i=>i.Name.Contains("Apple")).ThenBy(i=>i.Id).First().Id;
        int bagItem=ItemFor(25);
        Require(bagItem>0,"Magic bag fixture uses a real slot-25 item");
        for(int b=0;b<3;b++)
        {
            inv[InventoryConstants.BagSlotFor(b)]=Item(bagItem);
            for(int i=0;i<12;i++) inv[InventoryConstants.MagicBagPageStart(b)+i]=Item(stack,b*100+i+1);
        }
        inv[InventoryConstants.InventoryStart]=Item(stack,1);inv[InventoryConstants.InventoryStart+1]=Item(stack,100);
        Refresh();await Frames();
        for(int b=0;b<3;b++)
        {
            drawer.Get<BaseButton>("bag_tab_"+b)!.EmitSignal(BaseButton.SignalName.Pressed);await Frames();
            Require(bags.Select(c=>c.Slot).SequenceEqual(Enumerable.Range(InventoryConstants.MagicBagPageStart(b),12)),"Bag "+(b+1)+" binds the full correct page");
            Require(bags.Select(c=>c.Current.Count).SequenceEqual(Enumerable.Range(b*100+1,12)),"Bag "+(b+1)+" shows its own quantities");
            Require(bags.Take(3).Select(c=>c.Position.Y).Distinct().Count()==1 && bags[3].Position.Y>bags[2].Position.Y,"Bag contents use three columns");
            await Capture("bag-"+(b+1));
        }
        int[] positions={8,1,7,2,4,3,9,0,5};int[] codes={112,107,111,101,105,102,113,110,114};
        var seeded=new List<object>();
        for(int i=0;i<positions.Length;i++) {int id=ItemFor(codes[i]);if(id>0) inv[InventoryConstants.CospreStart+positions[i]]=Item(id);seeded.Add(new{position=positions[i],itemId=id,slotCode=codes[i],available=id>0});}
        var items=ItemData.All().Where(i=>i.Slot<100 && ItemData.Icon(i.Id)!=null && !string.IsNullOrWhiteSpace(i.Name)).OrderBy(i=>i.Id).DistinctBy(i=>i.Icon).Take(28).ToArray();
        for(int i=2;i<28;i++) inv[InventoryConstants.InventoryStart+i]=Item(items[i].Id,items[i].Countable>0?i+1:1);
        Refresh();await Frames();
        foreach(var c in all.Where(c=>InventoryConstants.IsCospreSlot(c.Slot))) Require(c.Current.IsEmpty==c.GetChildren().OfType<TextureRect>().First().Visible,"Costume placeholder visibility matches its real contents "+c.Slot);
        await Capture("equipped-costume");
        int raptor=ItemData.All().Where(i=>i.Name.Equals("Raptor",StringComparison.OrdinalIgnoreCase)).Select(i=>i.Id)
            .SelectMany(id=>Enumerable.Range(1,9999).Select(ext=>id+ext))
            .First(id=>ItemData.ExtFor(id) is {PoisonDamage:>=64,Plus:8});
        inv[InventoryConstants.RightHand]=Item(raptor);Refresh();await Frames(20);
        var poisonPortrait=Descendants(actual).OfType<InventoryPortrait>().Single();
        var poisonModel=(Node3D)typeof(InventoryPortrait).GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(poisonPortrait)!;
        Require(poisonPortrait.ClipContents && Descendants(poisonModel).OfType<FxWeaponGlow>().Any(),
            "Poison Raptor plus eight uses the actual native weapon glow in a clipped portrait viewport");
        GD.Print("POISON_RAPTOR_FIXTURE "+raptor+" "+ItemData.DisplayName(raptor));
        var camera=(Camera3D)typeof(InventoryPortrait).GetField("_camera",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(poisonPortrait)!;
        bool Attached(Node node) {for(var parent=node.GetParent();parent!=null && parent!=poisonModel;parent=parent.GetParent()) if(parent is BoneAttachment3D) return true;return false;}
        foreach(var mesh in Descendants(poisonModel).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null && m.IsVisibleInTree() && m is not FxMesh && !Attached(m)))
            Require(InventoryPortrait.FramingPoints(mesh).All(point=>new Rect2(Vector2.Zero,poisonPortrait.Size).Grow(-4).HasPoint(camera.UnprojectPosition(point))),
                "Portrait retains the full-size posed body without framing attached effects: "+mesh.Name);
        Require(poisonPortrait.Material is ShaderMaterial,"Portrait uses its native-radius alpha mask to clip attached effects without shrinking the body");
        using(var mask=((Texture2D)((ShaderMaterial)poisonPortrait.Material!).GetShaderParameter("portrait_mask")).GetImage())
            mask.SavePng(output+"/"+prefix+"-portrait-mask.png");
        var secondView=new SubViewport {Size=new Vector2I(176,260),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(secondView);
        var secondModel=CharacterPreview.Build(net.LastEnter.Race,0,InventoryConstants.VisualSlots.Select(s=>inv[s].ItemId).ToArray(),enableShine:true)!;
        secondView.AddChild(secondModel);await Frames(20);
        var particleViews=Descendants(this).OfType<FxParticles>().Select(p=>(Part:p,Emitter:typeof(FxParticles).GetField("_shared",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p) as GpuParticles3D)).Where(p=>p.Emitter!=null).ToArray();
        Require(particleViews.Length>0 && particleViews.All(p=>p.Part.GetViewport()==p.Emitter!.GetViewport()),
            "Identical preview and world effects always acquire an emitter in their own viewport");
        await Capture("poison-raptor");
        poisonPortrait._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
        poisonPortrait._GuiInput(new InputEventMouseMotion {Relative=new Vector2(65,0)});
        poisonPortrait._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
        await Capture("poison-raptor-rotated");secondView.QueueFree();await Frames();
        var costumeSeed=InventoryConstants.VisualSlots.Where(InventoryConstants.IsCospreSlot).Select(s=>(Slot:s,Item:inv[s])).ToArray();
        foreach(var entry in costumeSeed) inv[entry.Slot]=default;
        Refresh();await Frames(30);
        await Capture("poison-raptor-only");
        foreach(var entry in costumeSeed) inv[entry.Slot]=entry.Item;
        Refresh();await Frames();
        var tipCell=all.First(c=>InventoryConstants.IsCospreSlot(c.Slot) && !c.Current.IsEmpty);tipCell.OnHover!(tipCell.Slot,true);await Frames();
        Require(((CanvasLayer)DetailField(world,"_itemTipLayer")!).GetChildren().OfType<Control>().Any(c=>c.Visible),"Costume uses the actual native item tooltip");
        tipCell.OnHover!(tipCell.Slot,false);
        foreach(var portrait in Descendants(actual).OfType<InventoryPortrait>())
        {
            Require(typeof(InventoryPortrait).GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(portrait) is Node3D,"Portrait loads the actual character model");
            portrait._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true});
            portrait._GuiInput(new InputEventMouseMotion {Relative=new Vector2(12,0)});
            portrait._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false});
        }
        await Capture("portrait-rotated");
        Invoke("SlotClicked",InventoryConstants.InventoryStart+1);Require((int)typeof(InventoryWindow).GetField("_carried",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)! ==InventoryConstants.InventoryStart+1,"Left-click carry retains the original source slot");
        actual._Input(new InputEventKey {Pressed=true,Keycode=Key.Escape});Require((int)typeof(InventoryWindow).GetField("_carried",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)! ==-1,"Escape cancels carry without a move");
        initial=view.GlobalPosition;
        cornerPoint=corner.GlobalPosition+new Vector2(18,14);
        Input.ParseInputEvent(new InputEventMouseMotion {Position=cornerPoint,GlobalPosition=cornerPoint});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=cornerPoint,GlobalPosition=cornerPoint});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=cornerPoint,GlobalPosition=cornerPoint});await Frames();
        Require(view.GlobalPosition==initial && actual.Size==new Vector2(366,578),"Closing the drawer keeps the main inventory fixed");
        Require(!corner.Expanded && corner.ArrowDirection==-1,"Closing restores the left-pointing open arrow");
        await Capture("filled-closed");
        shell.Position=new Vector2(5,42);Invoke("SetCospreOpen",true);await Frames();
        Require(shell.Position.X==0 && shell.Position.Y==42,"Opening at the left edge clamps the expanded window into view");await Capture("viewport-fit");
        inv[InventoryConstants.BagSlotFor(2)]=default;Refresh();await Frames();
        Require(bags[0].Slot==InventoryConstants.MagicBagStart && drawer.Get<BaseButton>("bag_tab_2")!.Disabled,"Removing the selected bag falls back to an equipped bag");
        await Capture("removed-bag");
        var transferPrompt=(QuantityPrompt)typeof(InventoryWindow).GetField("_movePrompt",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(actual)!;
        Invoke("RequestMove",InventoryConstants.InventoryStart+1,InventoryConstants.MagicBagStart);await Frames();
        var amount=Descendants(transferPrompt).OfType<MoneyEdit>().Single();
        Require(transferPrompt.Visible && amount.Value==100,"Inventory-to-bag transfer asks for quantity, prefilled with the complete stack");
        await Capture("transfer-to-bag");
        transferPrompt.GetChildren().OfType<ClassicQuantityPanel>().Single()._Input(new InputEventKey {Pressed=true,Keycode=Key.Escape});
        Require(!transferPrompt.Visible && inv[InventoryConstants.InventoryStart+1].Count==100,"Escape cancels the amount without moving any items");
        Require(game.Inventory.TransferToInventorySlot(InventoryConstants.MagicBagStart)==InventoryConstants.InventoryStart,
            "Bag right-click chooses a compatible existing inventory stack before a free cell");
        bags[0]._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});await Frames();
        Require(transferPrompt.Visible && amount.Value==1,"Bag-to-inventory transfer asks for the quantity even when the stack count is one");
        await Capture("transfer-from-bag");transferPrompt.Close();
        bool acceptedQuantity=false;transferPrompt.Open(ItemData.Icon(stack),"Transfer","",100,25,n=>acceptedQuantity=n==25);
        transferPrompt.GetChildren().OfType<ClassicQuantityPanel>().Single()._Input(new InputEventKey {Pressed=true,Keycode=Key.Enter});
        Require(acceptedQuantity && !transferPrompt.Visible,"Enter confirms the selected amount exactly once");
        bool destroyed=false;var destroy=new ClassicInventoryDestroy(view,()=>destroyed=true);actual.AddChild(destroy);await Frames();
        await Capture("destroy-cancel");destroy._Input(new InputEventKey {Pressed=true,Keycode=Key.Escape});await Frames();
        Require(!destroyed,"Escape cancels native item destruction");
        destroy=new ClassicInventoryDestroy(view,()=>destroyed=true);actual.AddChild(destroy);await Frames();
        await Capture("destroy-confirm");destroy._Input(new InputEventKey {Pressed=true,Keycode=Key.Enter});await Frames();
        Require(destroyed,"Enter confirms native item destruction exactly once");
        view.Get<BaseButton>("btn_close")!.EmitSignal(BaseButton.SignalName.Pressed);Require(closed,"Original close action is preserved");
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
        System.IO.File.WriteAllText(output+"/"+prefix+".json",JsonSerializer.Serialize(new {nation,pluginHash=Hash(ProjectSettings.GlobalizePath("res://../bin/KnightOnlineUiClassic.dll")),
            clientHash=Hash(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/.godot/mono/temp/bin/ExportRelease/LibreKO.dll")),captures,seeded,checks},new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("INVENTORY_AUDIT_OK "+prefix+": "+captures.Count+" captures, "+checks.Count+" checks");
        Fx.BeginShutdown(GetTree().Root);shell.Free();
        ((Control)DetailField(world,"_invContent")!).Free();world.Free();net.Free();await Frames(20);
        foreach(var release in (List<Action>)typeof(Shutdown).GetField("Releases",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!) release();
        await Frames(10);GC.Collect();GC.WaitForPendingFinalizers();await Frames(10);
    }
}
