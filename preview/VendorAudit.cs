using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Reflection;
using System.Text.Json;
using ClassicSlot = KnightOnlineUiClassic.Layout.ItemSlot;
using NativeSlot = LibreKO.Domain.ItemSlot;

public partial class Preview
{
    private async Task CaptureVendorAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/npc-vendor-audit");System.IO.Directory.CreateDirectory(output);
        foreach(string pack in new[]{"build/client/LibreKO.pck","build/client/source-content/knightonline.pck"})
            if(!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../"+pack),false)) throw new Exception("Missing audit pack: "+pack);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.Disabled;GetWindow().Size=new Vector2I(1000,700);
        AddChild(new ColorRect { Color=new Color("252822"),Size=new Vector2(1000,700),MouseFilter=MouseFilterEnum.Ignore });
        var net=new Net();typeof(Net).GetProperty("I")!.SetValue(null,net);
        net.Sheet.SeedWealth(1_234_567,50_000);net.Sheet.SetMaxWeight(10_000);net.Sheet.SeedProgress(70,0,100);
        typeof(Net).GetProperty("LastEnter")!.SetValue(net,new MyInfo { Nation=nation,Class=nation==1?105:205,Race=nation==1?1:11,
            Name="Classic Preview",Gear=new int[8],Inventory=new NativeSlot[InventoryConstants.InventoryTotal] });
        ItemData.EnsureLoaded();
        var groups=(Dictionary<int,List<ItemData.SellEntry>>)typeof(ItemData).GetField("_sell",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var group=groups.Where(g=>g.Key!=249000 && g.Value.Count(e=>ItemData.Get(e.Id)!=null)>24 && g.Value.Any(e=>ItemData.Get(e.Id)?.Countable>0))
            .OrderBy(g=>g.Key).First();
        var entry=group.Value.Where(e=>ItemData.Get(e.Id) is { Countable: >0 } && ItemData.BuyPrice(e.Id)>0 && ItemData.IsSellable(e.Id)).OrderBy(e=>ItemData.BuyPrice(e.Id)).First();
        var world=new World();
        var inventory=(Inventory)typeof(World).GetProperty("Inv",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(world)!;
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        inventory[Inventory.GridStart]=new NativeSlot { ItemId=entry.Id,Count=63,Durability=ItemData.MaxDurabilityOf(entry.Id) };
        DetailCall(world,"BuildInventoryPanel");DetailCall(world,"BuildNpcDialog");DetailCall(world,"BuildVendorPanel");
        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;
        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());
        var invLayer=(CanvasLayer)DetailField(world,"_invLayer")!;invLayer.Reparent(this);
        var invWindow=new HudWindow("inventory","Inventory",new Vector2(520,48)) { Visible=false };
        invLayer.AddChild(invWindow);invWindow.Body.AddChild((Control)DetailField(world,"_invContent")!);
        ((Dictionary<string,HudWindow>)DetailField(world,"_mainWindows")!).Add("Inventory",invWindow);
        foreach(var c in invWindow.GetChildren().OfType<Control>()) c.Visible=false;
        invWindow.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());
        var host=(WindowHost)Create(typeof(WindowHost),"inventory","Inventory",invWindow,(Action)(()=>invWindow.Visible=false));
        var classicInventory=new InventoryWindow(host);invWindow.AddChild(classicInventory);invWindow.ResetSize();
        void RefreshBag()
        {
            DetailCall(world,"RefreshInventoryUI");
            typeof(PluginGame).GetMethod("RaiseInventory",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null);
        }
        var vendor=(HudWindow)DetailField(world,"_vendorPanel")!;
        var sourceActions=Descendants(vendor.Body).OfType<BaseButton>().Select(b=>b.GetInstanceId()).ToArray();
        bool nativeReference=OS.GetCmdlineUserArgs().Contains("vendor-native-reference");
        var shell=nativeReference?null:ClassicVendorSkin.Apply(vendor.Body)!;
        PluginHost.Ui.ReplaceDialogs(request=>new KnightOnlineUiClassic.Windows.MessageBox(request));
        ((CanvasLayer)DetailField(world,"_vendorLayer")!).Reparent(this);
        ((CanvasLayer)DetailField(world,"_itemTipLayer")!).Reparent(this);
        var quantity=(QuantityPrompt)DetailField(world,"_tradePrompt")!;quantity.Reparent(this);
        using var npcSeed=JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://../../LibreKO/Server/LibreKO.Game/Seed/Data/Npcs.json")));
        string shopName=npcSeed.RootElement.EnumerateArray().First(n=>n.GetProperty("SellingGroup").GetInt32()==group.Key).GetProperty("Name").GetString()!;
        void OpenShop(int sellingGroup)
        {
            var npc=npcSeed.RootElement.EnumerateArray().FirstOrDefault(n=>n.GetProperty("SellingGroup").GetInt32()==sellingGroup);
            string name=npc.ValueKind==JsonValueKind.Undefined?"Merchant":npc.GetProperty("Name").GetString()!;
            typeof(World).GetField("_vendorNpcName",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,name);
            DetailCall(world,"OpenVendor",sellingGroup);
        }
        typeof(World).GetField("_vendorNpcName",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,shopName);
        typeof(World).GetField("_vendorNpcId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,701);
        DetailCall(world,"OpenVendor",group.Key);
        async Task Frames(int count=6) { for(int i=0;i<count;i++) await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); }
        if(nativeReference)
        {
            await Frames(30); vendor.Position=new Vector2(96,48);invWindow.Position=new Vector2(520,48);await Frames(3);
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+(nation==1?"karus":"human")+"-native-catalogue.png");
            DetailCall(world,"AskBuy",entry.Id,-1);await Frames(5);
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+(nation==1?"karus":"human")+"-native-quantity.png");
            quantity.Close();vendor.Visible=false;invWindow.Visible=false;
            // The imported composition is a research reference, never a replacement for the live dialog.
            var referenceArt=Plugin.Kit.Layout("{nation}_personaltradeedit_us");
            var referenceLayer=new CanvasLayer {Layer=77};AddChild(referenceLayer);
            var reference=new KnightOnlineUiClassic.Layout.LayoutView(Plugin.Kit,referenceArt);referenceLayer.AddChild(reference);
            reference.Position=((new Vector2(1000,700)-reference.Size)/2).Round();
            reference.SetText("String_PersonTradeEdit_Msg","Please enter the quantity of the item.");
            if(reference.Get("edit_trade") is LineEdit referenceField) referenceField.Text="";
            var referenceIconArea=referenceArt.Find("area_trade_icon")!;
            reference.AddChild(new TextureRect {Texture=ItemData.Icon(entry.Id),Position=referenceIconArea.Position-new Vector2(referenceArt.X,referenceArt.Y),
                Size=referenceIconArea.SizeVec,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
            await Frames(5);GetViewport().GetTexture().GetImage().SavePng(output+"/"+(nation==1?"karus":"human")+"-original-quantity.png");
            System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-original-quantity.json",JsonSerializer.Serialize(new{x=reference.Position.X,y=reference.Position.Y,width=reference.Size.X,height=reference.Size.Y}));
            reference.Visible=false;
            var approvalReference=new KnightOnlineUiClassic.Layout.LayoutView(Plugin.Kit,Plugin.Kit.Layout("co_msgboxokcancel_us"));referenceLayer.AddChild(approvalReference);
            approvalReference.Position=((new Vector2(1000,700)-approvalReference.Size)/2).Round();
            approvalReference.SetText("text_msg",$"Buy 10 × {ItemData.DisplayName(entry.Id)}?\nTotal: {ItemData.BuyPrice(entry.Id)*10:n0} gold");
            await Frames(5);GetViewport().GetTexture().GetImage().SavePng(output+"/"+(nation==1?"karus":"human")+"-original-approval.png");
            System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-original-approval.json",JsonSerializer.Serialize(new{x=approvalReference.Position.X,y=approvalReference.Position.Y,width=approvalReference.Size.X,height=approvalReference.Size.Y}));
            GD.Print("VENDOR_NATIVE_REFERENCE_OK");world.Free();net.Free();return;
        }
        var screens=new List<object>();var checks=new List<string>();var countBadges=new List<object>();
        void Require(bool valid,string message) { if(!valid) throw new Exception("VENDOR_AUDIT: "+message);checks.Add(message); }
        var infoWindow=new LogWindow { Visible=false };AddChild(infoWindow);infoWindow.Visible=false;
        void ApplyGold(int total)
        {
            int before=game.Log.History.Count;
            DetailCall(world,"OnGoldChange",total);
            foreach(var line in game.Log.History.Skip(before))
                typeof(PluginGame).GetMethod("RaiseLogLine",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,new object[]{line});
            DetailCall(world,"OnVendorGold",total);
        }
        Label CountLabel(object cell)
        {
            for(var type=cell.GetType();type!=null;type=type.BaseType)
                if(type.GetField("_count",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly) is {} field)
                    return (Label)field.GetValue(cell)!;
            throw new Exception("Missing item quantity label: "+cell.GetType().Name);
        }
        string CountBadge(object cell) => CountLabel(cell).Text;
        async Task Capture(string name, bool normalise=true)
        {
            await Frames();if(normalise) vendor.Position=new Vector2(318,48);await Frames(2);
            var controls=Descendants(shell!).Concat(Descendants(quantity)).Concat(Descendants(infoWindow)).Concat(Descendants(invWindow)).Concat(Descendants(classicInventory)).Distinct()
                .OfType<Control>().Where(c=>c.IsVisibleInTree() && c is not Container).ToArray();
            foreach(var control in Descendants(shell!).OfType<Control>().Where(c=>c.IsVisibleInTree() && c is Button or LineEdit or ItemSlotView or Label)
                .Where(c=>!HasNoticeAncestor(c)))
                if(!vendor.GetGlobalRect().Grow(.1f).Encloses(control.GetGlobalRect())) { GetViewport().GetTexture().GetImage().SavePng(output+"/debug-overflow-"+nation+".png");throw new Exception("Shop control overflow: "+control.Name+" "+control.GetGlobalRect()+" min="+control.GetCombinedMinimumSize()+" custom="+control.CustomMinimumSize+" text="+(control is Label l?l.Text:"")); }
            var quantityPanel=Descendants(quantity).OfType<ClassicQuantityPanel>().Single();
            foreach(var control in Descendants(quantityPanel).OfType<Control>().Where(c=>c.IsVisibleInTree() && c is Button or LineEdit or Label or TextureRect))
                if(!quantityPanel.GetGlobalRect().Grow(.1f).Encloses(control.GetGlobalRect())) throw new Exception("Quantity control overflow: "+control.Name);
            foreach(var approval in Descendants(shell!).OfType<ClassicTradeApproval>().Where(a=>!a.IsQueuedForDeletion()))
                foreach(var control in Descendants(approval).OfType<Control>().Where(c=>c.IsVisibleInTree() && c is Button or Label or TextureRect))
                    if(!approval.PanelBounds.Grow(.1f).Encloses(control.GetGlobalRect())) throw new Exception("Approval control overflow: "+control.Name+" "+control.GetGlobalRect()+" panel="+approval.PanelBounds+" min="+control.GetCombinedMinimumSize());
            string file=(nation==1?"karus":"human")+"-"+name+".png";
            GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);
            screens.Add(new { name,file,bounds=new[]{vendor.Position.X,vendor.Position.Y,vendor.Size.X,vendor.Size.Y},
                controls=controls.Select(c=>new {name=c.Name.ToString(),type=c.GetType().Name,text=c is Label l?l.Text:c is Button b?b.Text:c is LineEdit e?e.Text:"",x=c.GetGlobalRect().Position.X,y=c.GetGlobalRect().Position.Y,width=c.Size.X,height=c.Size.Y}).ToArray() });
        }
        bool HasNoticeAncestor(Node node) { for(var p=node.GetParent();p!=null;p=p.GetParent()) if(p is Notice) return true;return false; }
        async Task PointerDrag(Control source,Control destination)
        {
            var from=source.GetGlobalRect().GetCenter();var to=destination.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion {Position=from,GlobalPosition=from});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=from,GlobalPosition=from});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=from+new Vector2(18,0),GlobalPosition=from+new Vector2(18,0),Relative=new Vector2(18,0),ButtonMask=MouseButtonMask.Left});await Frames(2);
            Require(GetViewport().GuiIsDragging(),"Pointer holds the actual item during drag");
            Input.ParseInputEvent(new InputEventMouseMotion {Position=to,GlobalPosition=to,Relative=to-from,ButtonMask=MouseButtonMask.Left});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=to,GlobalPosition=to});await Frames(2);
        }
        async Task RightClick(Control control)
        {
            var point=control.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion {Position=point,GlobalPosition=point});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true,Position=point,GlobalPosition=point});
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=false,Position=point,GlobalPosition=point});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames(2);
        }
        void Escape()
        {
            Input.ParseInputEvent(new InputEventKey {Pressed=true,Keycode=Key.Escape,PhysicalKeycode=Key.Escape});
            Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=Key.Escape,PhysicalKeycode=Key.Escape});
        }
        bool InFlight() => (bool)DetailField(world,"_tradeInFlight")!;
        ClassicTradeApproval Approval() => Descendants(shell!).OfType<ClassicTradeApproval>().Single(a=>!a.IsQueuedForDeletion());
        void VerifyApproval(bool buy, int itemId, int count, long total)
        {
            var labels=Descendants(Approval()).OfType<Label>().ToDictionary(l=>l.Name.ToString());
            Require(labels["trade_item_name"].Text==ItemData.DisplayName(itemId) && labels["trade_quantity"].Text==count.ToString("n0")
                && labels["trade_total"].Text==$"{(buy?"−":"+")}{total:n0} gold" && labels["trade_total_caption"].Text==(buy?"You pay":"You receive"),
                $"{(buy?"Buy":"Sale")} summary separates the real item, selected quantity and signed total in the correct currency");
            Require(labels["trade_item_name"].GetGlobalRect().End.Y < labels["trade_quantity"].GlobalPosition.Y
                && labels["trade_quantity"].GetGlobalRect().End.Y < labels["trade_total_caption"].GlobalPosition.Y
                && labels["trade_total_caption"].GetGlobalRect().End.Y <= labels["trade_total"].GlobalPosition.Y,
                "Item, quantity and transfer total occupy distinct non-overlapping bands");
        }
        async Task Approve()
        {
            var approval=Approval();
            Descendants(approval).OfType<Button>().Single(b=>b.Name=="btn_ok").EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
        }
        string Status() => Descendants(shell!).OfType<StatusLabel>().Single().Text;
        async Task Hold(Control source)
        {
            var point=source.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion {Position=point,GlobalPosition=point});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point});await Frames(2);
            Require(GetViewport().GuiIsDragging(),"Pressing a catalogue item holds its actual identity");
        }
        async Task ReleasePointer()
        {
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames(2);
        }
        await Capture("catalogue");
        Require(sourceActions.All(id=>Descendants(vendor).OfType<BaseButton>().Any(b=>b.GetInstanceId()==id)),"All original vendor action instances retained");
        var cells=(ItemSlotView[])DetailField(world,"_vendorCells")!;
        Require(cells.Select(c=>c.Position).Distinct().Count()==24 && cells.All(c=>c.Size==new Vector2(45,45)),"24 actual catalogue cells occupy distinct 45px bounds");
        Require(cells.All(cell=>CountLabel(cell).VerticalAlignment==VerticalAlignment.Bottom
            && CountLabel(cell).HorizontalAlignment==HorizontalAlignment.Right
            && CountLabel(cell).GetGlobalRect()==new Rect2(cell.GlobalPosition,cell.Size-new Vector2(2,2))),
            "All 24 store quantity labels use the same two-pixel right/bottom inset as the Inventory overlay, independent of socket padding");
        Require(shell!.BagCells.Count==28 && shell.BagCells.All(c=>c.Size==new Vector2(44,44)),"Original 28-slot trading inventory is embedded beneath the catalogue");
        Require(!invWindow.Visible,"Separate inventory stays hidden during the transaction");
        Require(!((Button)DetailField(world,"_vendorBuy")!).IsVisibleInTree(),"Trade is driven by item drag instead of a separate Buy action");
        var pager=(ServicePager)DetailField(world,"_vendorPager")!;pager.Step(1);await Capture("page-2");
        Require(pager.Page==1,"Native catalogue paging keeps original packet positions");
        var search=(LineEdit)DetailField(world,"_vendorSearch")!;
        Require(!search.IsVisibleInTree() && !Descendants(shell).OfType<LineEdit>().Any(e=>e.IsVisibleInTree()),"Original shop composition has no visible search field");
        Require(Descendants(shell).OfType<TextureRect>().Single(t=>t.Name=="trade_coin").IsVisibleInTree(),"Gold wallet uses the original nation-specific coin artwork");
        async Task<ClassicTradeCatalogueSlot> ShowItem(int id)
        {
            int currentGroup=(int)DetailField(world,"_vendorGroup")!;
            var entries=groups[currentGroup].Where(e=>ItemData.Get(e.Id)!=null && e.List>=0 && e.List<24).OrderBy(e=>e.Line).ThenBy(e=>e.List).ToArray();
            int index=Array.FindIndex(entries,e=>e.Id==id);if(index<0) throw new Exception("Missing real catalogue fixture");
            DetailCall(world,"ShowVendorPage",index/24);await Frames();
            return Descendants(cells[index%24]).OfType<ClassicTradeCatalogueSlot>().Single();
        }
        var dragHandle=Descendants(shell).OfType<HBoxContainer>().Single(c=>c.IsVisibleInTree());
        var dragStart=dragHandle.GetGlobalRect().GetCenter();var oldPosition=vendor.Position;var dragDelta=new Vector2(80,20);
        Input.ParseInputEvent(new InputEventMouseMotion {Position=dragStart,GlobalPosition=dragStart});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=dragStart,GlobalPosition=dragStart});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseMotion {Position=dragStart+dragDelta,GlobalPosition=dragStart+dragDelta,Relative=dragDelta,ButtonMask=MouseButtonMask.Left});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=dragStart+dragDelta,GlobalPosition=dragStart+dragDelta});await Frames(2);
        Require(vendor.Position.DistanceTo(oldPosition+dragDelta)<1 && !GetViewport().GuiIsDragging(),"Dragging the shop header moves the real native window without picking up an item");
        await Capture("window-dragged",false);vendor.Position=oldPosition;
        var hit=await ShowItem(entry.Id);
        await RightClick(hit);await Capture("right-click-buy-quantity");
        Require(quantity.Visible && !InFlight() && quantity.GetMeta("trade_action").AsString()=="Buy","Catalogue right-click opens the native buy quantity flow without sending a trade");Escape();await Frames();
        await RightClick(shell.BagCells[0]);await Capture("right-click-sale-quantity");
        var initialAmount=Descendants(quantity).OfType<MoneyEdit>().Single();
        Require(quantity.Visible && !InFlight() && initialAmount.Value==63,"Right-clicking a bag stack prefills its entire carried quantity");Escape();await Frames();
        var hover=hit.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion {Position=hover,GlobalPosition=hover});await Frames();
        var tip=(Control)DetailField(world,"_itemTipPanel")!;tip.Position=new Vector2(693,120);await Capture("item-tooltip");
        Require(tip.Visible && Descendants(tip).OfType<Label>().Any(l=>l.Text.StartsWith("Buying Price")),"Catalogue hover shows the native item tooltip and actual buying price");
        Input.ParseInputEvent(new InputEventMouseMotion {Position=Vector2.Zero,GlobalPosition=Vector2.Zero});await Frames(2);
        var target=shell.BagCells[2];
        var selectedCell=(ItemSlotView)hit.GetParent();
        var data=selectedCell.DragOut!(selectedCell);
        Require(target._CanDropData(Vector2.Zero,data),"Embedded bag accepts the native catalogue payload");
        await PointerDrag(hit,target);await Capture("buy-quantity");
        Require(quantity.Visible,"Dropping a stackable catalogue item opens the native quantity prompt");
        var amount=Descendants(quantity).OfType<MoneyEdit>().Single();
        var ok=Descendants(quantity).OfType<Button>().Single(b=>b.Name=="classic_trade_ok");
        Require(amount.Text.Length==0 && ok.Disabled,"Quantity begins blank and confirmation waits for an amount, as in OpenKO");
        var qp=Descendants(quantity).OfType<ClassicQuantityPanel>().Single();
        Require(qp.GetGlobalRect().GetCenter().DistanceTo(vendor.GetGlobalRect().GetCenter())<1,"Quantity editor is centered on the transaction window");
        Require(qp.Size==(nation==1?new Vector2(312,129):new Vector2(315,131)) && Descendants(qp).OfType<Button>().Count(b=>b.IsVisibleInTree())==2 && ok.Text==(nation==1?"O K":"O  K"),
            "Quantity uses the original compact PersonalTradeEdit dimensions and only OK/Cancel actions");
        Require(amount.GetGlobalRect()==new Rect2(qp.Position+KnightOnlineUiClassic.Layout.ClassicQuantityLayout.Amount.Position,KnightOnlineUiClassic.Layout.ClassicQuantityLayout.Amount.Size),
            "Actual quantity editor bounds match the original declarative input rectangle");
        amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);await Capture("quantity-10");
        Require(amount.Value==10 && !ok.Disabled,"Entering quantity updates the native live total and validation");
        amount.Text="0";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);await Capture("quantity-invalid");
        Require(ok.Disabled,"Zero quantity disables confirmation");
        long maximum=(long)typeof(QuantityPrompt).GetField("_max",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(quantity)!;
        amount.Value=maximum;amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);await Capture("quantity-max");
        Require(amount.Value==maximum && amount.Text==maximum.ToString("0") && !ok.Disabled,"Maximum quantity uses plain digits and respects the native money, weight and stack limit");
        amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);
        amount.GrabFocus();Input.ParseInputEvent(new InputEventKey {Pressed=true,Keycode=Key.Enter,PhysicalKeycode=Key.Enter});
        Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=Key.Enter,PhysicalKeycode=Key.Enter});await Capture("buy-approval");
        Require(!quantity.Visible && shell.ModalOpen && !InFlight() && inventory[Inventory.GridStart].Count==63,"Quantity Enter opens a separate final buy approval without sending or consuming the same key press");
        Require(Approval().PanelBounds.GetCenter().DistanceTo(vendor.GetGlobalRect().GetCenter())<1,"Final approval is centered on the current shop position");
        VerifyApproval(true,entry.Id,10,(long)ItemData.BuyPrice(entry.Id)*10);
        DetailCall(world,"MoveBetween",Inventory.GridStart,Inventory.GridStart+2);
        Require(!(bool)DetailField(world,"_moveInFlight")!,"Native inventory movement is locked while final approval is pending");
        Escape();await Frames();
        Require(!shell.ModalOpen && !InFlight() && inventory[Inventory.GridStart].Count==63,"Cancelling buy approval leaves the stack and wallet unchanged");
        await PointerDrag(hit,target);amount.Value=maximum;amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("maximum-buy-approval");VerifyApproval(true,entry.Id,(int)maximum,(long)ItemData.BuyPrice(entry.Id)*maximum);
        var approvalCancel=Descendants(Approval()).OfType<Button>().Single(b=>b.Name=="btn_cancel").GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion {Position=approvalCancel,GlobalPosition=approvalCancel});await Capture("approval-cancel-hover");
        Input.ParseInputEvent(new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Left,Position=approvalCancel,GlobalPosition=approvalCancel});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton {Pressed=false,ButtonIndex=MouseButton.Left,Position=approvalCancel,GlobalPosition=approvalCancel});await Frames();
        Require(!shell.ModalOpen && !InFlight() && inventory[Inventory.GridStart].Count==63,"The original quantity Cancel button cancels final approval without changing items or sending a trade");
        await RightClick(hit);amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed);await Frames();
        await Approve();await Capture("buy-pending");
        object pending=DetailField(world,"_pendingTrade")!;var pendingType=pending.GetType();
        Require((int)pendingType.GetField("AbsSlot")!.GetValue(pending)! == Inventory.GridStart && (int)pendingType.GetField("Count")!.GetValue(pending)! == 10,
            "Purchase prioritizes the existing matching stack before the empty drop destination");
        Require(inventory[Inventory.GridStart].Count==63 && inventory[Inventory.GridStart+2].IsEmpty && InFlight(),"Inventory waits for the authoritative trade acknowledgement");
        Require(game.Log.History.Count==0,"Pending and cancelled trades do not announce a gold transfer before the server acknowledges it");
        Require(shell.TransactionBlocked && Descendants(shell).OfType<Button>().Single(b=>b.TooltipText=="Close").Disabled,
            "Pending trade disables closing through the authoritative source state");
        await RightClick(hit);Require(!quantity.Visible && InFlight(),"Right-click cannot start another purchase while a trade acknowledgement is pending");
        Require(!target._CanDropData(Vector2.Zero,new Godot.Collections.Dictionary{{"invFrom",Inventory.GridStart},{"id",entry.Id}}),"Pending purchase rejects inventory movement");
        int pendingPage=pager.Page;shell.Page(1);Escape();await Frames();
        Require(pager.Page==pendingPage && vendor.Visible && InFlight(),"Pending trade rejects paging and Escape without losing its acknowledgement");
        DetailCall(world,"MoveBetween",Inventory.GridStart,Inventory.GridStart+2);
        Require(!(bool)DetailField(world,"_moveInFlight")!,"Native inventory handler also refuses a move during purchase acknowledgement");
        int buyCost=ItemData.BuyPrice(entry.Id)*10;ApplyGold(net.Sheet.Gold-buyCost);
        DetailCall(world,"OnTradeResult",true,1,net.Sheet.Gold,buyCost);RefreshBag();
        Require(inventory[Inventory.GridStart].ItemId==entry.Id && inventory[Inventory.GridStart].Count==73,"Successful acknowledgement merges the actual purchased quantity");
        Require(game.Log.History.Count==1 && game.Log.History[^1].Text==$"You spent −{buyCost:n0} gold."
            && game.Log.History[^1].Color==new Color("f07870") && game.Log.History[^1].Kind==GameLogKind.Item,
            "Authoritative purchase balance change records the actual signed gold expense in red in Info");
        ApplyGold(net.Sheet.Gold);
        Require(game.Log.History.Count==1,"Repeated unchanged balances do not duplicate the Info transfer");
        await Capture("purchase-confirmed");
        await PointerDrag(shell.BagCells[0],hit);await Capture("sell-quantity");
        Require(quantity.Visible && amount.Value==73 && quantity.GetMeta("trade_action").AsString()=="Sell","Dragging a stack to the catalogue prefills its entire carried quantity");
        amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);
        ok.EmitSignal(BaseButton.SignalName.Pressed);
        await Capture("sale-approval");
        Require(!quantity.Visible && shell.ModalOpen && !InFlight() && inventory[Inventory.GridStart].Count==73,"Sale quantity opens a final approval before any inventory mutation or packet");
        VerifyApproval(false,entry.Id,10,(long)ItemData.SellPrice(entry.Id)*10);
        Escape();await Frames();Require(!shell.ModalOpen && !InFlight() && inventory[Inventory.GridStart].Count==73,"Cancelling sale approval preserves all items");
        await RightClick(shell.BagCells[0]);amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed);await Frames();await Approve();
        int saleCredit=ItemData.SellPrice(entry.Id)*10;ApplyGold(net.Sheet.Gold+saleCredit);
        DetailCall(world,"OnTradeResult",true,1,net.Sheet.Gold,saleCredit);RefreshBag();
        Require(inventory[Inventory.GridStart].Count==63,"Successful sale removes only the confirmed quantity");await Capture("sale-confirmed");
        Require(game.Log.History.Count==2 && game.Log.History[^1].Text==$"You received +{saleCredit:n0} gold."
            && game.Log.History[^1].Color==new Color("79d892") && game.Log.History[^1].Kind==GameLogKind.Item,
            "Authoritative sale balance change records the actual signed gold income in green in Info");
        infoWindow.Visible=true;infoWindow.Position=new Vector2(688,410);infoWindow.Size=new Vector2(308,205);await Capture("money-info");
        var infoText=Descendants(infoWindow).OfType<RichTextLabel>().Single();
        Require(infoText.Text.Contains("[color=#f07870]") && infoText.Text.Contains("[color=#79d892]")
            && infoText.GetParsedText().Contains($"You spent −{buyCost:n0} gold.") && infoText.GetParsedText().Contains($"You received +{saleCredit:n0} gold."),
            "The rendered Classic Info control displays both expense and income with their distinct colors and signed amounts");
        infoWindow.Visible=false;
        await PointerDrag(hit,target);Escape();await Frames(2);
        Require(!quantity.Visible && !InFlight() && vendor.Visible,"Escape cancels the quantity editor while keeping the shop open");
        int gear=groups.Values.SelectMany(g=>g).Select(e=>e.Id).First(id=>ItemData.Get(id) is {Countable:0} && ItemData.IsSellable(id) && ItemData.SellPrice(id)>0);
        inventory[Inventory.GridStart+1]=new NativeSlot {ItemId=gear,Count=1,Durability=ItemData.MaxDurabilityOf(gear)};RefreshBag();await Frames();
        Require(CountBadge(shell.BagCells[1])=="","Classic inventory suppresses the quantity badge on single non-stackable equipment");
        await PointerDrag(shell.BagCells[1],hit);await Capture("equipment-sale-confirmation");
        Require(shell.ModalOpen && !quantity.Visible && !InFlight(),"Non-stackable equipment sale asks for confirmation before sending a trade");
        var notice=Approval();
        Descendants(notice).OfType<Button>().Single(b=>b.Name=="btn_cancel").EmitSignal(BaseButton.SignalName.Pressed);await Frames();
        Require(!InFlight() && inventory[Inventory.GridStart+1].ItemId==gear,"Cancelling equipment sale preserves the item and sends no trade");
        await RightClick(shell.BagCells[1]);await Frames();await Approve();
        Require(InFlight() && inventory[Inventory.GridStart+1].ItemId==gear,"Equipment sale confirmation still waits for server acknowledgement");
        int gearCredit=ItemData.SellPrice(gear);ApplyGold(net.Sheet.Gold+gearCredit);
        DetailCall(world,"OnTradeResult",true,1,net.Sheet.Gold,gearCredit);RefreshBag();await Capture("equipment-sale-confirmed");
        Require(inventory[Inventory.GridStart+1].IsEmpty,"Confirmed equipment sale removes the correct item");
        var gearGroup=groups.First(g=>g.Value.Any(e=>e.Id==gear));
        OpenShop(gearGroup.Key);hit=await ShowItem(gear);
        await RightClick(hit);await Capture("equipment-buy-approval");
        Require(!quantity.Visible && !InFlight() && shell.ModalOpen,"Right-click equipment purchase goes directly to final approval without a quantity prompt");
        await Approve();await Capture("equipment-buy-pending");
        Require(!quantity.Visible && InFlight() && inventory[Inventory.GridStart+1].IsEmpty,"Non-stackable equipment purchase needs no quantity editor and awaits acknowledgement");
        int gearCost=ItemData.BuyPrice(gear);ApplyGold(net.Sheet.Gold-gearCost);
        DetailCall(world,"OnTradeResult",true,1,net.Sheet.Gold,gearCost);RefreshBag();await Capture("equipment-buy-confirmed");
        Require(inventory[Inventory.GridStart+1].ItemId==gear,"Equipment purchase preserves the empty drop destination");
        inventory[Inventory.GridStart+1]=default;OpenShop(group.Key);hit=await ShowItem(entry.Id);
        inventory[Inventory.GridStart+1]=new NativeSlot {ItemId=entry.Id,Count=1,Durability=ItemData.MaxDurabilityOf(entry.Id)};RefreshBag();await Frames();
        Require(CountBadge(cells.Single(c=>c.Item.ItemId==entry.Id))=="1" && CountBadge(shell.BagCells[1])=="1",
            "Both the store catalogue and Classic trading bag display 1 for a one-unit stackable item");
        var storeBadge=CountLabel(cells.Single(c=>c.Item.ItemId==entry.Id));var inventoryBadge=CountLabel(shell.BagCells[1]);
        Require(storeBadge.GetThemeFont("font")==inventoryBadge.GetThemeFont("font")
            && storeBadge.GetThemeFontSize("font_size")==inventoryBadge.GetThemeFontSize("font_size")
            && storeBadge.GetThemeColor("font_outline_color")==inventoryBadge.GetThemeColor("font_outline_color")
            && storeBadge.GetThemeConstant("outline_size")==inventoryBadge.GetThemeConstant("outline_size")
            && storeBadge.GetGlobalRect().End==cells.Single(c=>c.Item.ItemId==entry.Id).GetGlobalRect().End-new Vector2(2,2)
            && inventoryBadge.GetGlobalRect().End==shell.BagCells[1].GetGlobalRect().End-new Vector2(2,2),
            "Store and Inventory count overlays share actual font, size, outline and exact two-pixel corner offsets");
        async Task RenderBadge(Label source,Vector2 slotSize,string text,string file)
        {
            var viewport=new SubViewport { Size=new Vector2I(64,64),TransparentBg=true,Disable3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var slot=new Control { Position=new Vector2(64,64)-slotSize,Size=slotSize,TextureFilter=CanvasItem.TextureFilterEnum.Nearest };viewport.AddChild(slot);
            var label=(Label)source.Duplicate();slot.AddChild(label);label.Text=text;
            label.AddThemeFontOverride("font",source.GetThemeFont("font"));
            await Frames(3);viewport.GetTexture().GetImage().SavePng(output+"/"+file);viewport.QueueFree();await Frames(2);
        }
        foreach(string text in new[]{"1","10","9999"})
        {
            string prefix=(nation==1?"karus":"human")+"-count-"+text;
            await RenderBadge(storeBadge,cells.Single(c=>c.Item.ItemId==entry.Id).Size,text,prefix+"-store.png");
            await RenderBadge(inventoryBadge,shell.BagCells[1].Size,text,prefix+"-inventory.png");
            countBadges.Add(new{text,store=prefix+"-store.png",inventory=prefix+"-inventory.png",rightInset=2,bottomInset=2});
        }
        Require(Descendants(classicInventory).OfType<ClassicSlot>().Where(c=>c.Current.ItemId==entry.Id && c.Current.Count==1).All(c=>CountBadge(c)=="1")
            && Descendants(classicInventory).OfType<ClassicSlot>().Any(c=>c.Current.ItemId==entry.Id && c.Current.Count==1),
            "The same one-unit stack displays 1 in the separate Classic inventory");
        var nativeInv=(System.Collections.IList)DetailField(world,"_invBagCells")!;
        Require(CountBadge(nativeInv[1]!)=="1","The native inventory also displays a one-unit stack badge");
        foreach(string typeName in new[]{"WarehouseCell","MerchantCell"})
        {
            var type=typeof(World).GetNestedType(typeName,BindingFlags.NonPublic)!;
            var cell=(Control)Activator.CreateInstance(type,0,typeName=="WarehouseCell"?(object)false:45)!;
            var set=type.GetMethod("Set",typeName=="WarehouseCell"?new[]{typeof(NativeSlot)}:new[]{typeof(NativeSlot),typeof(string)})!;
            set.Invoke(cell,typeName=="WarehouseCell"?new object[]{inventory[Inventory.GridStart+1]}:new object[]{inventory[Inventory.GridStart+1],""});
            Require(CountBadge(cell)=="1",typeName+": one-unit stack badge remains visible");
            set.Invoke(cell,typeName=="WarehouseCell"?new object[]{new NativeSlot{ItemId=gear,Count=1}}:new object[]{new NativeSlot{ItemId=gear,Count=1},""});
            Require(CountBadge(cell)=="",typeName+": single non-stackable equipment has no quantity badge");cell.Free();
        }
        var badgeFixture=new ItemSlotView(45);badgeFixture.Set(new NativeSlot{ItemId=gear,Count=1});
        Require(CountBadge(badgeFixture)=="","Store slot suppresses the quantity badge on single equipment");
        badgeFixture.Set(default);Require(CountBadge(badgeFixture)=="","Empty store slots show no quantity badge");badgeFixture.Free();
        var inventoryPosition=classicInventory.Position;
        classicInventory.Reparent(this);classicInventory.Position=new Vector2(520,48);vendor.Position=new Vector2(96,48);
        await Capture("single-stack-badges",false);
        Require(classicInventory.IsVisibleInTree() && Descendants(classicInventory).OfType<ClassicSlot>().Where(c=>c.Current.ItemId==entry.Id && c.Current.Count==1)
            .All(c=>c.IsVisibleInTree() && c.GetGlobalRect().Encloses(((Label)typeof(ClassicSlot).GetField("_count",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(c)!).GetGlobalRect())),
            "Standalone Classic inventory visibly renders its one-unit badge within the actual item-slot bounds");
        classicInventory.Reparent(invWindow);classicInventory.Position=inventoryPosition;vendor.Position=new Vector2(318,48);
        inventory[Inventory.GridStart+1]=new NativeSlot {ItemId=entry.Id,Count=100,Durability=ItemData.MaxDurabilityOf(entry.Id)};RefreshBag();await Frames();
        await RightClick(shell.BagCells[1]);await Capture("sale-stack-100-right-click");
        Require(amount.Text=="100" && amount.Value==100 && !ok.Disabled && !InFlight(),"Right-clicking a 100-unit stack opens sale quantity at 100 without sending a trade");Escape();await Frames();
        await PointerDrag(shell.BagCells[1],hit);await Capture("sale-stack-100-drag");
        Require(amount.Text=="100" && amount.Value==100 && !ok.Disabled && !InFlight(),"Dragging a 100-unit stack opens sale quantity at 100 without sending a trade");Escape();await Frames();
        inventory[Inventory.GridStart+1]=new NativeSlot {ItemId=entry.Id,Count=1,Durability=ItemData.MaxDurabilityOf(entry.Id)};RefreshBag();await Frames();
        await RightClick(shell.BagCells[1]);await Capture("single-stack-approval");
        Require(!quantity.Visible && shell.ModalOpen && !InFlight(),"A one-unit stack skips quantity and opens final sale approval");Escape();await Frames();
        inventory[Inventory.GridStart+1]=default;RefreshBag();await Frames();
        await PointerDrag(shell.BagCells[0],target);await Frames();
        Require((bool)DetailField(world,"_moveInFlight")! && inventory[Inventory.GridStart].Count==63,"Moving between embedded bag cells uses native inventory movement");
        Require(shell.TransactionBlocked,"Inventory acknowledgement locks subsequent shop trades");
        DetailCall(world,"AskBuy",entry.Id,-1);Require(!quantity.Visible && !InFlight(),"Native buy handler refuses a purchase while inventory movement is pending");
        DetailCall(world,"OnItemMoveResult",true);RefreshBag();await Capture("inventory-moved");
        Require(inventory[Inventory.GridStart].IsEmpty && inventory[Inventory.GridStart+2].Count==63,"Acknowledged inventory move updates the embedded bag");
        var saved=Enumerable.Range(Inventory.GridStart,Inventory.GridCount).Select(s=>inventory[s]).ToArray();
        for(int i=0;i<Inventory.GridCount;i++) inventory[Inventory.GridStart+i]=new NativeSlot {ItemId=entry.Id,Count=Inventory.StackMax};
        RefreshBag();DetailCall(world,"RefreshVendorDetail");await Frames();await PointerDrag(hit,target);await Capture("bags-full");
        Require(!quantity.Visible && !InFlight() && Status().Contains("full"),"Full inventory and capped stacks reject a purchase with a visible reason");
        for(int i=0;i<saved.Length;i++) inventory[Inventory.GridStart+i]=saved[i];RefreshBag();DetailCall(world,"RefreshVendorDetail");
        net.Sheet.SeedWealth(0,50_000);DetailCall(world,"OnVendorGold",0);await RightClick(hit);await Capture("insufficient-gold");
        Require(!quantity.Visible && !InFlight() && Status().Contains("gold"),"Right-click purchase with insufficient funds is refused with the native reason");
        net.Sheet.SeedWealth(1_234_567,50_000);DetailCall(world,"OnVendorGold",1_234_567);
        if(quantity.Visible) { Escape();await Frames(); }
        net.Sheet.SetGold(ItemData.BuyPrice(entry.Id));DetailCall(world,"OnVendorGold",net.Sheet.Gold);
        await PointerDrag(hit,target);await Capture("single-buy-approval");
        Require(!quantity.Visible && shell.ModalOpen && !InFlight(),"When only one unit can be bought, purchase skips quantity and requests approval");
        net.Sheet.SetGold(0);DetailCall(world,"OnVendorGold",0);await Approve();await Capture("approval-funds-changed");
        Require(!InFlight() && !shell.ModalOpen && Status().Contains("gold"),"Final buy confirmation rechecks funds changed after the approval opened");
        net.Sheet.SeedWealth(1_234_567,50_000);DetailCall(world,"OnVendorGold",1_234_567);
        var weighted=groups.SelectMany(g=>g.Value.Select(e=>(Group:g.Key,Entry:e))).First(e=>ItemData.Get(e.Entry.Id) is {Countable:>0,Weight:>1} && ItemData.BuyPrice(e.Entry.Id)>0);
        OpenShop(weighted.Group);hit=await ShowItem(weighted.Entry.Id);
        net.Sheet.SetMaxWeight(1);await PointerDrag(hit,target);await Capture("weight-limit");
        Require(!quantity.Visible && !InFlight() && Status().Contains("heavy"),"Weight limit produces the native refusal without changing inventory");net.Sheet.SetMaxWeight(10_000);
        OpenShop(group.Key);hit=await ShowItem(entry.Id);
        await PointerDrag(hit,target);amount.Text="999999";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);await Capture("quantity-over-limit");
        Require(ok.Disabled,"An amount above the actual trade limit cannot be confirmed");Escape();await Frames();
        var beforeRestricted=inventory[Inventory.GridStart+2];
        foreach(var flag in new[]{ItemFlag.Rented,ItemFlag.CharacterSeal,ItemFlag.Duplicate,ItemFlag.Sealed,ItemFlag.Bound})
        {
            var restricted=beforeRestricted;restricted.Flag=(byte)flag;inventory[Inventory.GridStart+2]=restricted;RefreshBag();await Frames();
            await PointerDrag(target,hit);
            Require(!quantity.Visible && !InFlight() && !shell.ModalOpen && Status().Contains("cannot be sold"),$"{flag} items are refused before a sale quantity or packet");
        }
        await Capture("restricted-item");inventory[Inventory.GridStart+2]=beforeRestricted;RefreshBag();await Frames();
        var changedStack=beforeRestricted;changedStack.UniqueId=12;inventory[Inventory.GridStart+2]=changedStack;RefreshBag();await Frames();await PointerDrag(target,hit);
        Require(!quantity.Visible && !InFlight() && Status().Contains("cannot be sold"),"Linked items retain the same restriction as the server");inventory[Inventory.GridStart+2]=beforeRestricted;RefreshBag();await Frames();
        await PointerDrag(target,hit);Require(quantity.Visible,"Normal sale still opens after a restricted-item refusal");
        inventory[Inventory.GridStart+2]=new NativeSlot{ItemId=gear,Count=1};
        amount.Text="1";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed);await Frames();
        Require(!InFlight() && inventory[Inventory.GridStart+2].ItemId==gear,"Sale quantity cannot sell a replacement item after the source slot changes");
        inventory[Inventory.GridStart+2]=beforeRestricted;RefreshBag();await Frames();
        await RightClick(target);amount.Text="10";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed);await Frames();
        var smallerStack=beforeRestricted;smallerStack.Count=5;inventory[Inventory.GridStart+2]=smallerStack;RefreshBag();await Approve();await Capture("approval-stack-changed");
        Require(!InFlight() && !shell.ModalOpen && inventory[Inventory.GridStart+2].Count==5 && Status().Contains("changed"),"Final sale approval rejects a stack reduced below the selected quantity instead of silently changing the sale");
        inventory[Inventory.GridStart+2]=beforeRestricted;RefreshBag();await Frames();
        foreach(int refusal in new[]{3,4,1})
        {
            await PointerDrag(hit,target);amount.Text="1";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);
            ok.EmitSignal(BaseButton.SignalName.Pressed);await Frames();await Approve();
            Require(InFlight(),"Server refusal fixture starts from a real pending purchase");
            int countBefore=inventory[Inventory.GridStart+2].Count;int moneyBefore=net.Sheet.Gold;
            DetailCall(world,"OnTradeResult",false,refusal,moneyBefore,0);await Capture("server-refusal-"+refusal);
            Require(!InFlight() && !shell.TransactionBlocked && inventory[Inventory.GridStart+2].Count==countBefore && net.Sheet.Gold==moneyBefore && Status().Length>0,
                $"Server refusal {refusal} releases the lock, preserves inventory and money, and displays its reason");
        }
        typeof(World).GetField("_selfDead",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,true);await Frames();
        DetailCall(world,"AskBuy",entry.Id,-1);DetailCall(world,"AskSell",Inventory.GridStart+2);
        Require(shell.TradeBlocked && !quantity.Visible && !InFlight() && !Descendants(shell).OfType<Button>().Single(b=>b.TooltipText=="Close").Disabled,"Death prevents buying and selling while allowing the shop to close");
        typeof(World).GetField("_selfDead",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,false);
        OpenShop(group.Key);await Frames();hit=Descendants(cells[0]).OfType<ClassicTradeCatalogueSlot>().Single();var oldPayload=cells[0].DragOut!(cells[0]);int oldId=cells[0].Item.ItemId;
        await Hold(hit);shell.Page(1);await Frames();await ReleasePointer();
        Require(!GetViewport().GuiIsDragging() && cells[0].Item.ItemId!=oldId && !target._CanDropData(Vector2.Zero,oldPayload),"Paging cancels the held item and rejects its stale catalogue identity");
        target._DropData(Vector2.Zero,oldPayload);Require(!quantity.Visible && !InFlight(),"Dropping a stale catalogue payload cannot buy the replacement cell item");
        shell.Page(-1);await Frames();await Hold(hit);Escape();await Frames();await ReleasePointer();
        Require(vendor.Visible && !GetViewport().GuiIsDragging(),"Escape cancels a held item before closing the shop");
        await Hold(hit);OpenShop(253000);await Frames();await ReleasePointer();
        Require(!GetViewport().GuiIsDragging() && !quantity.Visible,"Changing merchants cancels a held catalogue item");
        var merchantCatalogues=new List<object>();
        foreach(var fixture in new[]{(255000,"sundries"),(267000,"dc-sundries"),(277000,"ghost-sundries"),(253000,"potions"),(201001,"weapons"),(202001,"armor"),(254000,"upgrade-scrolls"),(250000,"scrolls")})
        {
            OpenShop(fixture.Item1);await Capture(fixture.Item2);
            var catalogue=(ShopCatalogue)DetailField(world,"_vendorCatalogue")!;
            var expected=groups.GetValueOrDefault(fixture.Item1,new()).Where(e=>ItemData.Get(e.Id)!=null && e.List>=0 && e.List<24).OrderBy(e=>e.Line).ThenBy(e=>e.List).ToArray();
            Require(pager.Pages==ShopCatalogue.PagesFor(expected.Length),$"{fixture.Item2}: page count packs all real source products without empty source pages");
            var ids=new List<int>();
            for(int p=0;p<pager.Pages;p++)
            {
                DetailCall(world,"ShowVendorPage",p);await Frames(2);ids.AddRange(cells.Where(c=>!c.Item.IsEmpty).Select(c=>c.Item.ItemId));
                Require(cells.Select(c=>c.Item.ItemId).SkipWhile(id=>id!=0).All(id=>id==0),$"{fixture.Item2} page {p+1}: occupied slots start at the top-left and have no gaps");
            }
            Require(ids.SequenceEqual(expected.Select(e=>e.Id)),$"{fixture.Item2}: every real product appears exactly in source order");
            var nativeEntries=(Dictionary<int,ItemData.SellEntry>)DetailField(world,"_vendorEntries")!;
            Require(expected.GroupBy(e=>e.Id).All(g=>nativeEntries[g.Key].Line==g.First().Line && nativeEntries[g.Key].List==g.First().List),$"{fixture.Item2}: visual packing retains original buy-packet Line/List coordinates");
            if(pager.Pages>1) await Capture(fixture.Item2+"-last-page");
            merchantCatalogues.Add(new{group=fixture.Item1,kind=fixture.Item2,products=expected.Length,pages=pager.Pages});
        }
        var longName=groups.Where(g=>g.Key!=249000).SelectMany(g=>g.Value.Select(e=>(Group:g.Key,Entry:e)))
            .Where(e=>ItemData.Get(e.Entry.Id)!=null && ItemData.BuyPrice(e.Entry.Id)>0).OrderByDescending(e=>ItemData.DisplayName(e.Entry.Id).Length).First();
        int savedGold=net.Sheet.Gold;net.Sheet.SetGold(int.MaxValue);net.Sheet.SetMaxWeight(int.MaxValue);
        OpenShop(longName.Group);DetailCall(world,"AskBuy",longName.Entry.Id,-1);await Frames();
        if(quantity.Visible) { amount.Text="1";amount.EmitSignal(LineEdit.SignalName.TextChanged,amount.Text);ok.EmitSignal(BaseButton.SignalName.Pressed); }
        await Capture("long-name-approval");VerifyApproval(true,longName.Entry.Id,1,ItemData.BuyPrice(longName.Entry.Id));
        Require(Descendants(Approval()).OfType<Label>().Single(l=>l.Name=="trade_item_name").TooltipText==ItemData.DisplayName(longName.Entry.Id),
            "The longest real catalogue name retains its complete tooltip while wrapping within the fixed item band");
        Escape();await Frames();net.Sheet.SetGold(savedGold);net.Sheet.SetMaxWeight(10_000);
        OpenShop(249000);await Capture("loyalty-shop");
        Require(((Label)DetailField(world,"_vendorEmpty")!).Visible && ((Button)DetailField(world,"_vendorBuy")!).Disabled,"An empty real catalogue has a clear message and disabled purchase");
        Require(!shell.CanSell(new Godot.Collections.Dictionary{{"invFrom",Inventory.GridStart+2}}),"National Points shop rejects item sale drops");
        Require(Descendants(shell).OfType<Label>().Any(l=>l.Text=="NP" && l.IsVisibleInTree()),"NP shop immediately displays the actual wallet currency");
        Require(!Status().Contains("Not enough gold"),"Changing shop currency clears obsolete refusal messages");
        Escape();await Frames();
        Require(!vendor.Visible && !quantity.Visible && !invWindow.Visible,"Escape calls original Close and restores the previously closed inventory state");
        invWindow.Visible=true;OpenShop(group.Key);await Frames();
        Require(!invWindow.Visible,"Previously open inventory is folded into the transaction window");
        var close=Descendants(shell).OfType<Button>().Single(b=>b.TooltipText=="Close");close.EmitSignal(BaseButton.SignalName.Pressed);await Frames();
        Require(!vendor.Visible && invWindow.Visible,"Close restores an inventory that was open before trading");
        var report=new {nation,catalogueGroup=group.Key,itemId=entry.Id,checks,screens,merchantCatalogues,countBadges,
            goldLog=game.Log.History.Select(line=>new{text=line.Text,color=line.Color.ToHtml(false),kind=line.Kind.ToString()}).ToArray(),
            clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/LibreKO.dll")))),
            pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll"))))};
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print($"VENDOR_AUDIT_OK: nation={nation}; captures={screens.Count}; checks={checks.Count}");
        // World never enters the tree: this audit invokes real source controls without starting a game.
        world.Free();net.Free();
    }
}
