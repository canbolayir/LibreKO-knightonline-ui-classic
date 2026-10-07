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
    private async Task CaptureMerchantAudit(PluginGame game,int nation)
    {
        string output=ProjectSettings.GlobalizePath("res://../../research/merchant-window-audit");if(OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR") is {Length:>0} directory)output=directory;System.IO.Directory.CreateDirectory(output);
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

        Seed();DetailCall(world,"BuildInventoryPanel");var orphan=(Control)DetailField(world,"_invContent")!;orphan.Visible=false;world.AddChild(orphan);DetailCall(world,"BuildNpcDialog");DetailCall(world,"BuildMerchantPanels");DetailCall(world,"BuildBuyMerchantPanels");

        var bridge=Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{world},null)!;

        var attach=typeof(PluginGame).GetMethod("Attach",BindingFlags.Instance|BindingFlags.NonPublic)!;attach.Invoke(game,Enumerable.Repeat(bridge,attach.GetParameters().Length).ToArray());


        var layers=new[]{"_mctLayer","_amountLayer","_merchantAdvertLayer","_itemTipLayer"}.Select(n=>(CanvasLayer)DetailField(world,n)!).ToArray();foreach(var layer in layers)layer.Reparent(this);
        var windows=layers.SelectMany(l=>Descendants(l)).OfType<HudWindow>().ToArray();
        foreach(var window in windows)if(ClassicMerchantSkin.WindowIds.Contains(window.Id))ClassicMerchantSkin.Apply(window.Body);
        var checks=new List<string>();var screens=new List<object>();
        void Require(bool value,string message){if(!value)throw new Exception("MERCHANT_AUDIT: "+message);checks.Add(message);}
        async Task Frames(int count=6){for(int i=0;i<count;i++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        async Task KeyPress(Key key){Input.ParseInputEvent(new InputEventKey {Pressed=true,Keycode=key});Input.ParseInputEvent(new InputEventKey {Pressed=false,Keycode=key});await Frames();}
        void Click(Button button)=>button.EmitSignal(BaseButton.SignalName.Pressed);
        async Task Capture(string state){await Frames();if(state!="moved-shop-approval")foreach(var window in windows.Where(w=>w.Visible))window.Position=((GetViewportRect().Size-window.Size)/2).Round();await Frames();
            foreach(var panel in layers.SelectMany(l=>Descendants(l)).OfType<ClassicMerchantPanel>().Where(p=>p.IsVisibleInTree())) {
                foreach(var cell in panel.GetChildren().OfType<Control>().Where(c=>c.Name.ToString().StartsWith("merchant_"))){Require(panel.GetGlobalRect().Encloses(cell.GetGlobalRect()),"Cell contained: "+state+" / "+cell.Name);Require(cell.GetChildren().OfType<Label>().Any(),"Native count remains in its own slot: "+cell.Name);foreach(var child in cell.GetChildren().OfType<Control>())Require(cell.GetGlobalRect().Encloses(child.GetGlobalRect()),"Slot overlay contained: "+cell.Name);
                    bool listing=cell.Name.ToString().StartsWith("merchant_listing_");int index=int.Parse(cell.Name.ToString().Split('_').Last());bool selling=panel.Window.Id=="sellstall";var art=Plugin.Kit.Layout(selling?"co_tradeinventory_us":"co_tradebuyinventory_us");var expected=art.Find((listing?"at":"a")+index)!.Position;if(!listing)expected.X=(selling?15:16)+index%7*48;Require(cell.Position==expected-Vector2.One*2 && cell.Size==new Vector2(48,48),"Actual cell matches original artwork rectangle: "+cell.Name);
                }
                foreach(var status in panel.GetChildren().OfType<Label>().Where(l=>l.HasMeta("merchant_status_rect")))foreach(var balance in panel.GetChildren().OfType<Label>().Where(l=>l.HasMeta("merchant_balance_rect")))Require(!status.GetGlobalRect().Intersects(balance.GetGlobalRect()),"Status does not overlap balance: "+state);
                Require(panel.GetChildren().OfType<Control>().Count(c=>c.Name.ToString().StartsWith("merchant_listing_"))==12 || panel.Window.Id=="merchantmenu","12 listings visible: "+state);
            }
            string file=(nation==1?"karus":"human")+"-"+state+".png";GetViewport().GetTexture().GetImage().SavePng(output+"/"+file);screens.Add(new{state,file});
        }
        var menu=(HudWindow)DetailField(world,"_merchantMenu")!;menu.Visible=true;await Capture("menu");var sellingButton=Descendants(menu).OfType<Button>().Single(b=>b.Text=="Selling Merchant");sellingButton.Notification((int)Control.NotificationMouseEnter);await Capture("menu-hover");sellingButton.ToggleMode=true;sellingButton.SetPressedNoSignal(true);await Capture("menu-pressed");sellingButton.SetPressedNoSignal(false);sellingButton.ToggleMode=false;sellingButton.Notification((int)Control.NotificationMouseExit);
        foreach(var button in Descendants(menu).OfType<Button>().Where(b=>b.IsVisibleInTree()))Require(button.GetThemeColor("font_hover_pressed_color")==new Color("efd9b4"),"Shared merchant pressed-hover text color: "+button.Text);foreach(var button in Descendants(menu).OfType<Button>().Where(b=>b.IsVisibleInTree()))Require(button.Size==new Vector2(144,27) && button.Position.X==91,"Merchant selector uses equal button rectangles: "+button.Text);menu.Visible=false;
        DetailCall(world,"OnMerchantOpenResult",Net.MerchantOpenAccepted);await Capture("selling-empty");
        var sourceCell=((Array)DetailField(world,"_sellBagCells")!).GetValue(0)!;
        sourceCell.GetType().GetMethod("_GuiInput")!.Invoke(sourceCell,new object[]{new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Left}});
        Require(!((CanvasLayer)DetailField(world,"_amountLayer")!).Visible,"Left press allows dragging before activation");
        sourceCell.GetType().GetMethod("_Notification")!.Invoke(sourceCell,new object[]{(int)Control.NotificationDragBegin});
        sourceCell.GetType().GetMethod("_GuiInput")!.Invoke(sourceCell,new object[]{new InputEventMouseButton {Pressed=false,ButtonIndex=MouseButton.Left}});
        Require(!((CanvasLayer)DetailField(world,"_amountLayer")!).Visible,"Drag release does not also open click prompt");
        var dropTarget=(Control)((Array)DetailField(world,"_sellStallCells")!).GetValue(5)!;var drop=new Godot.Collections.Dictionary {{"bagFrom",0},{"id",stack}};Require(dropTarget._CanDropData(Vector2.Zero,drop),"Native listing accepts inventory drag data");dropTarget._DropData(Vector2.Zero,drop);Require(((CanvasLayer)DetailField(world,"_amountLayer")!).Visible,"Native inventory drop opens pricing");var callback=(Action<int,int>)DetailField(world,"_amountAccept")!;Require((int)callback.Target!.GetType().GetField("stallSlot")!.GetValue(callback.Target)! == 5,"Drag pricing retains the actual destination slot");await KeyPress(Key.Escape);
        sourceCell.GetType().GetMethod("_GuiInput")!.Invoke(sourceCell,new object[]{new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Right}});await Capture("listing-price");
        var amount=(CanvasLayer)DetailField(world,"_amountLayer")!;var panel=Descendants(amount).OfType<ClassicMerchantAmountPanel>().Single();
        var price=(MoneyEdit)DetailField(world,"_amountPrice")!;price.Value=0;await KeyPress(Key.Enter);Require(amount.Visible,"Zero unit price remains in prompt");await Capture("invalid-price");
        price.Value=12345;await KeyPress(Key.Enter);await Capture("listing-quantity");
        var spin=(SpinBox)DetailField(world,"_amountCount")!;spin.GetLineEdit().Text="101";await KeyPress(Key.Enter);Require(amount.Visible,"Unavailable quantity rejected");await Capture("invalid-quantity");
        spin.GetLineEdit().Text="25";await KeyPress(Key.Enter);Require(!amount.Visible,"Enter submits a valid listing quantity");
        DetailCall(world,"OnMerchantItemAdd",true,stack,25,ItemData.MaxDurabilityOf(stack),12345,0,0);await Capture("selling-filled");
        DetailCall(world,"ConfirmSellStall");await Capture("advertisement");Require(((CanvasLayer)DetailField(world,"_merchantAdvertLayer")!).Visible,"Selling OK opens original advertisement step");await KeyPress(Key.Escape);Require(((HudWindow)DetailField(world,"_sellStallPanel")!).Visible,"Advertisement cancellation retains setup");
        DetailCall(world,"CloseSellStall");
        var items=new MerchantStallItem[12];for(int i=0;i<12;i++)items[i]=new MerchantStallItem {ItemId=i==0?stack:single[i-1],Count=i==0?100:1,Price=500,Durability=ItemData.MaxDurabilityOf(i==0?stack:single[i-1])};
        DetailCall(world,"OnMerchantList",123,items.ToArray());await Capture("shop");
        var shopCell=(Control)((Array)DetailField(world,"_shopCells")!).GetValue(0)!;shopCell.EmitSignal(Control.SignalName.MouseEntered);await Capture("item-tooltip");shopCell.EmitSignal(Control.SignalName.MouseExited);
        shopCell.GetType().GetMethod("_GuiInput")!.Invoke(shopCell,new object[]{new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Right}});await Capture("buy-quantity");spin.GetLineEdit().Text="10";await KeyPress(Key.Enter);await Capture("buy-approval");
        await KeyPress(Key.Enter);Require(!(bool)DetailField(world,"_merchantBuyPending")!,"Enter in approval does not purchase");
        await KeyPress(Key.Escape);Require(!amount.Visible && (bool)DetailField(world,"_shopShown")!,"Escape in purchase approval returns to browsing");
        DetailCall(world,"BuyFromStall",0);await Frames();spin.GetLineEdit().Text="10";await KeyPress(Key.Enter);Click(panel.GetChildren().OfType<Button>().Single(b=>b.Text=="Yes"));Require((bool)DetailField(world,"_merchantBuyPending")!,"Explicit Yes sends purchase exactly once");DetailCall(world,"BuyFromStall",1);Require(!amount.Visible,"Pending purchase blocks a duplicate prompt");
        inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=110,Durability=ItemData.MaxDurabilityOf(stack)};DetailCall(world,"OnMerchantBuy",true,stack,90,0,0);Require(inventory[Inventory.GridStart].Count==110,"Merchant ACK preserves authoritative stack amount");net.Sheet.SeedWealth(1_229_567,50_000);DetailCall(world,"OnMerchantGold",1_229_567);Require(((Label)DetailField(world,"_shopBalance")!).Text==1_229_567.ToString("n0"),"Merchant balance updates after gold notification");await Capture("purchase-result");DetailCall(world,"CloseShop");
        DetailCall(world,"OpenWishList");await Capture("wishlist-empty");DetailCall(world,"OnWishSlotClicked",0);await Capture("item-search");var search=(ItemSearchPanel)DetailField(world,"_wishFind")!;search.SetQuery("Raptor");search.Run();await Capture("item-search-results");DetailCall(world,"OnWishItemPicked",new ItemSearchHit(ItemData.Get(stack)!,null),1);await Frames();price.Value=100;await KeyPress(Key.Enter);spin.GetLineEdit().Text="12";await KeyPress(Key.Enter);await Capture("wishlist-filled");
        Require(!amount.Visible,"Wish quantity submits through native registration");
        var wishes=(MerchantWishItem[])DetailField(world,"_wishes")!;for(int i=1;i<12;i++)wishes[i]=new MerchantWishItem {ItemId=single[i-1],Count=1,Price=100};DetailCall(world,"RefreshWishList");await Capture("wishlist-full");DetailCall(world,"CloseWishList");
        DetailCall(world,"OnBuyMerchantList",123,items.ToArray());await Capture("buying-stall");var wantedOk=Descendants((Control)DetailField(world,"_wantedPanel")!).OfType<Button>().Single(b=>b.IsVisibleInTree() && b.Text=="OK");wantedOk.Notification((int)Control.NotificationMouseEnter);await Capture("buying-button-hover");wantedOk.ToggleMode=true;wantedOk.SetPressedNoSignal(true);await Capture("buying-button-pressed");wantedOk.SetPressedNoSignal(false);wantedOk.ToggleMode=false;wantedOk.Notification((int)Control.NotificationMouseExit);
        foreach(string state in new[]{"normal","hover","pressed","hover_pressed","disabled"}){var box=(StyleBoxTexture)wantedOk.GetThemeStylebox(state);var accent=new Color(1,.72f,.49f);Require(box.ModulateColor==(state=="hover"?accent.Lightened(.18f):state is "pressed" or "hover_pressed"?accent.Darkened(.18f):state=="disabled"?accent.Darkened(.5f):accent),"Buying button palette: "+state);}
        DetailCall(world,"SellToWanted",0,0);Require(spin.Value==100,"Wanted sale defaults to the complete available stack");await Capture("sell-quantity");spin.GetLineEdit().Text="5";await KeyPress(Key.Enter);await Capture("sell-approval");var approvalYes=Descendants(amount).OfType<Button>().Single(b=>b.IsVisibleInTree() && b.Text=="Yes");approvalYes.Notification((int)Control.NotificationMouseEnter);await Capture("sell-approval-hover");approvalYes.ToggleMode=true;approvalYes.SetPressedNoSignal(true);await Capture("sell-approval-pressed");approvalYes.SetPressedNoSignal(false);approvalYes.ToggleMode=false;approvalYes.Notification((int)Control.NotificationMouseExit);Require(((StyleBoxTexture)approvalYes.GetThemeStylebox("normal")).ModulateColor==new Color(1,.72f,.49f),"Buying approval button matches Copper frame");await KeyPress(Key.Enter);Require(DetailField(world,"_wantedSale")==null,"Enter does not submit a wanted sale");await KeyPress(Key.Escape);
        DetailCall(world,"SellToWanted",0,0);await Frames();spin.GetLineEdit().Text="5";await KeyPress(Key.Enter);Click(panel.GetChildren().OfType<Button>().Single(b=>b.Text=="Yes"));Require(DetailField(world,"_wantedSale")!=null,"Wanted sale records its quantity before sending");
        inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=105,Durability=ItemData.MaxDurabilityOf(stack)};DetailCall(world,"OnBuyMerchantSold",0,85,0,105);Require(inventory[Inventory.GridStart].Count==105,"Wanted sale ACK preserves the preceding authoritative stack update");Require(((Label)DetailField(world,"_wantedStatus")!).Text.Contains("Sold 5 x"),"Wanted-sale message retains quantity after server stack update");net.Sheet.SeedWealth(1_232_067,50_000);DetailCall(world,"OnMerchantGold",1_232_067);Require(((Label)DetailField(world,"_wantedBalance")!).Text==1_232_067.ToString("n0"),"Wanted-stall balance updates after payout notification");await Capture("wanted-sale-result");
        net.Sheet.SeedWealth(2_100_000_000,50_000);DetailCall(world,"RefreshWantedStall");DetailCall(world,"SellToWanted",0,0);Require(!amount.Visible,"Coin cap prevents an impossible seller payout");await Capture("seller-coin-limit");net.Sheet.SeedWealth(1_234_567,50_000);DetailCall(world,"CloseWantedStall");
        DetailCall(world,"OnMerchantList",123,items.ToArray());DetailCall(world,"BuyFromStall",0);await Frames();DetailCall(world,"CloseShop");Require(!amount.Visible,"Remote shop closure discards the quantity callback");
        DetailCall(world,"OnMerchantList",123,items.ToArray());net.Sheet.SeedWealth(750,50_000);DetailCall(world,"BuyFromStall",0);await Frames();Require(spin.MaxValue==1,"Purchase quantity is limited by available gold");await Capture("affordable-single-approval");await KeyPress(Key.Escape);
        net.Sheet.SeedWealth(0,50_000);DetailCall(world,"RefreshShop");DetailCall(world,"BuyFromStall",0);Require(!amount.Visible,"Insufficient funds do not open purchase prompt");await Capture("purchase-no-funds");
        net.Sheet.SeedWealth(1_234_567,50_000);for(int i=1;i<Inventory.GridCount;i++)inventory[Inventory.GridStart+i]=new NativeSlot {ItemId=single[0],Count=1,Durability=ItemData.MaxDurabilityOf(single[0])};DetailCall(world,"RefreshShop");DetailCall(world,"BuyFromStall",0);await Frames();spin.GetLineEdit().Text="10";await KeyPress(Key.Enter);Click(panel.GetChildren().OfType<Button>().Single(b=>b.Text=="Yes"));Require((bool)DetailField(world,"_merchantBuyPending")!,"A full inventory accepts purchase into a matching stack");inventory[Inventory.GridStart]=new NativeSlot{ItemId=stack,Count=115,Durability=ItemData.MaxDurabilityOf(stack)};DetailCall(world,"OnMerchantBuy",true,stack,90,0,0);await Capture("full-inventory-merge");
        inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=9998,Durability=ItemData.MaxDurabilityOf(stack)};DetailCall(world,"BuyFromStall",0);await Frames();Require(spin.MaxValue==1,"A full inventory limits quantity to remaining stack capacity");await KeyPress(Key.Escape);inventory[Inventory.GridStart]=new NativeSlot {ItemId=stack,Count=115,Durability=ItemData.MaxDurabilityOf(stack)};
        DetailCall(world,"BuyFromStall",0);await Frames();spin.GetLineEdit().Text="2";await KeyPress(Key.Enter);var shop=(HudWindow)DetailField(world,"_shopPanel")!;shop.Position=new Vector2(90,130);await Frames();Require(panel.GetGlobalRect().GetCenter().DistanceTo(shop.GetGlobalRect().GetCenter())<=1,"Approval follows the moved merchant window");await Capture("moved-shop-approval");await KeyPress(Key.Escape);
        DetailCall(world,"BuyFromStall",0);await Frames();spin.GetLineEdit().Text="1";await KeyPress(Key.Enter);Click(panel.GetChildren().OfType<Button>().Single(b=>b.Text=="Yes"));DetailCall(world,"CloseShop");DetailCall(world,"OnMerchantList",456,items.ToArray());DetailCall(world,"OnMerchantBuy",true,stack,89,0,0);Require(((MerchantStallItem[])DetailField(world,"_shopItems")!)[0].Count==100,"Late purchase ACK cannot change a different merchant's listing");await Capture("late-shop-ack");DetailCall(world,"CloseShop");
        DetailCall(world,"OnMerchantOpenResult",Net.MerchantOpenAccepted);for(int i=0;i<12;i++){inventory[Inventory.GridStart+i]=new NativeSlot{ItemId=stack,Count=9999,Durability=ItemData.MaxDurabilityOf(stack)};DetailCall(world,"OnMerchantItemAdd",true,stack,9999,ItemData.MaxDurabilityOf(stack),2_000_000_000,i,i);}await Capture("maximum-listings");
        GetWindow().Size=new Vector2I(640,480);await Frames();await Capture("compact-viewport");Require(GetViewportRect().Encloses(((HudWindow)DetailField(world,"_sellStallPanel")!).GetGlobalRect()),"Original selling composition fits a 640 by 480 viewport");GetWindow().Size=new Vector2I(800,720);await Frames();DetailCall(world,"CloseSellStall");
        var noticeLayer=new CanvasLayer {Layer=220};AddChild(noticeLayer);int leaves=0,stays=0;
        var request=(DialogRequest)Activator.CreateInstance(typeof(DialogRequest),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"Merchant","Leave your stall? This will close it.","Leave","Stay",true,(Action)(()=>leaves++),(Action)(()=>stays++),(Action)(()=>{})},null)!;
        var notice=new ClassicMerchantNotice(request);noticeLayer.AddChild(notice);await Capture("leave-warning");await KeyPress(Key.Enter);Require(leaves==0,"Enter never closes the active stall");await KeyPress(Key.Escape);Require(stays==1 && leaves==0,"Escape in leave warning keeps the stall");
        Descendants(notice).OfType<Button>().Single(b=>b.Text=="Leave").EmitSignal(BaseButton.SignalName.Pressed);Require(leaves==1,"Explicit Leave invokes original closure callback");noticeLayer.QueueFree();await Frames();
        var signLayer=new CanvasLayer();AddChild(signLayer);var styler=new ClassicMerchantSigns();signLayer.AddChild(styler);
        foreach(bool buying in new[]{false,true})foreach(int count in new[]{4,8}) {
            var sign=new PanelContainer {Position=new Vector2(310,250)};sign.SetMeta("merchant_buying",buying);signLayer.AddChild(sign);sign.AddToGroup("merchant_signs");var grid=new GridContainer {Columns=4};sign.AddChild(grid);
            for(int i=0;i<count;i++){var cell=(Control)Activator.CreateInstance(sourceCell.GetType(),new object[]{i,32})!;grid.AddChild(cell);cell.GetType().GetMethod("Set",new[]{typeof(NativeSlot),typeof(string)})!.Invoke(cell,new object[]{new NativeSlot{ItemId=i==0?stack:single[i-1],Count=(short)(i==0?25:1)},""});}
            styler._Process(0.3);await Frames();Require(sign.Size==new Vector2(180,count==4?71:107),"Original world sign dimensions: "+count);Require(grid.GetThemeConstant("v_separation")==4,"Compact premium icon row separation");Require(Descendants(sign).OfType<Label>().Any(l=>l.Text=="BUYING")==buying,"Buying sign identity remains explicit");
            Require(((StyleBoxTexture)sign.GetThemeStylebox("panel")).ModulateColor==(buying?new Color(1,.72f,.49f):new Color(.88f,.90f,.94f)),"Buying/selling sign frame accents remain distinct");
            {var caption=Descendants(sign).OfType<Label>().Single(l=>l.Text==(buying?"BUYING":"SELLING"));Require(Mathf.Abs(caption.GetGlobalRect().GetCenter().X-sign.GetGlobalRect().GetCenter().X)<0.1f,"Merchant caption centered in sign");Require(caption.GetGlobalRect().End.Y+4==grid.GetGlobalRect().Position.Y,"Merchant header separated from item rows");Require(sign.GetGlobalRect().Encloses(caption.GetGlobalRect()),"Merchant header contained in frame");}await Capture((buying?"buying-":"")+(count==4?"normal-sign":"premium-sign"));sign.QueueFree();await Frames();
        }
        signLayer.QueueFree();await Frames();
        GetWindow().Size=new Vector2I(1280,800);await Frames();
        DetailCall(world,"MarketPriceInit");
        var historyLayer=(CanvasLayer)DetailField(world,"_marketPriceLayer")!;historyLayer.Reparent(this);
        typeof(Net).GetMethod("SeedPreviewPremium",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(net,new object[]{1,1,72});
        var marketCache=(Dictionary<int,MarketPriceReply>)DetailField(world,"_marketPriceCache")!;
        var marketReply=new MarketPriceReply(MarketPrice.History,stack,Enumerable.Range(0,5).Select(i=>new MarketPriceDay(12000+i*1000,16000+i*1000,8000+i*1000)).ToArray(),123,DateTime.UtcNow);
        marketCache[stack]=marketReply;
        DetailCall(world,"OnMerchantOpenResult",Net.MerchantOpenAccepted);DetailCall(world,"StageStallItem",0);await Frames();price.Value=9000;await Frames();await Capture("market-price-low");
        Require(panel.Size==new Vector2(255,158),"Market hint extends only the original price frame rails");
        var history=panel.GetChildren().OfType<Button>().Single(b=>b.Name=="merchant_market_history");
        Require(history.IsVisibleInTree() && panel.GetGlobalRect().Encloses(history.GetGlobalRect()),"Market history action is visible and contained in the Classic prompt");
        var marketHint=panel.GetChildren().OfType<Label>().Single(l=>l.Name=="merchant_market_hint");
        Require(marketHint.IsVisibleInTree() && panel.GetGlobalRect().Encloses(marketHint.GetGlobalRect()),"Native market comparison label is visible in the Classic price stage");
        price.Value=50000;await Frames();await Capture("market-price-high");
        Click(history);await Frames();DetailCall(world,"OnMarketPrice",marketReply);await Capture("market-price-history");Require(amount.Visible && price.Value==50000,"Opening price history preserves the pricing operation");
        Require(GetViewportRect().Encloses(((HudWindow)DetailField(world,"_marketPricePanel")!).GetGlobalRect()),"New history window fits the reviewed viewport");
        await KeyPress(Key.Escape);Require(amount.Visible && price.Value==50000,"Classic prompt leaves Escape available while history is open");
        DetailCall(world,"CloseMarketPrice");Require(amount.Visible && price.Value==50000 && !(bool)DetailField(world,"_marketPriceShown")!,"History close preserves the underlying price prompt");
        await KeyPress(Key.Enter);await Frames();Require(panel.Size==new Vector2(255,106) && !history.IsVisibleInTree(),"Quantity retains the original size and hides pricing-only market controls: size="+panel.Size+", visible="+amount.Visible+", quantity="+amount.GetMeta("merchant_quantity")+", history="+history.IsVisibleInTree()+", open="+amount.GetMeta("merchant_market_open"));await Capture("market-price-quantity");
        await KeyPress(Key.Escape);DetailCall(world,"CloseSellStall");
        typeof(Net).GetMethod("SeedPreviewPremium",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(net,new object[]{0,0,0});
        DetailCall(world,"OnMerchantOpenResult",Net.MerchantOpenAccepted);DetailCall(world,"StageStallItem",0);await Frames();Require(panel.Size==new Vector2(255,106) && !history.IsVisibleInTree(),"Non-premium pricing retains the original composition");await Capture("market-price-non-premium");await KeyPress(Key.Escape);DetailCall(world,"CloseSellStall");
        System.IO.File.WriteAllText(output+"/"+(nation==1?"karus":"human")+"-verification.json",JsonSerializer.Serialize(new{nation,checks,screens,pluginHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/KnightOnlineUiClassic.dll")))),clientHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/LibreKO.dll"))))},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("MERCHANT_AUDIT_OK "+checks.Count);world.Free();net.Free();foreach(var layer in layers)layer.QueueFree();await Frames(3);
    }
}
