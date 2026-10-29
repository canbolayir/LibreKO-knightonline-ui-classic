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

        // The runner loads the audit client pack; only the source content (item icons) comes from the build.

        foreach(string pack in new[]{"build/client/source-content/knightonline.pck"})

            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false))throw new Exception("Missing pack "+pack);

        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(800,720);

        AddChild(new ColorRect {Color=new Color("252822"),Size=new Vector2(800,720),MouseFilter=MouseFilterEnum.Ignore});

        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);

        Game.Session.Self.Sheet.SeedWealth(1_234_567,50_000);Game.Session.Self.Sheet.SetMaxWeight(10_000);Game.Session.Self.Sheet.SeedProgress(70,0,100);

        Native.Set(Game.Session.Self, "LastEnter",new MyInfo {Nation=nation,Class=nation==1?105:205,Race=nation==1?1:11,Name="Classic Preview",Gear=new int[8]}); Game.Session.Self.Inventory.Reset(new NativeSlot[InventoryConstants.InventoryTotal]);

        ItemData.EnsureLoaded();

        var groups=(Dictionary<int,List<ItemData.SellEntry>>)typeof(ItemData).GetField("sell",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;

        var ids=groups.Values.SelectMany(g=>g).Select(e=>e.Id).Distinct().Where(id=>ItemData.Get(id)!=null && ItemData.IsSellable(id)).ToArray();

        int stack=ids.First(id=>ItemData.Get(id)!.Countable!=0);

        var single=ids.Where(id=>ItemData.Get(id)!.Countable==0).Take(13).ToArray();

        var world=PreviewFixtures.Prepared(new WorldRoot());var inventory=(Inventory)Game.Session.Self.Inventory;inventory.EnsureLength(InventoryConstants.InventoryTotal);

        void Seed() {inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=100,Durability=ItemData.MaxDurabilityOf(stack)};for(int i=0;i<single.Length;i++)inventory[Inventory.GridStart+i+1]=new NativeSlot {ItemId=single[i],Count=1,Durability=ItemData.MaxDurabilityOf(single[i])};}

        Seed();DetailCall(world,"InventoryWindow.BuildPanel");var orphan=(Control)DetailField(world,"InventoryWindow.InvContent")!;orphan.Visible=false;world.AddChild(orphan);DetailCall(world,"NpcDialogWindow.BuildNpcDialog");DetailCall(world,"Exchange.BuildPanel");

        var bridge=Native.Call(world, "CreatePluginAdapters")!;

        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;Native.Call(bridge, "AttachTo", game);

        // A trade request closes the vendor and the warehouse first; their quantity prompts exist in a running world.
        foreach(string closer in new[]{"Vendor.TradePrompt","Warehouse.AmountPrompt"}){var quantity=new QuantityPrompt(76);world.AddChild(quantity);Native.Set(world, closer, quantity);}
        // The client subscribes its partner-offer handler at world start, before any plugin preparer.
        Game.Session.Exchange.ExchangeOtherAddEvent+=(item,count,dura,unique)=>DetailCall(world,"Exchange.OnOtherAdd",item,count,dura,unique);
        NativeTrade.Prepare((HudWindow)DetailField(world,"Exchange.Panel")!,world);

        CanvasLayer TradeLayer(string name)=>name switch{"_exRequestLayer"=>NativeTrade.RequestLayer(world)!,"_exFinalLayer"=>NativeTrade.FinalLayer(world)!,_=>(CanvasLayer)DetailField(world,name)!};

        var auditLayers=new[]{"Exchange.layer","Exchange.amountLayer","Exchange.waitLayer","_exRequestLayer","_exFinalLayer","ItemTooltip.tipLayer"}.Select(TradeLayer).ToArray();

        foreach(var layer in auditLayers)layer.Reparent(this);

        var window=(HudWindow)DetailField(world,"Exchange.Panel")!;var shell=ClassicExchangeSkin.Apply(window.Body)!;

        Native.Set(world, "Exchange.PartnerName", "Knight Online");DetailCall(world,"Exchange.Open");

        var screens=new List<object>();var checks=new List<string>();

        void Require(bool value,string message) {if(!value)throw new Exception("TRADE_AUDIT: "+message);checks.Add(message);}

        async Task Frames(int count=6) {for(int i=0;i<count;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        // The box fits its message: the shared message-box rhythm, no empty band between text and buttons.
        void NoticeRhythm(ClassicExchangeNoticePanel panel,Label text,Button[] buttons,string what)
        {
            var box=KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.Button;
            Require(text.GetLineCount()*text.GetLineHeight()+(text.GetLineCount()-1)*text.GetThemeConstant("line_spacing")<=text.Size.Y+.5f && text.Size.Y<=text.GetLineCount()*(text.GetLineHeight()+text.GetThemeConstant("line_spacing")),what+": message rectangle is its wrapped lines "+text.GetRect());
            foreach(var button in buttons)
            {
                Require(button.Size==box && button.Position.Y==text.GetRect().End.Y+KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.ActionGap,what+": "+button.Name+" sits the common gap under the message "+button.GetRect());
                Require(panel.Size.Y-button.GetRect().End.Y==KnightOnlineUiClassic.Layout.ClassicOverlayLayout.Message.ButtonBottom,what+": "+button.Name+" keeps the bottom margin");
            }
        }

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

            var modal=Descendants((CanvasLayer)DetailField(world,"Exchange.amountLayer")!).OfType<ClassicExchangeAmountPanel>().Single();

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
        {var pairing=DetailField(world,"BagCompanions.Pairing")!;Require(!(bool)pairing.GetType().GetField("_openedByCompanion",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(pairing)!,"The Classic trade carries its own bag grid; the inventory the client docked is closed again");}
        Require(shell.GetNode<ClassicExchangeCell>("exchange_bag_0").Item?.ItemId==stack && shell.GetNode<ClassicExchangeCell>("exchange_bag_1").Item?.ItemId==single[0],"Carried grid shows the bag slots from the client inventory");

        foreach(var key in new[]{Key.Enter,Key.KpEnter}) {await KeyPress(key);await Frames(2);Require(!(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Enter never confirms empty trade: "+key);}

        Require(!((Button)DetailField(world,"Exchange.confirmBtn")!).HasFocus(),"Final decision has no keyboard focus");

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

        Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames();Require((bool)DetailField(world,"Exchange.amountShown")!,"Drag opens quantity prompt");

        var modalPanel=Descendants((CanvasLayer)DetailField(world,"Exchange.amountLayer")!).OfType<ClassicExchangeAmountPanel>().Single();

        await KeyPress(Key.Escape);Require(!(bool)DetailField(world,"Exchange.amountShown")!,"Escape cancels quantity without changing inventory");Require(inventory[Inventory.GridStart].Count==100,"Cancelled quantity keeps full stack");await Frames();

        

        shell.GetNode<ClassicExchangeCell>("exchange_bag_0")._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});await Frames();Require((bool)DetailField(world,"Exchange.amountShown")!,"Stack opens original quantity modal");

        var spin=(SpinBox)DetailField(world,"Exchange.amountSpin")!;Require(spin.GetLineEdit().Text=="100","Offer quantity starts at entire available stack");await Capture("quantity");

        foreach(string invalid in new[]{"","abc","0","-1","101","2147483648"}) {

            spin.GetLineEdit().Text=invalid;await KeyPress(Key.Enter);await Frames(2);

            Require((bool)DetailField(world,"Exchange.amountShown")! && !(bool)DetailField(world,"Exchange.addInFlight")!,"Invalid quantity remains open without sending: "+invalid);

        }

        await Capture("invalid-quantity");

        spin.GetLineEdit().Text="10";await KeyPress(Key.Enter);Require((bool)DetailField(world,"Exchange.addInFlight")!,"Quantity sends add request");

        Require(inventory[Inventory.GridStart].Count==100,"Inventory unchanged before server acknowledgment");DetailCall(world,"Exchange.OnConfirm");Require(!(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Cannot confirm pending add");

        DetailCall(world,"Exchange.OnAddResult",true);await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames(2);Require(!(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Enter after quantity acknowledgment never confirms final trade");Require(inventory[Inventory.GridStart].Count==90,"Successful add reduces only acknowledged quantity");

        DetailCall(world,"OfferSlotAmount",Inventory.GridStart,10);DetailCall(world,"Exchange.OnAddResult",true);Require((int)DetailCall(world,"ExchangeOfferCount")! ==1,"Repeated stack offers share one visible capacity slot");

        DetailCall(world,"OpenExchangeGold");await Frames();await Capture("coins");spin.GetLineEdit().Text="12345";await KeyPress(Key.KpEnter);DetailCall(world,"Exchange.OnAddResult",true);Require(Game.Session.Self.Sheet.Gold==1_222_222,"Coins deducted only after acknowledgment");

        for(int i=0;i<11;i++){DetailCall(world,"OfferSlotAmount",Inventory.GridStart+i+1,1);DetailCall(world,"Exchange.OnAddResult",true);}

        for(int i=0;i<12;i++)DetailCall(world,"Exchange.OnOtherAdd",single[i],1,ItemData.MaxDurabilityOf(single[i]),0);

        DetailCall(world,"Exchange.OnOtherAdd",LibreKO.Network.Requests.ExchangeRequests.ExchangeGoldItem,54321,(short)0,0);

        await Capture("twelve-slots");

        DetailCall(world,"Exchange.OfferSlot",Inventory.GridStart+12);Require(!(bool)DetailField(world,"Exchange.addInFlight")!,"Thirteenth distinct item is rejected");

        bag=shell.GetNode<ClassicExchangeCell>("exchange_bag_0");

        Require(!shell.GetNode<Control>("trade_other_offer").GetChildren().OfType<ClassicExchangeCell>().First()._CanDropData(Vector2.Zero,payload),"Partner grid remains read-only");

        Require((int)DetailCall(world,"ExchangeOfferCount")! ==12,"Capacity covers twelve offers");

        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Capture("final-approval-first");
        Require(NativeTrade.FinalPending(world) && !(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"First participant also needs explicit approval");
        var firstFinal=TradeLayer("_exFinalLayer");
        Click(Descendants(firstFinal).OfType<Button>().Single(b=>b.Name=="exchange_final_decline"));await Frames();
        Require(!firstFinal.Visible && !shell.OfferBlocked,"No returns to editable offers before partner locks");
        bag._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});await Frames();
        Require((bool)DetailField(world,"Exchange.amountShown")!,"Right click can edit the offer after declining approval");
        spin.GetLineEdit().Text="1";await KeyPress(Key.Enter);DetailCall(world,"Exchange.OnAddResult",true);
        Require(inventory[Inventory.GridStart].Count==79,"Declined approval allows adding another item quantity");
        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Frames();Require(firstFinal.Visible,"Trade can be pressed again after editing");
        DetailCall(world,"CloseExchangeFinal");
        DetailCall(world,"Exchange.OnOtherDecide");await Capture("partner-confirmed");

        foreach(var key in new[]{Key.Enter,Key.KpEnter}) {await KeyPress(key);await KeyPress(key,true);await Frames(2);Require(!(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Partner confirmation cannot turn Enter into a final decision: "+key);}

        Require(shell.OfferBlocked && !shell.Blocked,"Partner lock disables editing but allows own Trade decision");
        bag._GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true});DetailCall(world,"OfferSlotAmount",Inventory.GridStart,1);DetailCall(world,"OpenExchangeGold");
        Require(!(bool)DetailField(world,"Exchange.amountShown")! && !(bool)DetailField(world,"Exchange.addInFlight")!,"Partner lock prevents right click, item additions and coin edits");
        var finalLayer=TradeLayer("_exFinalLayer");
        var finalAccept=Descendants(finalLayer).OfType<Button>().Single(b=>b.Name=="exchange_final_accept");
        var finalDecline=Descendants(finalLayer).OfType<Button>().Single(b=>b.Name=="exchange_final_decline");
        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Capture("final-approval");
        var finalPanel=Descendants(finalLayer).OfType<ClassicExchangeNoticePanel>().Single();
        var prompt=finalPanel.GetNode<Label>("final_prompt");
        Require(prompt.GetRect()==KnightOnlineUiClassic.Layout.ClassicExchangeNoticeLayout.Message(true,prompt.Size.Y) && prompt.GetLineCount()*prompt.GetLineHeight()<=prompt.Size.Y+.5f,"Final approval prompt matches actual pixel bounds: "+prompt.GetRect());
        NoticeRhythm(finalPanel,prompt,new[]{finalAccept,finalDecline},"final approval");
        Require(prompt.Text=="Are you sure you want to trade?","Approval contains only a question without coin details");
        foreach(var control in new Control[]{prompt,finalAccept,finalDecline}) {
            Require(control.GetThemeFont("font")==Plugin.Kit.Bold && control.GetThemeFontSize("font_size")==12,"Approval uses the same Arial Bold 12px skill font: "+control.Name);
        }
        Require(finalAccept.Position.Y==finalDecline.Position.Y && finalAccept.Position.X==finalPanel.Size.X-finalDecline.GetRect().End.X,"Final approval buttons have equal baseline and side margins");
        Require(finalLayer.Visible && !(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Trade button opens approval without sending a decision");
        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);
        Require(finalLayer.Visible && !(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Enter and numpad Enter never accept final approval");
        Click(finalDecline);Require(!finalLayer.Visible && (bool)DetailField(world,"Exchange.shown")! && !(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"No returns to the unchanged trade");
        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Frames();await KeyPress(Key.Escape);
        Require(!finalLayer.Visible && (bool)DetailField(world,"Exchange.shown")!,"Escape cancels approval without cancelling trade");
        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Frames();
        // Delivered as the network does, through the client's reply event.
        Native.Get<Action<int,int,short,int>>(Game.Session.Exchange, "ExchangeOtherAddEvent")!.Invoke(stack,1,(short)0,0);
        Require(!finalLayer.Visible && !(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Partner offer changes invalidate pending approval");
        Click(finalAccept);Require(!(bool)DetailField(world,"Exchange.ConfirmedByMe")!,"Stale Yes cannot confirm a changed offer");
        Click((Button)DetailField(world,"Exchange.confirmBtn")!);await Frames();Click(finalAccept);
        Require((bool)DetailField(world,"Exchange.ConfirmedByMe")! && !finalLayer.Visible,"Explicit Yes locks own trade");await Capture("confirmed");

        DetailCall(world,"Exchange.Abort",true);Require(inventory[Inventory.GridStart].Count==100,"Cancel restores both stack submissions to their original slot");Require(Game.Session.Self.Sheet.Gold==1_234_567,"Cancel restores offered coins");

        DetailCall(world,"Exchange.Open");await Capture("cancel-restored");
        // Mouse-wheel amount (ff78eef): a part picked while dragging a stack is offered at once; the whole stack asks the count.
        {
            var source=shell.GetNode<ClassicExchangeCell>("exchange_bag_0");var target=shell.GetNode<Control>("trade_my_offer").GetChildren().OfType<ClassicExchangeCell>().First();
            Vector2 start=source.GetGlobalRect().GetCenter(),end=target.GetGlobalRect().GetCenter();
            void Wheel(MouseButton button) {
                Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=button,Pressed=true,Position=end,GlobalPosition=end,ButtonMask=MouseButtonMask.Left});
                Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=button,Pressed=false,Position=end,GlobalPosition=end,ButtonMask=MouseButtonMask.Left});
            }
            Input.ParseInputEvent(new InputEventMouseMotion {Position=start,GlobalPosition=start});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=start,GlobalPosition=start});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=start+new Vector2(18,0),GlobalPosition=start+new Vector2(18,0),Relative=new Vector2(18,0),ButtonMask=MouseButtonMask.Left});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=end,GlobalPosition=end,Relative=end-start,ButtonMask=MouseButtonMask.Left});await Frames(2);
            var dial=Descendants(GetTree().Root.GetNode("DragLayer")).OfType<KnightOnlineUiClassic.Layout.ClassicStackDial>().Single();
            var icon=(Control)dial.GetParent();
            Require(GetViewport().GuiIsDragging() && dial.Text=="100" && dial.GetThemeColor("font_color")==Colors.White,"A dragged stack shows its whole count in white on the drag icon");
            Require(dial.GetThemeFontSize("font_size")==KnightOnlineUiClassic.Layout.ItemCountStyle.FontSize && dial.GetThemeConstant("outline_size")==3
                && dial.GetGlobalRect().End==icon.GetGlobalRect().End && icon.GetGlobalRect().Size==source.GetGlobalRect().Grow(-2).Size,
                "The drag count uses the cell's count type on the icon's bottom-right corner "+dial.GetGlobalRect()+" "+icon.GetGlobalRect());
            for(int i=0;i<3;i++) Wheel(MouseButton.WheelDown);
            await Frames(2);
            Require(dial.Text=="97" && dial.Amount==97 && dial.GetThemeColor("font_color")==KnightOnlineUiClassic.Layout.ClassicStackDial.PartColour,"The wheel picks a part of the stack, shown in gold");
            await Capture("drag-amount");
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=end,GlobalPosition=end});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames();
            var pending=DetailField(world,"Exchange.pending")!;
            Require(!(bool)DetailField(world,"Exchange.amountShown")! && (bool)DetailField(world,"Exchange.addInFlight")! && (int)pending.GetType().GetField("Count")!.GetValue(pending)! ==97,
                "A part picked with the wheel is offered at once, without the quantity prompt");
            DetailCall(world,"Exchange.OnAddResult",true);await Frames();
            Require(inventory[Inventory.GridStart].Count==3 && shell.GetNode<ClassicExchangeCell>("exchange_bag_0").Item?.Count==3,"The acknowledged part leaves the rest of the stack in the bag");
            await Capture("drag-amount-offered");
            DetailCall(world,"Exchange.Abort",true);Require(inventory[Inventory.GridStart].Count==100,"Cancel restores the offered part");
            DetailCall(world,"Exchange.Open");await Frames();
        }

        DetailCall(world,"OfferSlotAmount",Inventory.GridStart,10);DetailCall(world,"Exchange.OnAddResult",false);Require(inventory[Inventory.GridStart].Count==100,"Rejected server add preserves inventory");await Capture("rejected-add");

        DetailCall(world,"OpenExchangeGold");await Frames();Require((bool)DetailField(world,"Exchange.amountShown")!,"Coins reuse original quantity frame");

        await KeyPress(Key.Escape);Require(Game.Session.Self.Sheet.Gold==1_234_567,"Cancelled coin offer preserves wallet");

        DetailCall(world,"Exchange.Abort",true);await Frames();

        var requestLayer=TradeLayer("_exRequestLayer");

        var request=Descendants(requestLayer).OfType<ClassicExchangeNoticePanel>().Single();

        var accept=Descendants(request).OfType<Button>().Single(b=>b.Name=="exchange_accept");

        var decline=Descendants(request).OfType<Button>().Single(b=>b.Name=="exchange_decline");

        DetailCall(world,"Exchange.OnRequest",123);await Capture("request");
        NoticeRhythm(request,request.Message,new[]{accept,decline},"trade request");

        Require(requestLayer.Visible && !((ConfirmationDialog)DetailField(world,"Exchange.askDialog")!).Visible,"Incoming request uses original message art instead of native desktop dialog");

        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames();Require((bool)DetailField(world,"Exchange.requestPending")! && !(bool)DetailField(world,"Exchange.shown")!,"Incoming request also requires a mouse decision");

        DetailCall(world,"Exchange.OnRequest",124);Require((int)DetailField(world,"Exchange.partnerId")! ==123,"Second request cannot overwrite pending requester");

        Click(decline);await Frames();Require(!(bool)DetailField(world,"Exchange.requestPending")! && !requestLayer.Visible,"No declines and closes request");

        DetailCall(world,"Exchange.OnRequest",123);await KeyPress(Key.Escape);await Frames();Require(!(bool)DetailField(world,"Exchange.requestPending")!,"Escape declines request");

        DetailCall(world,"Exchange.OnRequest",123);DetailCall(world,"Exchange.OnCancel");await Frames();Require(!requestLayer.Visible && !(bool)DetailField(world,"Exchange.requestPending")!,"Withdrawn request closes without accepting");

        Click(accept);Require(!(bool)DetailField(world,"Exchange.shown")!,"Stale accept callback cannot open withdrawn trade");

        DetailCall(world,"Exchange.OnRequest",123);Click(accept);await Frames();Require((bool)DetailField(world,"Exchange.shown")! && !requestLayer.Visible,"Yes opens trade exactly once");await Capture("request-accepted");

        DetailCall(world,"Exchange.Abort",true);DetailCall(world,"Exchange.ShowWait","Waiting for Knight Online to accept the trade…");await Capture("waiting");
        {var wait=Descendants((CanvasLayer)DetailField(world,"Exchange.waitLayer")!).OfType<ClassicExchangeNoticePanel>().Single();NoticeRhythm(wait,wait.Message,new[]{wait.Cancel},"trade waiting");}

        var waitLayer=(CanvasLayer)DetailField(world,"Exchange.waitLayer")!;

        await KeyPress(Key.Enter);await KeyPress(Key.KpEnter);await Frames();Require(waitLayer.Visible && (bool)DetailField(world,"Exchange.Waiting")!,"Enter leaves outgoing request waiting");

        Click(Descendants(waitLayer).OfType<ClassicExchangeNoticePanel>().Single().Cancel);await Frames();Require(!(bool)DetailField(world,"Exchange.Waiting")! && !waitLayer.Visible,"Waiting Cancel withdraws outgoing request");

        DetailCall(world,"Exchange.ShowWait","Waiting for Knight Online to accept the trade…");await KeyPress(Key.Escape);await Frames();Require(!(bool)DetailField(world,"Exchange.Waiting")!,"Escape withdraws outgoing request");

        DetailCall(world,"Exchange.ShowWait","Waiting for Knight Online to accept the trade…");DetailCall(world,"Exchange.OnAgree",false);Require(!waitLayer.Visible && !(bool)DetailField(world,"Exchange.shown")!,"Partner rejection closes waiting window");

        DetailCall(world,"Exchange.ShowWait","Waiting for Knight Online to accept the trade…");DetailCall(world,"Exchange.OnAgree",true);Require(!waitLayer.Visible && (bool)DetailField(world,"Exchange.shown")!,"Partner acceptance replaces waiting window with trade");

        DetailCall(world,"Exchange.Abort",true);

        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{nation,checks,screens},new JsonSerializerOptions {WriteIndented=true}));

        GD.Print("TRADE_AUDIT_OK "+checks.Count);world.Free();net.Free();foreach(var layer in auditLayers)layer.QueueFree();await Frames(3);

    }

}

