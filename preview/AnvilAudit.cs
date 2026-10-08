using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using KnightOnlineUiClassic.Layout;
using System.Reflection;
using System.Text.Json;

public partial class Preview
{
    private async Task CaptureAnvilAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/anvil-window-audit");if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} directory)output=directory;System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(800,650);
        AddChild(new ColorRect{Color=new Color("252822"),Size=new Vector2(800,650),MouseFilter=MouseFilterEnum.Ignore});
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo{Nation=nation,Class=nation==1?105:205,Race=nation==1?1:11,Name="Anvil Preview",Gear=new int[8],Inventory=new LibreKO.Domain.ItemSlot[InventoryConstants.InventoryTotal]});
        var world=new World();DetailCall(world,"BuildInventoryPanel");
        var orphan=(Control)DetailField(world,"_invContent")!;orphan.Visible=false;world.AddChild(orphan);
        DetailCall(world,"BuildNpcDialog");
        var window=(HudWindow)DetailCall(world,"BuildUpgradeUiPreview")!;AddChild(window);
        var sheet=typeof(World).GetProperty("Sheet",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;
        sheet.GetType().GetMethod("SetMaxWeight")!.Invoke(sheet,new object[]{17100});
        var tips=(CanvasLayer)DetailField(world,"_itemTipLayer")!;tips.Reparent(this);
        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());
        var shell=ClassicAnvilSkin.Apply(window.Body)!;
        PluginHost.Ui.ReplaceDialogs(request=>new ClassicAnvilNotice(request));
        var choice=NativeAnvil.Of(world)!.Choice;choice.Reparent(this);ClassicAnvilChoice.Apply(choice.Body);
        var screens=new List<object>();var checks=new List<string>();
        void Require(bool valid,string message){if(!valid)throw new Exception("ANVIL_AUDIT: "+message);checks.Add(message);}
        async Task Frames(int count=8){for(int i=0;i<count;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        async Task Capture(string name,int settleFrames=8)
        {
            await Frames(settleFrames);window.Position=new Vector2(216,32);choice.Position=new Vector2(260,180);await Frames(2);
            var controls=Descendants(shell).OfType<Control>().Where(c=>c.IsVisibleInTree()).ToArray();
            foreach(var control in controls.Where(c=>c.HasMeta("anvil_expected_rect")))
                Require(control.GetRect()==control.GetMeta("anvil_expected_rect").AsRect2(),"Measured bounds: "+control.Name+" / "+name);
            foreach(var modal in GetChildren().OfType<Notice>())
                foreach(var button in Descendants(modal).OfType<Button>())
                    Require(button.Size==new Vector2(73,20),"Original confirmation button bounds / "+name);
            Require(GetViewportRect().Encloses(window.GetGlobalRect()),"Complete 28-slot bench fits viewport / "+name);
            string file=(nation==1?"karus":"human")+"-"+name+".png";GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);
            screens.Add(new{name,file,controls=controls.Where(c=>c is Button or ItemSlotView).Select(c=>new{name=c.Name.ToString(),rect=c.GetGlobalRect().ToString()}).ToArray()});
        }
        bool Locked()=>typeof(World).GetProperty("UpgradeInteractionLocked",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world) is true;
        var inventory=(Inventory)typeof(World).GetProperty("Inv",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;
        var ids=(int[])DetailField(world,"_upgradeItemIds")!;
        var originDef=ItemData.Get(156210008)!;
        Require(Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_item_1").Item.Count==1,$"Staged scroll represents exactly one consumed material; origin class={originDef.Class} type={originDef.ItemType} grade={originDef.Grade}");
        Require(Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_1").Item.Count==2,"Bag shows only the unreserved scroll count");
        Require(Descendants(shell).OfType<ItemSlotView>().Count(c=>c.Name.ToString().StartsWith("anvil_bag_"))==28,"All 28 inventory cells visible without scrolling");
        await Capture("ready");
        Require(Descendants(shell).OfType<Label>().Any(l=>l.Text.StartsWith("Weight : ")&&l.Text.EndsWith($"{1710f:0.0}")),"Inventory weight and capacity are visible in the shared footer");
        var firstBag=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_0");
        var lastBag=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_27");
        float frameLeft=firstBag.Position.X-4,frameRight=lastBag.Position.X-4+51;
        Require(frameLeft+1-2==361-(frameRight-1)&&frameLeft+1-2==8,"Painted inventory rims have equal eight-pixel side insets");
        Require(window.Size.Y==553,"Window bounds end at the cropped footer baseline");
        var dragCell=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_1");
        var dragIcon=dragCell.GetChildren().OfType<TextureRect>().Single();
        var press=dragCell.GetGlobalTransform()*new Vector2(13,17);
        var grab=dragIcon.GetGlobalTransform().AffineInverse()*press;
        dragCell._GuiInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left,Position=new Vector2(13,17)});
        var cursor=press+new Vector2(30,10);
        var dragPreview=NativeAnvil.DragPreview(dragCell,new Vector2(13,17));AddChild(dragPreview);dragPreview.Position=cursor;
        var carried=dragPreview.GetChildren().OfType<TextureRect>().Single();
        var classicPreview=IconDragPreview.Create(dragIcon,grab);AddChild(classicPreview);classicPreview.Position=cursor;
        var classicCarried=classicPreview.GetChildren().OfType<TextureRect>().Single();
        Require(carried.Position.IsEqualApprox(classicCarried.Position)&&carried.Size.IsEqualApprox(classicCarried.Size),$"Native drag matches Classic inventory preview geometry: {carried.Position}/{classicCarried.Position}, {carried.Size}/{classicCarried.Size}");
        classicPreview.QueueFree();await Frames();
        Require(carried.Size==dragIcon.Size,"Native drag preserves the displayed inventory icon size");
        Require((carried.GetGlobalTransform()*grab).DistanceTo(cursor)<1,"Native drag keeps the pressed icon point under the cursor");
        await Capture("inventory-drag");
        dragPreview.QueueFree();await Frames();
        var upper=Descendants(shell).OfType<ItemSlotView>().Where(c=>c.Name.ToString().StartsWith("anvil_item_")).ToDictionary(c=>c.Index);
        Variant FromSocket(int index)=>new Godot.Collections.Dictionary{{"companionFrom",index}};
        Require(!upper[1]._CanDropData(Vector2.Zero,FromSocket(0)),"Staged Raptor cannot move into a scroll socket");
        inventory[Inventory.GridStart+8]=inventory[Inventory.GridStart];
        Variant duplicateWeapon=new Godot.Collections.Dictionary{{"invFrom",Inventory.GridStart+8}};
        Require(!upper[1]._CanDropData(Vector2.Zero,duplicateWeapon),"A duplicate Raptor in inventory cannot enter a scroll socket");
        DetailCall(world,"ClassicAnvilTake",Inventory.GridStart+8);
        Require(ids[1]==379021000&&ids.Count(id=>id==156210008)==1,"Right-clicking a duplicate Raptor cannot replace or add upgrade materials");
        inventory[Inventory.GridStart+8]=default;
        Variant Extra(int id)
        {inventory[Inventory.GridStart+8]=new LibreKO.Domain.ItemSlot{ItemId=id,Count=1,Durability=1};return new Godot.Collections.Dictionary{{"invFrom",Inventory.GridStart+8}};}
        Require(!upper[7]._CanDropData(Vector2.Zero,Extra(379021000)),"A second scroll is rejected");
        Require(!upper[1]._CanDropData(Vector2.Zero,Extra(379221000)),"Low-class scroll cannot replace BUS on a high-class Raptor");
        Require(!upper[2]._CanDropData(Vector2.Zero,Extra(379258000)),"Karivdis cannot replace Trina in a normal upgrade");
        Require(!upper[2]._CanDropData(Vector2.Zero,Extra(354000000)),"Accessory protection cannot enter the weapon bench");
        Require(!upper[1]._CanDropData(Vector2.Zero,Extra(379159000)),"Accessory scroll cannot enter the weapon bench");
        Require(!upper[7]._CanDropData(Vector2.Zero,Extra(890092000)),"Logos cannot be added alongside Trina");
        Require(upper[2]._CanDropData(Vector2.Zero,Extra(890092000)),"Supported Logos can replace Trina instead of duplicating protection");
        inventory[Inventory.GridStart+8]=default;
        Require(upper[8]._CanDropData(Vector2.Zero,FromSocket(1)),"Scroll can move to material socket eight");
        upper[8]._DropData(Vector2.Zero,FromSocket(1));
        Require(ids[1]==0&&ids[8]==379021000,"Moving a scroll preserves one reservation");
        Require(upper[6]._CanDropData(Vector2.Zero,FromSocket(8)),"Scroll in socket eight can move to socket six");
        upper[6]._DropData(Vector2.Zero,FromSocket(8));await Capture("scroll-moved");
        Require(ids[8]==0&&ids[6]==379021000,"Eight to six move updates the bench selection");
        Require(!upper[0]._CanDropData(Vector2.Zero,FromSocket(6)),"Scroll cannot replace the upgrade origin");
        upper[2]._DropData(Vector2.Zero,FromSocket(6));
        Require(ids[2]==379021000&&ids[6]==700002000,"Compatible material sockets can swap");
        upper[6]._DropData(Vector2.Zero,FromSocket(2));upper[1]._DropData(Vector2.Zero,FromSocket(6));
        var transferPreview=new UpgradeResult(2,2,1,new[]{new UpgradeSlotResult(156210009,0)});
        DetailCall(world,"OnUpgradeResult",transferPreview);DetailCall(world,"OnUpgradeResult",transferPreview);
        var potionCell=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_5");
        Require(potionCell.Look==SlotLook.Normal,"Unrelated inventory items retain their normal color");
        var potionDrag=potionCell.DragOut!(potionCell);
        Require(potionDrag.VariantType!=Variant.Type.Nil,"Unrelated item drag remains available");
        Require(!upper[1]._CanDropData(Vector2.Zero,potionDrag),"Upgrade sockets reject unrelated inventory items");
        Require(dragCell._CanDropData(Vector2.Zero,potionDrag),"Inventory permits unrelated items to be dragged to another bag cell");
        var originCell=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_item_0");
        originCell.EmitSignal(Control.SignalName.MouseEntered);
        var tooltip=(PanelContainer)DetailField(world,"_itemTipPanel")!;tooltip.Position=new Vector2(15,30);
        await Frames();Require(tooltip.Visible,"Hover opens the native item tooltip");await Capture("item-tooltip");
        originCell.EmitSignal(Control.SignalName.MouseExited);Require(!tooltip.Visible,"Leaving the slot closes the native item tooltip");
        DetailCall(world,"ConfirmUpgrade");var notice=(Notice)DetailField(world,"_upgradeConfirm")!;notice.Reparent(this);await Capture("confirmation");
        Input.ParseInputEvent(new InputEventKey{Pressed=true,Keycode=Key.Escape});await Frames();Require(!Locked()&&ids[0]!=0,"Escape cancels confirmation and preserves staged items");
        DetailCall(world,"ClassicAnvilCancel");await Capture("empty");Require(ids.All(i=>i==0)&&window.Visible,"Cancel resets selection while keeping the bench open");
        var cells=Descendants(shell).OfType<ItemSlotView>().ToArray();
        DetailCall(world,"ClassicAnvilTake",Inventory.GridStart+5);await Capture("invalid-material");Require(ids.All(i=>i==0),"Unrelated potion cannot be staged");
        world.StageAnvilUpgrade(156210008,379021000,700002000);
        var preview=new UpgradeResult(2,2,1,new[]{new UpgradeSlotResult(156210009,0)});
        DetailCall(world,"OnUpgradeResult",preview);DetailCall(world,"OnUpgradeResult",preview);await Capture("restaged");
        DetailCall(world,"ConfirmUpgrade");((Notice)DetailField(world,"_upgradeConfirm")!).Reparent(this);
        Input.ParseInputEvent(new InputEventKey{Pressed=true,Keycode=Key.Enter});await Frames();Require(Locked(),"Enter approves the modal and locks staging until the server replies");
        DetailCall(world,"ClearUpgradeSocket",0);Require(ids[0]!=0,"Pending request cannot clear the origin");await Capture("awaiting-result");
        DetailCall(world,"OnUpgradeResult",new UpgradeResult(2,1,1,new[]{new UpgradeSlotResult(156210009,0)}));
        Require(Locked(),"Server reply keeps the bench locked throughout its reveal");
        for(int i=0;i<15;i++){await Capture($"success-frame-{i:00}",0);await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);}
        await ToSignal(GetTree().CreateTimer(3.1),SceneTreeTimer.SignalName.Timeout);Require(!Locked(),"Reveal completion releases the interaction lock");await Capture("success-result");
        void Restage()
        {
            DetailCall(world,"ClassicAnvilCancel");world.StageAnvilUpgrade(156210008,379021000,700002000);
            DetailCall(world,"OnUpgradeResult",preview);DetailCall(world,"OnUpgradeResult",preview);
            DetailCall(world,"SendUpgrade");
        }
        Restage();DetailCall(world,"OnUpgradeResult",new UpgradeResult(2,1,0,new[]{new UpgradeSlotResult(0,0)}));
        for(int i=0;i<15;i++){await Capture($"failure-frame-{i:00}",0);await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);}
        await ToSignal(GetTree().CreateTimer(3.1),SceneTreeTimer.SignalName.Timeout);await Capture("failure-result");
        Restage();DetailCall(world,"OnUpgradeResult",new UpgradeResult(2,1,0,new[]{new UpgradeSlotResult(156210007,0)}));
        await ToSignal(GetTree().CreateTimer(3.1),SceneTreeTimer.SignalName.Timeout);await Capture("logos-retained-result");
        Require(Descendants(shell).OfType<Label>().Any(l=>l.Text.Contains("retained",StringComparison.OrdinalIgnoreCase)),"Logos failure reports retained or downgraded item, never destruction");
        DetailCall(world,"SetAnvilBench",Enum.ToObject(typeof(World).GetNestedType("AnvilBench",BindingFlags.NonPublic)!,1));DetailCall(world,"RefreshUpgradeActions");await Capture("compound-empty");
        for(int i=0;i<3;i++)inventory[Inventory.GridStart+i]=new LibreKO.Domain.ItemSlot{ItemId=330620270,Count=1,Durability=1};
        inventory[Inventory.GridStart+3]=new LibreKO.Domain.ItemSlot{ItemId=379159000,Count=3,Durability=1};
        for(int i=0;i<4;i++)DetailCall(world,"ClassicAnvilTake",Inventory.GridStart+i);
        Require(ids.Take(3).All(i=>i==330620270),"Compound stages three distinct copies of the same accessory");
        DetailCall(world,"OnUpgradeResult",new UpgradeResult(2,2,1,new[]{new UpgradeSlotResult(330620431,0)}));
        await Capture("compound-ready");
        DetailCall(world,"ClearUpgradeSocket",1);Require(ids[1]==0,"Right-click clearing removes only the chosen accessory");await Capture("compound-two-copies");
        DetailCall(world,"SetAnvilBench",Enum.ToObject(typeof(World).GetNestedType("AnvilBench",BindingFlags.NonPublic)!,0));
        inventory[Inventory.GridStart]=new LibreKO.Domain.ItemSlot{ItemId=156210008,Count=1,Durability=7000};
        inventory[Inventory.GridStart+1]=new LibreKO.Domain.ItemSlot{ItemId=379021000,Count=3,Durability=1};
        inventory[Inventory.GridStart+2]=new LibreKO.Domain.ItemSlot{ItemId=700002000,Count=5,Durability=1};
        Restage();DetailCall(world,"CloseUpgrade");Require(Locked(),"Closing a pending request retains its inventory lock");
        DetailCall(world,"OpenAnvilBench",Enum.ToObject(typeof(World).GetNestedType("AnvilBench",BindingFlags.NonPublic)!,0));
        DetailCall(world,"ClassicAnvilTake",Inventory.GridStart);Require(ids.All(i=>i==0),"Reopening cannot stage items before the earlier acknowledgment");await Capture("reopened-pending");
        DetailCall(world,"OnUpgradeResult",new UpgradeResult(2,1,0,new[]{new UpgradeSlotResult(0,0)}));Require(!Locked(),"Quiet acknowledgment releases the retained lock");await Capture("quiet-result");
        world.StageAnvilUpgrade(156210008,379021000,700002000);
        var refusal=new UpgradeResult(2,2,2,Array.Empty<UpgradeSlotResult>());
        DetailCall(world,"OnUpgradeResult",refusal);DetailCall(world,"OnUpgradeResult",refusal);
        Require(!((UpgradePreviewGate)DetailField(world,"_upgradePreviewGate")!).Pending,"Legacy server preview refusal releases the outstanding preview");
        await Capture("preview-refused");
        DetailCall(world,"ClassicAnvilCancel");
        var moveSource=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_5");
        var moveTarget=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_6");
        int originalCount=inventory[Inventory.GridStart+5].Count;
        var moveData=moveSource.DragOut!(moveSource);
        Require(moveTarget._CanDropData(Vector2.Zero,moveData),"Ordinary items can be rearranged in the embedded inventory");
        moveTarget._DropData(Vector2.Zero,moveData);DetailCall(world,"DeliverItemMoveResult",true);
        Require(moveSource.Item.IsEmpty&&moveTarget.Item.Count==originalCount,"Server-confirmed inventory moves refresh both embedded cells without losing count");
        await Capture("inventory-reordered");
        int scrollCount=inventory[Inventory.GridStart+1].Count;
        DetailCall(world,"ClassicAnvilTake",Inventory.GridStart+1);
        var returnTarget=Descendants(shell).OfType<ItemSlotView>().Single(c=>c.Name=="anvil_bag_7");
        returnTarget._DropData(Vector2.Zero,FromSocket(1));DetailCall(world,"DeliverItemMoveResult",true);
        Require(ids[1]==0&&returnTarget.Item.IsEmpty&&inventory[Inventory.GridStart+1].Count==scrollCount,"Returning a staged scroll stack to another bag cell releases its reservation and keeps the stack in place");
        await Capture("scroll-returned");
        window.Visible=false;choice.Visible=true;await Capture("choice");
        Require(Descendants(choice).OfType<Button>().Count(b=>b.IsVisibleInTree()&&b.Text is "Upgrade Item" or "Compound Accessory" or "Walk away")==3,"Original three-option selection menu uses live callbacks");
        string Hash(string p)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(p)));
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{clientHash=Hash(ProjectSettings.GlobalizePath("res://../../LibreKO/Client/.godot/mono/temp/bin/ExportRelease/LibreKO.dll")),pluginHash=Hash(ProjectSettings.GlobalizePath("res://../bin/KnightOnlineUiClassic.dll")),screens,checks},new JsonSerializerOptions{WriteIndented=true}));
        window.Free();choice.Free();tips.Free();world.Free();net.Free();await Frames();
        GD.Print("ANVIL_AUDIT_OK "+checks.Count);
    }
}
