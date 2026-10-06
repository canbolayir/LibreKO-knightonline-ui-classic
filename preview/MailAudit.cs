using Godot;
using LibreKO;
using LibreKO.Network;
using LibreKO.Plugins;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

/// <summary>Exercise the native mail protocol and bridge against an isolated loopback peer.</summary>
public sealed class MailAudit : IDisposable
{
    private readonly Net _net=new();
    private readonly World _world=new();
    private readonly KoConn _connection;
    private readonly TcpListener _listener=new(IPAddress.Loopback,0);
    private TcpClient? _peer;
    private readonly Net? _previous=Net.I;
    private readonly List<MailEntry> _mails=Enumerable.Range(1,3).Select(id=>new MailEntry { Id=id,Sender="Guardian",Subject="Mail "+id,SentAt=DateTime.UtcNow }).ToList();
    public IGameWindows Windows { get; }
    public int RefreshRequests { get; private set; }
    private static void Field(World world,string name,object value) => typeof(World).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(world,value);
    public MailAudit()
    {
        _connection=(KoConn)typeof(Net).GetField("_conn",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(_net)!;
        typeof(Net).GetProperty("I")!.SetValue(null,_net);
        Windows=(IGameWindows)Activator.CreateInstance(typeof(World).GetNestedType("PluginGameBridge",BindingFlags.NonPublic)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{_world},null)!;
        var list=new VBoxContainer();var unread=new CheckButton();_world.AddChild(list);_world.AddChild(unread);
        Field(_world,"_mailList",list);Field(_world,"_mailUnreadOnly",unread);Field(_world,"_mails",_mails);
        Field(_world,"_mailSelectedId",999);
        _net.MailReadEvent+=(id,ok,body)=>typeof(World).GetMethod("OnMailRead",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_world,new object[]{id,ok,body});
    }
    public async Task Connect()
    {
        _listener.Start();
        var accepting=_listener.AcceptTcpClientAsync();
        _connection.Connect("127.0.0.1",((IPEndPoint)_listener.LocalEndpoint).Port);
        _peer=await accepting.WaitAsync(TimeSpan.FromSeconds(4));
        for(int i=0;i<100 && !_connection.Connected;i++) await Task.Delay(10);
        if(!_connection.Connected) throw new Exception("Mail loopback fixture did not connect");
        SetCount(3);
        AuditAttachmentWireFormat();
        _net.SendMailClaimAttachment(42,1);
        byte[] claim=new byte[13];using var timeout=new CancellationTokenSource(4000);
        await _peer.GetStream().ReadExactlyAsync(claim,timeout.Token);
        if(!claim.SequenceEqual(new byte[]{0xaa,0x55,7,0,(byte)GameOpcodes.GS_MAIL,Net.MailSubClaimAttachment,42,0,0,0,1}.Concat(new byte[]{0x55,0xaa})))
            throw new Exception("Single attachment claim wire format changed");
    }
    private void Handle(Packet packet) => typeof(Net).GetMethod("HandleMail",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_net,new object[]{packet});
    private void AuditAttachmentWireFormat()
    {
        List<MailEntry>? parsed=null;
        void Receive(List<MailEntry> rows)=>parsed=rows;
        _net.MailListEvent+=Receive;
        var packet=new Packet(GameOpcodes.GS_MAIL);packet.WriteByte(Net.MailSubList);packet.WriteUShort(2);
        for(int id=1;id<=2;id++)
        {
            packet.WriteInt(id);packet.WriteSByteString("Guardian");packet.WriteSByteString("Mail "+id);
            packet.WriteByte(0);packet.WriteByte((byte)MailAttachmentState.Pending);packet.WriteLong(1_700_000_000);
            packet.WriteByte(1);packet.WriteByte((byte)MailAttachmentKind.Item);packet.WriteInt(810418000);packet.WriteInt(20);packet.WriteInt(9);
            packet.WriteByte((byte)(id==1?MailKind.Store:MailKind.Player));
        }
        Handle(packet);_net.MailListEvent-=Receive;
        if(parsed?.Count!=2 || parsed[1].Id!=2 || parsed[0].Kind!=MailKind.Store || parsed[1].Kind!=MailKind.Player || parsed.Any(m=>m.Items.Single().Remaining!=11))
            throw new Exception("Mail list lost record alignment or claimed attachment counts");
    }
    public void SetCount(ushort count)
    {
        var packet=new Packet(GameOpcodes.GS_MAIL);packet.WriteByte(Net.MailSubUnread);packet.WriteUShort(count);Handle(packet);
        if(Windows.NotificationCount("mail")!=count) throw new Exception("Native window bridge has a stale mail count");
    }
    public async Task Read(int id,bool ok,ushort remaining)
    {
        var packet=new Packet(GameOpcodes.GS_MAIL);packet.WriteByte(Net.MailSubRead);packet.WriteByte(ok?(byte)1:(byte)0);packet.WriteInt(id);packet.WriteSByteString(ok?"Read successfully.":"");Handle(packet);
        var stream=_peer!.GetStream();
        if(ok)
        {
            byte[] request=new byte[8];using var timeout=new CancellationTokenSource(4000);
            await stream.ReadExactlyAsync(request,timeout.Token);
            if(!request.SequenceEqual(new byte[]{0xaa,0x55,2,0,(byte)GameOpcodes.GS_MAIL,Net.MailSubUnread,0x55,0xaa}))
                throw new Exception("Reading mail did not request the authoritative unread count");
            RefreshRequests++;SetCount(remaining);
            if(!_mails.Single(m=>m.Id==id).Read) throw new Exception("A successful late mail read did not update the cached row");
        }
        else
        {
            if(stream.DataAvailable || _mails.Single(m=>m.Id==id).Read || Windows.NotificationCount("mail")!=remaining)
                throw new Exception("Failed reads must retain unread state and count");
        }
    }
    public void Dispose()
    {
        _connection.Close();_peer?.Dispose();_listener.Stop();_world.Free();_net.Free();
        typeof(Net).GetProperty("I")!.SetValue(null,_previous);
    }
}
