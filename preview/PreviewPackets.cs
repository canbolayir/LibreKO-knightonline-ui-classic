using System.Reflection;
using LibreKO.Network;

public static class PreviewPackets
{
    private static object? dispatcher;
    private static MethodInfo? dispatch;
    private static GameSession? builtFor;

    public static void Receive(Packet packet)
    {
        if (dispatcher == null || !ReferenceEquals(builtFor, Game.Session))
        {
            var client = typeof(Net).Assembly;
            var dispatcherType = client.GetType("LibreKO.Network.PacketDispatcher", throwOnError: true)!;
            dispatcher = Activator.CreateInstance(dispatcherType, nonPublic: true)!;
            dispatch = dispatcherType.GetMethod("Dispatch")!;
            client.GetType("LibreKO.Network.Readers.PacketReaders", throwOnError: true)!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [dispatcher, Game.Session, Game.Requests]);
            builtFor = Game.Session;
        }
        if (!(bool)dispatch!.Invoke(dispatcher, [packet])!)
            throw new InvalidOperationException($"No reader takes opcode 0x{packet.GetOpcode():X2}");
    }
}
