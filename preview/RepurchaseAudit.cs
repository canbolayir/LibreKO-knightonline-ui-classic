using Godot;
using LibreKO;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;
using KnightOnlineUiClassic;
using KnightOnlineUiClassic.Windows;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Spec = KnightOnlineUiClassic.Layout.ClassicRepurchaseLayout;
using NativeApi = KnightOnlineUiClassic.Native.Native;

public partial class Preview
{
    /// <summary>
    /// repurchase-audit: the client's buy-back window (Kaishan, 3277df5) in its Classic composition, both nations. The
    /// client's own facet builds the window; the server's list, purchase and refusal replies are fed to its handler and
    /// every request it sends is read from a loopback socket. Checks the declared and rendered bounds of every placed
    /// control, whole rows with scrolling, the grade, too-dear and expired inks (from the IGameRepurchase facade), the
    /// item tooltip, the client's confirmation before a purchase, the expired notice, Refresh, the empty list and Close.
    /// </summary>
    private async Task CaptureRepurchaseAudit(int nation, PluginInfo info)
    {
        string output = OS.GetEnvironment("LIBREKO_AUDIT_OUTPUT_DIR"); System.IO.Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(ProjectSettings.GlobalizePath("res://../../build/client/source-content/knightonline.pck"), false)) throw new Exception("Missing existing content pack");
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled; GetWindow().Size = new Vector2I(1000, 700);
        GetViewport().GuiEmbedSubwindows = true;
        AddChild(new ColorRect { Color = new Color("252822"), Size = new Vector2(1200, 850), MouseFilter = MouseFilterEnum.Ignore });
        string prefix = nation == 1 ? "karus" : "human";
        var original = Net.I; var net = new Net(); typeof(Net).GetProperty("I")!.SetValue(null, net);
        var me = Game.Session.Self.LastEnter; me.Nation = nation; me.Name = "BuyBackTester"; NativeApi.Call(Game.Session.Self, "SeedPreviewEnter", me);
        Game.Session.Self.Sheet.SetGold(1_000_000);
        // The plugin on the client's own facades (as in game): the skin reads IGameRepurchase.Rows.
        new Plugin().Initialize((PluginContext)Create(typeof(PluginContext), info, new List<PluginInfo> { info }, PluginHost.Ui, PluginHost.Game));
        var world = PreviewFixtures.Prepared(new WorldRoot());
        var layer = (CanvasLayer)DetailCall(world, "BuildRepurchaseClassicUiPreview")!; AddChild(layer);
        var checks = new List<string>(); var screens = new List<object>();
        void Require(bool valid, string text) { if (!valid) throw new Exception("REPURCHASE_AUDIT: " + text); checks.Add(text); }
        async Task Frames(int count = 6) { for (int i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        void Mouse(Vector2 at) => GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
        async Task Click(Control control)
        {
            var p = control.GetGlobalRect().GetCenter(); Mouse(p); await Frames(1);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Frames();
            Mouse(new Vector2(3, 3)); await Frames(2);
        }

        // Loopback: the requests the client sends (GS_ITEM_TRADE, sub-command 5 = buy-back, then the operation).
        var sent = new ConcurrentQueue<byte[]>();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var conn = NativeApi.Get<object>(net, "conn")!;
        NativeApi.Call(conn, "Connect", "127.0.0.1", port);
        using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var stream = peer.GetStream();
        using var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, stop.Token);
                    byte[] body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)) + 2]; await stream.ReadExactlyAsync(body, stop.Token);
                    sent.Enqueue(body[..^2]);
                }
            }
            catch (Exception) { }
        });
        for (int i = 0; i < 200 && !net.Connected; i++) await Frames(1);
        Require(net.Connected, "Loopback connection for the client's requests");
        List<byte[]> Drain() { var list = new List<byte[]>(); while (sent.TryDequeue(out var b)) list.Add(b); return list; }
        async Task<List<byte[]>> Requests() { await Frames(4); await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout); return Drain().Where(b => b.Length >= 3 && b[0] == (byte)GameOpcodes.GS_ITEM_TRADE && b[1] == 5).ToList(); }

        const int npc = 31508;
        string SystemText(int id, string fallback) => LibreKO.Domain.GameText.System(id, fallback);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int[] items = { 156211031, 156211038, 389015000, 1310610106, 208003000, 330310000, 208001000, 156210008, 136710000, 320410011, 208002000, 208004000, 208005000 };
        var rows = items.Select((id, i) => new RepurchaseRow((byte)i, id,
            i == 1 ? 2_400_000 : 1_000 + i * 12_345,
            (uint)(i == 2 ? now - 3600 : now + (i + 1) * 7200))).ToList();
        try
        {
            Drain();
            DetailCall(world, "Repurchase.Open", npc);
            var open = await Requests();
            Require(open.Count == 1 && (RepurchaseSubOpcode)open[0][2] == RepurchaseSubOpcode.Open, "Kaishan's buy-back flag opens the list with the client's request (op 1)");
            DetailCall(world, "Repurchase.On", PreviewFixtures.RepurchaseList(RepurchaseSubOpcode.Open, rows));
            var window = layer.GetChildren().OfType<HudWindow>().Single(w => w.Id == "repurchase");
            Require(window.Visible, "The list reply shows the client's window");
            NativeRepurchase.Prepare(window, world);
            var panel = ClassicRepurchaseSkin.Apply(window.Body)!;
            Require(panel != null, "The client's controls are prepared for the Classic composition");
            window.Position = new Vector2(280, 120);
            T Find<T>(string name) where T : Control => Descendants(panel!).OfType<T>().Single(c => c.Name == name);
            var scroll = Find<VScrollBar>("rb_scroll");
            Label Name(int r) => Find<Label>("rb_name_" + r); Label Price(int r) => Find<Label>("rb_price_" + r); Label Time(int r) => Find<Label>("rb_time_" + r);
            Button Buy(int r) => Find<Button>("rb_buy_" + r);
            var warning = new Color("ff6a5a");
            async Task Capture(string state)
            {
                await Frames();
                Require(window.Size == Spec.Size, state + " fixed Classic footprint " + window.Size);
                Require(GetViewportRect().Encloses(window.GetGlobalRect()), state + " complete window inside the viewport");
                foreach (var c in Descendants(panel!).OfType<Control>().Where(c => c.HasMeta(ClassicRepurchaseSkin.ExpectedRect)))
                {
                    var rect = c.GetMeta(ClassicRepurchaseSkin.ExpectedRect).AsRect2();
                    Require((c.Position - rect.Position).Length() < .1f && (c.Size - rect.Size).Length() < .1f, state + " declared and rendered bounds " + c.Name + " " + c.GetRect());
                    Require(window.GetGlobalRect().Encloses(c.GetGlobalRect()), state + " contained " + c.Name);
                }
                var list = panel!.GetNode<Control>("repurchase_list").GetGlobalRect();
                foreach (var c in Descendants(panel!).OfType<Control>().Where(c => c.IsVisibleInTree() && c.Name.ToString().StartsWith("rb_") && c.Name != "rb_refresh"))
                    Require(list.Encloses(c.GetGlobalRect()), state + " " + c.Name + " inside the list inset");
                Require(Find<Button>("rb_refresh").GetGlobalRect().Position.Y >= list.End.Y, state + " Refresh under the list");
                for (int r = 0; r < Spec.Rows; r++)
                    if (Name(r).Visible)
                    {
                        Require(Name(r).ClipText && Name(r).TextOverrunBehavior == TextServer.OverrunBehavior.TrimEllipsis, state + " long item names end in an ellipsis inside their column " + r);
                        foreach (var label in new[] { Price(r), Time(r) })
                            Require(label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X <= label.Size.X, state + " complete text " + label.Name + " " + label.Text);
                        Require(Buy(r).GetThemeStylebox("normal") is StyleBoxTexture && Buy(r).Visible, state + " Classic Buy button on row " + r);
                    }
                string file = prefix + "-repurchase-" + state + ".png";
                GetViewport().GetTexture().GetImage().SavePng(output + "/" + file);
                screens.Add(new { state, file, bounds = window.GetGlobalRect().ToString() });
            }
            void Inks(string state)
            {
                int offset = (int)scroll.Value;
                for (int r = 0; r < Spec.Rows; r++)
                {
                    int index = offset + r;
                    if (index >= rows.Count) { Require(!Name(r).Visible && !Buy(r).Visible, state + " row " + r + " past the list end is empty"); continue; }
                    var row = rows[index];
                    Require(Name(r).Text == ItemData.DisplayName(row.ItemId) && Name(r).GetThemeColor("font_color") == ItemGrade.Tint(row.ItemId), state + " row " + r + " names the stored item in its grade colour");
                    bool dear = row.Price > Game.Session.Self.Sheet.Gold, expired = row.DeletesAt <= now;
                    Require(Price(r).GetThemeColor("font_color") == (dear ? warning : ClassicDarkFrame.Value), state + " row " + r + (dear ? " price above the wallet in red" : " price in the frame's value gold"));
                    Require(Time(r).GetThemeColor("font_color") == (expired ? warning : ClassicDarkFrame.Value), state + " row " + r + (expired ? " passed deletion time in red" : " deletion time in the frame's value gold"));
                }
            }
            await Frames(8);
            await Capture("filled");
            Require(scroll.Visible && scroll.MaxValue == rows.Count - Spec.Rows, "Thirteen stored items scroll by whole rows (" + scroll.MaxValue + ")");
            Require(scroll.GetThemeStylebox("grabber").GetMinimumSize().Y >= Spec.Scroll.Size.Y * Spec.Rows / rows.Count - 1, "The grabber shows the ten-row share of the list (" + scroll.GetThemeStylebox("grabber").GetMinimumSize().Y + " px)");
            Require(Enumerable.Range(0, Spec.Rows).All(r => Name(r).Visible && Math.Abs(Name(r).GetGlobalRect().Position.Y - Name(0).GetGlobalRect().Position.Y - r * Spec.RowHeight) < .5f), "Ten whole rows on the 28-pixel pitch");
            Inks("filled");
            for (int c = 0; c < 4; c++) Require(Find<Label>("rb_head_" + c).Visible && Find<Label>("rb_head_" + c).GetThemeColor("font_color") == ClassicDarkFrame.Caption, "Column caption " + c + " in the caption gold");

            scroll.Value = 3; await Frames();
            Require(Name(0).Text == ItemData.DisplayName(rows[3].ItemId), "Scrolling shows the next whole rows (the client's render)");
            Inks("scrolled"); await Capture("scrolled");
            scroll.Value = 0; await Frames();

            // Hover: the client's item tooltip.
            Mouse(Name(0).GetGlobalRect().GetCenter()); await Frames();
            Require(world.ItemTooltipVisible, "Hovering a stored item opens the client's item tooltip");
            var tip = (Control)DetailField(world, "ItemTooltip.TipPanel")!; tip.Position = new Vector2(740, 120);
            await Capture("item-tooltip");
            Mouse(new Vector2(3, 3)); await Frames();
            Require(!world.ItemTooltipVisible, "Leaving the item hides the tooltip");
            Mouse(Buy(0).GetGlobalRect().GetCenter()); await Frames(); await Capture("buy-hover"); Mouse(new Vector2(3, 3)); await Frames(2);

            // Buy: the client's confirmation first, then its request with the row, item and NPC.
            Notice? Confirmation() => world.GetChildren().OfType<Notice>().LastOrDefault(n => !n.IsQueuedForDeletion());
            DialogRequest RequestOf(Notice notice) => (DialogRequest)typeof(Notice).GetField("request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notice)!;
            Drain();
            await Click(Buy(1));
            var ask = Confirmation();
            Require(ask != null && RequestOf(ask).HasCancel && RequestOf(ask).Message.Contains(ItemData.DisplayName(rows[1].ItemId)) && RequestOf(ask).Message.Contains("2,400,000"),
                "Buy asks with the client's confirmation naming the item and its price");
            Require((await Requests()).Count == 0, "Nothing is sent before the confirmation");
            ask!.Reparent(this); await Frames(); await Capture("confirmation");
            RequestOf(ask).Cancel(); await Frames();
            Require((await Requests()).Count == 0, "Cancel sends nothing");
            await Click(Buy(0));
            ask = Confirmation(); RequestOf(ask!).Confirm(); await Frames();
            var buy = await Requests();
            Require(buy.Count == 1 && (RepurchaseSubOpcode)buy[0][2] == RepurchaseSubOpcode.Buy && buy[0][3] == rows[0].Row && BinaryPrimitives.ReadInt32LittleEndian(buy[0].AsSpan(4)) == rows[0].ItemId
                && BinaryPrimitives.ReadInt32LittleEndian(buy[0].AsSpan(8)) == npc, "OK sends the client's buy-back request for the row, item and NPC");
            // An expired row: the client's notice, no request.
            await Click(Buy(2));
            var expired = Confirmation();
            Require(expired != null && !RequestOf(expired).HasCancel && RequestOf(expired).Message.Contains(SystemText(16653, "Re-purchase period has been expired.")), "An expired row shows the client's expired notice instead of buying");
            Require((await Requests()).Count == 0, "An expired row sends nothing");
            RequestOf(expired!).Dismiss(); await Frames();
            // The purchase reply: the client's notice and its refresh.
            DetailCall(world, "Repurchase.On", PreviewFixtures.RepurchaseBought(rows[0].ItemId)); await Frames();
            var done = Confirmation();
            Require(done != null && RequestOf(done).Message == SystemText(16654, "You have obtained an item from the re-purchase."), "A completed buy-back shows the client's notice");
            Require((await Requests()).Any(r => (RepurchaseSubOpcode)r[2] == RepurchaseSubOpcode.Refresh), "A completed buy-back refreshes the list");
            RequestOf(done!).Dismiss(); await Frames();
            DetailCall(world, "Repurchase.On", PreviewFixtures.RepurchaseBought(rows[1].ItemId, RepurchaseError.NoCoins)); await Frames();
            var refused = Confirmation();
            Require(refused != null && RequestOf(refused).Message == SystemText(1702, "You don't have enough gold."), "A refused buy-back shows the client's refusal");
            RequestOf(refused!).Dismiss(); Drain();

            // Refresh: the client's request; a shorter list hides the scroll bar.
            await Click(Find<Button>("rb_refresh"));
            Require((await Requests()).Count(r => (RepurchaseSubOpcode)r[2] == RepurchaseSubOpcode.Refresh) == 1, "Refresh sends the client's refresh request");
            DetailCall(world, "Repurchase.On", PreviewFixtures.RepurchaseList(RepurchaseSubOpcode.Refresh, rows.Skip(6).ToList())); await Frames();
            rows = rows.Skip(6).ToList();
            Require(!scroll.Visible, "Seven stored items need no scroll bar");
            Inks("short"); await Capture("short");
            DetailCall(world, "Repurchase.On", PreviewFixtures.RepurchaseList(RepurchaseSubOpcode.Refresh, new List<RepurchaseRow>())); await Frames();
            rows = new List<RepurchaseRow>();
            var empty = Find<Label>("rb_empty");
            Require(empty.Visible && Enumerable.Range(0, Spec.Rows).All(r => !Name(r).Visible && !Buy(r).Visible), "An empty list shows only the client's empty line");
            Require(Enumerable.Range(1, Spec.Rows - 1).All(r => !panel!.GetNode<Control>("repurchase_rule_" + r).Visible), "An empty list draws no row rules");
            Require(Math.Abs(empty.GetGlobalRect().GetCenter().Y - panel!.GetNode<Control>("repurchase_list").GetGlobalRect().Position.Y - (Spec.RowsTop - Spec.ListTop) - Spec.Rows * Spec.RowHeight / 2f) < 1, "The empty line is centred in the rows area");
            await Capture("empty");

            // Close: the client's close request.
            Drain();
            await Click(Descendants(panel!).OfType<Button>().Single(b => b.TooltipText == "Close"));
            Require(!window.Visible && (await Requests()).Count(r => (RepurchaseSubOpcode)r[2] == RepurchaseSubOpcode.Close) == 1, "Close hides the window and sends the client's close request");
        }
        finally
        {
            stop.Cancel();
            NativeApi.Call(conn, "Close");
            layer.Free();
            DetailCall(world, "Repurchase.Dispose");
            world.Free(); net.Free(); typeof(Net).GetProperty("I")!.SetValue(null, original);
        }
        System.IO.File.WriteAllText(output + "/" + prefix + "-repurchase.json", JsonSerializer.Serialize(new { nation, checks, screens, transport = "the client's requests read from a loopback socket; the server replies are scripted" }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("REPURCHASE_AUDIT_OK " + prefix + " " + checks.Count + " checks");
    }
}
