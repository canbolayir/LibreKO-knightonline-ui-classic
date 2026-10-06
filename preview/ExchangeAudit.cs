using Godot;

using LibreKO;

using LibreKO.Domain;

using LibreKO.Network;

using LibreKO.Plugins;

using KnightOnlineUiClassic;

using KnightOnlineUiClassic.Windows;

using System.Reflection;

using System.Text.Json;

using NativeSlot=LibreKO.Domain.ItemSlot;



public partial class Preview

{

    private async Task CaptureExchangeAudit(PluginGame game,int nation)

    {

        string output=ProjectSettings.GlobalizePath("res://../../research/trade-window-audit");System.IO.Directory.CreateDirectory(output);

        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})

            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);

        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(800,720);

        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(800,720),MouseFilter=MouseFilterEnum.Ignore});

        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);

        net.Sheet.SeedWealth(1_234_567,50_000);net.Sheet.SetMaxWeight(10_000);net.Sheet.SeedProgress(70,0,100);

        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo {Nation=nation,Class=nation==1?105:205,Race=nation==1?1:11,Name="Classic Preview",Gear=new int[8],Inventory=new NativeSlot[InventoryConstants.InventoryTotal]});

        ItemData.EnsureLoaded();

        var groups=(Dictionary<int,List<ItemData.SellEntry>>)typeof(ItemData).GetField("_sell",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;

        var ids=groups.Values.SelectMany(g=>g).Select(e=>e.Id).Distinct().Where(id=>ItemData.Get(id)!=null && ItemData.IsSellable(id)).ToArray();

        int stack=ids.First(id=>ItemData.Get(id)!.Countable!=0);

        var single=ids.Where(id=>ItemData.Get(id)!.Countable==0).Take(13).ToArray();

        var world=new World();var inventory=(Inventory)typeof(World).GetProperty("Inv",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;inventory.EnsureLength(InventoryConstants.InventoryTotal);

        void Seed() {inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=100,Durability=ItemData.MaxDurabilityOf(stack)};for(int i=0;i<single.Length;i++)inventory[Inventory.GridStart+i+1]=new NativeSlot {ItemId=single[i],Count=1,Durability=ItemData.MaxDurabilityOf(single[i])};}

        Seed();DetailCall(world,"BuildInventoryPanel");var orphan=(Control)DetailField(world,"_invContent")!;orphan.Visible=false;world.AddChild(orphan);DetailCall(world,"BuildNpcDialog");DetailCall(world,"BuildExchangePanel");

        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;

        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());

        var auditLayers=new[]{"_exLayer","_exAmountLayer","_exWaitLayer","_exRequestLayer","_exFinalLayer","_itemTipLayer"}.Select(name=>(CanvasLayer)DetailField(world,name)!).ToArray();

        foreach(var layer in auditLayers)layer.Reparent(this);

        var window=(HudWindow)DetailField(world,"_exPanel")!;var shell=ClassicExchangeSkin.Apply(window.Body)!;

        typeof(World).GetField("_exPartnerName",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,"Knight Online");DetailCall(world,"OpenExchange");

        var screens=new List<object>();var checks=new List<string>();

        void Require(bool value,string message) {if(!value)throw new Exception("TRADE_AUDIT: "+message);checks.Add(message);}

        async Task Frames(int count=6) {for(int i=0;i<count;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}

        async Task KeyPress(Key key,bool echo=false) {

            Input.ParseInputEvent(new InputEventKey {Pressed=true,Echo=echo,Keycode=key,PhysicalKeycode=key});

            Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=key,PhysicalKeycode=key});

            await Frames(2);

        }

        void Click(Button button)=>button.EmitSignal(BaseButton.SignalName.Pressed);

        IEnumerable<Button> controlsButtons()=>Descendants(shell).OfType<Button>();

        async Task Capture(string name) {

            await Frames();window.Position=new Vector2(218,40);await Frames(2);

            foreach(string gridName in new[]{"trade_my_offer","trade_other_offer"}) {

                var grid=shell.GetNode<Control>(gridName);var cells=grid.GetChildren().OfType<ClassicExchangeCell>().ToArray();Require(cells.Length==12,"Twelve simultaneous slots: "+gridName+" / "+name);

                foreach(var cell in cells)Require(grid.GetGlobalRect().Encloses(cell.GetGlobalRect()),"Offer slot fully inside visible grid: "+name+" / "+cell.Position);

            }

            foreach(var button in controlsButtons()) if(button.HasMeta("party_expected_rect"))Require(button.GetRect()==button.GetMeta("party_expected_rect").AsRect2(),"Exact artwork button bounds: "+button.Name+" / "+name);

            Require(!Descendants(shell).OfType<ScrollContainer>().Any(),"No scrolling offers: "+name);

            var modal=Descendants((CanvasLayer)DetailField(world,"_exAmountLayer")!).OfType<ClassicExchangeAmountPanel>().Single();

            foreach(var control in Descendants(modal).OfType<Control>().Where(c=>c.IsVisibleInTree() && c is Button or Label or TextureRect or LineEdit))Require(modal.GetGlobalRect().Encloses(control.GetGlobalRect()),"Quantity modal control contained: "+control.Name+" / "+name);

            foreach(var notice in auditLayers.SelectMany(l=>Descendants(l)).OfType<ClassicExchangeNoticePanel>().Where(n=>n.IsVisibleInTree())) {

                Require(notice.Position==((new Vector2(800,720)-notice.Size)/2).Round(),"Original request or waiting panel is centered: "+name);

                foreach(var control in Descendants(notice).OfType<Control>())Require(notice.GetGlobalRect().Encloses(control.GetGlobalRect()),"Request/wait control contained: "+control.Name+" / "+name);

            }

            var controls=Descendants(shell).OfType<Control>().Where(c=>c.IsVisibleInTree() && c is Button or Label or ClassicExchangeCell).ToArray();

            foreach(var control in controls)Require(window.GetGlobalRect().Grow(.1f).Encloses(control.GetGlobalRect()),"Trade bounds: "+control.Name+" / "+name);

            string file=(nation==1?"karus":"human")+"-"+name+".png";GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);

            screens.Add(new{name,file,controls=controls.Select(c=>new{name=c.Name.ToString(),x=c.GetGlobalRect().Position.X,y=c.GetGlobalRect().Position.Y,width=c.Size.X,height=c.Size.Y}).ToArray()});

        }

        await Frames(20);await Capture("empty");

        foreach(var key in new[]{Key.Enter,Key.KpEnter}) {await KeyPress(key);await Frames(2);Require(!(bool)DetailField(world,"_exConfirmedByMe")!,"Enter never confirms empty trade: "+key);}

        Require(!((Button)DetailField(world,"_exConfirmBtn")!).HasFocus(),"Final decision has no keyboard focus");

        var bag=shell.GetNode<ClassicExchangeCell>("exchange_bag_0");

        var payload=Variant.From(new Godot.Collections.Dictionary { ["invFrom"]=Inventory.GridStart,["id"]=stack });

        var own=shell.GetNode<Control>("trade_my_offer").GetChildren().OfType<ClassicExchangeCell>().First();

        var other=shell.GetNode<Control>("trade_other_offer").GetChildren().OfType<ClassicExchangeCell>().First();

        Require(own._CanDropData(Vector2.Zero,payload),"Own offer accepts inventory drag");Require(!other._CanDropData(Vector2.Zero,payload),"Partner offer rejects inventory drag");

        var from=bag.GetGlobalRect().GetCenter();var to=own.GetGlobalRect().GetCenter();

        Input.ParseInputEvent(new InputEventMouseMotion {Position=from,GlobalPosition=from});await Frames(2);

        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=from,GlobalPosition=from});await Frames(2);

        Input.ParseInputEvent(new InputEventMouseMotion {Position=from+new Vector2(18,0),GlobalPosition=from+new Vector2(18,0),Relative=new Vector2(18,0),ButtonMask=MouseButtonMask.Left});await Frames(2);

        Require(GetViewport().GuiIsDragging(),"Physical pointer drag carries actual inventory item");

        Input.ParseInputEvent(new InputEventMouseMotion {Position=to,GlobalPosition=to,Relative=to-from,ButtonMask=MouseButtonMask.Left});await Frames(2);

        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=to,GlobalPosition=to});await Frames(2);

        Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames();Require((bool)DetailField(world,"_exAmountShown")!,"Drag opens quantity prompt");

        var modalPanel=Descendants((CanvasLayer)DetailField(world,"_exAmountLayer")!).OfType<ClassicExchangeAmountPanel>().Single();

        await KeyPress(Key.Escape);Require(!(bool)DetailField(world,"_exAmountShown")!,"Escape cancels quantity without changing inventory");Require(inventory[Inventory.GridStart].Count==100,"Cancelled quantity keeps full stack");await Frames();

        

        shell.GetNode<ClassicExchangeCell>("exchange_bag_0")._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});await Frames();Require((bool)DetailField(world,"_exAmountShown")!,"Stack opens original quantity modal");

        var spin=(SpinBox)DetailField(world,"_exAmountSpin")!;Require(spin.GetLineEdit().Text=="100","Offer quantity starts at entire available stack");await Capture("quantity");

        foreach(string invalid in new[]{"","abc","0","-1","101","2147483648"}) {

            spin.GetLineEdit().Text=invalid;await KeyPress(Key.Enter);await Frames(2);

            Require((bool)DetailField(world,"_exAmountShown")! && !(bool)DetailField(world,"_exAddInFlight")!,"Invalid quantity remains open without sending: "+invalid);

        }

        await Capture("invalid-quantity");

        spin.GetLineEdit().Text="10";await KeyPress(Key.Enter);Require((bool)DetailField(world,"_exAddInFlight")!,"Quantity sends add request");

        Require(inventory[Inventory.GridStart].Count==100,"Inventory unchanged before server acknowledgment");DetailCall(world,"OnExchangeConfirm");Require(!(bool)DetailField(world,"_exConfirmedByMe")!,"Cannot confirm pending add");

        DetailCall(world,"OnExchangeAddResult",true);await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames(2);Require(!(bool)DetailField(world,"_exConfirmedByMe")!,"Enter after quantity acknowledgment never confirms final trade");Require(inventory[Inventory.GridStart].Count==90,"Successful add reduces only acknowledged quantity");

        DetailCall(world,"OfferSlotAmount",Inventory.GridStart,10);DetailCall(world,"OnExchangeAddResult",true);Require((int)DetailCall(world,"ExchangeOfferCount")! ==1,"Repeated stack offers share one visible capacity slot");

        DetailCall(world,"OpenExchangeGold");await Frames();await Capture("coins");spin.GetLineEdit().Text="12345";await KeyPress(Key.KpEnter);DetailCall(world,"OnExchangeAddResult",true);Require(net.Sheet.Gold==1_222_222,"Coins deducted only after acknowledgment");

        for(int i=0;i<11;i++){DetailCall(world,"OfferSlotAmount",Inventory.GridStart+i+1,1);DetailCall(world,"OnExchangeAddResult",true);}

        for(int i=0;i<12;i++)DetailCall(world,"OnExchangeOtherAdd",single[i],1,ItemData.MaxDurabilityOf(single[i]));

        DetailCall(world,"OnExchangeOtherAdd",Net.ExchangeGoldItem,54321,(short)0);

        await Capture("twelve-slots");

        DetailCall(world,"OfferSlot",Inventory.GridStart+12);Require(!(bool)DetailField(world,"_exAddInFlight")!,"Thirteenth distinct item is rejected");

        bag=shell.GetNode<ClassicExchangeCell>("exchange_bag_0");

        Require(!shell.GetNode<Control>("trade_other_offer").GetChildren().OfType<ClassicExchangeCell>().First()._CanDropData(Vector2.Zero,payload),"Partner grid remains read-only");

        Require((int)DetailCall(world,"ExchangeOfferCount")! ==12,"Capacity covers twelve offers");

        Click((Button)DetailField(world,"_exConfirmBtn")!);await Capture("final-approval-first");
        Require((bool)DetailField(world,"_exFinalPending")! && !(bool)DetailField(world,"_exConfirmedByMe")!,"First participant also needs explicit approval");
        var firstFinal=(CanvasLayer)DetailField(world,"_exFinalLayer")!;
        Click(Descendants(firstFinal).OfType<Button>().Single(b=>b.Name=="exchange_final_decline"));await Frames();
        Require(!firstFinal.Visible && !shell.OfferBlocked,"No returns to editable offers before partner locks");
        bag._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});await Frames();
        Require((bool)DetailField(world,"_exAmountShown")!,"Right click can edit the offer after declining approval");
        spin.GetLineEdit().Text="1";await KeyPress(Key.Enter);DetailCall(world,"OnExchangeAddResult",true);
        Require(inventory[Inventory.GridStart].Count==79,"Declined approval allows adding another item quantity");
        Click((Button)DetailField(world,"_exConfirmBtn")!);await Frames();Require(firstFinal.Visible,"Trade can be pressed again after editing");
        DetailCall(world,"CloseExchangeFinal");
        DetailCall(world,"OnExchangeOtherDecide");await Capture("partner-confirmed");

        foreach(var key in new[]{Key.Enter,Key.KpEnter}) {await KeyPress(key);await KeyPress(key,true);await Frames(2);Require(!(bool)DetailField(world,"_exConfirmedByMe")!,"Partner confirmation cannot turn Enter into a final decision: "+key);}

        Require(shell.OfferBlocked && !shell.Blocked,"Partner lock disables editing but allows own Trade decision");
        bag._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});DetailCall(world,"OfferSlotAmount",Inventory.GridStart,1);DetailCall(world,"OpenExchangeGold");
        Require(!(bool)DetailField(world,"_exAmountShown")! && !(bool)DetailField(world,"_exAddInFlight")!,"Partner lock prevents right click, item additions and coin edits");
        var finalLayer=(CanvasLayer)DetailField(world,"_exFinalLayer")!;
        var finalAccept=Descendants(finalLayer).OfType<Button>().Single(b=>b.Name=="exchange_final_accept");
        var finalDecline=Descendants(finalLayer).OfType<Button>().Single(b=>b.Name=="exchange_final_decline");
        Click((Button)DetailField(world,"_exConfirmBtn")!);await Capture("final-approval");
        var finalPanel=Descendants(finalLayer).OfType<ClassicExchangeNoticePanel>().Single();
        var prompt=finalPanel.GetNode<Label>("final_prompt");
        Require(prompt.GetRect()==new Rect2(24,35,279,64),"Final approval prompt matches actual pixel bounds");
        Require(prompt.Text=="Are you sure you want to trade?","Approval contains only a question without coin details");
        foreach(var control in new Control[]{prompt,finalAccept,finalDecline}) {
            Require(control.GetThemeFont("font")==Plugin.Kit.Bold && control.GetThemeFontSize("font_size")==12,"Approval uses the same Arial Bold 12px skill font: "+control.Name);
        }
        Require(finalAccept.Position.Y==finalDecline.Position.Y && finalAccept.Position.X==finalPanel.Size.X-finalDecline.GetRect().End.X,"Final approval buttons have equal baseline and side margins");
        Require(finalLayer.Visible && !(bool)DetailField(world,"_exConfirmedByMe")!,"Trade button opens approval without sending a decision");
        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);
        Require(finalLayer.Visible && !(bool)DetailField(world,"_exConfirmedByMe")!,"Enter and numpad Enter never accept final approval");
        Click(finalDecline);Require(!finalLayer.Visible && (bool)DetailField(world,"_exShown")! && !(bool)DetailField(world,"_exConfirmedByMe")!,"No returns to the unchanged trade");
        Click((Button)DetailField(world,"_exConfirmBtn")!);await Frames();await KeyPress(Key.Escape);
        Require(!finalLayer.Visible && (bool)DetailField(world,"_exShown")!,"Escape cancels approval without cancelling trade");
        Click((Button)DetailField(world,"_exConfirmBtn")!);await Frames();
        DetailCall(world,"OnExchangeOtherAdd",stack,1,(short)0);
        Require(!finalLayer.Visible && !(bool)DetailField(world,"_exConfirmedByMe")!,"Partner offer changes invalidate pending approval");
        Click(finalAccept);Require(!(bool)DetailField(world,"_exConfirmedByMe")!,"Stale Yes cannot confirm a changed offer");
        Click((Button)DetailField(world,"_exConfirmBtn")!);await Frames();Click(finalAccept);
        Require((bool)DetailField(world,"_exConfirmedByMe")! && !finalLayer.Visible,"Explicit Yes locks own trade");await Capture("confirmed");

        DetailCall(world,"AbortExchange",true);Require(inventory[Inventory.GridStart].Count==100,"Cancel restores both stack submissions to their original slot");Require(net.Sheet.Gold==1_234_567,"Cancel restores offered coins");

        DetailCall(world,"OpenExchange");await Capture("cancel-restored");

        DetailCall(world,"OfferSlotAmount",Inventory.GridStart,10);DetailCall(world,"OnExchangeAddResult",false);Require(inventory[Inventory.GridStart].Count==100,"Rejected server add preserves inventory");await Capture("rejected-add");

        DetailCall(world,"OpenExchangeGold");await Frames();Require((bool)DetailField(world,"_exAmountShown")!,"Coins reuse original quantity frame");

        await KeyPress(Key.Escape);Require(net.Sheet.Gold==1_234_567,"Cancelled coin offer preserves wallet");

        DetailCall(world,"AbortExchange",true);await Frames();

        var requestLayer=(CanvasLayer)DetailField(world,"_exRequestLayer")!;

        var request=Descendants(requestLayer).OfType<ClassicExchangeNoticePanel>().Single();

        var accept=Descendants(request).OfType<Button>().Single(b=>b.Name=="exchange_accept");

        var decline=Descendants(request).OfType<Button>().Single(b=>b.Name=="exchange_decline");

        DetailCall(world,"OnExchangeRequest",123);await Capture("request");

        Require(requestLayer.Visible && !((ConfirmationDialog)DetailField(world,"_exAskDialog")!).Visible,"Incoming request uses original message art instead of native desktop dialog");

        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames();Require((bool)DetailField(world,"_exRequestPending")! && !(bool)DetailField(world,"_exShown")!,"Incoming request also requires a mouse decision");

        DetailCall(world,"OnExchangeRequest",124);Require((int)DetailField(world,"_exPartnerId")! ==123,"Second request cannot overwrite pending requester");

        Click(decline);await Frames();Require(!(bool)DetailField(world,"_exRequestPending")! && !requestLayer.Visible,"No declines and closes request");

        DetailCall(world,"OnExchangeRequest",123);await KeyPress(Key.Escape);await Frames();Require(!(bool)DetailField(world,"_exRequestPending")!,"Escape declines request");

        DetailCall(world,"OnExchangeRequest",123);DetailCall(world,"OnExchangeCancel");await Frames();Require(!requestLayer.Visible && !(bool)DetailField(world,"_exRequestPending")!,"Withdrawn request closes without accepting");

        Click(accept);Require(!(bool)DetailField(world,"_exShown")!,"Stale accept callback cannot open withdrawn trade");

        DetailCall(world,"OnExchangeRequest",123);Click(accept);await Frames();Require((bool)DetailField(world,"_exShown")! && !requestLayer.Visible,"Yes opens trade exactly once");await Capture("request-accepted");

        DetailCall(world,"AbortExchange",true);DetailCall(world,"ShowExchangeWait","Waiting for Knight Online to accept the trade…");await Capture("waiting");

        var waitLayer=(CanvasLayer)DetailField(world,"_exWaitLayer")!;

        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames();Require(waitLayer.Visible && (bool)DetailField(world,"_exWaiting")!,"Enter leaves outgoing request waiting");

        Click(Descendants(waitLayer).OfType<ClassicExchangeNoticePanel>().Single().Cancel);await Frames();Require(!(bool)DetailField(world,"_exWaiting")! && !waitLayer.Visible,"Waiting Cancel withdraws outgoing request");

        DetailCall(world,"ShowExchangeWait","Waiting for Knight Online to accept the trade…");await KeyPress(Key.Escape);await Frames();Require(!(bool)DetailField(world,"_exWaiting")!,"Escape withdraws outgoing request");

        DetailCall(world,"ShowExchangeWait","Waiting for Knight Online to accept the trade…");DetailCall(world,"OnExchangeAgree",false);Require(!waitLayer.Visible && !(bool)DetailField(world,"_exShown")!,"Partner rejection closes waiting window");

        DetailCall(world,"ShowExchangeWait","Waiting for Knight Online to accept the trade…");DetailCall(world,"OnExchangeAgree",true);Require(!waitLayer.Visible && (bool)DetailField(world,"_exShown")!,"Partner acceptance replaces waiting window with trade");

        DetailCall(world,"AbortExchange",true);

        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{nation,checks,screens},new JsonSerializerOptions {WriteIndented=true}));

        GD.Print("TRADE_AUDIT_OK "+checks.Count);world.Free();net.Free();foreach(var layer in auditLayers)layer.QueueFree();await Frames(3);

    }

}

